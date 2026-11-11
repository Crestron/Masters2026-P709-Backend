namespace P709.Core.Configuration;

/// <summary>A configuration file (settings or room configuration) is empty, malformed or inconsistent. The message says what to fix.</summary>
public sealed class InvalidConfigurationException : Exception
{
    /// <summary>Creates the exception.</summary>
    public InvalidConfigurationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
