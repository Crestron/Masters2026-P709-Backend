namespace P709.Core.Nvx;

/// <summary>
/// How the room's DM-NVX devices are reached and which multicast stream belongs to which source. Read from the <c>nvx</c>
/// section of the room configuration. A source with no entry in <see cref="Sources"/> is selected exactly as before, with no
/// NVX routing.
/// </summary>
/// <param name="Username">Login used for every NVX in the room.</param>
/// <param name="Password">
/// Password for <paramref name="Username"/>. Read from the file but never written back out, so it is not sent to the front-end
/// with the rest of the room configuration.
/// </param>
/// <param name="ReceiverAddress">IP address or host name of the NVX receiver feeding the room's display. It is the only NVX the program controls.</param>
/// <param name="Sources">The multicast address of each source that arrives over NVX.</param>
public sealed record NvxConfig(
    string Username,
    string Password,
    string ReceiverAddress,
    IReadOnlyList<NvxSourceConfig> Sources)
{
    /// <summary>Tells Newtonsoft.Json never to serialise <see cref="Password"/>: the hub sends the room configuration to every client.</summary>
    public bool ShouldSerializePassword() => false;

    /// <summary>The route for <paramref name="sourceId"/>, or null if that source does not arrive over NVX.</summary>
    public NvxSourceConfig? FindSource(string? sourceId) =>
        (Sources ?? []).FirstOrDefault(source => source.SourceId == sourceId);
}

/// <summary>Where one source's NVX stream comes from. The transmitter is not controlled: it streams to this address on its own.</summary>
/// <param name="SourceId">Id of a <c>SourceConfig</c> in the room configuration.</param>
/// <param name="MulticastAddress">The IPv4 multicast address (224.0.0.0 to 239.255.255.255) the source's transmitter streams to. The receiver is pointed at it.</param>
public sealed record NvxSourceConfig(string SourceId, string MulticastAddress);
