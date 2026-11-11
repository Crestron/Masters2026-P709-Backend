using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using P709.Core.Configuration;
using P709.Web.Hubs;
using P709.Web.Mcp;

namespace P709.Web.Tests;

/// <summary>
/// Runs the real web host and drives <see cref="RoomTools"/>, the room operations the MCP tools call, while a panel-like
/// SignalR client listens on the room hub: whatever the AI does must reach the panel as the same events the hub sends.
/// Uses the shipped room configuration: lights 1-3 are dimmers, light 4 (Whiteboard) is a switch.
/// </summary>
public class RoomToolsTests : IAsyncLifetime
{
    private const string Receiver = "192.168.0.210";

    private readonly RoomConfiguration _roomConfiguration = RoomConfiguration.Load(AppContext.BaseDirectory);
    private readonly FakeNvxClient _receiver = new(Receiver);
    private readonly RecordingLoggerProvider _logs = new();
    private readonly ConcurrentQueue<string> _events = new();
    private ILoggerFactory? _loggerFactory;
    private WebApplication? _app;
    private HubConnection? _panel;

    private RoomState RoomState => _app!.Services.GetRequiredService<RoomState>();

    private RoomTools Tools => _app!.Services.GetRequiredService<RoomTools>();

    public async ValueTask InitializeAsync()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs));
        _app = WebHost.Build(_roomConfiguration, _loggerFactory, nvxReceiver: _receiver);
        _app.Urls.Clear();
        _app.Urls.Add("http://127.0.0.1:0");
        await _app.StartAsync();

        var server = _app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var baseUrl = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First();

        _panel = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}{RoomHub.Path}", HttpTransportType.WebSockets)
            .AddNewtonsoftJsonProtocol()
            .Build();

        _panel.On<string, int>("LightLevelChanged", (id, level) => _events.Enqueue($"LightLevelChanged {id} {level}"));
        _panel.On<string?>("PresetSelected", id => _events.Enqueue($"PresetSelected {id ?? "null"}"));
        _panel.On<int>("VolumeLevelChanged", level => _events.Enqueue($"VolumeLevelChanged {level}"));
        _panel.On<bool>("AudioMutedChanged", muted => _events.Enqueue($"AudioMutedChanged {muted}"));
        _panel.On<string>("SystemPowerStateChanged", state => _events.Enqueue($"SystemPowerStateChanged {state}"));
        _panel.On<string>("SelectedSourceChanged", id => _events.Enqueue($"SelectedSourceChanged {id}"));
        _panel.On<bool>("StreamRunningChanged", running => _events.Enqueue($"StreamRunningChanged {running}"));

        await _panel.StartAsync(TestContext.Current.CancellationToken);

        // The panel is sent the power state when it connects; start each test from a clean event list.
        await WaitForAsync("SystemPowerStateChanged off");
        _events.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_panel is not null)
        {
            await _panel.DisposeAsync();
        }

        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        _loggerFactory?.Dispose();
    }

    // Waits until the panel has received an event, so assertions about order and absence see everything sent before it.
    private async Task WaitForAsync(string expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!_events.Contains(expected))
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for \"{expected}\". Received: {string.Join(" | ", _events)}.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    // Sends a known event and waits for it: every event sent before it has then arrived too.
    private async Task FlushAsync()
    {
        await Tools.SetVolumeLevelAsync(13);
        await WaitForAsync("VolumeLevelChanged 13");
    }

    [Fact(DisplayName = "TEST1: Light and preset names come from the room configuration, in order.")]
    public void Names_FromRoomConfiguration_AreInConfigurationOrder()
    {
        string[] expectedLights = ["Wall Sconces", "Overhead", "Perimeter", "Whiteboard"];

        Assert.True(expectedLights.SequenceEqual(Tools.LightNames), $"Lights were {string.Join(", ", Tools.LightNames)}.");
        Assert.True(Tools.PresetNames.Count == 5 && Tools.PresetNames[0] == "High", $"Presets were {string.Join(", ", Tools.PresetNames)}.");
    }

    [Fact(DisplayName = "TEST2: Setting a light level updates the room state and tells the panel.")]
    public async Task SetLightLevel_WithDimmer_UpdatesStateAndBroadcasts()
    {
        await Tools.SetLightLevelAsync(1, 42);

        await WaitForAsync("LightLevelChanged 2 42");
        Assert.True(RoomState.LightLevels["2"] == 42, $"Light 2 was {RoomState.LightLevels["2"]}.");
        Assert.True(Tools.GetLightLevel(1) == 42, $"GetLightLevel(1) was {Tools.GetLightLevel(1)}.");
    }

    [Fact(DisplayName = "TEST3: A light level above 100 is set to 100.")]
    public async Task SetLightLevel_AboveHundred_ClampsToHundred()
    {
        await Tools.SetLightLevelAsync(0, 150);

        await WaitForAsync("LightLevelChanged 1 100");
        Assert.True(RoomState.LightLevels["1"] == 100, $"Light 1 was {RoomState.LightLevels["1"]}.");
    }

    [Theory(DisplayName = "TEST4: A switch light snaps to 0 below 50 and to 100 from 50.")]
    [InlineData(30, 0)]
    [InlineData(49, 0)]
    [InlineData(50, 100)]
    [InlineData(80, 100)]
    public async Task SetLightLevel_WithSwitch_SnapsToOffOrOn(ushort requested, int expected)
    {
        await Tools.SetLightLevelAsync(3, requested);

        await WaitForAsync($"LightLevelChanged 4 {expected}");
        Assert.True(RoomState.LightLevels["4"] == expected, $"Whiteboard was {RoomState.LightLevels["4"]}.");
    }

    [Fact(DisplayName = "TEST5: Changing one light after a preset clears the scene in the room state, and, like RoomHub, sends only the level.")]
    public async Task SetLightLevel_AfterPreset_ClearsSceneLikeRoomHub()
    {
        await Tools.SelectPresetAsync(0);
        await WaitForAsync("PresetSelected high");
        _events.Clear();

        await Tools.SetLightLevelAsync(0, 10);
        await FlushAsync();

        Assert.True(RoomState.SelectedPresetId is null, $"Preset was {RoomState.SelectedPresetId}.");
        string[] expected = ["LightLevelChanged 1 10", "VolumeLevelChanged 13"];
        Assert.True(expected.SequenceEqual(_events), $"Received: {string.Join(" | ", _events)}.");
    }

    [Fact(DisplayName = "TEST6: Recalling a preset sets each light, then reports the preset.")]
    public async Task SelectPreset_WithPreset_SetsLevelsThenReportsPreset()
    {
        await Tools.SelectPresetAsync(1);

        await WaitForAsync("PresetSelected presentation");
        string[] expected = ["LightLevelChanged 1 30", "LightLevelChanged 2 60", "LightLevelChanged 3 80", "LightLevelChanged 4 0", "PresetSelected presentation"];
        Assert.True(expected.SequenceEqual(_events), $"Received: {string.Join(" | ", _events)}.");
        Assert.True(Tools.IsPresetSelected(1) && !Tools.IsPresetSelected(0), "Expected only the presentation preset to read as selected.");
    }

    [Fact(DisplayName = "TEST7: Setting the volume updates the room state and tells the panel.")]
    public async Task SetVolumeLevel_UpdatesStateAndBroadcasts()
    {
        await Tools.SetVolumeLevelAsync(70);

        await WaitForAsync("VolumeLevelChanged 70");
        Assert.True(Tools.GetVolumeLevel() == 70, $"Volume was {Tools.GetVolumeLevel()}.");
    }

    [Fact(DisplayName = "TEST8: Muting updates the room state and tells the panel.")]
    public async Task SetAudioMuted_UpdatesStateAndBroadcasts()
    {
        await Tools.SetAudioMutedAsync(true);

        await WaitForAsync("AudioMutedChanged True");
        Assert.True(Tools.IsAudioMuted(), "Expected audio muted.");
    }

    [Fact(DisplayName = "TEST9: A light or preset index past the end is rejected and changes nothing.")]
    public async Task Commands_WithIndexPastEnd_ThrowAndChangeNothing()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Tools.SetLightLevelAsync(4, 10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Tools.SelectPresetAsync(99));
        await FlushAsync();

        Assert.True(RoomState.LightLevels.Count == 0 && RoomState.SelectedPresetId is null, "Expected no light or preset change.");
        Assert.True(_events.Count == 1, $"Expected only the flush event. Received: {string.Join(" | ", _events)}.");
    }

    [Fact(DisplayName = "TEST10: Turning the system on runs the power-up sequence and tells the panel each step.")]
    public async Task SetSystemPower_OnFromOff_RunsSequence()
    {
        Tools.PowerSequenceDuration = TimeSpan.Zero;

        await Tools.SetSystemPowerAsync(true);

        await WaitForAsync("SystemPowerStateChanged on");
        string[] expected = ["SystemPowerStateChanged starting", "SystemPowerStateChanged on"];
        Assert.True(expected.SequenceEqual(_events), $"Received: {string.Join(" | ", _events)}.");
        Assert.True(Tools.IsSystemOn(), "Expected the system on.");
    }

    [Fact(DisplayName = "TEST11: Turning the system off runs the power-down sequence and tells the panel each step.")]
    public async Task SetSystemPower_OffFromOn_RunsSequence()
    {
        Tools.PowerSequenceDuration = TimeSpan.Zero;
        RoomState.SetSystemPower(SystemPowerState.On);

        await Tools.SetSystemPowerAsync(false);

        await WaitForAsync("SystemPowerStateChanged off");
        string[] expected = ["SystemPowerStateChanged stopping", "SystemPowerStateChanged off"];
        Assert.True(expected.SequenceEqual(_events), $"Received: {string.Join(" | ", _events)}.");
        Assert.True(!Tools.IsSystemOn(), "Expected the system off.");
    }

    [Theory(DisplayName = "TEST12: A power request in the middle of a power sequence does nothing.")]
    [InlineData(SystemPowerState.Starting, true)]
    [InlineData(SystemPowerState.Starting, false)]
    [InlineData(SystemPowerState.Stopping, true)]
    [InlineData(SystemPowerState.Stopping, false)]
    public async Task SetSystemPower_DuringSequence_DoesNothing(SystemPowerState state, bool isOn)
    {
        Tools.PowerSequenceDuration = TimeSpan.Zero;
        RoomState.SetSystemPower(state);

        await Tools.SetSystemPowerAsync(isOn);
        await FlushAsync();

        Assert.True(RoomState.SystemPower == state, $"Power was {RoomState.SystemPower}.");
        Assert.True(_events.Count == 1, $"Expected only the flush event. Received: {string.Join(" | ", _events)}.");
        Assert.True(!Tools.IsSystemOn(), "Starting and stopping do not read as on.");
    }

    [Fact(DisplayName = "TEST13: Source names and MCP source types come from the room configuration, in order.")]
    public void Sources_FromRoomConfiguration_AreInConfigurationOrder()
    {
        ushort[] expectedTypes = [410, 10, 10, 20, 20];

        Assert.True(Tools.SourceNames.Count == 5 && Tools.SourceNames[0] == "AirMedia", $"Sources were {string.Join(", ", Tools.SourceNames)}.");
        Assert.True(expectedTypes.SequenceEqual(Tools.SourceTypes), $"Types were {string.Join(", ", Tools.SourceTypes)}.");
    }

    [Theory(DisplayName = "TEST14: Every source type has an MCP source type code.")]
    [InlineData(SourceType.WirelessPresenter, 410)]
    [InlineData(SourceType.LaptopHdmi, 10)]
    [InlineData(SourceType.LaptopUsbC, 10)]
    [InlineData(SourceType.Tv, 230)]
    [InlineData(SourceType.Other, 20)]
    [InlineData(SourceType.Ohp, 0)]
    public void ToMcpSourceType_ForEachType_ReturnsItsCode(SourceType type, ushort expected)
    {
        Assert.True(RoomTools.ToMcpSourceType(type) == expected, $"{type} gave {RoomTools.ToMcpSourceType(type)}.");
    }

    [Fact(DisplayName = "TEST15: No source reads as the no-source index until one is selected.")]
    public void GetCurrentSourceIndex_BeforeAnySelect_IsNoSource()
    {
        Assert.True(Tools.GetCurrentSourceIndex() == RoomTools.NoSource, $"Index was {Tools.GetCurrentSourceIndex()}.");
    }

    [Fact(DisplayName = "TEST16: Selecting an NVX source routes the receiver, then tells the panel the source and that the stream runs.")]
    public async Task SelectSource_WithNvxSource_RoutesThenBroadcasts()
    {
        RoomState.SetSystemPower(SystemPowerState.On);

        await Tools.SelectSourceAsync(1);

        await WaitForAsync("StreamRunningChanged True");
        string[] expectedNvx = ["SetReceiveMulticastAddress 239.8.0.211/live.sdp", "StartReceive", "SetPreviewEnabled True"];
        string[] expectedEvents = ["SelectedSourceChanged 2", "StreamRunningChanged True"];
        Assert.True(expectedNvx.SequenceEqual(_receiver.Log), $"Unexpected NVX calls: {string.Join(" | ", _receiver.Log)}.");
        Assert.True(expectedEvents.SequenceEqual(_events), $"Received: {string.Join(" | ", _events)}.");
        Assert.True(Tools.GetCurrentSourceIndex() == 1, $"Index was {Tools.GetCurrentSourceIndex()}.");
    }

    [Fact(DisplayName = "TEST17: Selecting a source that is not on NVX makes no NVX calls and does not report the stream.")]
    public async Task SelectSource_WithNonNvxSource_SkipsNvx()
    {
        RoomState.SetSystemPower(SystemPowerState.On);

        await Tools.SelectSourceAsync(0);
        await FlushAsync();

        Assert.True(_receiver.Log.IsEmpty, $"Unexpected NVX calls: {string.Join(" | ", _receiver.Log)}.");
        string[] expected = ["SelectedSourceChanged 1", "VolumeLevelChanged 13"];
        Assert.True(expected.SequenceEqual(_events), $"Received: {string.Join(" | ", _events)}.");
    }

    [Fact(DisplayName = "TEST18: Selecting a source while the system is off starts it first.")]
    public async Task SelectSource_WhenOff_StartsSystemFirst()
    {
        Tools.PowerSequenceDuration = TimeSpan.Zero;

        await Tools.SelectSourceAsync(0);

        await WaitForAsync("SelectedSourceChanged 1");
        string[] expected = ["SystemPowerStateChanged starting", "SystemPowerStateChanged on", "SelectedSourceChanged 1"];
        Assert.True(expected.SequenceEqual(_events), $"Received: {string.Join(" | ", _events)}.");
    }

    [Fact(DisplayName = "TEST19: Selecting a source while the system is starting selects it without a second power sequence.")]
    public async Task SelectSource_WhileStarting_SelectsWithoutPowerSequence()
    {
        RoomState.SetSystemPower(SystemPowerState.Starting);

        await Tools.SelectSourceAsync(0);

        await WaitForAsync("SelectedSourceChanged 1");
        Assert.True(_events.SequenceEqual(["SelectedSourceChanged 1"]), $"Received: {string.Join(" | ", _events)}.");
        Assert.True(RoomState.SystemPower == SystemPowerState.Starting, $"Power was {RoomState.SystemPower}.");
    }

    [Fact(DisplayName = "TEST20: An NVX that refuses a route is logged as an error, and the source is still selected.")]
    public async Task SelectSource_WhenNvxFails_LogsAndSelects()
    {
        RoomState.SetSystemPower(SystemPowerState.On);
        _receiver.Failure = new P709.Core.Nvx.NvxException("The NVX at 192.168.0.210 answered HTTP 500.");

        await Tools.SelectSourceAsync(1);
        await FlushAsync();

        Assert.True(_logs.Entries.Any(entry => entry.Level == LogLevel.Error && entry.Message.Contains("Could not route source \"2\"")),
            "Expected the routing failure logged as an error.");
        Assert.True("2" == RoomState.SelectedSourceId && !RoomState.IsStreamRunning, $"Source was {RoomState.SelectedSourceId}, stream running {RoomState.IsStreamRunning}.");
    }

    [Fact(DisplayName = "TEST20a: An offline NVX does not stop a source being selected, and is not logged as an error.")]
    public async Task SelectSource_WhenNvxOffline_SelectsWithoutError()
    {
        RoomState.SetSystemPower(SystemPowerState.On);
        _receiver.Failure = new P709.Core.Nvx.NvxUnreachableException("Could not reach the NVX at 192.168.0.210");

        await Tools.SelectSourceAsync(1);
        await FlushAsync();

        Assert.True(!_logs.Entries.Any(entry => entry.Level == LogLevel.Error), $"No error expected: {string.Join(" | ", _logs.Entries)}");
        Assert.True("2" == RoomState.SelectedSourceId, $"Source was {RoomState.SelectedSourceId}.");
    }

    [Fact(DisplayName = "TEST21: A source index past the end is rejected.")]
    public async Task SelectSource_WithIndexPastEnd_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Tools.SelectSourceAsync(5));
    }
}
