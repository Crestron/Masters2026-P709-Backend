namespace P709.Core.Connection;

/// <summary>Sends a serial (string) join value to the touch panel. The Crestron program supplies the real implementation.</summary>
public interface IPanelSerialOutput
{
    /// <summary>Sets the serial join to the given value.</summary>
    /// <param name="join">The serial join number.</param>
    /// <param name="value">The value to send.</param>
    void SetSerial(uint join, string value);
}
