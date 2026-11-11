using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;

namespace P709.Web.Mcp;

/// <summary>
/// Pushes troubleshooting and registration changes to every <see cref="McpHub"/> client. They happen when Claude or the
/// gateway acts, not when a client calls the hub, so they are sent through the hub context for the life of the web host.
/// </summary>
/// <remarks>
/// Changes are raised on Claude's and the gateway's threads, which must not wait on the panels, so they are queued and sent
/// one at a time, in order: each troubleshooting event carries the whole state, and the last one sent must be the latest.
/// </remarks>
public sealed class McpHubBroadcaster(
    TroubleshootingService troubleshooting,
    McpStatus status,
    IHubContext<McpHub> hubContext,
    ILogger<McpHubBroadcaster> logger) : BackgroundService
{
    private readonly Channel<(string EventName, object Argument)> _outbox = Channel.CreateUnbounded<(string, object)>(new UnboundedChannelOptions { SingleReader = true });

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        troubleshooting.StateChanged += OnTroubleshootingStateChanged;
        status.Changed += OnStatusChanged;

        try
        {
            await foreach (var (eventName, argument) in _outbox.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await hubContext.Clients.All.SendAsync(eventName, argument, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Could not send {Event} to the panels: {Reason}", eventName, exception.Message);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The web host is stopping.
        }
        finally
        {
            troubleshooting.StateChanged -= OnTroubleshootingStateChanged;
            status.Changed -= OnStatusChanged;
        }
    }

    private void OnTroubleshootingStateChanged(TroubleshootingState state) => _outbox.Writer.TryWrite((McpHub.TroubleshootingStateChangedEvent, state));

    private void OnStatusChanged(string value) => _outbox.Writer.TryWrite((McpHub.McpStatusChangedEvent, value));
}
