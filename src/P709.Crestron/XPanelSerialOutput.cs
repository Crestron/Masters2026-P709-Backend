using Crestron.SimplSharpPro.DeviceSupport;
using P709.Core.Connection;

namespace P709.Crestron;

/// <summary>Adapts a Crestron touch panel to <see cref="IPanelSerialOutput"/>.</summary>
internal sealed class XPanelSerialOutput(BasicTriList panel) : IPanelSerialOutput
{
    public void SetSerial(uint join, string value) => panel.StringInput[join].StringValue = value;
}
