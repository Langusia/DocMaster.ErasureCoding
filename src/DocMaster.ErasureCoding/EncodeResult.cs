namespace DocMaster.ErasureCoding;

/// <summary>
/// Result of encoding operation with metadata
/// </summary>
public class EncodeResult
{
    /// <summary>Encoded shards (data + parity)</summary>
    public byte[][] Shards { get; set; } = Array.Empty<byte[]>();

    /// <summary>Original data size before encoding</summary>
    public int OriginalSize { get; set; }

    /// <summary>Size of each shard in bytes</summary>
    public int ShardSize { get; set; }

    /// <summary>SHA256 checksums of each shard (hex strings)</summary>
    public string[] ShardChecksums { get; set; } = Array.Empty<string>();

    /// <summary>Number of data shards</summary>
    public int DataShards { get; set; }

    /// <summary>Number of parity shards</summary>
    public int ParityShards { get; set; }

    /// <summary>Library version used for encoding</summary>
    public string LibraryVersion { get; set; } = IsaLErasureCoder.LibraryVersion;

    /// <summary>Timestamp when encoded</summary>
    public DateTime EncodedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether data was compressed before encoding</summary>
    public bool IsCompressed { get; set; }

    /// <summary>Original size before compression (if compressed)</summary>
    public int? UncompressedSize { get; set; }
}
