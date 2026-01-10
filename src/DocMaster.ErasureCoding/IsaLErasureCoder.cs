using System.Buffers;
using System.Runtime.InteropServices;

namespace DocMaster.ErasureCoding;

/// <summary>
/// Intel ISA-L implementation of Reed-Solomon erasure coding
/// </summary>
public sealed class IsaLErasureCoder : IErasureCoder
{
    private readonly int _dataShards;
    private readonly int _parityShards;
    private readonly int _totalShards;

    // Unmanaged memory for encoding tables (allocated once, reused)
    private IntPtr _encodeMatrix;
    private IntPtr _encodeTables;

    private bool _disposed;
    private readonly object _encodeLock = new object();

    private const long MAX_INPUT_SIZE = 1_073_741_824L; // 1GB
    private const int MIN_BUFFER_SIZE = 4096; // Avoid tiny ArrayPool allocations

    /// <summary>
    /// Library version for metadata tracking
    /// </summary>
    public const string LibraryVersion = "1.0.0";

    /// <summary>
    /// Creates a new erasure coder with specified configuration
    /// </summary>
    /// <param name="dataShards">Number of data shards (k): 2-32</param>
    /// <param name="parityShards">Number of parity shards (m): 1-16</param>
    /// <exception cref="InvalidShardConfigurationException">Invalid configuration</exception>
    public IsaLErasureCoder(int dataShards, int parityShards)
    {
        ValidateConfiguration(dataShards, parityShards);

        _dataShards = dataShards;
        _parityShards = parityShards;
        _totalShards = dataShards + parityShards;

        try
        {
            // Allocate unmanaged memory for encoding tables
            int matrixSize = _dataShards * _totalShards;
            int tablesSize = _dataShards * _parityShards * 32;

            _encodeMatrix = Marshal.AllocHGlobal(matrixSize);
            _encodeTables = Marshal.AllocHGlobal(tablesSize);

            // Zero-initialize
            unsafe
            {
                byte* matrixPtr = (byte*)_encodeMatrix;
                byte* tablesPtr = (byte*)_encodeTables;

                for (int i = 0; i < matrixSize; i++)
                    matrixPtr[i] = 0;

                for (int i = 0; i < tablesSize; i++)
                    tablesPtr[i] = 0;

                // Generate Cauchy matrix
                IsaLNative.gf_gen_cauchy1_matrix(matrixPtr, _totalShards, _dataShards);

                // Initialize encoding tables (use only parity rows)
                byte* parityMatrix = matrixPtr + (_dataShards * _dataShards);
                IsaLNative.ec_init_tables(_dataShards, _parityShards, parityMatrix, tablesPtr);
            }
        }
        catch
        {
            // Cleanup on failure
            if (_encodeTables != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_encodeTables);
                _encodeTables = IntPtr.Zero;
            }

            if (_encodeMatrix != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_encodeMatrix);
                _encodeMatrix = IntPtr.Zero;
            }

            throw;
        }
    }

    /// <summary>
    /// Encodes data into data and parity shards
    /// </summary>
    public unsafe byte[][] Encode(byte[] data)
    {
        ThrowIfDisposed();

        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (data.Length == 0)
            throw new ArgumentException("Data cannot be empty", nameof(data));

        if (data.Length > MAX_INPUT_SIZE)
            throw new ArgumentException($"Data exceeds maximum size of {MAX_INPUT_SIZE} bytes", nameof(data));

        // Calculate shard size (with padding)
        int shardSize = (data.Length + _dataShards - 1) / _dataShards;
        int bufferSize = Math.Max(shardSize, MIN_BUFFER_SIZE);

        // Allocate result arrays
        byte[][] shards = new byte[_totalShards][];
        for (int i = 0; i < _totalShards; i++)
        {
            shards[i] = new byte[shardSize];
        }

        // Rent temporary buffers from ArrayPool
        byte[][] tempBuffers = new byte[_totalShards][];
        GCHandle[] handles = new GCHandle[_totalShards];
        byte*[] dataPointers = stackalloc byte*[_dataShards];
        byte*[] parityPointers = stackalloc byte*[_parityShards];

        try
        {
            // Copy data to data shards (with zero-padding on last shard)
            int offset = 0;
            for (int i = 0; i < _dataShards; i++)
            {
                int copyLength = Math.Min(shardSize, data.Length - offset);
                if (copyLength > 0)
                {
                    Buffer.BlockCopy(data, offset, shards[i], 0, copyLength);
                    offset += copyLength;
                }
                // Remaining bytes are already zero (new byte[] initializes to 0)
            }

            // Pin all shards
            for (int i = 0; i < _totalShards; i++)
            {
                handles[i] = GCHandle.Alloc(shards[i], GCHandleType.Pinned);
            }

            // Build pointer arrays
            for (int i = 0; i < _dataShards; i++)
            {
                dataPointers[i] = (byte*)handles[i].AddrOfPinnedObject();
            }

            for (int i = 0; i < _parityShards; i++)
            {
                parityPointers[i] = (byte*)handles[_dataShards + i].AddrOfPinnedObject();
            }

            // Perform encoding (lock to ensure thread safety)
            lock (_encodeLock)
            {
                ThrowIfDisposed(); // Double-check after acquiring lock

                IsaLNative.ec_encode_data(
                    shardSize,
                    _dataShards,
                    _parityShards,
                    (byte*)_encodeTables,
                    dataPointers,
                    parityPointers
                );
            }
        }
        finally
        {
            // Free all GCHandles
            for (int i = 0; i < _totalShards; i++)
            {
                if (handles[i].IsAllocated)
                    handles[i].Free();
            }
        }

        return shards;
    }

    /// <summary>
    /// Decodes original data from available shards
    /// </summary>
    public unsafe byte[] Decode(byte[][] shards, bool[] shardPresent, int originalSize)
    {
        ThrowIfDisposed();

        if (shards == null)
            throw new ArgumentNullException(nameof(shards));

        if (shardPresent == null)
            throw new ArgumentNullException(nameof(shardPresent));

        if (shards.Length != _totalShards)
            throw new ArgumentException($"Expected {_totalShards} shards, got {shards.Length}", nameof(shards));

        if (shardPresent.Length != _totalShards)
            throw new ArgumentException($"Expected {_totalShards} shard presence flags, got {shardPresent.Length}", nameof(shardPresent));

        if (originalSize < 0)
            throw new ArgumentException("Original size cannot be negative", nameof(originalSize));

        // Count available shards
        int availableCount = 0;
        for (int i = 0; i < _totalShards; i++)
        {
            if (shardPresent[i])
                availableCount++;
        }

        if (availableCount < _dataShards)
            throw new InsufficientShardsException(_dataShards, availableCount);

        // Check if all data shards are present (fast path)
        bool allDataShardsPresent = true;
        for (int i = 0; i < _dataShards; i++)
        {
            if (!shardPresent[i])
            {
                allDataShardsPresent = false;
                break;
            }
        }

        int shardSize = shards[0]?.Length ?? 0;

        if (allDataShardsPresent)
        {
            // Fast path: concatenate data shards directly
            byte[] result = new byte[originalSize];
            int offset = 0;

            for (int i = 0; i < _dataShards && offset < originalSize; i++)
            {
                int copyLength = Math.Min(shardSize, originalSize - offset);
                Buffer.BlockCopy(shards[i], 0, result, offset, copyLength);
                offset += copyLength;
            }

            return result;
        }

        // Slow path: reconstruct missing shards
        IntPtr decodeMatrix = IntPtr.Zero;
        IntPtr invertMatrix = IntPtr.Zero;
        IntPtr decodeTables = IntPtr.Zero;
        IntPtr decodeIndex = IntPtr.Zero;
        IntPtr errorIndex = IntPtr.Zero;
        GCHandle[] handles = new GCHandle[_totalShards];

        try
        {
            // Allocate temporary matrices
            decodeMatrix = Marshal.AllocHGlobal(_dataShards * _dataShards);
            invertMatrix = Marshal.AllocHGlobal(_dataShards * _dataShards);
            decodeTables = Marshal.AllocHGlobal(_dataShards * _dataShards * 32);
            decodeIndex = Marshal.AllocHGlobal(_dataShards);
            errorIndex = Marshal.AllocHGlobal(_parityShards);

            // Build decode and error index arrays
            byte* decodeIndexPtr = (byte*)decodeIndex;
            byte* errorIndexPtr = (byte*)errorIndex;
            int decodeCount = 0;
            int errorCount = 0;

            for (int i = 0; i < _totalShards; i++)
            {
                if (shardPresent[i] && decodeCount < _dataShards)
                {
                    decodeIndexPtr[decodeCount++] = (byte)i;
                }
                else if (!shardPresent[i])
                {
                    errorIndexPtr[errorCount++] = (byte)i;
                }
            }

            // Generate decode matrix
            int ret = IsaLNative.gf_gen_decode_matrix(
                (byte*)_encodeMatrix,
                (byte*)decodeMatrix,
                (byte*)invertMatrix,
                decodeIndexPtr,
                errorIndexPtr,
                _dataShards,
                _totalShards,
                errorCount
            );

            if (ret != 0)
                throw new InvalidOperationException($"Failed to generate decode matrix: error code {ret}");

            // Initialize decode tables
            IsaLNative.ec_init_tables(
                _dataShards,
                errorCount,
                (byte*)decodeMatrix,
                (byte*)decodeTables
            );

            // Reconstruct missing shards
            byte*[] srcPointers = stackalloc byte*[_dataShards];
            byte*[] dstPointers = stackalloc byte*[errorCount];

            // Pin available shards
            for (int i = 0; i < decodeCount; i++)
            {
                int shardIndex = decodeIndexPtr[i];
                handles[shardIndex] = GCHandle.Alloc(shards[shardIndex], GCHandleType.Pinned);
                srcPointers[i] = (byte*)handles[shardIndex].AddrOfPinnedObject();
            }

            // Allocate and pin missing shards
            for (int i = 0; i < errorCount; i++)
            {
                int shardIndex = errorIndexPtr[i];
                shards[shardIndex] = new byte[shardSize];
                handles[shardIndex] = GCHandle.Alloc(shards[shardIndex], GCHandleType.Pinned);
                dstPointers[i] = (byte*)handles[shardIndex].AddrOfPinnedObject();
            }

            // Perform reconstruction
            lock (_encodeLock)
            {
                ThrowIfDisposed();

                IsaLNative.ec_encode_data(
                    shardSize,
                    _dataShards,
                    errorCount,
                    (byte*)decodeTables,
                    srcPointers,
                    dstPointers
                );
            }

            // Concatenate data shards
            byte[] result = new byte[originalSize];
            int offset = 0;

            for (int i = 0; i < _dataShards && offset < originalSize; i++)
            {
                int copyLength = Math.Min(shardSize, originalSize - offset);
                Buffer.BlockCopy(shards[i], 0, result, offset, copyLength);
                offset += copyLength;
            }

            return result;
        }
        finally
        {
            // Free all handles
            for (int i = 0; i < _totalShards; i++)
            {
                if (handles[i].IsAllocated)
                    handles[i].Free();
            }

            // Free temporary matrices (in reverse order)
            if (errorIndex != IntPtr.Zero) Marshal.FreeHGlobal(errorIndex);
            if (decodeIndex != IntPtr.Zero) Marshal.FreeHGlobal(decodeIndex);
            if (decodeTables != IntPtr.Zero) Marshal.FreeHGlobal(decodeTables);
            if (invertMatrix != IntPtr.Zero) Marshal.FreeHGlobal(invertMatrix);
            if (decodeMatrix != IntPtr.Zero) Marshal.FreeHGlobal(decodeMatrix);
        }
    }

    /// <summary>
    /// Validates shard configuration
    /// </summary>
    private static void ValidateConfiguration(int dataShards, int parityShards)
    {
        if (dataShards < 2 || dataShards > 32)
            throw new InvalidShardConfigurationException(
                $"DataShards must be between 2 and 32, got {dataShards}", nameof(dataShards));

        if (parityShards < 1 || parityShards > 16)
            throw new InvalidShardConfigurationException(
                $"ParityShards must be between 1 and 16, got {parityShards}", nameof(parityShards));

        if (dataShards + parityShards > 48)
            throw new InvalidShardConfigurationException(
                $"TotalShards (k+m) must not exceed 48, got {dataShards + parityShards}");

        if (parityShards > dataShards)
            throw new InvalidShardConfigurationException(
                $"ParityShards ({parityShards}) must not exceed DataShards ({dataShards})");
    }

    /// <summary>
    /// Throws if the coder has been disposed
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(IsaLErasureCoder));
    }

    /// <summary>
    /// Disposes unmanaged resources
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_encodeLock)
        {
            if (_disposed)
                return;

            _disposed = true;

            // Free in reverse allocation order
            if (_encodeTables != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_encodeTables);
                _encodeTables = IntPtr.Zero;
            }

            if (_encodeMatrix != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_encodeMatrix);
                _encodeMatrix = IntPtr.Zero;
            }
        }
    }

    public int DataShards => _dataShards;
    public int ParityShards => _parityShards;
    public int TotalShards => _totalShards;
}
