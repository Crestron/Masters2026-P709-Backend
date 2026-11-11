using P709.Core.Nvx;

namespace P709.Core.Tests.Nvx;

public class NvxResponsesTests
{
    // The Preview reply documented for Preview 2.1.0, trimmed to two images.
    private const string PreviewReply = """
        {"Device":{"Preview":{"IsPreviewOutputEnabled":true,"StatusCode":0,"HostPreviewImage":"Local",
          "LocalPreview":{"RelativePath":"preview","IPv4Path":"https://10.0.2.39/preview/","FQDNPath":"https://NVX-363-Tx/preview/"},
          "BaseFileName":"preview",
          "ImageList":{
            "Image1":{"IsImageAvailable":true,"Name":"preview_540px.jpeg","Size":"13044","IPv4Path":"https://10.0.2.39/preview/preview_540px.jpeg","Width":960,"Height":540},
            "Image3":{"IsImageAvailable":true,"Name":"preview_135px.jpeg","Size":"2044","IPv4Path":"https://10.0.2.39/preview/preview_135px.jpeg","Width":240,"Height":135}},
          "Version":"2.1.0"}}}
        """;

    [Fact(DisplayName = "TEST1: Accept a reply where every property was applied.")]
    public void EnsureSuccess_WithStatusZero_DoesNotThrow()
    {
        NvxResponses.EnsureSuccess(SimulatedNvx.OkReply);
    }

    [Fact(DisplayName = "TEST2: Accept an informational StatusId, such as restart required.")]
    public void EnsureSuccess_WithPositiveStatus_DoesNotThrow()
    {
        NvxResponses.EnsureSuccess("""{"Actions":[{"Results":[{"Path":"Device.DeviceSpecific","Property":"DeviceMode","StatusId":1,"StatusInfo":"Restart required"}]}]}""");
    }

    [Fact(DisplayName = "TEST3: Reject a reply with a negative StatusId, naming the property and the reason.")]
    public void EnsureSuccess_WithNegativeStatus_ThrowsWithPath()
    {
        const string reply = """{"Actions":[{"Results":[{"Path":"Device.StreamReceive.Streams[0]","Property":"MulticastAddress","StatusId":-1,"StatusInfo":"Value out of range"}]}]}""";

        var exception = Assert.Throws<NvxException>(() => NvxResponses.EnsureSuccess(reply));

        Assert.True(exception.Message.Contains("Device.StreamReceive.Streams[0].MulticastAddress"), $"Expected the property path in: {exception.Message}");
        Assert.True(exception.Message.Contains("Value out of range"), $"Expected the NVX's reason in: {exception.Message}");
    }

    [Fact(DisplayName = "TEST4: Reject a reply that is not JSON.")]
    public void EnsureSuccess_WithHtml_ThrowsNvxException()
    {
        Assert.Throws<NvxException>(() => NvxResponses.EnsureSuccess("<html>login</html>"));
    }

    [Fact(DisplayName = "TEST5: Read the image URLs, sizes and enabled state from a current Preview reply.")]
    public void ParsePreview_WithCurrentFirmware_UsesReportedUrls()
    {
        var actual = NvxResponses.ParsePreview(PreviewReply);

        Assert.True(actual.IsEnabled, "Expected previews to be enabled.");
        Assert.True(2 == actual.Images.Count, $"Expected 2 images, got {actual.Images.Count}.");
        Assert.True("https://10.0.2.39/preview/preview_540px.jpeg" == actual.Images[0].Url, $"Wrong URL: {actual.Images[0].Url}.");
        Assert.True(960 == actual.Images[0].Width && 540 == actual.Images[0].Height, "Wrong image size.");
    }

    [Fact(DisplayName = "TEST6: Skip an image the NVX gives no URL for, and treat one that does not say it is available as not ready.")]
    public void ParsePreview_WithIncompleteImages_SkipsOrMarksNotReady()
    {
        const string reply = """{"Device":{"Preview":{"IsPreviewOutputEnabled":true,"ImageList":{"Image1":{"Name":"no_url.jpeg"},"Image2":{"Name":"preview_270px.jpeg","IPv4Path":"https://10.0.0.21/preview/preview_270px.jpeg","Width":480}}}}}""";

        var actual = NvxResponses.ParsePreview(reply);

        Assert.True("preview_270px.jpeg" == actual.Images.Single().Name, "Expected only the image with a URL.");
        Assert.True(!actual.Images.Single().IsAvailable, "An image that does not report IsImageAvailable should not be treated as ready.");
    }

    [Fact(DisplayName = "TEST7: Pick the largest image that is available.")]
    public void Largest_WithMixedImages_ReturnsLargestAvailable()
    {
        var preview = new NvxPreview(true,
        [
            new NvxPreviewImage("big", "u1", 960, 540, false),
            new NvxPreviewImage("medium", "u2", 480, 270, true),
            new NvxPreviewImage("small", "u3", 240, 135, true)
        ]);

        var actual = preview.Largest();

        Assert.True("medium" == actual?.Name, $"Expected the largest available image, got {actual?.Name}.");
    }

    [Fact(DisplayName = "TEST8: Read the receiver's stream status.")]
    public void ParseReceiveStatus_WithDocumentedReply_ReadsStatus()
    {
        const string reply = """{"Device":{"StreamReceive":{"Version":"2.0.1","Streams":[{"Status":"Stream Stopped","MulticastAddress":"239.8.0.21","Processing":false,"Start":false}]}}}""";

        var actual = NvxResponses.ParseReceiveStatus(reply);

        Assert.True(new NvxStreamStatus("Stream Stopped", "239.8.0.21", false) == actual, $"Unexpected status {actual}.");
    }
}
