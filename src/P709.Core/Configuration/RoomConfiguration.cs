using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using P709.Core.Nvx;

namespace P709.Core.Configuration;

/// <summary>
/// Configuration handed to the front-end when it connects. It is deliberately a rich, nested object (lists, nested objects,
/// enums, SVG markup) so the labs can prove the whole shape survives the trip to Svelte.
/// The values supplied today are placeholders; the shape is what the front-end builds against.
/// Loaded once at start-up by <see cref="Load"/>; change the room by editing the file and restarting the program.
/// </summary>
/// <param name="ConfigurationVersion">Increase when the shape changes, so the front-end can detect a mismatch.</param>
/// <param name="RoomName">Display name of the room.</param>
/// <param name="RoomSvg">Complete inline SVG markup for the room itself: the base image the lights are placed over.</param>
/// <param name="Sources">Selectable sources, in display order.</param>
/// <param name="Lights">Lights, placed over the room image by their <see cref="LightConfig.Positions"/>.</param>
/// <param name="LightingPresets">Lighting presets: named sets of light levels.</param>
/// <param name="Shades">Shades (blinds and curtains), in display order.</param>
/// <param name="ShadePresets">Shade presets: named sets of shade positions (and optional tilts) recalled together.</param>
/// <param name="McpHubPath">
/// Optional URL path of the MCP hub, for example <c>/hubs/mcp</c>.
/// </param>
/// <param name="Concierge">Optional configuration for the concierge service integration.</param>
/// <param name="Nvx">Optional DM-NVX configuration: the receiver, its login and the multicast stream for each source that arrives over NVX.</param>
public sealed record RoomConfiguration(
    int ConfigurationVersion,
    string RoomName,
    string RoomSvg,
    IReadOnlyList<SourceConfig> Sources,
    IReadOnlyList<LightConfig> Lights,
    IReadOnlyList<LightingPresetConfig> LightingPresets,
    IReadOnlyList<ShadeConfig> Shades,
    string? McpHubPath = null,
    IReadOnlyList<ShadePresetConfig>? ShadePresets = null,
    ConciergeConfig? Concierge = null,
    NvxConfig? Nvx = null)
{
    /// <summary>Name of the room configuration file.</summary>
    public const string FileName = "P709.roomconfig.json";

    /// <summary>Loads and validates <see cref="FileName"/> from <paramref name="folder"/>.</summary>
    /// <param name="folder">Folder holding the file.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidConfigurationException">The file is malformed or inconsistent.</exception>
    public static RoomConfiguration Load(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        // TODO: Lab 0.1 - Load the room configuration JSON file and validate it
        // Hint: The configuration file definitions include a staic helper method to load the file,
        //       This method will call the generic JSON config file loader, specifiying the record type and where to find the file.
        //       The returned configuration should then be validated using the static validator class.
        // Ref:  The implementation of LabSettings.Load follows a similar pattern.
        // Add your work below this line

        // Lab 0.1 - End

        // REF: Lab 0.1 - Load the room configuration JSON file and validate it
        var config = JsonConfigFile.Load<RoomConfiguration>(Path.Combine(folder, FileName));
        RoomConfigValidator.Validate(config);

        return config;
        // REF: Lab 0.1 - End
    }
}

/// <summary>A selectable source.</summary>
/// <param name="Id">Stable identifier used in commands.</param>
/// <param name="Name">Display name.</param>
/// <param name="Type">What kind of source this is.</param>
/// <param name="IconSvg">Optional complete inline SVG markup for the source's icon. When absent (null) the front-end uses its own icon for the <paramref name="Type"/>.</param>
public sealed record SourceConfig(
    string Id,
    string Name,
    [property: JsonConverter(typeof(StringEnumConverter))] SourceType Type,
    string? IconSvg = null);

/// <summary>
/// What kind of source this is. The names on the wire are the <c>EnumMember</c> values (for example "laptopUsbC"),
/// which the front-end switches on, so changing one is a breaking change: bump <see cref="RoomConfiguration.ConfigurationVersion"/>.
/// </summary>
public enum SourceType
{
    /// <summary>A television.</summary>
    [EnumMember(Value = "tv")]
    Tv,

    /// <summary>An overhead projector.</summary>
    [EnumMember(Value = "ohp")]
    Ohp,

    /// <summary>A wireless presentation device.</summary>
    [EnumMember(Value = "wirelessPresenter")]
    WirelessPresenter,

    /// <summary>A laptop connected by HDMI.</summary>
    [EnumMember(Value = "laptopHdmi")]
    LaptopHdmi,

    /// <summary>A device connected by USB-C.</summary>
    [EnumMember(Value = "laptopUsbC")]
    LaptopUsbC,

    /// <summary>Anything else.</summary>
    [EnumMember(Value = "other")]
    Other
}

/// <summary>
/// Current state of the room's overall power sequence. The names on the wire are the <c>EnumMember</c> values.
/// </summary>
public enum SystemPowerState
{
    /// <summary>The system is fully off.</summary>
    [EnumMember(Value = "off")]
    Off,

    /// <summary>The system is powering on.</summary>
    [EnumMember(Value = "starting")]
    Starting,

    /// <summary>The system is fully on.</summary>
    [EnumMember(Value = "on")]
    On,

    /// <summary>The system is powering off.</summary>
    [EnumMember(Value = "stopping")]
    Stopping
}

/// <summary>
/// Identifies which page of the UI clients should be synchronized to. The names on the wire are the <c>EnumMember</c> values.
/// </summary>
public enum PageKey
{
    /// <summary>The home page.</summary>
    [EnumMember(Value = "home")]
    Home,

    /// <summary>The lights page.</summary>
    [EnumMember(Value = "lights")]
    Lights,

    /// <summary>The shades page.</summary>
    [EnumMember(Value = "shades")]
    Shades,

    /// <summary>The sources page.</summary>
    [EnumMember(Value = "sources")]
    Sources,

    /// <summary>The support page.</summary>
    [EnumMember(Value = "support")]
    Support
}

/// <summary>A light the panel can control.</summary>
/// <param name="Id">Stable identifier used in commands and presets.</param>
/// <param name="Name">Display name.</param>
/// <param name="Type">What kind of light this is.</param>
/// <param name="IconSvg">Complete inline SVG markup for the light's icon.</param>
/// <param name="Positions">Where the light's control sits over the room image. More than one position draws the same light in multiple spots (for example a run of downlights).</param>
public sealed record LightConfig(
    string Id,
    string Name,
    [property: JsonConverter(typeof(StringEnumConverter))] LightType Type,
    string IconSvg,
    IReadOnlyList<PositionConfig> Positions);

/// <summary>
/// What kind of light this is. The names on the wire are the <c>EnumMember</c> values (for example <c>dimmer</c>),
/// which the front-end switches on, so changing one is a breaking change: bump <see cref="RoomConfiguration.ConfigurationVersion"/>.
/// </summary>
public enum LightType
{
    /// <summary>A light whose level can be set anywhere between 0 and 100.</summary>
    [EnumMember(Value = "dimmer")]
    Dimmer,

    /// <summary>A light that is only ever fully on or fully off.</summary>
    [EnumMember(Value = "switch")]
    Switch
}

/// <summary>A position over the room image, as a percentage of its width (X, from the left) and height (Y, from the top), so it suits any rendered size.</summary>
/// <param name="XPercent">Percent across, 0 to 100.</param>
/// <param name="YPercent">Percent down, 0 to 100.</param>
public sealed record PositionConfig(double XPercent, double YPercent);

/// <summary>A shade (blind or curtain) the panel can control. Its position is a percentage: 0 is fully closed, 100 is fully open.</summary>
/// <param name="Id">Stable identifier used in commands.</param>
/// <param name="Name">Display name.</param>
/// <param name="Type">What kind of shade this is.</param>
/// <param name="SupportsTilt">Whether this shade's slats can be tilted independently of its position. Defaults to false.</param>
public sealed record ShadeConfig(
    string Id,
    string Name,
    [property: JsonConverter(typeof(StringEnumConverter))] ShadeType Type,
    bool SupportsTilt = false);

/// <summary>
/// What kind of shade this is. The names on the wire are the <c>EnumMember</c> values (for example <c>roller</c>),
/// which the front-end switches on, so changing one is a breaking change: bump <see cref="RoomConfiguration.ConfigurationVersion"/>.
/// </summary>
public enum ShadeType
{
    /// <summary>A roller blind.</summary>
    [EnumMember(Value = "roller")]
    Roller,

    /// <summary>A curtain or drape that draws sideways.</summary>
    [EnumMember(Value = "curtain")]
    Curtain,

    /// <summary>A venetian blind.</summary>
    [EnumMember(Value = "venetian")]
    Venetian
}

/// <summary>A lighting preset: a named set of light levels recalled together.</summary>
/// <param name="Id">Stable identifier used in commands.</param>
/// <param name="Name">Display name.</param>
/// <param name="Levels">The level each light in the preset is set to.</param>
public sealed record LightingPresetConfig(string Id, string Name, IReadOnlyList<LightLevelConfig> Levels);

/// <summary>The level one light is set to by a preset.</summary>
/// <param name="LightId">Id of a <see cref="LightConfig"/>.</param>
/// <param name="LevelPercent">Level, 0 to 100.</param>
public sealed record LightLevelConfig(string LightId, int LevelPercent);

/// <summary>A shade preset: a named set of shade positions (and optional tilts) recalled together.</summary>
/// <param name="Id">Stable identifier used in commands.</param>
/// <param name="Name">Display name.</param>
/// <param name="Targets">The position (and optional tilt) each shade in the preset is set to.</param>
public sealed record ShadePresetConfig(string Id, string Name, IReadOnlyList<ShadePresetTargetConfig> Targets);

/// <summary>The position (and optional tilt) one shade is set to by a shade preset.</summary>
/// <param name="ShadeId">Id of a <see cref="ShadeConfig"/>.</param>
/// <param name="PositionPercent">Position, 0 (closed) to 100 (open).</param>
/// <param name="TiltPercent">Optional tilt, 0 to 100. Only valid for a shade with <see cref="ShadeConfig.SupportsTilt"/>.</param>
public sealed record ShadePresetTargetConfig(string ShadeId, int PositionPercent, int? TiltPercent = null);

/// <summary>Configuration for connecting to the concierge service.</summary>
/// <param name="BaseUrl">Base URL of the concierge service, for example <c>http://192.168.200.115:5002</c>.</param>
/// <param name="RoomId">Identifier of this room as known to the concierge service.</param>
/// <param name="Token">Token used to authenticate with the concierge service.</param>
public sealed record ConciergeConfig(string BaseUrl, string RoomId, string Token);
