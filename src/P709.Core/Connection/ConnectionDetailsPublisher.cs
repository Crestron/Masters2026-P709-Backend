namespace P709.Core.Connection;

/// <summary>
/// Publishes the connection details on <see cref="PanelJoins.ConnectionDetailsSerial"/>.
/// Call <see cref="Publish"/> each time the panel comes online so a reconnecting panel always gets the current value.
/// </summary>
/// <param name="port">Port the web host listens on. Take it from <see cref="LabSettings.ApiPort"/>, as the web host does.</param>
/// <param name="hostAddressProvider">Where this host's address comes from (the processor on Crestron, a fake in tests).</param>
/// <param name="output">Where the serial join is set (the XPanel on Crestron, a recorder in tests).</param>
public sealed class ConnectionDetailsPublisher(int port, IHostAddressProvider hostAddressProvider, IPanelSerialOutput output)
{
    /// <summary>Publishes the details.</summary>
    /// <returns>True if a value was sent; false if no host address was available.</returns>
    public bool Publish()
    {
        var details = ConnectionDetails.For(hostAddressProvider.GetHostAddress(), port);
        if (details is null)
        {
            return false;
        }

        output.SetSerial(PanelJoins.ConnectionDetailsSerial, details.ToJson());
        return true;
    }
}
