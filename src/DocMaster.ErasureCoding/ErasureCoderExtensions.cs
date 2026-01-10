using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace DocMaster.ErasureCoding;

/// <summary>
/// Extension methods for IErasureCoder
/// </summary>
public static class ErasureCoderExtensions
{
    /// <summary>
    /// Encode data and generate metadata (checksums, sizes)
    /// </summary>
    /// <param name="coder">Erasure coder instance</param>
    /// <param name="data">Data to encode</param>
    /// <returns>Encode result with shards and metadata</returns>
    public static EncodeResult EncodeWithMetadata(this IErasureCoder coder, byte[] data)
    {
        if (coder == null)
            throw new ArgumentNullException(nameof(coder));

        var shards = coder.Encode(data);

        return new EncodeResult
        {
            Shards = shards,
            OriginalSize = data.Length,
            ShardSize = shards[0].Length,
            ShardChecksums = shards
                .Select(shard => Convert.ToHexString(SHA256.HashData(shard)))
                .ToArray(),
            DataShards = coder.DataShards,
            ParityShards = coder.ParityShards,
            LibraryVersion = IsaLErasureCoder.LibraryVersion,
            EncodedAt = DateTime.UtcNow,
            IsCompressed = false
        };
    }

    /// <summary>
    /// Compress data using ISA-L deflate, then encode with metadata
    /// </summary>
    /// <param name="coder">Erasure coder instance</param>
    /// <param name="data">Data to compress and encode</param>
    /// <param name="compressionLevel">Compression level (0-3, default 2)</param>
    /// <returns>Encode result with compressed data</returns>
    public static unsafe EncodeResult CompressAndEncode(this IErasureCoder coder, byte[] data, int compressionLevel = 2)
    {
        if (coder == null)
            throw new ArgumentNullException(nameof(coder));

        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (compressionLevel < 0 || compressionLevel > 3)
            throw new ArgumentException("Compression level must be 0-3", nameof(compressionLevel));

        int uncompressedSize = data.Length;

        // Compress data using ISA-L deflate
        byte[] compressed = IsaLCompress(data, compressionLevel);

        // Encode compressed data
        var shards = coder.Encode(compressed);

        return new EncodeResult
        {
            Shards = shards,
            OriginalSize = compressed.Length,
            ShardSize = shards[0].Length,
            ShardChecksums = shards
                .Select(shard => Convert.ToHexString(SHA256.HashData(shard)))
                .ToArray(),
            DataShards = coder.DataShards,
            ParityShards = coder.ParityShards,
            LibraryVersion = IsaLErasureCoder.LibraryVersion,
            EncodedAt = DateTime.UtcNow,
            IsCompressed = true,
            UncompressedSize = uncompressedSize
        };
    }

    /// <summary>
    /// Decode shards and decompress if needed
    /// </summary>
    /// <param name="coder">Erasure coder instance</param>
    /// <param name="shards">Encoded shards</param>
    /// <param name="shardPresent">Shard availability flags</param>
    /// <param name="metadata">Encoding metadata</param>
    /// <returns>Decoded and decompressed data</returns>
    public static byte[] DecodeAndDecompress(this IErasureCoder coder, byte[][] shards, bool[] shardPresent, EncodeResult metadata)
    {
        if (coder == null)
            throw new ArgumentNullException(nameof(coder));

        if (metadata == null)
            throw new ArgumentNullException(nameof(metadata));

        // Decode
        byte[] data = coder.Decode(shards, shardPresent, metadata.OriginalSize);

        // Decompress if needed
        if (metadata.IsCompressed && metadata.UncompressedSize.HasValue)
        {
            data = IsaLDecompress(data, metadata.UncompressedSize.Value);
        }

        return data;
    }

    /// <summary>
    /// Compress data using ISA-L deflate
    /// </summary>
    private static unsafe byte[] IsaLCompress(byte[] input, int level)
    {
        // Allocate output buffer (worst case: input size + 5 bytes per 16KB block)
        int maxOutputSize = input.Length + ((input.Length / 16384) + 1) * 5 + 1024;
        byte[] output = ArrayPool<byte>.Shared.Rent(maxOutputSize);

        GCHandle inputHandle = default;
        GCHandle outputHandle = default;

        try
        {
            inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
            outputHandle = GCHandle.Alloc(output, GCHandleType.Pinned);

            var stream = new IsaLNative.isal_zstream();
            IsaLNative.isal_deflate_init(&stream);

            stream.next_in = (byte*)inputHandle.AddrOfPinnedObject();
            stream.avail_in = (uint)input.Length;
            stream.next_out = (byte*)outputHandle.AddrOfPinnedObject();
            stream.avail_out = (uint)maxOutputSize;
            stream.level = level;
            stream.end_of_stream = 1; // Finish in one call

            int ret = IsaLNative.isal_deflate(&stream);

            if (ret != IsaLNative.COMP_OK && ret != IsaLNative.ISAL_END_INPUT)
                throw new InvalidOperationException($"Compression failed with code: {ret}");

            // Copy result to exact-sized array
            int compressedSize = (int)stream.total_out;
            byte[] result = new byte[compressedSize];
            Buffer.BlockCopy(output, 0, result, 0, compressedSize);

            return result;
        }
        finally
        {
            if (inputHandle.IsAllocated) inputHandle.Free();
            if (outputHandle.IsAllocated) outputHandle.Free();
            ArrayPool<byte>.Shared.Return(output, clearArray: true);
        }
    }

    /// <summary>
    /// Decompress data using ISA-L inflate
    /// </summary>
    private static unsafe byte[] IsaLDecompress(byte[] input, int expectedOutputSize)
    {
        byte[] output = new byte[expectedOutputSize];

        GCHandle inputHandle = default;
        GCHandle outputHandle = default;

        try
        {
            inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
            outputHandle = GCHandle.Alloc(output, GCHandleType.Pinned);

            var state = new IsaLNative.inflate_state();
            IsaLNative.isal_inflate_init(&state);

            state.next_in = (byte*)inputHandle.AddrOfPinnedObject();
            state.avail_in = (uint)input.Length;
            state.next_out = (byte*)outputHandle.AddrOfPinnedObject();
            state.avail_out = (uint)expectedOutputSize;

            int ret = IsaLNative.isal_inflate(&state);

            if (ret != IsaLNative.ISAL_DECOMP_OK && ret != IsaLNative.ISAL_END_INPUT)
                throw new InvalidOperationException($"Decompression failed with code: {ret}");

            if (state.total_out != expectedOutputSize)
                throw new InvalidOperationException(
                    $"Decompressed size mismatch: expected {expectedOutputSize}, got {state.total_out}");

            return output;
        }
        finally
        {
            if (inputHandle.IsAllocated) inputHandle.Free();
            if (outputHandle.IsAllocated) outputHandle.Free();
        }
    }
}
