using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using P709.Core.Configuration;
using P709.Core.Nvx;
using P709.Web.Hubs;
using P709.Web.Nvx;

namespace P709.Web.Tests;

/// <summary>
/// Runs the real web host with fake NVX clients, so the NVX part of the hub contract and the preview endpoint are pinned down
/// without any NVX on the network. Uses the shipped room configuration: sources "2" and "3" arrive over NVX, "1" does not.
/// </summary>
public class NvxHubTests : IAsyncLifetime
{
    private const string Receiver = "192.168.0.210";

    private readonly RoomConfiguration _roomConfiguration = RoomConfiguration.Load(AppContext.BaseDirectory);
    private readonly FakeNvxClient _receiver = new(Receiver);
    private readonly RecordingLoggerProvider _logs = new();
    private ILoggerFactory? _loggerFactory;
    private WebApplication? _app;
    private string _baseUrl = string.Empty;

    private RoomState RoomState => _app!.Services.GetRequiredService<RoomState>();

    public async ValueTask InitializeAsync()
    {
        // The program passes its Crestron-backed logger factory in the same way; here it records what is logged.
        _loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs));
        _app = WebHost.Build(_roomConfiguration, _loggerFactory, nvxReceiver: _receiver);
        _app.Urls.Clear();
        _app.Urls.Add("http://127.0.0.1:0");
        await _app.StartAsync();

        var server = _app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        _baseUrl = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First();

        // Skip the ten-second power-up sequence: these tests are about routing, not power.
        RoomState.SetSystemPower(SystemPowerState.On);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        _loggerFactory?.Dispose();
    }

    private bool WasLogged(LogLevel level, string fragment) =>
        _logs.Entries.Any(entry => entry.Level == level && entry.Message.Contains(fragment));

    private HubConnection CreateConnection() =>
        new HubConnectionBuilder()
            .WithUrl($"{_baseUrl}{RoomHub.Path}", HttpTransportType.WebSockets)
            .AddNewtonsoftJsonProtocol()
            .Build();

    [Fact(DisplayName = "TEST1: Selecting an NVX source routes its multicast stream to the receiver, starts it and enables the preview.")]
    public async Task SelectSource_WithNvxSource_RoutesReceiver()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        await connection.InvokeAsync("SelectSource", "2", TestContext.Current.CancellationToken);

        var expected = new[] { "SetReceiveMulticastAddress 239.8.0.211/live.sdp", "StartReceive", "SetPreviewEnabled True" };
        Assert.True(expected.SequenceEqual(_receiver.Log), $"Unexpected NVX calls: {string.Join(" | ", _receiver.Log)}.");
        Assert.True("2" == RoomState.SelectedSourceId && RoomState.IsStreamRunning, "Expected source 2 selected with the stream running.");
    }

    [Fact(DisplayName = "TEST2: Selecting an NVX source tells every client the stream is running.")]
    public async Task SelectSource_WithNvxSource_BroadcastsStreamRunning()
    {
        await using var sender = CreateConnection();
        await using var other = CreateConnection();

        var received = new TaskCompletionSource<bool>();
        other.On<bool>("StreamRunningChanged", isRunning => received.TrySetResult(isRunning));

        await sender.StartAsync(TestContext.Current.CancellationToken);
        await other.StartAsync(TestContext.Current.CancellationToken);

        await sender.InvokeAsync("SelectSource", "2", TestContext.Current.CancellationToken);

        var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(actual, "Expected StreamRunningChanged true.");
    }

    [Fact(DisplayName = "TEST3: Selecting a source that is not on NVX sends nothing to the NVX.")]
    public async Task SelectSource_WithOtherSource_SendsNothing()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        await connection.InvokeAsync("SelectSource", "1", TestContext.Current.CancellationToken);

        Assert.True("1" == RoomState.SelectedSourceId, $"Expected source 1 selected, was {RoomState.SelectedSourceId}.");
        Assert.True(_receiver.Log.IsEmpty, $"Nothing should be sent to the NVX, got: {string.Join(" | ", _receiver.Log)}.");
    }

    [Fact(DisplayName = "TEST4: When the NVX is offline, SelectSource still selects the source and reports the stream not running.")]
    public async Task SelectSource_WhenNvxOffline_SelectsSourceAnyway()
    {
        await using var connection = CreateConnection();
        var streamRunning = new List<bool>();
        connection.On<bool>("StreamRunningChanged", isRunning => streamRunning.Add(isRunning));
        await connection.StartAsync(TestContext.Current.CancellationToken);
        _receiver.Failure = new NvxUnreachableException("Could not reach the NVX at 192.168.0.210");

        await connection.InvokeAsync("SelectSource", "2", TestContext.Current.CancellationToken);
        await connection.InvokeAsync("GetRoomState", TestContext.Current.CancellationToken); // flushes the broadcasts

        Assert.True("2" == RoomState.SelectedSourceId, $"Expected source 2 selected, was {RoomState.SelectedSourceId}.");
        Assert.True(!RoomState.IsStreamRunning, "The stream should be reported as not running.");
        Assert.True(streamRunning.SequenceEqual([false]), $"Expected StreamRunningChanged false, got: {string.Join(", ", streamRunning)}.");
    }

    [Fact(DisplayName = "TEST4a: An offline NVX is logged once as a warning, not on every request, and its return is logged once.")]
    public async Task OfflineNvx_IsLoggedOnceEachWay()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient();
        _receiver.Failure = new NvxUnreachableException("Could not reach the NVX at 192.168.0.210");

        await connection.InvokeAsync("SelectSource", "2", TestContext.Current.CancellationToken);
        await connection.InvokeAsync("SelectSource", "3", TestContext.Current.CancellationToken);
        await client.GetAsync($"{_baseUrl}{NvxPreviewEndpoint.Path}", TestContext.Current.CancellationToken);
        await client.GetAsync($"{_baseUrl}{NvxPreviewEndpoint.Path}", TestContext.Current.CancellationToken);

        var problems = _logs.Entries.Where(entry => entry.Level >= LogLevel.Warning).ToList();
        Assert.True(problems.Count == 1 && problems[0].Message.Contains("is offline"),
            $"Expected one offline warning, got: {string.Join(" | ", problems)}");
        Assert.True("3" == RoomState.SelectedSourceId, $"Expected source 3 selected, was {RoomState.SelectedSourceId}.");
    }

    [Theory(DisplayName = "TEST5: SetStreamRunning starts or stops the receiver and broadcasts StreamRunningChanged.")]
    [InlineData(true, "StartReceive")]
    [InlineData(false, "StopReceive")]
    public async Task SetStreamRunning_SendsCommandAndBroadcasts(bool isRunning, string expectedCall)
    {
        await using var connection = CreateConnection();

        var received = new TaskCompletionSource<bool>();
        connection.On<bool>("StreamRunningChanged", value => received.TrySetResult(value));

        await connection.StartAsync(TestContext.Current.CancellationToken);

        await connection.InvokeAsync("SetStreamRunning", isRunning, TestContext.Current.CancellationToken);

        var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(isRunning == actual, $"Expected StreamRunningChanged {isRunning}, got {actual}.");
        Assert.True(expectedCall == _receiver.Log.Single(), $"Unexpected NVX calls: {string.Join(" | ", _receiver.Log)}.");
    }

    [Fact(DisplayName = "TEST6: SetPreviewEnabled switches the receiver's preview and broadcasts PreviewEnabledChanged.")]
    public async Task SetPreviewEnabled_SendsCommandAndBroadcasts()
    {
        await using var connection = CreateConnection();

        var received = new TaskCompletionSource<bool>();
        connection.On<bool>("PreviewEnabledChanged", isEnabled => received.TrySetResult(isEnabled));

        await connection.StartAsync(TestContext.Current.CancellationToken);

        await connection.InvokeAsync("SetPreviewEnabled", false, TestContext.Current.CancellationToken);

        var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(!actual, "Expected PreviewEnabledChanged false.");
        Assert.True("SetPreviewEnabled False" == _receiver.Log.Single(), $"Unexpected NVX calls: {string.Join(" | ", _receiver.Log)}.");
    }

    [Fact(DisplayName = "TEST7: GetPreviewUrl returns the proxy path.")]
    public async Task GetPreviewUrl_ReturnsPath()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        var actual = await connection.InvokeAsync<string?>("GetPreviewUrl", TestContext.Current.CancellationToken);

        Assert.True("/nvx/preview" == actual, $"Expected /nvx/preview, got {actual}.");
    }

    [Fact(DisplayName = "TEST8: GetRoomState reports whether the stream is running.")]
    public async Task GetRoomState_IncludesIsStreamRunning()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);
        await connection.InvokeAsync("SelectSource", "2", TestContext.Current.CancellationToken);

        var actual = await connection.InvokeAsync<JObject>("GetRoomState", TestContext.Current.CancellationToken);

        Assert.True(actual.Value<bool?>("isStreamRunning") == true, $"Expected isStreamRunning true in: {actual}");
    }

    [Fact(DisplayName = "TEST9: GetRoomConfiguration never sends the NVX password to the front-end.")]
    public async Task GetRoomConfiguration_OmitsNvxPassword()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        var actual = await connection.InvokeAsync<JObject>("GetRoomConfiguration", TestContext.Current.CancellationToken);

        Assert.True(actual["nvx"] is JObject, "Expected the nvx section on the wire.");
        Assert.True(actual["nvx"]!["password"] is null, $"The password must not be sent: {actual["nvx"]}");
    }

    [Fact(DisplayName = "TEST10: The preview endpoint serves the receiver's image, uncached.")]
    public async Task PreviewEndpoint_ServesImage()
    {
        using var client = new HttpClient();

        var response = await client.GetAsync($"{_baseUrl}{NvxPreviewEndpoint.Path}", TestContext.Current.CancellationToken);

        var body = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.True(HttpStatusCode.OK == response.StatusCode, $"Expected 200, got {(int)response.StatusCode}.");
        Assert.True("image/jpeg" == response.Content.Headers.ContentType?.MediaType, $"Unexpected content type {response.Content.Headers.ContentType}.");
        Assert.True(response.Headers.CacheControl?.NoStore == true, "A live preview must not be cached.");
        Assert.True($"https://{Receiver}/preview/preview_540px.jpeg" == body, $"Expected the receiver's image, got {body}.");
    }

    [Fact(DisplayName = "TEST11: The preview endpoint answers 503 while previews are turned off.")]
    public async Task PreviewEndpoint_WhenDisabled_Returns503()
    {
        using var client = new HttpClient();
        _receiver.Preview = new NvxPreview(false, []);

        var response = await client.GetAsync($"{_baseUrl}{NvxPreviewEndpoint.Path}", TestContext.Current.CancellationToken);

        Assert.True(HttpStatusCode.ServiceUnavailable == response.StatusCode, $"Expected 503, got {(int)response.StatusCode}.");
    }

    [Fact(DisplayName = "TEST12: The preview endpoint answers 502 when the receiver fails.")]
    public async Task PreviewEndpoint_WhenNvxFails_Returns502()
    {
        using var client = new HttpClient();
        _receiver.Failure = new NvxException("Could not reach the NVX at 192.168.0.210");

        var response = await client.GetAsync($"{_baseUrl}{NvxPreviewEndpoint.Path}", TestContext.Current.CancellationToken);

        Assert.True(HttpStatusCode.BadGateway == response.StatusCode, $"Expected 502, got {(int)response.StatusCode}.");
    }

    [Fact(DisplayName = "TEST13: An NVX that refuses a route is logged as an error, naming the NVX, and the source is still selected.")]
    public async Task SelectSource_WhenNvxRefuses_LogsErrorAndSelects()
    {
        await using var connection = CreateConnection();
        await connection.StartAsync(TestContext.Current.CancellationToken);
        _receiver.Failure = new NvxException("The NVX at 192.168.0.210 answered HTTP 500.");

        await connection.InvokeAsync("SelectSource", "2", TestContext.Current.CancellationToken);

        Assert.True(WasLogged(LogLevel.Error, "The NVX at 192.168.0.210 answered HTTP 500."), $"Expected an error log naming the NVX, got: {string.Join(" | ", _logs.Entries)}");
        Assert.True("2" == RoomState.SelectedSourceId, $"Expected source 2 selected, was {RoomState.SelectedSourceId}.");
    }

    [Fact(DisplayName = "TEST14: An NVX failure behind the preview endpoint is logged as a warning.")]
    public async Task PreviewEndpoint_WhenNvxFails_LogsWarning()
    {
        using var client = new HttpClient();
        _receiver.Failure = new NvxException("Could not reach the NVX at 192.168.0.210");

        await client.GetAsync($"{_baseUrl}{NvxPreviewEndpoint.Path}", TestContext.Current.CancellationToken);

        Assert.True(WasLogged(LogLevel.Warning, "Could not reach the NVX at 192.168.0.210"), $"Expected a warning log naming the NVX, got: {string.Join(" | ", _logs.Entries)}");
    }

    [Fact(DisplayName = "TEST15: Previews turned off are logged as information only, not as a warning or error.")]
    public async Task PreviewEndpoint_WhenDisabled_LogsInformationOnly()
    {
        using var client = new HttpClient();
        _receiver.Preview = new NvxPreview(false, []);

        await client.GetAsync($"{_baseUrl}{NvxPreviewEndpoint.Path}", TestContext.Current.CancellationToken);

        var nvxEntries = _logs.Entries.Where(entry => entry.Category == NvxPreviewEndpoint.LogCategory).ToList();
        Assert.True(nvxEntries.Count == 1 && nvxEntries[0].Level == LogLevel.Information, $"Expected one information entry, got: {string.Join(" | ", nvxEntries)}");
    }
}
