using CrestronAgentLib.SimplSharpPro.Troubleshooting;
using P709.Web.Mcp;

namespace P709.Crestron;

/// <summary>
/// The MCP toolkit's troubleshooting session (which talks to Claude) as the <see cref="ITroubleshootingSession"/> the web host
/// uses, so P709.Web never references the toolkit. Each turn becomes a <see cref="TroubleshootingReply"/>.
/// </summary>
internal sealed class TroubleshootingSessionAdapter : ITroubleshootingSession
{
    private readonly McpTroubleshootingSession _session;

    public TroubleshootingSessionAdapter(McpTroubleshootingSession session)
    {
        _session = session;
        // The turn is built from Claude's JSON reply, so don't trust it to always carry a message and a list of answers.
        _session.TurnReceived += turn => ReplyReceived?.Invoke(new TroubleshootingReply(turn.Message ?? "", turn.AnswerOptions ?? [], turn.Done));
    }

    public event Action<bool>? ProcessingChanged
    {
        add => _session.ProcessingChanged += value;
        remove => _session.ProcessingChanged -= value;
    }

    public event Action<string>? StatusChanged
    {
        add => _session.StatusChanged += value;
        remove => _session.StatusChanged -= value;
    }

    public event Action<TroubleshootingReply>? ReplyReceived;

    public event Action<string>? XiOAlertRaised
    {
        add => _session.XiOAlertRaised += value;
        remove => _session.XiOAlertRaised -= value;
    }

    public Task StartAsync() => _session.StartAsync();

    public Task AnswerAsync(string answer) => _session.AnswerAsync(answer);
}
