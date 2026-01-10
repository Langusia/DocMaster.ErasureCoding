using System.Security.Cryptography;
using System.Text.Json;
using DocMaster.ErasureCoding;
using DocMaster.ErasureCoding.TestApi.Models;
using Microsoft.Extensions.Options;

namespace DocMaster.ErasureCoding.TestApi.Services;

public class LocalStorageService : IStorageService
{
    private readonly string _basePath;
    private readonly IErasureCoder _erasureCoder;
    private readonly ILogger<LocalStorageService> _logger;

    public LocalStorageService(
        IOptions<StorageOptions> storageOptions,
        IErasureCoder erasureCoder,
        ILogger<LocalStorageService> logger)
    {
        _basePath = storageOptions.Value.BasePath;
        _erasureCoder = erasureCoder;
        _logger = logger;

        // Create storage directory if needed
        if (storageOptions.Value.CreateIfNotExists && !Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
            _logger.LogInformation("Created storage directory: {Path}", Path.GetFullPath(_basePath));
        }
    }

    public async Task StoreFileAsync(string filename, byte[] data)
    {
        var fileDir = Path.Combine(_basePath, filename);
        Directory.CreateDirectory(fileDir);

        // Encode with metadata
        var result = _erasureCoder.EncodeWithMetadata(data);

        _logger.LogInformation(
            "Encoding {Filename}: {Size} bytes → {ShardCount} shards of {ShardSize} bytes each",
            filename, data.Length, result.Shards.Length, result.ShardSize);

        // Write shards
        for (int i = 0; i < result.Shards.Length; i++)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(fileDir, $"shard{i}"),
                result.Shards[i]
            );
        }

        // Verify shards after write
        for (int i = 0; i < result.Shards.Length; i++)
        {
            var writtenData = await File.ReadAllBytesAsync(Path.Combine(fileDir, $"shard{i}"));
            var checksum = Convert.ToHexString(SHA256.HashData(writtenData));

            if (checksum != result.ShardChecksums[i])
            {
                throw new InvalidOperationException($"Shard {i} checksum mismatch after write!");
            }
        }

        // Create metadata
        var metadata = new FileMetadata
        {
            Filename = filename,
            OriginalSize = result.OriginalSize,
            ShardSize = result.ShardSize,
            DataShards = result.DataShards,
            ParityShards = result.ParityShards,
            ShardChecksums = result.ShardChecksums,
            FileChecksum = Convert.ToHexString(SHA256.HashData(data)),
            CreatedAt = result.EncodedAt,
            LibraryVersion = result.LibraryVersion,
            IsCompressed = result.IsCompressed,
            UncompressedSize = result.UncompressedSize
        };

        await File.WriteAllTextAsync(
            Path.Combine(fileDir, "metadata.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true })
        );

        _logger.LogInformation("Stored {Filename} with {ShardCount} shards", filename, result.Shards.Length);
    }

    public async Task<byte[]> RetrieveFileAsync(string filename)
    {
        var fileDir = Path.Combine(_basePath, filename);

        if (!Directory.Exists(fileDir))
            throw new FileNotFoundException($"File not found: {filename}");

        // Load metadata
        var metadataJson = await File.ReadAllTextAsync(Path.Combine(fileDir, "metadata.json"));
        var metadata = JsonSerializer.Deserialize<FileMetadata>(metadataJson)
            ?? throw new InvalidOperationException("Invalid metadata");

        // Load shards
        var shards = new byte[_erasureCoder.TotalShards][];
        var present = new bool[_erasureCoder.TotalShards];
        int availableCount = 0;

        for (int i = 0; i < _erasureCoder.TotalShards; i++)
        {
            var shardPath = Path.Combine(fileDir, $"shard{i}");

            if (File.Exists(shardPath))
            {
                shards[i] = await File.ReadAllBytesAsync(shardPath);
                present[i] = true;
                availableCount++;

                // Verify shard checksum
                var checksum = Convert.ToHexString(SHA256.HashData(shards[i]));
                if (checksum != metadata.ShardChecksums[i])
                {
                    _logger.LogWarning("Shard {Index} checksum mismatch for {Filename}", i, filename);
                }
            }
        }

        _logger.LogInformation(
            "Retrieved {Filename}: {Available}/{Total} shards available",
            filename, availableCount, _erasureCoder.TotalShards);

        if (availableCount < _erasureCoder.DataShards)
        {
            throw new InsufficientShardsException(_erasureCoder.DataShards, availableCount);
        }

        // Decode
        var data = _erasureCoder.Decode(shards, present, (int)metadata.OriginalSize);

        // Verify file checksum
        var fileChecksum = Convert.ToHexString(SHA256.HashData(data));
        if (fileChecksum != metadata.FileChecksum)
        {
            throw new InvalidOperationException("File checksum mismatch - data corrupted!");
        }

        return data;
    }

    public Task<List<object>> ListFilesAsync()
    {
        if (!Directory.Exists(_basePath))
            return Task.FromResult(new List<object>());

        var files = Directory.GetDirectories(_basePath)
            .Select(dir =>
            {
                var filename = Path.GetFileName(dir);
                var metadataPath = Path.Combine(dir, "metadata.json");

                if (File.Exists(metadataPath))
                {
                    var json = File.ReadAllText(metadataPath);
                    var metadata = JsonSerializer.Deserialize<FileMetadata>(json);

                    return new
                    {
                        filename,
                        size = metadata?.OriginalSize ?? 0,
                        createdAt = metadata?.CreatedAt ?? DateTime.MinValue,
                        shards = (metadata?.DataShards ?? 0) + (metadata?.ParityShards ?? 0),
                        libraryVersion = metadata?.LibraryVersion ?? "unknown",
                        isCompressed = metadata?.IsCompressed ?? false
                    };
                }

                return new
                {
                    filename,
                    size = 0L,
                    createdAt = DateTime.MinValue,
                    shards = 0,
                    libraryVersion = "unknown",
                    isCompressed = false
                };
            })
            .Cast<object>()
            .ToList();

        return Task.FromResult(files);
    }

    public Task DeleteFileAsync(string filename)
    {
        var fileDir = Path.Combine(_basePath, filename);

        if (!Directory.Exists(fileDir))
            throw new FileNotFoundException($"File not found: {filename}");

        Directory.Delete(fileDir, recursive: true);

        _logger.LogInformation("Deleted {Filename}", filename);

        return Task.CompletedTask;
    }

    public Task DeleteShardAsync(string filename, int shardIndex)
    {
        var fileDir = Path.Combine(_basePath, filename);

        if (!Directory.Exists(fileDir))
            throw new FileNotFoundException($"File not found: {filename}");

        var shardPath = Path.Combine(fileDir, $"shard{shardIndex}");

        if (File.Exists(shardPath))
        {
            File.Delete(shardPath);
            _logger.LogInformation("Deleted shard {Index} for {Filename}", shardIndex, filename);
        }

        return Task.CompletedTask;
    }

    public async Task<object> HealFileAsync(string filename)
    {
        var fileDir = Path.Combine(_basePath, filename);

        if (!Directory.Exists(fileDir))
            throw new FileNotFoundException($"File not found: {filename}");

        // Load metadata
        var metadataJson = await File.ReadAllTextAsync(Path.Combine(fileDir, "metadata.json"));
        var metadata = JsonSerializer.Deserialize<FileMetadata>(metadataJson)
            ?? throw new InvalidOperationException("Invalid metadata");

        // Load available shards
        var shards = new byte[_erasureCoder.TotalShards][];
        var present = new bool[_erasureCoder.TotalShards];
        int availableCount = 0;
        var missingIndices = new List<int>();

        for (int i = 0; i < _erasureCoder.TotalShards; i++)
        {
            var shardPath = Path.Combine(fileDir, $"shard{i}");

            if (File.Exists(shardPath))
            {
                shards[i] = await File.ReadAllBytesAsync(shardPath);
                present[i] = true;
                availableCount++;
            }
            else
            {
                missingIndices.Add(i);
            }
        }

        if (availableCount < _erasureCoder.DataShards)
        {
            throw new InsufficientShardsException(_erasureCoder.DataShards, availableCount);
        }

        if (missingIndices.Count == 0)
        {
            return new
            {
                message = "File is healthy, no healing needed",
                filename,
                availableShards = availableCount,
                totalShards = _erasureCoder.TotalShards,
                healedShards = 0
            };
        }

        _logger.LogInformation(
            "Healing {Filename}: {Available}/{Total} shards available, reconstructing {Missing} shards",
            filename, availableCount, _erasureCoder.TotalShards, missingIndices.Count);

        // Decode to get original data
        var originalData = _erasureCoder.Decode(shards, present, (int)metadata.OriginalSize);

        // Re-encode to regenerate all shards
        var newResult = _erasureCoder.EncodeWithMetadata(originalData);

        // Write missing shards
        int healedCount = 0;
        foreach (var index in missingIndices)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(fileDir, $"shard{index}"),
                newResult.Shards[index]
            );
            healedCount++;
        }

        _logger.LogInformation("Healed {Filename}: reconstructed {Count} shards", filename, healedCount);

        return new
        {
            message = $"Successfully healed {healedCount} missing shards",
            filename,
            availableShardsBefore = availableCount,
            availableShardsAfter = _erasureCoder.TotalShards,
            healedShards = healedCount,
            totalShards = _erasureCoder.TotalShards
        };
    }

    public async Task<object> GetFileStatusAsync(string filename)
    {
        var fileDir = Path.Combine(_basePath, filename);

        if (!Directory.Exists(fileDir))
            throw new FileNotFoundException($"File not found: {filename}");

        // Load metadata
        var metadataJson = await File.ReadAllTextAsync(Path.Combine(fileDir, "metadata.json"));
        var metadata = JsonSerializer.Deserialize<FileMetadata>(metadataJson)
            ?? throw new InvalidOperationException("Invalid metadata");

        // Check shard availability
        int availableCount = 0;
        var shardStatus = new List<object>();

        for (int i = 0; i < _erasureCoder.TotalShards; i++)
        {
            var shardPath = Path.Combine(fileDir, $"shard{i}");
            bool exists = File.Exists(shardPath);

            if (exists)
                availableCount++;

            shardStatus.Add(new
            {
                index = i,
                type = i < _erasureCoder.DataShards ? "data" : "parity",
                available = exists
            });
        }

        string health;
        if (availableCount >= _erasureCoder.TotalShards)
            health = "healthy";
        else if (availableCount >= _erasureCoder.DataShards)
            health = "degraded";
        else
            health = "critical";

        return new
        {
            filename,
            health,
            availableShards = availableCount,
            totalShards = _erasureCoder.TotalShards,
            minimumRequired = _erasureCoder.DataShards,
            canDecode = availableCount >= _erasureCoder.DataShards,
            originalSize = metadata.OriginalSize,
            createdAt = metadata.CreatedAt,
            libraryVersion = metadata.LibraryVersion,
            isCompressed = metadata.IsCompressed,
            shards = shardStatus
        };
    }
}
