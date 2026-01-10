namespace DocMaster.ErasureCoding.TestApi.Models;

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string BasePath { get; set; } = "./storage";
    public bool CreateIfNotExists { get; set; } = true;
}
