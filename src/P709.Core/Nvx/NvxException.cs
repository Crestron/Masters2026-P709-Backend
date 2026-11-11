namespace P709.Core.Nvx;

/// <summary>An NVX refused a request, could not be reached, or answered with something the program does not understand. The message says which and why.</summary>
public class NvxException : Exception
{
    /// <summary>Creates the exception.</summary>
    public NvxException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The NVX could not be reached: no answer, a network error or a timeout. An NVX that answers but refuses a request throws a
/// plain <see cref="NvxException"/> instead.
/// </summary>
public sealed class NvxUnreachableException : NvxException
{
    /// <summary>Creates the exception.</summary>
    public NvxUnreachableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
