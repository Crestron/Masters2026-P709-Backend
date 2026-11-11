using P709.Core.Connection;

namespace P709.Core.Tests.Connection;

internal sealed class FakeHostAddressProvider(string? address) : IHostAddressProvider
{
    public string? GetHostAddress() => address;
}

internal sealed class RecordingPanelSerialOutput : IPanelSerialOutput
{
    public List<(uint Join, string Value)> Sent { get; } = [];

    public void SetSerial(uint join, string value) => Sent.Add((join, value));
}
