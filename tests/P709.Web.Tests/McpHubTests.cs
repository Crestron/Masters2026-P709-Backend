using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using P709.Core.Configuration;
using P709.Web.Hubs;
using P709.Web.Mcp;

namespace P709.Web.Tests;

/// <summary>
/// Runs the real web host and talks to the MCP hub at the room configuration's <c>mcpHubPath</c> (<c>/hubs/mcp</c>), as the
/// Svelte troubleshooting UI does, with a fake troubleshooting session in place of Claude.
/// </summary>
public class McpHubTests : IAsyncDisposable
{
    private readonly RoomConfiguration _roomConfiguration = RoomConfiguration.Load(AppContext.BaseDirectory);
    private readonly FakeTroubleshootingSession _session = new();
    private readonly ConcurrentQueue<JObject> _stateChanges = new();
    private readonly ConcurrentQueue<string> _statusChanges = new();
    private WebApplication? _app;
    private HubConnection? _connection;

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    // Starts the host (with or without MCP hosting) and connects a client to the MCP hub, recording its events.
    private async Task<HubConnection> ConnectAsync(McpHosting? mcp)
    {
        _app = WebHost.Build(_roomConfiguration, mcp: mcp);
        _app.Urls.Clear();
        _app.Urls.Add("http://127.0.0.1:0");
        await _app.StartAsync(TestContext.Current.CancellationToken);

        var server = _app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var baseUrl = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First();

        _connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}{_roomConfiguration.McpHubPath}", HttpTransportType.WebSockets)
            .AddNewtonsoftJsonProtocol()
            .Build();

        _connection.On<JObject>(McpHub.TroubleshootingStateChangedEvent, _stateChanges.Enqueue);
        _connection.On<string>(McpHub.McpStatusChangedEvent, _statusChanges.Enqueue);

        await _connection.StartAsync(TestContext.Current.CancellationToken);
        return _connection;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {what}.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact(DisplayName = "TEST1: With no MCP hosting, troubleshooting reports unavailable, in camelCase.")]
    public async Task GetTroubleshootingState_WithNoMcp_ReportsUnavailable()
    {
        var connection = await ConnectAsync(mcp: null);

        var state = await connection.InvokeAsync<JObject>("GetTroubleshootingState", TestContext.Current.CancellationToken);

        Assert.True(state.Value<bool>("isAvailable") == false, $"State was {state}.");
        Assert.True(state.Value<string>("status") == TroubleshootingService.NoApiKeyStatus, $"State was {state}.");
    }

    [Fact(DisplayName = "TEST2: Starting troubleshooting starts the session, and Claude's reply reaches the client.")]
    public async Task StartTroubleshooting_WithSession_SendsReplyToClient()
    {
        var connection = await ConnectAsync(new McpHosting(() => _session));

        await connection.InvokeAsync("StartTroubleshooting", TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _session.StartCalls == 1, "the session to start");
        _session.RaiseReply("Is the display on?", "Yes", "No");

        await WaitUntilAsync(() => _stateChanges.Any(s => s.Value<string>("message") == "Is the display on?"), "the reply");
        var reply = _stateChanges.Last(s => s.Value<string>("message") == "Is the display on?");
        Assert.True(reply["answerOptions"]!.ToObject<string[]>()!.SequenceEqual(["Yes", "No"]), $"State was {reply}.");

        _session.Complete();
    }

    [Fact(DisplayName = "TEST3: Answering troubleshooting passes the chosen option's text to the session.")]
    public async Task AnswerTroubleshooting_WithOption_ReachesSession()
    {
        var connection = await ConnectAsync(new McpHosting(() => _session));
        await connection.InvokeAsync("StartTroubleshooting", TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _session.StartCalls == 1, "the session to start");
        _session.RaiseReply("Is the display on?", "Yes", "No");
        _session.Complete();
        await WaitUntilAsync(() => _stateChanges.Any(s => s.Value<bool>("isBusy") == false && s.Value<string>("message") != ""), "the turn to finish");

        await connection.InvokeAsync("AnswerTroubleshooting", 0, TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => _session.Answers.Contains("Yes"), "the answer to reach the session");
        _session.Complete();
    }

    [Fact(DisplayName = "TEST4: The MCP registration status is readable and its changes reach the client.")]
    public async Task McpStatus_WhenSet_ReachesClient()
    {
        var connection = await ConnectAsync(mcp: null);

        var initial = await connection.InvokeAsync<string>("GetMcpStatus", TestContext.Current.CancellationToken);
        _app!.Services.GetRequiredService<McpStatus>().Set("Registered");

        Assert.True(initial == "NotStarted", $"Initial status was {initial}.");
        await WaitUntilAsync(() => _statusChanges.Contains("Registered"), "McpStatusChanged");
    }

    [Fact(DisplayName = "TEST5: The room hub still works alongside the MCP hub.")]
    public async Task RoomHub_WithMcpHub_StillServesConfiguration()
    {
        await ConnectAsync(new McpHosting(() => _session));
        var server = _app!.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var baseUrl = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First();
        await using var room = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}{RoomHub.Path}", HttpTransportType.WebSockets)
            .AddNewtonsoftJsonProtocol()
            .Build();
        await room.StartAsync(TestContext.Current.CancellationToken);

        var configuration = await room.InvokeAsync<JObject>("GetRoomConfiguration", TestContext.Current.CancellationToken);

        Assert.True(configuration.Value<string>("roomName") == _roomConfiguration.RoomName, $"Configuration was {configuration}.");
    }
}

/// <summary>The MCP hook-up when the program runs the host: the services-ready callback, and a callback that fails.</summary>
public class WebHostMcpHostingTests
{
    private readonly RoomConfiguration _roomConfiguration = RoomConfiguration.Load(AppContext.BaseDirectory);

    [Fact(DisplayName = "TEST1: RunAsync hands the MCP callback services that can make the room tools.")]
    public async Task RunAsync_WithServicesReady_PassesServicesWithRoomTools()
    {
        using var shutdown = new CancellationTokenSource();
        var ready = new TaskCompletionSource<RoomTools?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mcp = new McpHosting(OnServicesReady: services => ready.TrySetResult(services.GetService<RoomTools>()));

        var run = WebHost.RunAsync(_roomConfiguration, port: 0, cancellationToken: shutdown.Token, mcp: mcp);
        var tools = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await shutdown.CancelAsync();
        await run;

        Assert.True(tools is not null, "Expected RoomTools to resolve.");
    }

    [Fact(DisplayName = "TEST2: A failing MCP callback is logged as an error, and the web host still runs.")]
    public async Task RunAsync_WithFailingServicesReady_LogsAndKeepsRunning()
    {
        var logs = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        using var shutdown = new CancellationTokenSource();
        var mcp = new McpHosting(OnServicesReady: _ => throw new InvalidOperationException("gateway down"));

        var run = WebHost.RunAsync(_roomConfiguration, loggerFactory, port: 0, cancellationToken: shutdown.Token, mcp: mcp);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!logs.Entries.Any(entry => entry.Level == LogLevel.Error && entry.Message.Contains("gateway down")))
        {
            Assert.True(DateTime.UtcNow < deadline && !run.IsCompleted, "Expected the failure logged while the host keeps running.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(!run.IsCompleted, "The web host stopped after the MCP callback failed.");
        await shutdown.CancelAsync();
        await run;
    }
}
