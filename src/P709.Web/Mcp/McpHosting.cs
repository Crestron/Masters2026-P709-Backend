namespace P709.Web.Mcp;

/// <summary>
/// What the program hands the web host so MCP can be connected to it. The MCP Gateway client (<c>McpRoom</c>) needs the
/// Crestron SDK, so it lives in the Crestron project; this is how the two meet without Web referencing it.
/// </summary>
/// <param name="CreateTroubleshootingSession">
/// Makes a troubleshooting session per conversation. Null when no Anthropic API key is configured: troubleshooting then
/// reports itself unavailable.
/// </param>
/// <param name="OnServicesReady">
/// Called once the web host's services exist, before it starts listening, so the program can register the MCP tools against
/// <see cref="RoomTools"/> and start the gateway connection. A failure is logged; the web host still runs.
/// </param>
public sealed record McpHosting(
    Func<ITroubleshootingSession>? CreateTroubleshootingSession = null,
    Action<IServiceProvider>? OnServicesReady = null);
