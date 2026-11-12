using Microsoft.AspNetCore.SignalR;

namespace P709.Web.Mcp;

/// <summary>
/// The hub at the room configuration's <c>mcpHubPath</c> (<c>/hubs/mcp</c>): MCP-related communication with the panel only,
/// meaning the AI troubleshooting conversation and the gateway registration status. Room control stays on the room hub.
/// </summary>
/// <remarks>
/// Provisional: the method names, event names and <see cref="TroubleshootingState"/> are still to be matched to the Svelte
/// troubleshooting UI. Changes are pushed by <see cref="McpHubBroadcaster"/>, since they come from Claude, not from a client call.
/// </remarks>
public class McpHub(TroubleshootingService troubleshooting, McpStatus status, ILogger<McpHub> logger) : Hub
{
    /// <summary>Event sent to every client, with the whole <see cref="TroubleshootingState"/>, after every change.</summary>
    public const string TroubleshootingStateChangedEvent = "TroubleshootingStateChanged";

    /// <summary>Event sent to every client, with the new <see cref="McpStatus"/> value, when the registration state changes.</summary>
    public const string McpStatusChangedEvent = "McpStatusChanged";

    // TODO: Lab 4.5 - Give the troubleshooting UI its query and commands
    // Hint: The Svelte troubleshooting UI calls three hub methods. Add them as public methods, each one line onto the
    //       TroubleshootingService passed to the constructor:
    //       - GetTroubleshootingState(): returns troubleshooting.State;
    //       - StartTroubleshooting(): starts troubleshooting.StartAsync;
    //       - AnswerTroubleshooting(int optionIndex): starts troubleshooting.AnswerAsync(optionIndex).
    //       Claude's turn can take a while, and a hub handles one call per client at a time, so the two commands must not wait
    //       for it: start them with RunInBackground (below) and return. The UI hears the result as TroubleshootingStateChanged.
    // Ref:  The README.md holds the definition of the MCP hub API
    // Add your work below this line

    // Lab 4.5 - End

    /// <summary>Query: the MCP Gateway registration state.</summary>
    public string GetMcpStatus() => status.Value;

    // The service shows a failed turn in its state; this only catches what escapes it, such as a session that cannot be made.
    private void RunInBackground(string what, Func<Task> call) => _ = Task.Run(async () =>
    {
        try
        {
            await call();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not {What}: {Reason}", what, exception.Message);
        }
    });
}
