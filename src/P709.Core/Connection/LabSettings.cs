using Newtonsoft.Json;
using P709.Core.Configuration;

namespace P709.Core.Connection;

/// <summary>
/// Settings that differ per deployment and so must never be hard-coded in the program.
/// Read from <see cref="FileName"/> the same way as the room configuration: a missing or invalid file stops the program at start-up.
/// </summary>
public sealed class LabSettings
{
    /// <summary>Name of the settings file.</summary>
    public const string FileName = "P709.settings.json";

    /// <summary>Port used when the settings file does not set <c>apiPort</c>.</summary>
    /// <remarks>ASP.Net uses 5000 as the default, but that is in use by VC-4, so we changed it</remarks>
    public const int DefaultApiPort = 6001;

    /// <summary>
    /// TCP port the web host listens on, and that is published to the panel. Every program sharing a host needs its own:
    /// several VC-4 rooms on one server cannot all use the default. Remember to open the port on the host firewall.
    /// </summary>
    [JsonProperty("apiPort")]
    public int ApiPort { get; init; } = DefaultApiPort;

    /// <summary>MCP Gateway registration and the troubleshooting API key. Null (no <c>mcp</c> section) leaves MCP off.</summary>
    [JsonProperty("mcp")]
    public McpSettings? Mcp { get; init; }

    /// <summary>Loads and validates <see cref="FileName"/> from <paramref name="folder"/>.</summary>
    /// <param name="folder">Folder holding the file.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidConfigurationException">The file is empty, malformed, names an unknown setting (usually a typo) or has a setting out of range.</exception>
    public static LabSettings Load(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var settings = JsonConfigFile.Load<LabSettings>(Path.Combine(folder, FileName));
        LabSettingsValidator.Validate(settings);

        return settings;
    }
}
