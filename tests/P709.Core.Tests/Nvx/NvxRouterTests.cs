using P709.Core.Nvx;

namespace P709.Core.Tests.Nvx;

public class NvxRouterTests
{
    private const string Receiver = "10.0.0.50";

    private readonly FakeNvxClient _receiver = new(Receiver);

    private static NvxConfig CreateConfig() =>
        new("admin", "secret", Receiver, [new NvxSourceConfig("laptop", "239.8.0.21")]);

    private NvxRouter CreateRouter() => new(CreateConfig(), _receiver);

    [Fact(DisplayName = "TEST1: Routing a source sets the receiver's multicast address, starts it, then enables its preview.")]
    public async Task RouteSource_WithNvxSource_RoutesInOrder()
    {
        var routed = await CreateRouter().RouteSourceAsync("laptop", TestContext.Current.CancellationToken);

        var expected = new[] { "SetReceiveMulticastAddress 239.8.0.21", "StartReceive", "SetPreviewEnabled True" };
        Assert.True(routed, "Expected the source to be routed.");
        Assert.True(expected.SequenceEqual(_receiver.Log), $"Unexpected calls: {string.Join(" | ", _receiver.Log)}.");
    }

    [Fact(DisplayName = "TEST2: A source that does not arrive over NVX is not routed and nothing is sent.")]
    public async Task RouteSource_WithOtherSource_SendsNothing()
    {
        var routed = await CreateRouter().RouteSourceAsync("tv", TestContext.Current.CancellationToken);

        Assert.True(!routed, "Expected false for a source with no NVX route.");
        Assert.True(_receiver.Log.Count == 0, "Nothing should be sent.");
    }

    [Fact(DisplayName = "TEST3: A room with no NVX routes nothing and has no preview.")]
    public async Task Router_WithNoNvx_DoesNothing()
    {
        var router = new NvxRouter(null, null);

        Assert.True(!router.IsConfigured && !router.HasRoute("laptop"), "A room with no NVX has no routes.");
        Assert.True(!await router.RouteSourceAsync("laptop", TestContext.Current.CancellationToken), "Expected false.");
        Assert.True(!await router.SetStreamRunningAsync(true, TestContext.Current.CancellationToken), "Expected false.");
        Assert.True(await router.GetPreviewImageAsync(TestContext.Current.CancellationToken) is null, "Expected no preview.");
    }

    [Theory(DisplayName = "TEST4: Start or stop the receiver's stream.")]
    [InlineData(true, "StartReceive")]
    [InlineData(false, "StopReceive")]
    public async Task SetStreamRunning_SendsStartOrStop(bool isRunning, string expectedCall)
    {
        await CreateRouter().SetStreamRunningAsync(isRunning, TestContext.Current.CancellationToken);

        Assert.True(expectedCall == _receiver.Log.Single(), $"Unexpected calls: {string.Join(" | ", _receiver.Log)}.");
    }

    [Fact(DisplayName = "TEST5: Turn the receiver's preview off.")]
    public async Task SetPreviewEnabled_GoesToReceiver()
    {
        var sent = await CreateRouter().SetPreviewEnabledAsync(false, TestContext.Current.CancellationToken);

        Assert.True(sent, "Expected the command to be sent.");
        Assert.True("SetPreviewEnabled False" == _receiver.Log.Single(), $"Unexpected calls: {string.Join(" | ", _receiver.Log)}.");
    }

    [Fact(DisplayName = "TEST6: Fetch the largest available preview image from the receiver.")]
    public async Task GetPreviewImage_DownloadsLargestImage()
    {
        _receiver.Preview = new NvxPreview(true,
        [
            new NvxPreviewImage("small", $"https://{Receiver}/preview/small.jpeg", 240, 135, true),
            new NvxPreviewImage("large", $"https://{Receiver}/preview/large.jpeg", 960, 540, true)
        ]);

        var image = await CreateRouter().GetPreviewImageAsync(TestContext.Current.CancellationToken);

        Assert.True(image is not null, "Expected an image.");
        Assert.True(_receiver.Log.Contains($"DownloadImage https://{Receiver}/preview/large.jpeg"), $"Expected the large image to be downloaded: {string.Join(" | ", _receiver.Log)}.");
    }

    [Fact(DisplayName = "TEST7: Report previews turned off as unavailable rather than turning them on.")]
    public async Task GetPreviewImage_WhenDisabled_ThrowsUnavailable()
    {
        _receiver.Preview = new NvxPreview(false, []);

        await Assert.ThrowsAsync<NvxPreviewUnavailableException>(() => CreateRouter().GetPreviewImageAsync(TestContext.Current.CancellationToken));
        Assert.True(!_receiver.Log.Any(call => call.StartsWith("SetPreviewEnabled")), "Fetching a preview must not change the preview setting.");
    }

    [Fact(DisplayName = "TEST8: Report a preview with no image ready yet as unavailable.")]
    public async Task GetPreviewImage_WithNoImageReady_ThrowsUnavailable()
    {
        _receiver.Preview = new NvxPreview(true, [new NvxPreviewImage("large", "u", 960, 540, false)]);

        await Assert.ThrowsAsync<NvxPreviewUnavailableException>(() => CreateRouter().GetPreviewImageAsync(TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "TEST9: An NVX failure while routing is passed on to the caller.")]
    public async Task RouteSource_WhenReceiverFails_Throws()
    {
        _receiver.Failure = new NvxException("Could not reach the NVX at 10.0.0.50");

        await Assert.ThrowsAsync<NvxException>(() => CreateRouter().RouteSourceAsync("laptop", TestContext.Current.CancellationToken));
    }
}
