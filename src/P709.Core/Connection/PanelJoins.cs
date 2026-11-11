namespace P709.Core.Connection;

/// <summary>The panel join map for the lab, in one place so the Crestron program and the front-end agree.</summary>
public static class PanelJoins
{
    /// <summary>IPID of the touch panel (XPanel) the CrComLib connection is made to.</summary>
    public const uint PanelIpId = 0x03;

    /// <summary>Serial join (processor to panel) carrying the <see cref="ConnectionDetails"/> JSON.</summary>
    public const uint ConnectionDetailsSerial = 1;
}
