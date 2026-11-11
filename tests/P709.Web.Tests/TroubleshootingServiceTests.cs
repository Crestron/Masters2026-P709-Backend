using Microsoft.Extensions.Logging;
using P709.Web.Mcp;

namespace P709.Web.Tests;

/// <summary>
/// The room's AI troubleshooting conversation, driven by a fake session: when a conversation starts, which answers are
/// passed on, and what the panel is shown at each point.
/// </summary>
public class TroubleshootingServiceTests : IDisposable
{
    private readonly RecordingLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<FakeTroubleshootingSession> _sessions = [];
    private readonly List<TroubleshootingState> _changes = [];

    public TroubleshootingServiceTests()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs));
    }

    public void Dispose() => _loggerFactory.Dispose();

    // A service whose factory hands out a new fake session per conversation, recording every state change.
    private TroubleshootingService CreateService()
    {
        var service = new TroubleshootingService(() =>
        {
            var session = new FakeTroubleshootingSession();
            _sessions.Add(session);
            return session;
        }, _loggerFactory.CreateLogger<TroubleshootingService>());

        service.StateChanged += _changes.Add;
        return service;
    }

    [Fact(DisplayName = "TEST1: With no session factory (no API key) troubleshooting is unavailable and Start does nothing.")]
    public async Task Start_WithNoFactory_IsUnavailableAndDoesNothing()
    {
        var service = new TroubleshootingService(null, _loggerFactory.CreateLogger<TroubleshootingService>());
        service.StateChanged += _changes.Add;

        await service.StartAsync();

        Assert.True(!service.State.IsAvailable, "Expected troubleshooting unavailable.");
        Assert.True(service.State.Status == TroubleshootingService.NoApiKeyStatus, $"Status was \"{service.State.Status}\".");
        Assert.True(_changes.Count == 0, "Expected no state change.");
    }

    [Fact(DisplayName = "TEST2: Starting creates a session, is busy until the turn finishes, and shows Claude's reply.")]
    public async Task Start_WithFactory_StartsSessionAndShowsReply()
    {
        var service = CreateService();

        var start = service.StartAsync();

        Assert.True(service.State.IsAvailable && service.State.IsBusy, "Expected available and busy while the turn runs.");
        Assert.True(_sessions.Count == 1 && _sessions[0].StartCalls == 1, "Expected one session, started once.");

        _sessions[0].RaiseReply("msg", "A", "B");
        _sessions[0].Complete();
        await start;

        var state = service.State;
        Assert.True(state.Message == "msg" && state.AnswerOptions.SequenceEqual(["A", "B"]), $"Unexpected reply: {state.Message} / {string.Join(",", state.AnswerOptions)}.");
        Assert.True(state.SelectedAnswerIndex is null && !state.IsBusy, "Expected no answer chosen and not busy.");
    }

    [Fact(DisplayName = "TEST3: Answering passes the chosen option's text to Claude and shows it while Claude works.")]
    public async Task Answer_WithOption_SendsItsText()
    {
        var service = CreateService();
        var start = service.StartAsync();
        _sessions[0].RaiseReply("msg", "A", "B");
        _sessions[0].Complete();
        await start;

        var answer = service.AnswerAsync(1);

        var state = service.State;
        Assert.True(_sessions[0].Answers.SequenceEqual(["B"]), $"Answers sent: {string.Join(",", _sessions[0].Answers)}.");
        Assert.True(state.SelectedAnswerIndex == 1 && state.Status == "B" && state.Message == "" && state.IsBusy,
            $"Unexpected state: {state.SelectedAnswerIndex}, \"{state.Status}\", \"{state.Message}\", busy {state.IsBusy}.");

        _sessions[0].Complete();
        await answer;
    }

    [Fact(DisplayName = "TEST4: While Claude is working, Start and Answer do nothing.")]
    public async Task StartAndAnswer_WhileBusy_DoNothing()
    {
        var service = CreateService();
        var start = service.StartAsync();

        await service.StartAsync();
        await service.AnswerAsync(0);

        Assert.True(_sessions.Count == 1 && _sessions[0].StartCalls == 1, "Expected no second session.");
        Assert.True(_sessions[0].Answers.IsEmpty, "Expected no answer sent.");

        _sessions[0].Complete();
        await start;
    }

    [Theory(DisplayName = "TEST5: An answer index that is not a current option is ignored.")]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task Answer_WithIndexOutOfRange_IsIgnored(int optionIndex)
    {
        var service = CreateService();
        var start = service.StartAsync();
        _sessions[0].RaiseReply("msg", "A", "B");
        _sessions[0].Complete();
        await start;

        await service.AnswerAsync(optionIndex);

        Assert.True(_sessions[0].Answers.IsEmpty, "Expected no answer sent.");
        Assert.True(!service.State.IsBusy && service.State.SelectedAnswerIndex is null, "Expected the state unchanged.");
    }

    [Fact(DisplayName = "TEST6: Events from an earlier conversation are ignored.")]
    public async Task Events_FromEarlierSession_AreIgnored()
    {
        var service = CreateService();
        var first = service.StartAsync();
        _sessions[0].Complete();
        await first;
        var second = service.StartAsync();

        _sessions[0].RaiseReply("old", "X");
        _sessions[0].RaiseStatus("old status");

        Assert.True(service.State.Message == "" && service.State.Status == "", $"Stale event applied: \"{service.State.Message}\" / \"{service.State.Status}\".");

        _sessions[1].Complete();
        await second;
    }

    [Fact(DisplayName = "TEST7: A conversation that fails shows why and is no longer busy.")]
    public async Task Start_WhenSessionThrows_ShowsFailure()
    {
        var service = CreateService();
        var start = service.StartAsync();

        _sessions[0].Fail(new Exception("boom"));
        await start;

        Assert.True(service.State.Status == "Troubleshooting failed: boom", $"Status was \"{service.State.Status}\".");
        Assert.True(!service.State.IsBusy, "Expected not busy.");
    }

    [Fact(DisplayName = "TEST8: The session's status and processing updates are shown.")]
    public async Task SessionEvents_StatusAndProcessing_AreShown()
    {
        var service = CreateService();
        var start = service.StartAsync();

        _sessions[0].RaiseStatus("Checking the lights");
        _sessions[0].RaiseProcessing(false);

        Assert.True(service.State.Status == "Checking the lights" && !service.State.IsBusy, $"Unexpected state: \"{service.State.Status}\", busy {service.State.IsBusy}.");

        _sessions[0].Complete();
        await start;
    }

    [Fact(DisplayName = "TEST9: An XiO Cloud alert from the session is logged as a warning.")]
    public async Task XiOAlert_IsLoggedAsWarning()
    {
        var service = CreateService();
        var start = service.StartAsync();

        _sessions[0].RaiseXiOAlert("Display offline");

        Assert.True(_logs.Entries.Any(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Display offline")), "Expected the alert logged as a warning.");

        _sessions[0].Complete();
        await start;
    }

    [Fact(DisplayName = "TEST10: Every change is announced with the new state.")]
    public async Task StateChanged_OnEachChange_CarriesNewState()
    {
        var service = CreateService();
        var start = service.StartAsync();
        _sessions[0].RaiseReply("msg", "A");
        _sessions[0].Complete();
        await start;

        Assert.True(_changes.Count >= 3, $"Expected at least start, reply and finish; got {_changes.Count}.");
        Assert.True(_changes[^1] == service.State, "The last change should be the current state.");
        Assert.True(_changes.Any(change => change.Message == "msg"), "Expected a change carrying the reply.");
    }

    [Fact(DisplayName = "TEST11: A session that cannot be created shows why, and is not busy.")]
    public async Task Start_WhenFactoryThrows_ShowsFailure()
    {
        var service = new TroubleshootingService(() => throw new InvalidOperationException("gateway not registered"),
            _loggerFactory.CreateLogger<TroubleshootingService>());
        service.StateChanged += _changes.Add;

        await service.StartAsync();

        Assert.True(service.State.Status == "Troubleshooting failed: gateway not registered", $"Status was \"{service.State.Status}\".");
        Assert.True(!service.State.IsBusy, "Expected not busy.");
        Assert.True(_changes.Count > 0 && _changes[^1] == service.State, "Expected the failure announced.");
    }
}
