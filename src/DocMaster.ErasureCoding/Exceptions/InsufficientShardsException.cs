using System;

namespace DocMaster.ErasureCoding.Exceptions;

/// <summary>
/// Exception thrown when not enough shards are available to decode
/// </summary>
public class InsufficientShardsException : InvalidOperationException
{
    public int Required { get; }
    public int Available { get; }

    public InsufficientShardsException(int required, int available)
        : base($"Insufficient shards: need {required}, have {available}")
    {
        Required = required;
        Available = available;
    }
}
