using P709.Core.Nvx;

namespace P709.Web.Nvx;

/// <summary>
/// <c>GET /nvx/preview</c>: the NVX receiver's preview image (a snapshot of what the display is being sent), fetched from the
/// receiver by the program and passed on.
/// </summary>
/// <remarks>
/// The panel cannot load the image from the NVX itself: the NVX serves it over HTTPS with a self-signed certificate, behind a
/// cookie-based login the panel does not hold (credentials in the URL do not help, browsers block them on images). The
/// program already holds a logged-in session, so it fetches the image and serves it on the same host and port as the hub,
/// over plain HTTP, where CORS is already set up.
/// </remarks>
public static class NvxPreviewEndpoint
{
    /// <summary>Log category for the endpoint's messages.</summary>
    public const string LogCategory = "P709.Web.Nvx.NvxPreviewEndpoint";

    /// <summary>
    /// The preview URL path. It is relative to the web host: the front-end resolves it against the hub URL's origin.
    /// </summary>
    public const string Path = "/nvx/preview";

    /// <summary>Maps the endpoint.</summary>
    public static IEndpointRouteBuilder MapNvxPreview(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, HandleAsync);
        return endpoints;
    }

    // 200 with the image; 404 if the room has no NVX; 503 if previews are off or not ready; 502 if the NVX failed.
    // The panel may ask every few seconds, so nothing that repeats on every request is logged above debug level: previews off
    // or not ready is a normal state, and an offline NVX is logged once by the web host when it goes offline and comes back.
    // Only an NVX that answers but refuses is logged as a warning (the Crestron error log).
    private static async Task<IResult> HandleAsync(NvxRouter router, HttpResponse response, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(LogCategory);

        // A preview is a live snapshot, so it must never be served from a cache.
        response.Headers.CacheControl = "no-store";

        try
        {
            var image = await router.GetPreviewImageAsync(cancellationToken);

            return image is null
                ? Results.NotFound("This room has no NVX receiver.")
                : Results.File(image.Content, image.ContentType);
        }
        catch (NvxPreviewUnavailableException exception)
        {
            logger.LogInformation("NVX preview unavailable: {Reason}", exception.Message);
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (NvxUnreachableException exception)
        {
            logger.LogDebug("NVX preview could not be fetched: {Reason}", exception.Message);
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (NvxException exception)
        {
            logger.LogWarning(exception, "NVX preview could not be fetched: {Reason}", exception.Message);
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
