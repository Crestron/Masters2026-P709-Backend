using P709.Core.Nvx;

namespace P709.Core.Tests.Nvx;

public class NvxRequestsTests
{
    [Fact(DisplayName = "TEST1: Route the receiver by setting its first stream's location to rtsp://<multicast address>.")]
    public void SetReceiveMulticastAddress_WithAddress_BuildsStreamReceiveBody()
    {
        var actual = NvxRequests.SetReceiveMulticastAddress("239.8.0.21");

        const string expected = """{"Device":{"StreamReceive":{"Streams":[{"StreamLocation":"rtsp://239.8.0.21"}]}}}""";
        Assert.True(expected == actual, $"Expected {expected}, got {actual}.");
    }

    [Fact(DisplayName = "TEST2: Refuse to build a route to a blank multicast address.")]
    public void SetReceiveMulticastAddress_WithBlankAddress_Throws()
    {
        Assert.Throws<ArgumentException>(() => NvxRequests.SetReceiveMulticastAddress(" "));
    }

    [Theory(DisplayName = "TEST3: Start and stop set the trigger property to true on the receiver's first stream.")]
    [InlineData(true, """{"Device":{"StreamReceive":{"Streams":[{"Start":true}]}}}""")]
    [InlineData(false, """{"Device":{"StreamReceive":{"Streams":[{"Stop":true}]}}}""")]
    public void ReceiveCommands_BuildTriggerBody(bool start, string expected)
    {
        var actual = start ? NvxRequests.StartReceive() : NvxRequests.StopReceive();

        Assert.True(expected == actual, $"Expected {expected}, got {actual}.");
    }

    [Theory(DisplayName = "TEST4: Enable or disable the preview images with IsPreviewOutputEnabled.")]
    [InlineData(true, """{"Device":{"Preview":{"IsPreviewOutputEnabled":true}}}""")]
    [InlineData(false, """{"Device":{"Preview":{"IsPreviewOutputEnabled":false}}}""")]
    public void SetPreviewEnabled_BuildsPreviewBody(bool isEnabled, string expected)
    {
        var actual = NvxRequests.SetPreviewEnabled(isEnabled);

        Assert.True(expected == actual, $"Expected {expected}, got {actual}.");
    }
}
