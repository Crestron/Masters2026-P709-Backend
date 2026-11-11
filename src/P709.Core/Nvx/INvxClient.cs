namespace P709.Core.Nvx;

/// <summary>
/// Talks to one DM-NVX over its REST API. <see cref="NvxRestClient"/> is the real one; the tests use a fake.
/// Every method logs in first if needed, and throws <see cref="NvxException"/> when the NVX refuses or cannot be reached.
/// </summary>
public interface INvxClient
{
    /// <summary>The NVX's IP address or host name.</summary>
    string Address { get; }

    /// <summary>Receiver: points the stream at a source's multicast address (its StreamLocation becomes <c>rtsp://&lt;address&gt;</c>).</summary>
    Task SetReceiveMulticastAddressAsync(string multicastAddress, CancellationToken cancellationToken = default);

    /// <summary>Receiver: starts receiving the stream.</summary>
    Task StartReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Receiver: stops receiving the stream.</summary>
    Task StopReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Receiver: reads the state of the stream.</summary>
    Task<NvxStreamStatus> GetReceiveStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Turns the preview image generator on or off.</summary>
    Task SetPreviewEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default);

    /// <summary>Reads whether previews are on and where each preview image is.</summary>
    Task<NvxPreview> GetPreviewAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads an image from the NVX (for example a preview), using this client's login.</summary>
    /// <param name="url">The image's URL on this NVX, from <see cref="NvxPreviewImage.Url"/>.</param>
    /// <returns>The image bytes and their content type.</returns>
    Task<NvxImage> DownloadImageAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>An image downloaded from an NVX.</summary>
/// <param name="Content">The image bytes.</param>
/// <param name="ContentType">The media type, for example <c>image/jpeg</c>.</param>
public sealed record NvxImage(byte[] Content, string ContentType);
