namespace P709.Web.Mcp;

/// <summary>
/// The room's one AI troubleshooting conversation, shared by every panel: a panel that joins part-way sees the conversation
/// already running. Holds what the panel shows (<see cref="State"/>) and announces every change (<see cref="StateChanged"/>);
/// <see cref="McpHub"/> passes those on to the panels.
/// </summary>
/// <remarks>
/// Each conversation gets a fresh session, and events from an earlier one are ignored. A failure is shown in the status line
/// rather than thrown, because nobody is waiting on the call: the panel only sees the state.
/// </remarks>
/// <param name="createSession">Makes a session per conversation. Null when no Anthropic API key is configured, or MCP is off.</param>
/// <param name="logger">Logs the XiO Cloud alerts Claude raises.</param>
public sealed class TroubleshootingService(Func<ITroubleshootingSession>? createSession, ILogger<TroubleshootingService> logger)
{
    /// <summary>The status shown while troubleshooting is unavailable.</summary>
    public const string NoApiKeyStatus = "Anthropic API key not configured";

    private readonly Lock _gate = new();
    private ITroubleshootingSession? _session;

    /// <summary>What the panel shows now.</summary>
    public TroubleshootingState State { get; private set; } = createSession is null
        ? new TroubleshootingState(false, false, NoApiKeyStatus, "", [], null, false)
        : new TroubleshootingState(true, false, "", "", [], null, false);

    /// <summary>Raised after every change, with the new state.</summary>
    public event Action<TroubleshootingState>? StateChanged;

    /// <summary>
    /// Starts a new conversation and waits for Claude's first turn. Does nothing while troubleshooting is unavailable or
    /// Claude is still working, so a panel that opens troubleshooting part-way joins the running conversation.
    /// </summary>
    public async Task StartAsync()
    {
        ITroubleshootingSession session;

        lock (_gate)
        {
            if (createSession is null || State.IsBusy)
            {
                return;
            }

            try
            {
                session = _session = createSession();
            }
            catch (Exception exception)
            {
                // The panel only sees the state, so a conversation that cannot even start must say why there.
                _session = null;
                Change(State with { Status = $"Troubleshooting failed: {exception.Message}", Message = "", AnswerOptions = [], SelectedAnswerIndex = null });
                return;
            }

            Change(State with { IsBusy = true, Status = "", Message = "", AnswerOptions = [], SelectedAnswerIndex = null, IsDone = false });
        }

        session.ProcessingChanged += isBusy => Apply(session, state => state with { IsBusy = isBusy });
        session.StatusChanged += status => Apply(session, state => state with { Status = status });
        session.ReplyReceived += reply => Apply(session, state => state with
        {
            Message = reply.Message,
            AnswerOptions = reply.AnswerOptions,
            SelectedAnswerIndex = null,
            Status = "",
            IsDone = reply.IsDone
        });
        session.XiOAlertRaised += alert => logger.LogWarning("Troubleshooting raised an XiO Cloud alert: {Alert}", alert);

        await RunAsync(session, session.StartAsync);
    }

    /// <summary>
    /// Sends the answer at <paramref name="optionIndex"/> in <see cref="TroubleshootingState.AnswerOptions"/> to Claude and waits
    /// for the next turn. Ignored while Claude is working, with no conversation, or for an index that is not a current option.
    /// </summary>
    public async Task AnswerAsync(int optionIndex)
    {
        ITroubleshootingSession session;
        string answer;

        lock (_gate)
        {
            if (_session is null || State.IsBusy || optionIndex < 0 || optionIndex >= State.AnswerOptions.Count)
            {
                return;
            }

            session = _session;
            answer = State.AnswerOptions[optionIndex];
            Change(State with { SelectedAnswerIndex = optionIndex, Status = answer, Message = "", IsBusy = true });
        }

        await RunAsync(session, () => session.AnswerAsync(answer));
    }

    // Runs one turn: clears busy when it ends, or shows why it failed.
    private async Task RunAsync(ITroubleshootingSession session, Func<Task> turn)
    {
        try
        {
            await turn();
            Apply(session, state => state with { IsBusy = false });
        }
        catch (Exception exception)
        {
            Apply(session, state => state with { IsBusy = false, Status = $"Troubleshooting failed: {exception.Message}" });
        }
    }

    // Applies a change, but only if it comes from the current conversation.
    private void Apply(ITroubleshootingSession source, Func<TroubleshootingState, TroubleshootingState> change)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(source, _session))
            {
                return;
            }

            Change(change(State));
        }
    }

    // Sets the state and announces it while still holding the lock, so changes from different threads (Claude's events, a turn
    // finishing) are announced in the order they were made: the last state announced is always the current one. The only
    // listener, McpHubBroadcaster, just queues it.
    private void Change(TroubleshootingState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}
