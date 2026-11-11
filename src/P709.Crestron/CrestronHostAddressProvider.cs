using Crestron.SimplSharp;
using P709.Core.Connection;

namespace P709.Crestron;

/// <summary>Reads the processor's (or VC-4 server's) own IP address from the Crestron runtime.</summary>
internal sealed class CrestronHostAddressProvider : IHostAddressProvider
{
    public string? GetHostAddress()
    {
        var address = CrestronEthernetHelper.GetEthernetParameter(
            CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_CURRENT_IP_ADDRESS,
            CrestronEthernetHelper.GetAdapterdIdForSpecifiedAdapterType(EthernetAdapterType.EthernetLANAdapter));

        return string.IsNullOrWhiteSpace(address) ? null : address;
    }
}
