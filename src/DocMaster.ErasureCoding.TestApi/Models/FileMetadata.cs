using System;

namespace DocMaster.ErasureCoding.TestApi.Models;

public class FileMetadata
{
    public string Filename { get; set; } = string.Empty;
    public long OriginalSize { get; set; }
    public int ShardSize { get; set; }
    public int DataShards { get; set; }
    public int ParityShards { get; set; }
    public string[] ShardChecksums { get; set; } = Array.Empty<string>();
    public string FileChecksum { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string LibraryVersion { get; set; } = string.Empty;
    public bool IsCompressed { get; set; }
    public int? UncompressedSize { get; set; }
}
