using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace P709.Core.Nvx;

/// <summary>Reads the JSON an NVX sends back. Kept apart from the HTTP code so every shape the API documents can be tested from a string.</summary>
public static class NvxResponses
{
    /// <summary>
    /// Checks the reply to a POST. The NVX answers <c>{"Actions":[{"Results":[{"Path":..,"Property":..,"StatusId":..,"StatusInfo":..}]}]}</c>,
    /// one result per property it was sent; a negative <c>StatusId</c> means that property was not applied.
    /// </summary>
    /// <param name="json">The response body.</param>
    /// <exception cref="NvxException">Any result has a negative StatusId, listing each one that failed.</exception>
    public static void EnsureSuccess(string json)
    {
        var results = Parse(json).SelectTokens("Actions[*].Results[*]").OfType<JObject>();

        var failures = results
            .Where(result => (result.Value<int?>("StatusId") ?? 0) < 0)
            .Select(result =>
            {
                var property = result.Value<string>("Property");
                var path = result.Value<string>("Path") + (string.IsNullOrEmpty(property) ? "" : "." + property);
                return $"{path}: {result.Value<string>("StatusInfo")} (StatusId {result.Value<int>("StatusId")})";
            })
            .ToList();

        if (failures.Count > 0)
        {
            throw new NvxException("The NVX rejected the request. " + string.Join("; ", failures));
        }
    }

    /// <summary>Reads <c>GET /Device/StreamReceive</c>: the state of the receiver's first stream.</summary>
    /// <exception cref="NvxException">The reply has no stream.</exception>
    public static NvxStreamStatus ParseReceiveStatus(string json)
    {
        var stream = Parse(json).SelectToken("Device.StreamReceive.Streams[0]") as JObject
            ?? throw new NvxException("The NVX reply has no Device.StreamReceive.Streams[0].");

        return new NvxStreamStatus(
            stream.Value<string>("Status") ?? "",
            stream.Value<string>("MulticastAddress") ?? "",
            stream.Value<bool?>("Processing") ?? false);
    }

    /// <summary>
    /// Reads <c>GET /Device/Preview</c> (Preview object version 2.1.0, firmware 6 and later): whether preview images are being
    /// produced, and where each one can be downloaded.
    /// </summary>
    /// <param name="json">The response body.</param>
    /// <exception cref="NvxException">The reply has no Preview object.</exception>
    public static NvxPreview ParsePreview(string json)
    {
        var preview = Parse(json).SelectToken("Device.Preview") as JObject
            ?? throw new NvxException("The NVX reply has no Device.Preview.");

        // ImageList is a dictionary of images ("Image1", "Image2", ...), one per size. Each reports its full URL in IPv4Path.
        var images = (preview["ImageList"] as JObject)?.Properties().Select(property => property.Value).OfType<JObject>() ?? [];

        var list = images
            .Where(image => !string.IsNullOrWhiteSpace(image.Value<string>("IPv4Path")))
            .Select(image => new NvxPreviewImage(
                image.Value<string>("Name") ?? "",
                image.Value<string>("IPv4Path")!,
                image.Value<int?>("Width") ?? 0,
                image.Value<int?>("Height") ?? 0,
                image.Value<bool?>("IsImageAvailable") ?? false))
            .ToList();

        return new NvxPreview(preview.Value<bool?>("IsPreviewOutputEnabled") ?? false, list);
    }

    private static JObject Parse(string json)
    {
        try
        {
            return JObject.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new NvxException($"The NVX reply is not valid JSON: {exception.Message}", exception);
        }
    }
}

/// <summary>State of an NVX stream.</summary>
/// <param name="Status">What the NVX reports, for example <c>Stream Stopped</c>.</param>
/// <param name="MulticastAddress">The multicast address the stream uses; empty if none is set.</param>
/// <param name="Processing">True while the NVX is still acting on the last command. Wait for false before sending another.</param>
public sealed record NvxStreamStatus(string Status, string MulticastAddress, bool Processing);

/// <summary>An NVX's preview images.</summary>
/// <param name="IsEnabled">Whether the NVX is producing preview images (<c>IsPreviewOutputEnabled</c>).</param>
/// <param name="Images">Every image size the NVX offers.</param>
public sealed record NvxPreview(bool IsEnabled, IReadOnlyList<NvxPreviewImage> Images)
{
    /// <summary>The largest image that is ready to download, or null if there is none.</summary>
    public NvxPreviewImage? Largest() =>
        Images.Where(image => image.IsAvailable).OrderByDescending(image => image.Width).FirstOrDefault();
}

/// <summary>One preview image an NVX offers.</summary>
/// <param name="Name">File name, for example <c>preview_540px.jpeg</c>.</param>
/// <param name="Url">Where to download it. It is on the NVX, so it needs the NVX's login and its self-signed certificate.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="IsAvailable">Whether the image has been produced yet.</param>
public sealed record NvxPreviewImage(string Name, string Url, int Width, int Height, bool IsAvailable);
