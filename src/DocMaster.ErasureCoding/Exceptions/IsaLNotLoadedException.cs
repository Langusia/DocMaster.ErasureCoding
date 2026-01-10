using System;

namespace DocMaster.ErasureCoding.Exceptions;

/// <summary>
/// Exception thrown when ISA-L library cannot be loaded
/// </summary>
public class IsaLNotLoadedException : Exception
{
    public IsaLNotLoadedException(string message) : base(message) { }
    public IsaLNotLoadedException(string message, Exception innerException)
        : base(message, innerException) { }
}
