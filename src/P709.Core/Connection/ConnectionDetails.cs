using Newtonsoft.Json;

namespace P709.Core.Connection;

/// <summary>
/// Everything the front-end needs to open its own SignalR connection to the back-end.
/// This is the payload delivered to the touch panel on <see cref="PanelJoins.ConnectionDetailsSerial"/>.
/// </summary>
/// <param name="HubUrl">Absolute URL of the SignalR hub, reachable from the panel's browser.</param>
public sealed record ConnectionDetails([property: JsonProperty("hubUrl")] string HubUrl)
{
    /// <summary>Path the SignalR hub is mapped to. The web host maps the hub here, so the published URL always matches.</summary>
    public const string HubPath = "/hubs/room";

    private static readonly JsonSerializerSettings _settings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None
    };

    /// <summary>Builds the details for a host and port, or returns null when no host address is known yet.</summary>
    public static ConnectionDetails? For(string? host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        return new ConnectionDetails($"http://{host}:{port}{HubPath}");
    }

    /// <summary>Serialises these details to the JSON string sent over the serial join.</summary>
    public string ToJson() => JsonConvert.SerializeObject(this, _settings);

    /// <summary>Parses the JSON produced by <see cref="ToJson"/>.</summary>
    public static ConnectionDetails FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonConvert.DeserializeObject<ConnectionDetails>(json, _settings)
            ?? throw new JsonSerializationException("Connection details JSON was empty.");
    }
}
