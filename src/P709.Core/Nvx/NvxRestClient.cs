using System.Net;
using System.Text;

namespace P709.Core.Nvx;

/// <summary>
/// Talks to one DM-NVX over HTTPS using its REST API. Logs in on first use and again whenever the NVX says the session has
/// expired (403), so callers never deal with the login themselves.
/// </summary>
/// <remarks>
/// <para>
/// The NVX keeps the session in cookies. The login sets six of them (<c>TRACKID</c>, <c>userstr</c>, <c>userid</c>, <c>iv</c>,
/// <c>tag</c>, <c>AuthByPasswd</c>); every later request must send them all back, and <c>AuthByPasswd</c> changes as the session
/// goes on. This class keeps them itself, refreshing them from every response, rather than leaving it to the HTTP handler, so
/// the behaviour is visible here and can be tested with a fake handler.
/// </para>
/// <para>
/// An NVX ships with a self-signed certificate, so <see cref="CreateHandler"/> accepts any certificate. That is fine for a lab
/// network; on a production network install a trusted certificate on the NVX and validate it instead.
/// </para>
/// </remarks>
public sealed class NvxRestClient : INvxClient, IDisposable
{
    /// <summary>Login page: GET it for a TRACKID cookie, then POST the username and password to it.</summary>
    public const string LoginPath = "/userlogin.html";

    /// <summary>Root of every object: GET <c>/Device/&lt;Object&gt;</c> to read, POST a body rooted at <c>Device</c> here to change.</summary>
    public const string DevicePath = "/Device";

    /// <summary>The cookie that proves the login succeeded.</summary>
    public const string AuthCookieName = "AuthByPasswd";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly string _username;
    private readonly string _password;
    private readonly SemaphoreSlim _loginGate = new(1, 1);

    private CookieContainer _cookies = new();
    private bool _isLoggedIn;

    /// <summary>Creates a client. Nothing is sent until the first call.</summary>
    /// <param name="address">The NVX's IP address or host name.</param>
    /// <param name="username">Login username.</param>
    /// <param name="password">Login password.</param>
    /// <param name="handler">The HTTP handler to send through. Null uses <see cref="CreateHandler"/>; tests pass a fake.</param>
    public NvxRestClient(string address, string username, string password, HttpMessageHandler? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        Address = address;
        _username = username;
        _password = password;
        _baseUri = new Uri($"https://{address}/");
        _http = new HttpClient(handler ?? CreateHandler(), disposeHandler: true) { BaseAddress = _baseUri, Timeout = RequestTimeout };
    }

    /// <inheritdoc />
    public string Address { get; }

    /// <summary>The handler used with a real NVX: cookies are left to this class, redirects are not followed, and the NVX's self-signed certificate is accepted.</summary>
    public static HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
        UseCookies = false,
        AllowAutoRedirect = false,
        // An NVX that is off or unplugged never answers the connection: give up on it well before the request timeout.
        ConnectTimeout = ConnectTimeout,
        SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true }
    };

    /// <summary>
    /// Logs in, replacing any earlier session. Called automatically by every other method, so only call it to check the
    /// credentials up front.
    /// </summary>
    /// <exception cref="NvxException">The NVX rejected the username or password, or could not be reached.</exception>
    public async Task LoginAsync(CancellationToken cancellationToken = default)
    {
        await _loginGate.WaitAsync(cancellationToken);

        try
        {
            StartNewSession();

            // Step 1: the NVX hands out a TRACKID cookie with its login page.
            await GetLoginPageAsync(cancellationToken);

            // Step 2: send the username and password.
            // TODO: Lab 2.1a - Send the username and password to the NVX
            // Hint: Create a POST request to LoginPath whose Content is the credentials as a web form: a FormUrlEncodedContent
            //       built from a Dictionary holding "login" (_username) and "passwd" (_password). Send it with SendAsync and
            //       keep the reply in a variable called loginResponse: step 3 checks it.
            //       Dispose both the request and the reply (using var ...).
            // Ref:  DM-NVX REST API, Authentication
            //       https://sdkcon78221.crestron.com/sdk/DM_NVX_REST_API/Content/Topics/Authentication.htm
            // Add your work below this line

            // Lab 2.1a - End

            // Step 3: check the NVX accepted the login.
            // TODO: Lab 2.1b - Check the login worked
            // Hint: SendAsync has stored every cookie the reply set. The NVX only sets the AuthByPasswd cookie when the
            //       username and password are right, so check for it with HasCookie(AuthCookieName). If it is missing, throw an
            //       NvxException that names the NVX (Address) and the user, and include the reply's status code to help.
            // Add your work below this line

            // Lab 2.1b - End

            _isLoggedIn = true;
        }
        finally
        {
            _loginGate.Release();
        }
    }

    /// <inheritdoc />
    public Task SetReceiveMulticastAddressAsync(string multicastAddress, CancellationToken cancellationToken = default) =>
        PostDeviceAsync(NvxRequests.SetReceiveMulticastAddress(multicastAddress), cancellationToken);

    /// <inheritdoc />
    public Task StartReceiveAsync(CancellationToken cancellationToken = default) =>
        PostDeviceAsync(NvxRequests.StartReceive(), cancellationToken);

    /// <inheritdoc />
    public Task StopReceiveAsync(CancellationToken cancellationToken = default) =>
        PostDeviceAsync(NvxRequests.StopReceive(), cancellationToken);

    /// <inheritdoc />
    public async Task<NvxStreamStatus> GetReceiveStatusAsync(CancellationToken cancellationToken = default) =>
        NvxResponses.ParseReceiveStatus(await GetDeviceAsync("StreamReceive", cancellationToken));

    /// <inheritdoc />
    public Task SetPreviewEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default) =>
        PostDeviceAsync(NvxRequests.SetPreviewEnabled(isEnabled), cancellationToken);

    /// <inheritdoc />
    public async Task<NvxPreview> GetPreviewAsync(CancellationToken cancellationToken = default) =>
        NvxResponses.ParsePreview(await GetDeviceAsync("Preview", cancellationToken));

    /// <inheritdoc />
    /// <exception cref="NvxException">The URL is not on this NVX (the login cookies must never be sent elsewhere), or the download failed.</exception>
    public async Task<NvxImage> DownloadImageAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.Equals(uri.Host, _baseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new NvxException($"Refusing to download \"{url}\": it is not on the NVX at {Address}.");
        }

        using var response = await SendAuthenticatedAsync(() => new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";

        return new NvxImage(content, contentType);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
        _loginGate.Dispose();
    }

    // Forgets the previous session: its cookies and the logged-in flag.
    private void StartNewSession()
    {
        _isLoggedIn = false;
        _cookies = new CookieContainer();
    }

    // Only the TRACKID cookie is wanted from the login page. SendAsync copies it into _cookies, the client's cookie store,
    // and sends it on the next request, so the reply itself is not read.
    private async Task GetLoginPageAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LoginPath);
        using var response = await SendAsync(request, cancellationToken);
    }

    // GET /Device/<deviceObject> and return the body.
    private async Task<string> GetDeviceAsync(string deviceObject, CancellationToken cancellationToken)
    {
        using var response = await SendAuthenticatedAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{DevicePath}/{deviceObject}"), cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    // POST a body rooted at Device to /Device, and fail if any property was rejected.
    private async Task PostDeviceAsync(string body, CancellationToken cancellationToken)
    {
        using var response = await SendAuthenticatedAsync(() => new HttpRequestMessage(HttpMethod.Post, DevicePath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }, cancellationToken);

        NvxResponses.EnsureSuccess(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    // Sends a request with the session, logging in first if needed and once more if the session has expired (403).
    // The request is built by a callback because a sent HttpRequestMessage cannot be sent again.
    private async Task<HttpResponseMessage> SendAuthenticatedAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        if (!_isLoggedIn)
        {
            await LoginAsync(cancellationToken);
        }

        var response = await CreateAndSendAsync(createRequest, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            response.Dispose();
            await LoginAsync(cancellationToken);
            response = await CreateAndSendAsync(createRequest, cancellationToken);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            response.Dispose();
            throw new NvxException($"The NVX at {Address} refused the request even after logging in again. Check the nvx username and password.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new NvxException($"The NVX at {Address} answered HTTP {status}.");
        }

        return response;
    }

    // Builds a brand-new request and sends it. An HttpRequestMessage can only be sent once, so a retry needs a fresh one.
    // The request (and its body) is disposed once sent; the response is returned for the caller to dispose.
    private async Task<HttpResponseMessage> CreateAndSendAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        using var request = createRequest();
        return await SendAsync(request, cancellationToken);
    }

    // Every request carries the session cookies; every response's cookies replace the ones held.
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var cookieHeader = _cookies.GetCookieHeader(_baseUri);

        if (cookieHeader.Length > 0)
        {
            request.Headers.Add("Cookie", cookieHeader);
        }

        // The NVX expects a browser-like Origin on POSTs (the login included) and a Referer on every request.
        if (request.Method == HttpMethod.Post)
        {
            request.Headers.Add("Origin", $"https://{Address}");
        }

        request.Headers.Referrer ??= _baseUri;

        HttpResponseMessage response;

        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new NvxUnreachableException($"Could not reach the NVX at {Address}: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NvxUnreachableException($"The NVX at {Address} did not answer within {RequestTimeout.TotalSeconds:0} seconds.", exception);
        }

        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                _cookies.SetCookies(_baseUri, setCookie);
            }
        }

        return response;
    }

    private bool HasCookie(string name) => _cookies.GetCookies(_baseUri)[name] is { Value.Length: > 0 };
}
