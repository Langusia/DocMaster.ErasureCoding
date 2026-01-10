using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DocMaster.ErasureCoding;

/// <summary>
/// P/Invoke declarations for Intel ISA-L library
/// </summary>
internal static class IsaLNative
{
    private const string LibName = "isal";

    static IsaLNative()
    {
        // Register DllImport resolver for cross-platform library loading
        NativeLibrary.SetDllImportResolver(typeof(IsaLNative).Assembly, DllImportResolver);
    }

    private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibName)
            return IntPtr.Zero;

        // Try bundled library first (development)
        string rid = RuntimeInformation.RuntimeIdentifier;
        string libFileName = GetLibraryFileName();
        string bundledPath = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", libFileName);

        if (File.Exists(bundledPath) && NativeLibrary.TryLoad(bundledPath, out IntPtr handle))
            return handle;

        // Fall back to system library (production)
        if (NativeLibrary.TryLoad(libFileName, assembly, searchPath, out handle))
            return handle;

        // Failed - throw with installation instructions
        throw new IsaLNotLoadedException(
            $"Could not load ISA-L library.{Environment.NewLine}" +
            $"Platform: {rid}{Environment.NewLine}" +
            $"Library: {libFileName}{Environment.NewLine}{Environment.NewLine}" +
            $"Installation instructions:{Environment.NewLine}" +
            $"- Linux: sudo apt-get install libisal2{Environment.NewLine}" +
            $"- Windows: Download from https://github.com/intel/isa-l/releases{Environment.NewLine}" +
            $"- macOS: brew install isa-l{Environment.NewLine}{Environment.NewLine}" +
            $"For development, place library in: {bundledPath}"
        );
    }

    private static string GetLibraryFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "libisal.so.2";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "isal.dll";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "libisal.dylib";

        throw new PlatformNotSupportedException(
            $"Platform not supported: {RuntimeInformation.OSDescription}"
        );
    }

    // ========== Erasure Coding Functions ==========

    /// <summary>
    /// Generate Cauchy encoding matrix
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe void gf_gen_cauchy1_matrix(
        byte* matrix,  // Output: k × (k+m) matrix
        int rows,      // k + m (total shards)
        int cols       // k (data shards)
    );

    /// <summary>
    /// Initialize encoding tables from matrix
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe void ec_init_tables(
        int k,                  // Data shards
        int m,                  // Parity shards
        byte* encode_matrix,    // Parity rows only (m rows × k cols)
        byte* encode_tables     // Output: k × m × 32 bytes
    );

    /// <summary>
    /// Perform erasure encoding or decoding
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe void ec_encode_data(
        int len,        // Shard size in bytes
        int k,          // Number of source shards
        int m,          // Number of output shards
        byte* tables,   // Encoding/decoding tables
        byte** src,     // Array of k source shard pointers
        byte** dest     // Array of m destination shard pointers
    );

    /// <summary>
    /// Generate decode matrix for reconstruction
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gf_gen_decode_matrix(
        byte* encode_matrix,   // Original k × (k+m) matrix
        byte* decode_matrix,   // Output: k × k matrix
        byte* invert_matrix,   // Temp: k × k matrix
        byte* decode_index,    // Output: indices of available shards
        byte* error_index,     // Indices of missing shards
        int k,                 // Data shards
        int n,                 // Total shards
        int nerrs              // Number of errors (missing shards)
    );

    // ========== Compression Functions (DEFLATE) ==========

    /// <summary>
    /// Structure for deflate state
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct isal_zstream
    {
        public byte* next_in;       // Next input byte
        public uint avail_in;       // Number of bytes available at next_in
        public uint total_in;       // Total number of bytes read so far

        public byte* next_out;      // Next output byte
        public uint avail_out;      // Remaining free space at next_out
        public uint total_out;      // Total number of bytes output so far

        public IntPtr state;        // Internal state (opaque)
        public int level;           // Compression level (0-3)
        public int level_buf_size;  // Size of level buffer
        public IntPtr level_buf;    // Level buffer pointer
        public int end_of_stream;   // End of stream flag
        public int gzip_flag;       // GZIP format flag
        public int hist_bits;       // History bits
        public IntPtr hufftables;   // Huffman tables pointer
    }

    /// <summary>
    /// Structure for inflate state
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct inflate_state
    {
        public byte* next_in;       // Next input byte
        public uint avail_in;       // Number of bytes available at next_in
        public uint total_in;       // Total number of bytes read so far

        public byte* next_out;      // Next output byte
        public uint avail_out;      // Remaining free space at next_out
        public uint total_out;      // Total number of bytes output so far

        public uint read_in;        // Bytes read from input
        public uint read_in_length; // Length of read buffer
        public IntPtr state;        // Internal state
        public int block_state;     // Block processing state
        public int dict_length;     // Dictionary length
        public int bfinal;          // Final block flag
        public int crc_flag;        // CRC check flag
        public uint crc;            // CRC value
        public int hist_bits;       // History bits
        public IntPtr tmp_out_buff; // Temporary output buffer
        public uint tmp_out_start;  // Temp buffer start
        public uint tmp_out_end;    // Temp buffer end
    }

    /// <summary>
    /// Initialize deflate stream
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe void isal_deflate_init(isal_zstream* stream);

    /// <summary>
    /// Perform deflate compression
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int isal_deflate(isal_zstream* stream);

    /// <summary>
    /// Initialize inflate stream
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe void isal_inflate_init(inflate_state* state);

    /// <summary>
    /// Perform inflate decompression
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int isal_inflate(inflate_state* state);

    // Compression constants
    internal const int ISAL_DECOMP_OK = 0;
    internal const int ISAL_END_INPUT = 1;
    internal const int ISAL_OUT_OVERFLOW = 2;

    internal const int COMP_OK = 0;
    internal const int INVALID_FLUSH = -7;
    internal const int STATELESS_OVERFLOW = -1;
}
