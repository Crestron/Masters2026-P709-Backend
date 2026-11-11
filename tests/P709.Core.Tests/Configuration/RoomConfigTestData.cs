using P709.Core.Configuration;

namespace P709.Core.Tests.Configuration;

internal static class RoomConfigTestData
{
    // Base64 for "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"/>", since configs now carry base64-encoded SVG.
    public const string Svg = "PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAxMCAxMCIvPg==";

    public static RoomConfiguration CreateConfig(
        int configVersion = 1,
        string roomName = "Test Room",
        string roomSvg = Svg,
        IReadOnlyList<SourceConfig>? sources = null,
        IReadOnlyList<LightConfig>? lights = null,
        IReadOnlyList<LightingPresetConfig>? lightingPresets = null,
        IReadOnlyList<ShadeConfig>? shades = null,
        string? mcpHubPath = "/hubs/mcp",
        IReadOnlyList<ShadePresetConfig>? shadePresets = null,
        ConciergeConfig? concierge = null) =>
        new(
            configVersion,
            roomName,
            roomSvg,
            sources ?? [new SourceConfig("tv", "TV", SourceType.Tv, Svg)],
            lights ?? [new LightConfig("front", "Front", LightType.Dimmer, Svg, [new PositionConfig(25, 30)])],
            lightingPresets ?? [new LightingPresetConfig("on", "On", [new LightLevelConfig("front", 100)])],
            shades ?? [new ShadeConfig("window", "Window", ShadeType.Roller)],
            mcpHubPath,
            shadePresets,
            concierge);

    public static ConciergeConfig CreateConcierge(
        string baseUrl = "http://192.168.200.115:5002",
        string roomId = "meeting-room-1",
        string token = "demo-concierge-token-2026") =>
        new(baseUrl, roomId, token);

    public static ShadeConfig CreateShade(string id = "window", bool supportsTilt = false) =>
        new(id, id, ShadeType.Roller, supportsTilt);

    public static ShadePresetConfig CreateShadePreset(string id = "meeting", string shadeId = "window", int positionPercent = 50, int? tiltPercent = null) =>
        new(id, id, [new ShadePresetTargetConfig(shadeId, positionPercent, tiltPercent)]);

    public static LightConfig CreateLight(string id = "front", double x = 25, double y = 30, string iconSvg = Svg, LightType type = LightType.Dimmer) =>
        new(id, id, type, iconSvg, [new PositionConfig(x, y)]);

    public static LightingPresetConfig CreateLightingPreset(string id = "on", string lightId = "front", int levelPercent = 100) =>
        new(id, id, [new LightLevelConfig(lightId, levelPercent)]);

    public static SourceConfig CreateSource(string id = "tv", string? iconSvg = Svg) =>
        new(id, id, SourceType.Tv, iconSvg);
}
