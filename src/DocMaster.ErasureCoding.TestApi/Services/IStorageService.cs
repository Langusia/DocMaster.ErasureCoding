using System.Collections.Generic;
using System.Threading.Tasks;

namespace DocMaster.ErasureCoding.TestApi.Services;

public interface IStorageService
{
    Task StoreFileAsync(string filename, byte[] data);
    Task<byte[]> RetrieveFileAsync(string filename);
    Task<List<object>> ListFilesAsync();
    Task DeleteFileAsync(string filename);
    Task DeleteShardAsync(string filename, int shardIndex);
    Task<object> HealFileAsync(string filename);
    Task<object> GetFileStatusAsync(string filename);
}
