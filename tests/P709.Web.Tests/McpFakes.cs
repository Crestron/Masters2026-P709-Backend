using System.Collections.Concurrent;
using P709.Web.Mcp;

namespace P709.Web.Tests;

/// <summary>
/// A fake troubleshooting session standing in for the MCP toolkit's (which calls Claude). Each StartAsync or AnswerAsync call
/// stays pending until the test calls <see cref="Complete"/> or <see cref="Fail"/>, so a test can see the service while it is
/// busy. The Raise methods play the session's events.
/// </summary>
internal sealed class FakeTroubleshootingSession : ITroubleshootingSession
{
    private TaskCompletionSource _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Action<bool>? ProcessingChanged;

    public event Action<string>? StatusChanged;

    public event Action<TroubleshootingReply>? ReplyReceived;

    public event Action<string>? XiOAlertRaised;

    public int StartCalls { get; private set; }

    public ConcurrentQueue<string> Answers { get; } = new();

    public Task StartAsync()
    {
        StartCalls++;
        return NewCall();
    }

    public Task AnswerAsync(string answer)
    {
        Answers.Enqueue(answer);
        return NewCall();
    }

    public void Complete() => _pending.TrySetResult();

    public void Fail(Exception exception) => _pending.TrySetException(exception);

    public void RaiseReply(string message, params string[] answerOptions) =>
        ReplyReceived?.Invoke(new TroubleshootingReply(message, answerOptions, IsDone: false));

    public void RaiseStatus(string status) => StatusChanged?.Invoke(status);

    public void RaiseProcessing(bool isProcessing) => ProcessingChanged?.Invoke(isProcessing);

    public void RaiseXiOAlert(string alert) => XiOAlertRaised?.Invoke(alert);

    private Task NewCall()
    {
        _pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _pending.Task;
    }
}
