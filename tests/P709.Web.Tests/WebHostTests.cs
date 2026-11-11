using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using P709.Core.Configuration;
using P709.Web;
using P709.Web.Hubs;

namespace P709.Web.Tests;

/// <summary>Runs the real web host on a free local port, so the hub contract the front-end relies on is pinned down.</summary>
public class WebHostTests : IAsyncLifetime
{
    private WebApplication? _app;
    private readonly RoomConfiguration _roomConfiguration = RoomConfiguration.Load(AppContext.BaseDirectory);
    private string _baseUrl = string.Empty;

    private static string DecodeBase64(string? value) =>
        value is null ? "" : Encoding.UTF8.GetString(Convert.FromBase64String(value));

    public async ValueTask InitializeAsync()
    {
        _app = WebHost.Build(_roomConfiguration);
        _app.Urls.Clear();
        _app.Urls.Add("http://127.0.0.1:0");
        await _app.StartAsync();

        var server = _app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        _baseUrl = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    [Fact(DisplayName = "TEST1: Health endpoint reports healthy.")]
    public async Task Health_WhenHostStarted_ReturnsOk()
    {
        using var client = new HttpClient();

        var response = await client.GetAsync($"{_baseUrl}/health", TestContext.Current.CancellationToken);

        Assert.True(HttpStatusCode.OK == response.StatusCode, $"Expected 200, got {(int)response.StatusCode}.");
    }

    [Fact(DisplayName = "TEST2: A SignalR client can connect to the hub at the published path.")]
    public async Task RoomHub_WithSignalRClient_Connects()
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl($"{_baseUrl}{RoomHub.Path}", HttpTransportType.WebSockets)
            .Build();

        await connection.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(HubConnectionState.Connected == connection.State, $"Expected Connected, was {connection.State}.");
    }

    [Fact(DisplayName = "TEST3: CORS allows the panel's origin to negotiate a connection.")]
    public async Task RoomHub_NegotiateFromForeignOrigin_AllowsOrigin()
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{RoomHub.Path}/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", "http://panel.example.com");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"), "Missing CORS allow-origin header.");
    }

    [Fact(DisplayName = "TEST4: GetRoomConfiguration returns the rich configuration object.")]
    public async Task GetConfig_WithConnectedClient_ReturnsConfigFromFile()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        var actual = await connection.InvokeAsync<RoomConfiguration>("GetRoomConfiguration", TestContext.Current.CancellationToken);

        var expected = _roomConfiguration;
        Assert.True(expected.RoomName == actual.RoomName, $"Room name was {actual.RoomName}.");
        Assert.True(expected.RoomSvg == actual.RoomSvg, "The room SVG did not survive the round trip.");
        Assert.True(expected.Sources.SequenceEqual(actual.Sources), "The sources did not survive the round trip.");
        Assert.True(expected.Lights.Count == actual.Lights.Count, $"Expected {expected.Lights.Count} lights, got {actual.Lights.Count}.");
        Assert.True(expected.Lights.Zip(actual.Lights).All(pair =>
            pair.First.Id == pair.Second.Id &&
            pair.First.Name == pair.Second.Name &&
            pair.First.Type == pair.Second.Type &&
            pair.First.IconSvg == pair.Second.IconSvg &&
            pair.First.Positions.SequenceEqual(pair.Second.Positions)), "The lights (including SVG and positions) did not survive the round trip.");
        Assert.True(expected.LightingPresets.Count == actual.LightingPresets.Count, $"Expected {expected.LightingPresets.Count} presets, got {actual.LightingPresets.Count}.");
        Assert.True(expected.LightingPresets[0].Levels.SequenceEqual(actual.LightingPresets[0].Levels), "The nested preset levels did not survive the round trip.");
        Assert.True(expected.Shades.SequenceEqual(actual.Shades), "The shades did not survive the round trip.");
        Assert.True(expected.McpHubPath == actual.McpHubPath, $"mcpHubPath was {actual.McpHubPath}.");
    }

    [Fact(DisplayName = "TEST5: GetRoomConfiguration is sent to the client as camelCase JSON with enums as strings.")]
    public async Task GetConfig_OnTheWire_UsesCamelCaseAndStringEnums()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        var actual = await connection.InvokeAsync<JObject>("GetRoomConfiguration", TestContext.Current.CancellationToken);

        Assert.True("Masters P709 - Conference Room" == actual.Value<string>("roomName"), "Expected a camelCase roomName property.");
        Assert.True(DecodeBase64(actual.Value<string>("roomSvg")).StartsWith("<svg"), "Expected roomSvg to be base64-encoded SVG markup.");
        Assert.True(actual.ContainsKey("configurationVersion"), "Expected a camelCase configurationVersion property.");
        var types = actual["sources"]!.Select(source => source.Value<string>("type")).ToList();
        var expectedTypes = new[] { "wirelessPresenter", "laptopHdmi", "laptopHdmi", "other", "other" };
        Assert.True(expectedTypes.SequenceEqual(types!), $"Expected the source type names on the wire, got: {string.Join(", ", types)}.");
        var sourcesById = actual["sources"]!.ToDictionary(source => source.Value<string>("id")!);
        Assert.True(DecodeBase64(sourcesById["5"].Value<string>("iconSvg")).StartsWith("<svg"), "Expected iconSvg to be base64-encoded SVG markup on a source that has an icon.");
        Assert.True(sourcesById["1"].Value<string>("iconSvg") is null, "Expected no iconSvg on a source without one.");
        Assert.True(actual["lights"]?[0]?["positions"]?[0]?.Value<double?>("xPercent") is not null, "Expected a nested light position with a camelCase xPercent.");
        Assert.True(actual["lightingPresets"]?[0]?["levels"]?[0]?.Value<int?>("levelPercent") is not null, "Expected nested preset levels with levelPercent.");
        var shadeTypes = actual["shades"]!.Select(shade => shade.Value<string>("type")).ToList();
        Assert.True(new[] { "roller", "curtain", "venetian" }.SequenceEqual(shadeTypes!), $"Expected the shade type names on the wire, got: {string.Join(", ", shadeTypes)}.");
        Assert.True("/hubs/mcp" == actual.Value<string>("mcpHubPath"), "Expected a camelCase mcpHubPath property.");
    }

    [Fact(DisplayName = "TEST6: SetLightLevel broadcasts LightLevelChanged to all clients.")]
    public async Task SetLightLevel_WithConnectedClients_BroadcastsToAll()
    {
        await using var sender = CreateConnection();
        await using var other = CreateConnection();

        var received = new TaskCompletionSource<(string LightId, int LevelPercent)>();
        other.On<string, int>("LightLevelChanged", (lightId, levelPercent) => received.TrySetResult((lightId, levelPercent)));

        await sender.StartAsync(TestContext.Current.CancellationToken);
        await other.StartAsync(TestContext.Current.CancellationToken);

        await sender.InvokeAsync("SetLightLevel", "1", 42, TestContext.Current.CancellationToken);

        var (lightId, levelPercent) = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True("1" == lightId && 42 == levelPercent, $"Expected (1, 42), got ({lightId}, {levelPercent}).");
    }

    [Fact(DisplayName = "TEST7: SetSystemPower broadcasts SystemPowerStateChanged, including to the sender.")]
    public async Task SetSystemPower_WithConnectedClient_BroadcastsToSender()
    {
        await using var connection = CreateConnection();

        var received = new TaskCompletionSource<SystemPowerState>();
        connection.On<SystemPowerState>("SystemPowerStateChanged", state =>
        {
            if (state == SystemPowerState.On)
            {
                received.TrySetResult(state);
            }
        });

        await connection.StartAsync(TestContext.Current.CancellationToken);

        await connection.InvokeAsync("SetSystemPower", true, TestContext.Current.CancellationToken);

        var state = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(SystemPowerState.On == state, $"Expected On, got {state}.");
    }

    [Fact(DisplayName = "TEST8: A newly connected client is sent the current SystemPowerStateChanged.")]
    public async Task OnConnected_AfterPowerChange_ReceivesCurrentState()
    {
        await using var first = CreateConnection();
        await first.StartAsync(TestContext.Current.CancellationToken);
        await first.InvokeAsync("SetSystemPower", true, TestContext.Current.CancellationToken);

        await using var second = CreateConnection();
        var received = new TaskCompletionSource<SystemPowerState>();
        second.On<SystemPowerState>("SystemPowerStateChanged", state => received.TrySetResult(state));

        await second.StartAsync(TestContext.Current.CancellationToken);

        var state = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(SystemPowerState.On == state, $"Expected the newly connected client to learn On, got {state}.");
    }

    [Fact(DisplayName = "TEST9: SetCurrentPage sends CurrentPageChanged to other clients but not the sender.")]
    public async Task SetCurrentPage_WithConnectedClients_ExcludesSender()
    {
        await using var sender = CreateConnection();
        await using var other = CreateConnection();

        var otherReceived = new TaskCompletionSource<PageKey>();
        other.On<PageKey>("CurrentPageChanged", pageKey => otherReceived.TrySetResult(pageKey));

        var senderReceived = false;
        sender.On<PageKey>("CurrentPageChanged", _ => senderReceived = true);

        await sender.StartAsync(TestContext.Current.CancellationToken);
        await other.StartAsync(TestContext.Current.CancellationToken);

        await sender.InvokeAsync("SetCurrentPage", PageKey.Lights, TestContext.Current.CancellationToken);

        var pageKey = await otherReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(PageKey.Lights == pageKey, $"Expected Lights, got {pageKey}.");
        Assert.True(!senderReceived, "The sender should not receive its own CurrentPageChanged event.");
    }

    [Fact(DisplayName = "TEST10: SetVolumeLevel broadcasts VolumeLevelChanged to all clients, including the sender.")]
    public async Task SetVolumeLevel_WithConnectedClients_BroadcastsToAll()
    {
        await using var sender = CreateConnection();
        await using var other = CreateConnection();

        var senderReceived = new TaskCompletionSource<int>();
        var otherReceived = new TaskCompletionSource<int>();
        sender.On<int>("VolumeLevelChanged", levelPercent => senderReceived.TrySetResult(levelPercent));
        other.On<int>("VolumeLevelChanged", levelPercent => otherReceived.TrySetResult(levelPercent));

        await sender.StartAsync(TestContext.Current.CancellationToken);
        await other.StartAsync(TestContext.Current.CancellationToken);

        await sender.InvokeAsync("SetVolumeLevel", 42, TestContext.Current.CancellationToken);

        var senderLevel = await senderReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var otherLevel = await otherReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(42 == senderLevel, $"The sender should be told 42, was told {senderLevel}.");
        Assert.True(42 == otherLevel, $"The other client should be told 42, was told {otherLevel}.");
    }

    [Fact(DisplayName = "TEST11: SetVolumeLevel records the level in the room state.")]
    public async Task SetVolumeLevel_WithConnectedClient_UpdatesRoomState()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        await connection.InvokeAsync("SetVolumeLevel", 42, TestContext.Current.CancellationToken);

        var roomState = _app!.Services.GetRequiredService<RoomState>();
        Assert.True(42 == roomState.VolumeLevelPercent, $"Expected the room state to hold 42, held {roomState.VolumeLevelPercent}.");
    }

    [Fact(DisplayName = "TEST12: SetShadePosition broadcasts ShadePositionChanged to all clients, including the sender.")]
    public async Task SetShadePosition_WithConnectedClients_BroadcastsToAll()
    {
        await using var sender = CreateConnection();
        await using var other = CreateConnection();

        var senderReceived = new TaskCompletionSource<(string ShadeId, int PositionPercent)>();
        var otherReceived = new TaskCompletionSource<(string ShadeId, int PositionPercent)>();
        sender.On<string, int>("ShadePositionChanged", (shadeId, positionPercent) => senderReceived.TrySetResult((shadeId, positionPercent)));
        other.On<string, int>("ShadePositionChanged", (shadeId, positionPercent) => otherReceived.TrySetResult((shadeId, positionPercent)));

        await sender.StartAsync(TestContext.Current.CancellationToken);
        await other.StartAsync(TestContext.Current.CancellationToken);

        await sender.InvokeAsync("SetShadePosition", "2", 75, TestContext.Current.CancellationToken);

        var senderChange = await senderReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var otherChange = await otherReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(("2", 75) == senderChange, $"The sender should be told (2, 75), was told {senderChange}.");
        Assert.True(("2", 75) == otherChange, $"The other client should be told (2, 75), was told {otherChange}.");
    }

    [Fact(DisplayName = "TEST13: GetRoomState returns the whole room state, listing every configured light and shade.")]
    public async Task GetRoomState_AfterChanges_ReturnsSnapshot()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        await connection.InvokeAsync("SelectPreset", new SelectPresetRequest("presentation"), TestContext.Current.CancellationToken);
        await connection.InvokeAsync("SetShadePosition", "3", 40, TestContext.Current.CancellationToken);
        await connection.InvokeAsync("SetVolumeLevel", 25, TestContext.Current.CancellationToken);

        var actual = await connection.InvokeAsync<RoomStateSnapshot>("GetRoomState", TestContext.Current.CancellationToken);

        var expectedLights = new[] { new LightLevel("1", 30), new LightLevel("2", 60), new LightLevel("3", 80), new LightLevel("4", 0) };
        var expectedShades = new[] { new ShadePosition("1", 0), new ShadePosition("2", 0), new ShadePosition("3", 40) };
        var expectedShadeTilts = new[] { new ShadeTilt("3", 0) };
        Assert.True(SystemPowerState.Off == actual.SystemPower, $"Power was {actual.SystemPower}.");
        Assert.True(25 == actual.VolumeLevelPercent && !actual.IsAudioMuted, $"Volume was {actual.VolumeLevelPercent}, muted {actual.IsAudioMuted}.");
        Assert.True(actual.SelectedSourceId is null && PageKey.Home == actual.CurrentPage, $"Source was {actual.SelectedSourceId}, page {actual.CurrentPage}.");
        Assert.True("presentation" == actual.SelectedPresetId, $"Selected preset was {actual.SelectedPresetId}.");
        Assert.True(expectedLights.SequenceEqual(actual.LightLevels), $"Unexpected light levels: {string.Join(", ", actual.LightLevels)}.");
        Assert.True(expectedShades.SequenceEqual(actual.ShadePositions), $"Unexpected shade positions: {string.Join(", ", actual.ShadePositions)}.");
        Assert.True(expectedShadeTilts.SequenceEqual(actual.ShadeTilts), $"Unexpected shade tilts: {string.Join(", ", actual.ShadeTilts)}.");
        Assert.True(actual.ActiveShadePresetId is null, $"Expected no active shade preset, was {actual.ActiveShadePresetId}.");
    }

    [Fact(DisplayName = "TEST15: SelectPreset records the current scene, and changing a single light clears it.")]
    public async Task SelectPreset_ThenSetLightLevel_ClearsSelectedPreset()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);
        var roomState = _app!.Services.GetRequiredService<RoomState>();

        await connection.InvokeAsync("SelectPreset", new SelectPresetRequest("meeting"), TestContext.Current.CancellationToken);
        var afterPreset = roomState.SelectedPresetId;

        await connection.InvokeAsync("SetLightLevel", "1", 10, TestContext.Current.CancellationToken);

        Assert.True("meeting" == afterPreset, $"Expected the scene to be meeting after the recall, was {afterPreset ?? "null"}.");
        Assert.True(roomState.SelectedPresetId is null, $"Expected no scene after a single light change, was {roomState.SelectedPresetId}.");
    }

    [Theory(DisplayName = "TEST14: SetShadePosition rejects an unknown shade or a position outside 0-100, and changes nothing.")]
    [InlineData("nope", 50)]
    [InlineData("1", -1)]
    [InlineData("1", 101)]
    public async Task SetShadePosition_WithBadArguments_ThrowsAndLeavesStateAlone(string shadeId, int positionPercent)
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("SetShadePosition", shadeId, positionPercent, TestContext.Current.CancellationToken));

        var roomState = _app!.Services.GetRequiredService<RoomState>();
        Assert.True(roomState.ShadePositions.Count == 0, "A rejected command should not change the room state.");
    }

    [Fact(DisplayName = "TEST16: SetShadeTilt broadcasts ShadeTiltChanged to all clients, including the sender.")]
    public async Task SetShadeTilt_WithConnectedClients_BroadcastsToAll()
    {
        await using var sender = CreateConnection();
        await using var other = CreateConnection();

        var senderReceived = new TaskCompletionSource<(string ShadeId, int TiltPercent)>();
        var otherReceived = new TaskCompletionSource<(string ShadeId, int TiltPercent)>();
        sender.On<string, int>("ShadeTiltChanged", (shadeId, tiltPercent) => senderReceived.TrySetResult((shadeId, tiltPercent)));
        other.On<string, int>("ShadeTiltChanged", (shadeId, tiltPercent) => otherReceived.TrySetResult((shadeId, tiltPercent)));

        await sender.StartAsync(TestContext.Current.CancellationToken);
        await other.StartAsync(TestContext.Current.CancellationToken);

        await sender.InvokeAsync("SetShadeTilt", "3", 55, TestContext.Current.CancellationToken);

        var senderChange = await senderReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var otherChange = await otherReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(("3", 55) == senderChange, $"The sender should be told (3, 55), was told {senderChange}.");
        Assert.True(("3", 55) == otherChange, $"The other client should be told (3, 55), was told {otherChange}.");
    }

    [Theory(DisplayName = "TEST17: SetShadeTilt rejects an unknown shade, a non-tilt-capable shade, or a tilt outside 0-100.")]
    [InlineData("nope", 50)]
    [InlineData("1", 50)]
    [InlineData("3", -1)]
    [InlineData("3", 101)]
    public async Task SetShadeTilt_WithBadArguments_ThrowsAndLeavesStateAlone(string shadeId, int tiltPercent)
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("SetShadeTilt", shadeId, tiltPercent, TestContext.Current.CancellationToken));

        var roomState = _app!.Services.GetRequiredService<RoomState>();
        Assert.True(roomState.ShadeTilts.Count == 0, "A rejected command should not change the room state.");
    }

    [Fact(DisplayName = "TEST18: SelectShadePreset applies each target, broadcasts position/tilt changes, then ShadePresetSelected.")]
    public async Task SelectShadePreset_WithConnectedClient_AppliesTargetsAndBroadcasts()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        var positions = new List<(string ShadeId, int PositionPercent)>();
        var tilts = new List<(string ShadeId, int TiltPercent)>();
        var presetSelected = new TaskCompletionSource<string?>();
        connection.On<string, int>("ShadePositionChanged", (shadeId, positionPercent) => positions.Add((shadeId, positionPercent)));
        connection.On<string, int>("ShadeTiltChanged", (shadeId, tiltPercent) => tilts.Add((shadeId, tiltPercent)));
        connection.On<string?>("ShadePresetSelected", presetId => presetSelected.TrySetResult(presetId));

        await connection.InvokeAsync("SelectShadePreset", new SelectShadePresetRequest("meeting"), TestContext.Current.CancellationToken);

        var presetId = await presetSelected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True("meeting" == presetId, $"Expected meeting, got {presetId}.");
        Assert.True(positions.Contains(("3", 65)), "Expected shade 3 to be moved to 65.");
        Assert.True(tilts.Contains(("3", 40)), "Expected shade 3 to be tilted to 40.");

        var roomState = _app!.Services.GetRequiredService<RoomState>();
        Assert.True("meeting" == roomState.SelectedShadePresetId, $"Expected the active shade preset to be meeting, was {roomState.SelectedShadePresetId}.");
    }

    [Fact(DisplayName = "TEST19: SelectShadePreset rejects an unknown preset id, and changes nothing.")]
    public async Task SelectShadePreset_WithUnknownPresetId_ThrowsAndLeavesStateAlone()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("SelectShadePreset", new SelectShadePresetRequest("nope"), TestContext.Current.CancellationToken));

        var roomState = _app!.Services.GetRequiredService<RoomState>();
        Assert.True(roomState.ShadePositions.Count == 0, "A rejected preset should not change the room state.");
        Assert.True(roomState.SelectedShadePresetId is null, "A rejected preset should not become the active scene.");
    }

    [Fact(DisplayName = "TEST20: SelectShadePreset records the active shade preset without immediately clearing it.")]
    public async Task SelectShadePreset_DoesNotClearItsOwnSelection()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        await connection.InvokeAsync("SelectShadePreset", new SelectShadePresetRequest("meeting"), TestContext.Current.CancellationToken);

        var roomState = _app!.Services.GetRequiredService<RoomState>();
        Assert.True("meeting" == roomState.SelectedShadePresetId, $"Expected meeting to remain the active shade preset, was {roomState.SelectedShadePresetId}.");
    }

    [Fact(DisplayName = "TEST21: Changing a shade's position manually clears the active shade preset.")]
    public async Task SelectShadePreset_ThenSetShadePosition_ClearsActiveShadePreset()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);
        var roomState = _app!.Services.GetRequiredService<RoomState>();

        await connection.InvokeAsync("SelectShadePreset", new SelectShadePresetRequest("meeting"), TestContext.Current.CancellationToken);
        var afterPreset = roomState.SelectedShadePresetId;

        await connection.InvokeAsync("SetShadePosition", "1", 10, TestContext.Current.CancellationToken);

        Assert.True("meeting" == afterPreset, $"Expected the scene to be meeting after the recall, was {afterPreset ?? "null"}.");
        Assert.True(roomState.SelectedShadePresetId is null, $"Expected no scene after a single shade position change, was {roomState.SelectedShadePresetId}.");
    }

    [Fact(DisplayName = "TEST22: Changing a shade's tilt manually clears the active shade preset.")]
    public async Task SelectShadePreset_ThenSetShadeTilt_ClearsActiveShadePreset()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);
        var roomState = _app!.Services.GetRequiredService<RoomState>();

        await connection.InvokeAsync("SelectShadePreset", new SelectShadePresetRequest("meeting"), TestContext.Current.CancellationToken);
        await connection.InvokeAsync("SetShadeTilt", "3", 10, TestContext.Current.CancellationToken);

        Assert.True(roomState.SelectedShadePresetId is null, $"Expected no scene after a single shade tilt change, was {roomState.SelectedShadePresetId}.");
    }

    private HubConnection CreateConnection() =>
        new HubConnectionBuilder()
            .WithUrl($"{_baseUrl}{RoomHub.Path}", HttpTransportType.WebSockets)
            .AddNewtonsoftJsonProtocol()
            .Build();
}
