using P709.Core.Configuration;
using static P709.Core.Tests.Configuration.RoomConfigTestData;

namespace P709.Core.Tests.Configuration;

public class RoomConfigValidatorTests
{
    private static void AssertInvalid(RoomConfiguration config, string expectedFragment)
    {
        var exception = Assert.Throws<InvalidConfigurationException>(() => RoomConfigValidator.Validate(config));

        Assert.True(exception.Message.Contains(expectedFragment), $"Expected the message to mention '{expectedFragment}', was: {exception.Message}");
    }

    [Fact(DisplayName = "TEST1: Accept a complete, consistent config.")]
    public void Validate_WithValidConfig_DoesNotThrow()
    {
        RoomConfigValidator.Validate(CreateConfig());
    }

    [Fact(DisplayName = "TEST2: Reject a config version below 1.")]
    public void Validate_WithZeroVersion_Throws()
    {
        AssertInvalid(CreateConfig(configVersion: 0), "configurationVersion");
    }

    [Fact(DisplayName = "TEST3: Reject a blank room name.")]
    public void Validate_WithBlankRoomName_Throws()
    {
        AssertInvalid(CreateConfig(roomName: " "), "roomName");
    }

    [Fact(DisplayName = "TEST4: Reject a room image that is not SVG.")]
    public void Validate_WithNonSvgRoomImage_Throws()
    {
        AssertInvalid(CreateConfig(roomSvg: "<png/>"), "roomSvg");
    }

    [Fact(DisplayName = "TEST5: Reject duplicate source ids.")]
    public void Validate_WithDuplicateSourceIds_Throws()
    {
        AssertInvalid(CreateConfig(sources: [CreateSource("a"), CreateSource("a")]), "sources has more than one entry with id \"a\"");
    }

    [Fact(DisplayName = "TEST6: Reject duplicate light ids.")]
    public void Validate_WithDuplicateLightIds_Throws()
    {
        AssertInvalid(CreateConfig(lights: [CreateLight("a"), CreateLight("a")], lightingPresets: []), "lights has more than one entry with id \"a\"");
    }

    [Fact(DisplayName = "TEST7: Reject a blank id.")]
    public void Validate_WithBlankPresetId_Throws()
    {
        AssertInvalid(CreateConfig(lightingPresets: [CreateLightingPreset(id: "")]), "lightingPresets needs an id");
    }

    [Fact(DisplayName = "TEST8: Reject a source or light icon that is not SVG.")]
    public void Validate_WithNonSvgIcon_Throws()
    {
        AssertInvalid(CreateConfig(sources: [CreateSource(iconSvg: "x")]), "source \"tv\" iconSvg");
        AssertInvalid(CreateConfig(lights: [CreateLight(iconSvg: "x")], lightingPresets: []), "light \"front\" iconSvg");
    }

    [Fact(DisplayName = "TEST13: Accept a source with no icon, because the icon is optional.")]
    public void Validate_WithSourceWithoutIcon_DoesNotThrow()
    {
        RoomConfigValidator.Validate(CreateConfig(sources: [CreateSource(iconSvg: null)]));
    }

    [Theory(DisplayName = "TEST9: Reject a light positioned outside the room image.")]
    [InlineData(-1, 50)]
    [InlineData(101, 50)]
    [InlineData(50, -1)]
    [InlineData(50, 101)]
    public void Validate_WithLightOutsideImage_Throws(double x, double y)
    {
        AssertInvalid(CreateConfig(lights: [CreateLight(x: x, y: y)], lightingPresets: []), "position");
    }

    [Fact(DisplayName = "TEST14: Reject a light with no positions.")]
    public void Validate_WithNoLightPositions_Throws()
    {
        AssertInvalid(CreateConfig(lights: [new LightConfig("front", "Front", LightType.Dimmer, Svg, [])], lightingPresets: []), "needs at least one position");
    }

    [Fact(DisplayName = "TEST10: Reject a lighting preset that refers to an unknown light.")]
    public void Validate_WithPresetForUnknownLight_Throws()
    {
        AssertInvalid(CreateConfig(lightingPresets: [CreateLightingPreset(lightId: "nope")]), "unknown light \"nope\"");
    }

    [Theory(DisplayName = "TEST11: Reject a lighting preset level outside 0-100.")]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_WithPresetLevelOutOfRange_Throws(int level)
    {
        AssertInvalid(CreateConfig(lightingPresets: [CreateLightingPreset(levelPercent: level)]), "between 0 and 100");
    }

    [Fact(DisplayName = "TEST15: Reject duplicate shade ids.")]
    public void Validate_WithDuplicateShadeIds_Throws()
    {
        AssertInvalid(CreateConfig(shades: [CreateShade(), CreateShade()]), "shades has more than one entry with id \"window\"");
    }

    [Fact(DisplayName = "TEST17: Accept a room with no shades and no MCP hub path, because both are optional.")]
    public void Validate_WithNoShadesOrMcpHubPath_DoesNotThrow()
    {
        RoomConfigValidator.Validate(CreateConfig(shades: [], mcpHubPath: null));
    }

    [Theory(DisplayName = "TEST18: Reject an MCP hub path that is not a path, or that clashes with the room hub.")]
    [InlineData("hubs/mcp", "starting with /")]
    [InlineData("/hubs/ mcp", "starting with /")]
    [InlineData("/hubs/room", "must differ")]
    public void Validate_WithBadMcpHubPath_Throws(string mcpHubPath, string expectedFragment)
    {
        AssertInvalid(CreateConfig(mcpHubPath: mcpHubPath), expectedFragment);
    }

    [Fact(DisplayName = "TEST12: Report every problem at once, not just the first.")]
    public void Validate_WithSeveralProblems_ReportsAll()
    {
        var config = CreateConfig(configVersion: 0, roomName: "", lightingPresets: [CreateLightingPreset(lightId: "nope")]);

        var exception = Assert.Throws<InvalidConfigurationException>(() => RoomConfigValidator.Validate(config));

        Assert.True(exception.Message.Contains("configurationVersion") && exception.Message.Contains("roomName") && exception.Message.Contains("nope"), $"Not all problems were reported: {exception.Message}");
    }

    [Fact(DisplayName = "TEST19: Accept a config with no shade presets, because they are optional.")]
    public void Validate_WithNoShadePresets_DoesNotThrow()
    {
        RoomConfigValidator.Validate(CreateConfig(shadePresets: []));
    }

    [Fact(DisplayName = "TEST20: Reject duplicate shade preset ids.")]
    public void Validate_WithDuplicateShadePresetIds_Throws()
    {
        AssertInvalid(CreateConfig(shadePresets: [CreateShadePreset("meeting"), CreateShadePreset("meeting")]), "shadePresets has more than one entry with id \"meeting\"");
    }

    [Fact(DisplayName = "TEST21: Reject a shade preset with a blank id.")]
    public void Validate_WithBlankShadePresetId_Throws()
    {
        AssertInvalid(CreateConfig(shadePresets: [CreateShadePreset(id: "")]), "shadePresets needs an id");
    }

    [Fact(DisplayName = "TEST22: Reject a shade preset target that refers to an unknown shade.")]
    public void Validate_WithShadePresetForUnknownShade_Throws()
    {
        AssertInvalid(CreateConfig(shadePresets: [CreateShadePreset(shadeId: "nope")]), "unknown shade \"nope\"");
    }

    [Theory(DisplayName = "TEST23: Reject a shade preset position outside 0-100.")]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_WithShadePresetPositionOutOfRange_Throws(int positionPercent)
    {
        AssertInvalid(CreateConfig(shadePresets: [CreateShadePreset(positionPercent: positionPercent)]), "between 0 and 100");
    }

    [Fact(DisplayName = "TEST24: Reject a shade preset tilt for a shade that does not support tilt.")]
    public void Validate_WithShadePresetTiltForUnsupportedShade_Throws()
    {
        AssertInvalid(CreateConfig(shades: [CreateShade(supportsTilt: false)], shadePresets: [CreateShadePreset(tiltPercent: 40)]), "does not support tilt");
    }

    [Fact(DisplayName = "TEST25: Accept a shade preset tilt for a shade that supports tilt.")]
    public void Validate_WithShadePresetTiltForSupportedShade_DoesNotThrow()
    {
        RoomConfigValidator.Validate(CreateConfig(shades: [CreateShade(supportsTilt: true)], shadePresets: [CreateShadePreset(tiltPercent: 40)]));
    }

    [Theory(DisplayName = "TEST26: Reject a shade preset tilt outside 0-100.")]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_WithShadePresetTiltOutOfRange_Throws(int tiltPercent)
    {
        AssertInvalid(CreateConfig(shades: [CreateShade(supportsTilt: true)], shadePresets: [CreateShadePreset(tiltPercent: tiltPercent)]), "tilt");
    }

    [Fact(DisplayName = "TEST27: Accept a config with no concierge, because it is optional.")]
    public void Validate_WithNoConcierge_DoesNotThrow()
    {
        RoomConfigValidator.Validate(CreateConfig(concierge: null));
    }

    [Fact(DisplayName = "TEST28: Accept a config with a valid concierge.")]
    public void Validate_WithValidConcierge_DoesNotThrow()
    {
        RoomConfigValidator.Validate(CreateConfig(concierge: CreateConcierge()));
    }

    [Theory(DisplayName = "TEST29: Reject a concierge with a blank or non-absolute baseUrl.")]
    [InlineData("")]
    [InlineData("not-a-url")]
    public void Validate_WithBadConciergeBaseUrl_Throws(string baseUrl)
    {
        AssertInvalid(CreateConfig(concierge: CreateConcierge(baseUrl: baseUrl)), "concierge baseUrl");
    }

    [Fact(DisplayName = "TEST30: Reject a concierge with a blank roomId.")]
    public void Validate_WithBlankConciergeRoomId_Throws()
    {
        AssertInvalid(CreateConfig(concierge: CreateConcierge(roomId: "")), "concierge roomId");
    }

    [Fact(DisplayName = "TEST31: Reject a concierge with a blank token.")]
    public void Validate_WithBlankConciergeToken_Throws()
    {
        AssertInvalid(CreateConfig(concierge: CreateConcierge(token: "")), "concierge token");
    }
}
