using P709.Core.Nvx;

namespace P709.Core.Tests.Nvx;

public class NvxConnectivityGuardTests
{
    private const string Receiver = "10.0.0.50";

    private readonly FakeNvxClient _receiver = new(Receiver);
    private readonly ManualTimeProvider _time = new();
    private readonly List<NvxConnectivityChange> _changes = [];

    private NvxConnectivityGuard CreateGuard()
    {
        var guard = new NvxConnectivityGuard(_receiver, _time);
        guard.ConnectivityChanged += _changes.Add;
        return guard;
    }

    [Fact(DisplayName = "TEST1: An unreachable NVX is reported offline once, however many calls fail.")]
    public async Task Unreachable_ReportsOfflineOnce()
    {
        var guard = CreateGuard();
        _receiver.Failure = new NvxUnreachableException("Could not reach the NVX at 10.0.0.50");

        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<NvxUnreachableException>(() => guard.StartReceiveAsync(TestContext.Current.CancellationToken));
            _time.Advance(NvxConnectivityGuard.RetryInterval);
        }

        Assert.True(!guard.IsOnline, "Expected offline.");
        Assert.True(_changes is [{ IsOnline: false, Reason: "Could not reach the NVX at 10.0.0.50" }], $"Unexpected changes: {string.Join(", ", _changes)}.");
    }

    [Fact(DisplayName = "TEST2: While offline, calls fail at once without reaching the NVX until the retry interval has passed.")]
    public async Task Offline_SkipsCallsUntilRetry()
    {
        var guard = CreateGuard();
        _receiver.Failure = new NvxUnreachableException("Could not reach the NVX at 10.0.0.50");
        await Assert.ThrowsAsync<NvxUnreachableException>(() => guard.StartReceiveAsync(TestContext.Current.CancellationToken));
        _receiver.Failure = null;

        _time.Advance(NvxConnectivityGuard.RetryInterval - TimeSpan.FromSeconds(1));
        var skipped = await Assert.ThrowsAsync<NvxUnreachableException>(() => guard.StartReceiveAsync(TestContext.Current.CancellationToken));
        var reachedBeforeRetry = _receiver.Log.Count;

        _time.Advance(TimeSpan.FromSeconds(1));
        await guard.StartReceiveAsync(TestContext.Current.CancellationToken);

        Assert.True(skipped.Message.Contains("offline"), $"Unexpected message: {skipped.Message}");
        Assert.True(reachedBeforeRetry == 0, $"No call should reach the NVX before the retry, got: {string.Join(" | ", _receiver.Log)}.");
        Assert.True(guard.IsOnline && _changes is [{ IsOnline: false }, { IsOnline: true }], $"Unexpected changes: {string.Join(", ", _changes)}.");
    }

    [Fact(DisplayName = "TEST3: An NVX that answers but refuses is online, and the failure is passed on unchanged.")]
    public async Task Refused_StaysOnline()
    {
        var guard = CreateGuard();
        var refusal = new NvxException("The NVX at 10.0.0.50 answered HTTP 500.");
        _receiver.Failure = refusal;

        var thrown = await Assert.ThrowsAsync<NvxException>(() => guard.StartReceiveAsync(TestContext.Current.CancellationToken));

        Assert.True(ReferenceEquals(refusal, thrown), "Expected the NVX's own exception.");
        Assert.True(guard.IsOnline && _changes.Count == 0, $"Expected online with no changes, got: {string.Join(", ", _changes)}.");
    }

    [Fact(DisplayName = "TEST4: The router reports an offline receiver through its ConnectivityChanged event.")]
    public async Task Router_ForwardsConnectivity()
    {
        var router = new NvxRouter(new NvxConfig("admin", "secret", Receiver, [new NvxSourceConfig("laptop", "239.8.0.21")]), _receiver, _time);
        router.ConnectivityChanged += _changes.Add;
        _receiver.Failure = new NvxUnreachableException("Could not reach the NVX at 10.0.0.50");

        await Assert.ThrowsAsync<NvxUnreachableException>(() => router.RouteSourceAsync("laptop", TestContext.Current.CancellationToken));

        Assert.True(!router.IsOnline && _changes is [{ IsOnline: false, Address: Receiver }], $"Unexpected changes: {string.Join(", ", _changes)}.");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
