namespace P709.Core.Nvx;

/// <summary>
/// The room's view of its NVX receiver: routes a source to the display by pointing the receiver at the source's multicast
/// address, starts and stops the receiver's stream, and fetches the receiver's preview image (a snapshot of what it is showing).
/// Transmitters are not controlled: they stream to their multicast addresses on their own.
/// </summary>
/// <param name="config">The room's NVX configuration, or null when the room has no NVX: then nothing is routed.</param>
/// <param name="receiver">The client for the receiver at <see cref="NvxConfig.ReceiverAddress"/>, or null when the room has no NVX. The tests pass a fake.</param>
/// <param name="timeProvider">The clock for the offline retry interval. Null uses the system clock; tests pass a fake.</param>
public sealed class NvxRouter(NvxConfig? config, INvxClient? receiver, TimeProvider? timeProvider = null)
{
    // Every call to the receiver goes through the guard, so an offline receiver fails fast and is reported once, not per call.
    private readonly NvxConnectivityGuard? receiver = receiver is null ? null : new NvxConnectivityGuard(receiver, timeProvider);

    /// <summary>
    /// Raised once when the receiver goes offline and once when it comes back, rather than on every failed call. While it is
    /// offline, calls fail at once with <see cref="NvxUnreachableException"/> and the receiver is retried every
    /// <see cref="NvxConnectivityGuard.RetryInterval"/>.
    /// </summary>
    public event Action<NvxConnectivityChange>? ConnectivityChanged
    {
        add
        {
            if (receiver is not null)
            {
                receiver.ConnectivityChanged += value;
            }
        }
        remove
        {
            if (receiver is not null)
            {
                receiver.ConnectivityChanged -= value;
            }
        }
    }

    /// <summary>Whether the receiver answered the last call that reached it. True when the room has no NVX.</summary>
    public bool IsOnline => receiver?.IsOnline ?? true;

    /// <summary>Whether the room has an NVX receiver at all.</summary>
    public bool IsConfigured => config is not null && receiver is not null;

    /// <summary>Whether <paramref name="sourceId"/> arrives over NVX.</summary>
    public bool HasRoute(string? sourceId) => receiver is not null && config?.FindSource(sourceId) is not null;

    /// <summary>
    /// Shows a source on the display: points the receiver at the source's multicast address, starts the stream, and turns on
    /// the receiver's preview images so the preview is ready to fetch.
    /// </summary>
    /// <returns>True if the source was routed; false if it does not arrive over NVX (nothing is sent).</returns>
    /// <exception cref="NvxException">The receiver refused a request or could not be reached.</exception>
    public async Task<bool> RouteSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        var route = config?.FindSource(sourceId);

        if (receiver is null || route is null)
        {
            return false;
        }

        // TODO: Lab 2.2b - Route the source's multicast stream to the receiver
        // Hint: Three calls on the receiver, in this order: point it at the route's MulticastAddress (SetReceiveMulticastAddressAsync),
        //       start the receive stream, then enable its preview images.
        // Ref:  INvxClient
        // Add your work below this line

        // Lab 2.2b - End

        return true;
    }

    /// <summary>Starts or stops the receiver's stream, leaving its multicast address as it is.</summary>
    /// <returns>True if the command was sent; false if the room has no NVX.</returns>
    /// <exception cref="NvxException">The receiver refused the request or could not be reached.</exception>
    public async Task<bool> SetStreamRunningAsync(bool isRunning, CancellationToken cancellationToken = default)
    {
        if (receiver is null)
        {
            return false;
        }

        // TODO: Lab 2.3b - Start or stop the receiver's stream
        // Hint: One line each way: StartReceiveAsync or StopReceiveAsync on the receiver.
        // Add your work below this line

        // Lab 2.3b - End

        return true;
    }

    /// <summary>Turns the receiver's preview images on or off.</summary>
    /// <returns>True if the command was sent; false if the room has no NVX.</returns>
    /// <exception cref="NvxException">The receiver refused the request or could not be reached.</exception>
    public async Task<bool> SetPreviewEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default)
    {
        if (receiver is null)
        {
            return false;
        }

        await receiver.SetPreviewEnabledAsync(isEnabled, cancellationToken);
        return true;
    }

    /// <summary>Downloads the largest preview image from the receiver: a snapshot of what the display is being sent.</summary>
    /// <returns>The image, or null if the room has no NVX.</returns>
    /// <exception cref="NvxPreviewUnavailableException">Previews are turned off, or no image is ready yet.</exception>
    /// <exception cref="NvxException">The receiver refused the request or could not be reached.</exception>
    public async Task<NvxImage?> GetPreviewImageAsync(CancellationToken cancellationToken = default)
    {
        if (receiver is null)
        {
            return null;
        }

        // TODO: Lab 2.4b - Fetch the receiver's preview image
        // Hint: Read the receiver's preview (GetPreviewAsync). If it is not enabled, or it has no Largest() image, throw an
        //       NvxPreviewUnavailableException saying which. Otherwise download that image's Url with DownloadImageAsync:
        //       the receiver's client holds the login, which is why the program fetches the image for the panel.
        // Add your work below this line

        // Lab 2.4b - End

        return null;
    }
}

/// <summary>The receiver is reachable but has no preview image to give: previews are off, or the first image is not ready yet.</summary>
public sealed class NvxPreviewUnavailableException : NvxException
{
    /// <summary>Creates the exception.</summary>
    public NvxPreviewUnavailableException(string message)
        : base(message)
    {
    }
}
