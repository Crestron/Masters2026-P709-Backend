namespace P709.Core.Nvx;

/// <summary>
/// Wraps an NVX client and keeps track of whether the NVX can be reached, so an offline NVX neither slows the room down nor
/// floods the log. <see cref="NvxRouter"/> puts one around its receiver; nothing else needs to use it.
/// </summary>
/// <remarks>
/// <para>
/// When a call finds the NVX unreachable (<see cref="NvxUnreachableException"/>), the NVX is marked offline and
/// <see cref="ConnectivityChanged"/> is raised once. From then on, calls fail at once with an
/// <see cref="NvxUnreachableException"/> instead of waiting for a timeout, except that one call every
/// <see cref="RetryInterval"/> is let through to try the NVX again. The first call that gets an answer marks it online and
/// raises the event again.
/// </para>
/// <para>
/// An NVX that answers but refuses a request (for example a wrong password) is online: that failure is passed on unchanged.
/// </para>
/// </remarks>
public sealed class NvxConnectivityGuard : INvxClient
{
    /// <summary>How long calls are skipped after the NVX is found offline, before it is tried again.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    private readonly INvxClient _inner;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private DateTimeOffset _nextAttempt;

    /// <summary>Creates the guard. The NVX is assumed online until a call finds otherwise.</summary>
    /// <param name="inner">The client to guard.</param>
    /// <param name="timeProvider">The clock used for the retry interval. Null uses the system clock; tests pass a fake.</param>
    public NvxConnectivityGuard(INvxClient inner, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Raised when the NVX goes offline or comes back. The argument is null when it is back online, or the reason it could not
    /// be reached when it goes offline. Raised on the thread of the call that found the change.
    /// </summary>
    public event Action<NvxConnectivityChange>? ConnectivityChanged;

    /// <summary>Whether the NVX answered the last call that reached it.</summary>
    public bool IsOnline { get; private set; } = true;

    /// <inheritdoc />
    public string Address => _inner.Address;

    /// <inheritdoc />
    public Task SetReceiveMulticastAddressAsync(string multicastAddress, CancellationToken cancellationToken = default) =>
        RunAsync(() => _inner.SetReceiveMulticastAddressAsync(multicastAddress, cancellationToken));

    /// <inheritdoc />
    public Task StartReceiveAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() => _inner.StartReceiveAsync(cancellationToken));

    /// <inheritdoc />
    public Task StopReceiveAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() => _inner.StopReceiveAsync(cancellationToken));

    /// <inheritdoc />
    public Task<NvxStreamStatus> GetReceiveStatusAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() => _inner.GetReceiveStatusAsync(cancellationToken));

    /// <inheritdoc />
    public Task SetPreviewEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default) =>
        RunAsync(() => _inner.SetPreviewEnabledAsync(isEnabled, cancellationToken));

    /// <inheritdoc />
    public Task<NvxPreview> GetPreviewAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() => _inner.GetPreviewAsync(cancellationToken));

    /// <inheritdoc />
    public Task<NvxImage> DownloadImageAsync(string url, CancellationToken cancellationToken = default) =>
        RunAsync(() => _inner.DownloadImageAsync(url, cancellationToken));

    private async Task RunAsync(Func<Task> call) =>
        await RunAsync(async () =>
        {
            await call();
            return true;
        });

    private async Task<T> RunAsync<T>(Func<Task<T>> call)
    {
        lock (_gate)
        {
            if (!IsOnline)
            {
                var now = _time.GetUtcNow();

                if (now < _nextAttempt)
                {
                    var wait = Math.Ceiling((_nextAttempt - now).TotalSeconds);
                    throw new NvxUnreachableException($"The NVX at {Address} is offline. It will be tried again in {wait:0} seconds.");
                }

                // This call tries the NVX again. Calls made while it does are skipped, so only one waits on an offline NVX.
                _nextAttempt = now + RetryInterval;
            }
        }

        try
        {
            var result = await call();
            MarkOnline();
            return result;
        }
        catch (NvxUnreachableException exception)
        {
            MarkOffline(exception.Message);
            throw;
        }
        catch (NvxException)
        {
            // It answered, even if it refused: it is online.
            MarkOnline();
            throw;
        }
    }

    private void MarkOnline()
    {
        lock (_gate)
        {
            if (IsOnline)
            {
                return;
            }

            IsOnline = true;
        }

        ConnectivityChanged?.Invoke(new NvxConnectivityChange(Address, true, null));
    }

    private void MarkOffline(string reason)
    {
        lock (_gate)
        {
            _nextAttempt = _time.GetUtcNow() + RetryInterval;

            if (!IsOnline)
            {
                return;
            }

            IsOnline = false;
        }

        ConnectivityChanged?.Invoke(new NvxConnectivityChange(Address, false, reason));
    }
}

/// <summary>An NVX went offline or came back.</summary>
/// <param name="Address">The NVX's address.</param>
/// <param name="IsOnline">Whether it is now online.</param>
/// <param name="Reason">Why it could not be reached, when it has gone offline; null when it is back.</param>
public sealed record NvxConnectivityChange(string Address, bool IsOnline, string? Reason);
