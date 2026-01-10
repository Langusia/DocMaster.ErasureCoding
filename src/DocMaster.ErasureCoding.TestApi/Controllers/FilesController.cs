using Microsoft.AspNetCore.Mvc;
using DocMaster.ErasureCoding.TestApi.Services;

namespace DocMaster.ErasureCoding.TestApi.Controllers;

/// <summary>
/// File operations with erasure coding
/// </summary>
[ApiController]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly IStorageService _storage;
    private readonly ILogger<FilesController> _logger;

    public FilesController(IStorageService storage, ILogger<FilesController> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Upload and encode a file
    /// </summary>
    /// <param name="filename">Filename to store</param>
    /// <param name="file">File content</param>
    /// <returns>Upload result with file metadata</returns>
    [HttpPost("{filename}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadFile(string filename, IFormFile file)
    {
        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            byte[] data = ms.ToArray();

            _logger.LogInformation("Uploading file: {Filename}, Size: {Size} bytes", filename, data.Length);

            await _storage.StoreFileAsync(filename, data);

            return Ok(new
            {
                filename,
                size = data.Length,
                message = "File uploaded and encoded successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file: {Filename}", filename);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Download and decode a file
    /// </summary>
    /// <param name="filename">Filename to retrieve</param>
    /// <returns>File content</returns>
    [HttpGet("{filename}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFile(string filename)
    {
        try
        {
            _logger.LogInformation("Downloading file: {Filename}", filename);

            var data = await _storage.RetrieveFileAsync(filename);
            return File(data, "application/octet-stream", filename);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = $"File not found: {filename}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file: {Filename}", filename);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// List all stored files
    /// </summary>
    /// <returns>List of files with metadata</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFiles()
    {
        try
        {
            var files = await _storage.ListFilesAsync();
            return Ok(files);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list files");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete a file and all its shards
    /// </summary>
    /// <param name="filename">Filename to delete</param>
    [HttpDelete("{filename}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFile(string filename)
    {
        try
        {
            _logger.LogInformation("Deleting file: {Filename}", filename);

            await _storage.DeleteFileAsync(filename);
            return NoContent();
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = $"File not found: {filename}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file: {Filename}", filename);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete a specific shard (simulate node failure for testing)
    /// </summary>
    /// <param name="filename">Filename</param>
    /// <param name="shardIndex">Shard index to delete (0-8 for RS(6,3))</param>
    [HttpDelete("{filename}/shard/{shardIndex}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteShard(string filename, int shardIndex)
    {
        try
        {
            _logger.LogWarning("Deleting shard {ShardIndex} for file: {Filename}", shardIndex, filename);

            await _storage.DeleteShardAsync(filename, shardIndex);

            return Ok(new
            {
                message = $"Shard {shardIndex} deleted for testing",
                filename,
                shardIndex
            });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = $"File or shard not found: {filename}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete shard {ShardIndex} for file: {Filename}", shardIndex, filename);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Reconstruct missing shards (healing/recovery)
    /// </summary>
    /// <param name="filename">Filename to heal</param>
    /// <returns>Healing result with statistics</returns>
    [HttpPost("{filename}/heal")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HealFile(string filename)
    {
        try
        {
            _logger.LogInformation("Healing file: {Filename}", filename);

            var result = await _storage.HealFileAsync(filename);

            return Ok(result);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = $"File not found: {filename}" });
        }
        catch (InsufficientShardsException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to heal file: {Filename}", filename);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get file health status (how many shards are available)
    /// </summary>
    /// <param name="filename">Filename to check</param>
    /// <returns>Health status with shard availability</returns>
    [HttpGet("{filename}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFileStatus(string filename)
    {
        try
        {
            var status = await _storage.GetFileStatusAsync(filename);
            return Ok(status);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = $"File not found: {filename}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get status for file: {Filename}", filename);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
