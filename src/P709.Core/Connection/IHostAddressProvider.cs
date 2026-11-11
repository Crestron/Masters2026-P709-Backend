namespace P709.Core.Connection;

/// <summary>Finds the address of this host that the touch panel's browser can reach.</summary>
public interface IHostAddressProvider
{
    /// <summary>The host name or IP address, or null when none is available yet.</summary>
    string? GetHostAddress();
}
