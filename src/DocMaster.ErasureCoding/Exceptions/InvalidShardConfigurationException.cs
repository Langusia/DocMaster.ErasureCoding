namespace DocMaster.ErasureCoding;

/// <summary>
/// Exception thrown when shard configuration is invalid
/// </summary>
public class InvalidShardConfigurationException : ArgumentException
{
    public InvalidShardConfigurationException(string message) : base(message) { }
    public InvalidShardConfigurationException(string message, string paramName)
        : base(message, paramName) { }
}
