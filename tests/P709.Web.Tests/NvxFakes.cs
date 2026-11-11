using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using P709.Core.Nvx;

namespace P709.Web.Tests;

/// <summary>A fake NVX receiver: records what it is asked, in order, in <see cref="Log"/>. Thread-safe: the hub calls it from request threads.</summary>
internal sealed class FakeNvxClient(string address) : INvxClient
{
    public ConcurrentQueue<string> Log { get; } = new();

    public string Address { get; } = address;

    public NvxPreview Preview { get; set; } = new(true, [new NvxPreviewImage("preview_540px.jpeg", $"https://{address}/preview/preview_540px.jpeg", 960, 540, true)]);

    public NvxException? Failure { get; set; }

    public Task SetReceiveMulticastAddressAsync(string multicastAddress, CancellationToken cancellationToken = default) => Record($"SetReceiveMulticastAddress {multicastAddress}");

    public Task StartReceiveAsync(CancellationToken cancellationToken = default) => Record("StartReceive");

    public Task StopReceiveAsync(CancellationToken cancellationToken = default) => Record("StopReceive");

    public async Task<NvxStreamStatus> GetReceiveStatusAsync(CancellationToken cancellationToken = default)
    {
        await Record("GetReceiveStatus");
        return new NvxStreamStatus("Stream Started", "", false);
    }

    public Task SetPreviewEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default) => Record($"SetPreviewEnabled {isEnabled}");

    public async Task<NvxPreview> GetPreviewAsync(CancellationToken cancellationToken = default)
    {
        await Record("GetPreview");
        return Preview;
    }

    public async Task<NvxImage> DownloadImageAsync(string url, CancellationToken cancellationToken = default)
    {
        await Record($"DownloadImage {url}");
        return new NvxImage(Encoding.UTF8.GetBytes(url), "image/jpeg");
    }

    private Task Record(string call)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Log.Enqueue(call);
        return Task.CompletedTask;
    }
}

/// <summary>Captures everything logged through the program's logger factory, standing in for the Crestron logger.</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Category, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, Entries);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<(LogLevel, string, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((logLevel, category, formatter(state, exception)));
    }
}
