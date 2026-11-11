using P709.Core.Configuration;

namespace P709.Core.Tests.Configuration;

/// <summary>Checks the P709.roomconfig.json that ships with the program, so a bad edit is caught by the tests and not on a processor.</summary>
public class ShippedRoomConfigTests
{
    private readonly RoomConfiguration _config = RoomConfiguration.Load(AppContext.BaseDirectory);

    [Fact(DisplayName = "TEST1: The shipped file loads and passes validation.")]
    public void ShippedFile_Loads_IsValid()
    {
        Assert.True(!string.IsNullOrWhiteSpace(_config.RoomName), "The shipped room needs a name.");
    }

    [Fact(DisplayName = "TEST2: The shipped file has sources, lights and presets.")]
    public void ShippedFile_HasContent()
    {
        Assert.True(_config.Sources.Count > 0, "Expected sources.");
        Assert.True(_config.Lights.Count > 0, "Expected lights.");
        Assert.True(_config.LightingPresets.Count > 0, "Expected presets.");
    }

    [Fact(DisplayName = "TEST4: Only the Signage source carries its own icon; the rest use the front-end's.")]
    public void ShippedFile_SourceIcons_AreAsDesigned()
    {
        var withIcon = _config.Sources.Where(source => source.IconSvg is not null).Select(source => source.Id).OrderBy(id => id);

        Assert.True(new[] { "5" }.SequenceEqual(withIcon), $"Unexpected sources with icons: {string.Join(", ", withIcon)}.");
    }

    [Fact(DisplayName = "TEST3: The shipped file's source types are all valid, defined values.")]
    public void ShippedFile_SourceTypes_AreValid()
    {
        var validTypes = Enum.GetValues<SourceType>().ToHashSet();

        Assert.True(_config.Sources.All(source => validTypes.Contains(source.Type)), "Every shipped source type should be a defined SourceType value.");
    }

    [Fact(DisplayName = "TEST5: The shipped file's light types are all valid, defined values.")]
    public void ShippedFile_LightTypes_AreValid()
    {
        var validTypes = Enum.GetValues<LightType>().ToHashSet();

        Assert.True(_config.Lights.All(light => validTypes.Contains(light.Type)), "Every shipped light type should be a defined LightType value.");
    }

    [Fact(DisplayName = "TEST6: Every shipped light has at least one position, within the room image.")]
    public void ShippedFile_LightPositions_AreValid()
    {
        Assert.True(_config.Lights.All(light => light.Positions.Count > 0), "Every shipped light should have at least one position.");
        Assert.True(_config.Lights.SelectMany(light => light.Positions).All(position => position.XPercent is >= 0 and <= 100 && position.YPercent is >= 0 and <= 100), "Every shipped light position should be within 0-100.");
    }

    [Fact(DisplayName = "TEST8: The shipped file has the three example shades, each with a defined type.")]
    public void ShippedFile_Shades_AreAsDesigned()
    {
        var validTypes = Enum.GetValues<ShadeType>().ToHashSet();

        Assert.True(3 == _config.Shades.Count, $"Expected 3 shades, found {_config.Shades.Count}.");
        Assert.True(_config.Shades.All(shade => validTypes.Contains(shade.Type)), "Every shipped shade type should be a defined ShadeType value.");
    }

    [Fact(DisplayName = "TEST9: The shipped file reserves /hubs/mcp for the MCP hub.")]
    public void ShippedFile_McpHubPath_IsSet()
    {
        Assert.True("/hubs/mcp" == _config.McpHubPath, $"mcpHubPath was {_config.McpHubPath ?? "absent"}.");
    }

    [Fact(DisplayName = "TEST10: The shipped file has a valid concierge configuration.")]
    public void ShippedFile_Concierge_IsAsDesigned()
    {
        Assert.True(_config.Concierge is not null, "Expected a concierge configuration.");
        Assert.True("http://172.16.17.15:6002" == _config.Concierge!.BaseUrl, $"concierge baseUrl was {_config.Concierge.BaseUrl}.");
        Assert.True("masters-2026-p709" == _config.Concierge.RoomId, $"concierge roomId was {_config.Concierge.RoomId}.");
        Assert.True("demo-concierge-token-2026" == _config.Concierge.Token, $"concierge token was {_config.Concierge.Token}.");
    }

    [Fact(DisplayName = "TEST11: The shipped file routes the two HDMI laptops over NVX.")]
    public void ShippedFile_HasNvxRoutesForLaptops()
    {
        Assert.True(_config.Nvx is not null, "Expected an nvx section.");
        var routed = _config.Nvx!.Sources.Select(source => source.SourceId).OrderBy(id => id);

        Assert.True(new[] { "2", "3" }.SequenceEqual(routed), $"Unexpected NVX sources: {string.Join(", ", routed)}.");
    }

    [Fact(DisplayName = "TEST7: Every preset refers only to lights that still exist.")]
    public void ShippedFile_PresetLevels_ReferenceExistingLights()
    {
        var lightIds = _config.Lights.Select(light => light.Id).ToHashSet();

        Assert.True(_config.LightingPresets.SelectMany(preset => preset.Levels).All(level => lightIds.Contains(level.LightId)), "Every preset level should refer to a light that exists.");
    }
}
