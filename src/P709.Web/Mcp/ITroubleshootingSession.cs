namespace P709.Web.Mcp;

/// <summary>
/// One AI troubleshooting conversation: Claude inspects the room through the MCP tools and asks the user questions with a
/// short list of answers. On a processor this is the MCP toolkit's <c>McpTroubleshootingSession</c>, wrapped by the Crestron
/// project; it is an interface here so Web never references the toolkit, and so tests can fake it.
/// </summary>
public interface ITroubleshootingSession
{
    /// <summary>True while Claude is working on a turn.</summary>
    event Action<bool>? ProcessingChanged;

    /// <summary>A short progress or status line.</summary>
    event Action<string>? StatusChanged;

    /// <summary>Claude's reply for a turn.</summary>
    event Action<TroubleshootingReply>? ReplyReceived;

    /// <summary>Claude escalated the problem: the text of the XiO Cloud alert it raised.</summary>
    event Action<string>? XiOAlertRaised;

    /// <summary>Starts the conversation. Claude looks at the room first, so there is no question to pass.</summary>
    Task StartAsync();

    /// <summary>Sends the user's answer to Claude's last question.</summary>
    Task AnswerAsync(string answer);
}

/// <summary>Claude's reply for one turn of a troubleshooting conversation.</summary>
/// <param name="Message">What Claude says.</param>
/// <param name="AnswerOptions">The answers the user can pick from. Empty when Claude asks nothing.</param>
/// <param name="IsDone">Whether Claude considers the conversation finished.</param>
public sealed record TroubleshootingReply(string Message, IReadOnlyList<string> AnswerOptions, bool IsDone);
