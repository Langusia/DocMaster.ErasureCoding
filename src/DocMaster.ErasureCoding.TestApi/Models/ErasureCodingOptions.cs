namespace DocMaster.ErasureCoding.TestApi.Models;

public class ErasureCodingOptions
{
    public const string SectionName = "ErasureCoding";

    public int DataShards { get; set; } = 6;
    public int ParityShards { get; set; } = 3;
    public long MaxInputSize { get; set; } = 1_073_741_824; // 1GB
}
