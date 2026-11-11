using System.Net;
using System.Text;
using P709.Core.Nvx;

namespace P709.Core.Tests.Nvx;

/// <summary>One request the simulated NVX received.</summary>
internal sealed record RecordedRequest(HttpMethod Method, string Url, string Path, string? Body, IReadOnlyDictionary<string, string> Headers)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    // The Cookie header, split into name=value pairs.
    public IReadOnlyDictionary<string, string> Cookies =>
        (Header("Cookie") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair.Length > 1 ? pair[1] : "");
}

/// <summary>
/// Stands in for an NVX at the HTTP level, behaving as the DM-NVX REST API documents: the cookie login on /userlogin.html,
/// 403 for a request without a valid session, a fresh AuthByPasswd cookie on every API reply, and GET/POST on /Device.
/// </summary>
internal sealed class SimulatedNvx : HttpMessageHandler
{
    public const string Username = "admin";
    public const string Password = "secret";

    private int _authGeneration;

    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Replies to GET /Device/&lt;key&gt;, keyed by the path after /Device/.</summary>
    public Dictionary<string, string> DeviceObjects { get; } = [];

    /// <summary>Reply to POST /Device.</summary>
    public string PostReply { get; set; } = OkReply;

    /// <summary>When true, the next API request is refused with 403, as if the session had expired.</summary>
    public bool ExpireSessionOnce { get; set; }

    /// <summary>When set, every request fails as if the NVX could not be reached.</summary>
    public bool IsUnreachable { get; set; }

    /// <summary>Bytes served for any request under /preview/.</summary>
    public byte[] PreviewImage { get; set; } = [0xFF, 0xD8, 0xFF, 0xD9];

    public const string OkReply = """{"Actions":[{"Operation":"SetPartial","Results":[{"Path":"Device.StreamReceive.Streams[0]","Property":"Start","StatusId":0,"StatusInfo":"OK"}],"TargetObject":"StreamReceive","Version":"2.0.1"}]}""";

    public string CurrentAuthCookie => $"crypt%3A{_authGeneration}";

    public IEnumerable<RecordedRequest> ApiRequests => Requests.Where(request => request.Path != NvxRestClient.LoginPath);

    public int LoginCount => Requests.Count(request => request.Method == HttpMethod.Post && request.Path == NvxRestClient.LoginPath);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join("; ", header.Value), StringComparer.OrdinalIgnoreCase);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!.ToString(), request.RequestUri!.AbsolutePath, body, headers);
        Requests.Add(recorded);

        if (IsUnreachable)
        {
            throw new HttpRequestException("No route to host");
        }

        if (recorded.Path == NvxRestClient.LoginPath)
        {
            return recorded.Method == HttpMethod.Get ? LoginPage() : Login(body);
        }

        var cookies = recorded.Cookies;

        if (ExpireSessionOnce || !cookies.TryGetValue(NvxRestClient.AuthCookieName, out var auth) || auth != CurrentAuthCookie)
        {
            ExpireSessionOnce = false;
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        }

        // The NVX hands out a new AuthByPasswd on later requests; the client must use the new one from then on.
        _authGeneration++;
        HttpResponseMessage response;

        if (recorded.Path.StartsWith("/preview/"))
        {
            response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(PreviewImage) };
            response.Content.Headers.ContentType = new("image/jpeg");
        }
        else if (recorded.Method == HttpMethod.Post && recorded.Path == NvxRestClient.DevicePath)
        {
            response = Json(PostReply);
        }
        else if (recorded.Method == HttpMethod.Get && DeviceObjects.TryGetValue(recorded.Path[(NvxRestClient.DevicePath.Length + 1)..], out var reply))
        {
            response = Json(reply);
        }
        else
        {
            response = new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        response.Headers.Add("Set-Cookie", $"{NvxRestClient.AuthCookieName}={CurrentAuthCookie}; Path=/; Secure; HttpOnly");
        return response;
    }

    private static HttpResponseMessage LoginPage()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>login</html>") };
        response.Headers.Add("Set-Cookie", "TRACKID=track-1; Path=/; Secure; HttpOnly");
        return response;
    }

    private HttpResponseMessage Login(string? body)
    {
        if (body != $"login={Username}&passwd={Password}")
        {
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        }

        _authGeneration++;
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri("/", UriKind.Relative);

        foreach (var cookie in new[] { $"{NvxRestClient.AuthCookieName}={CurrentAuthCookie}", "userstr=user-1", "userid=id-1", "iv=iv-1", "tag=tag-1" })
        {
            response.Headers.Add("Set-Cookie", $"{cookie}; Path=/; Secure; HttpOnly");
        }

        return response;
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>A fake NVX receiver: records what it is asked, in order, in <see cref="Log"/>.</summary>
internal sealed class FakeNvxClient(string address) : INvxClient
{
    public List<string> Log { get; } = [];

    public string Address { get; } = address;

    public NvxPreview Preview { get; set; } = new(true, [new NvxPreviewImage("preview_540px.jpeg", $"https://{address}/preview/preview_540px.jpeg", 960, 540, true)]);

    public NvxException? Failure { get; set; }

    public Task SetReceiveMulticastAddressAsync(string multicastAddress, CancellationToken cancellationToken = default) => Record($"SetReceiveMulticastAddress {multicastAddress}");

    public Task StartReceiveAsync(CancellationToken cancellationToken = default) => Record("StartReceive");

    public Task StopReceiveAsync(CancellationToken cancellationToken = default) => Record("StopReceive");

    public async Task<NvxStreamStatus> GetReceiveStatusAsync(CancellationToken cancellationToken = default)
    {
        await Record("GetReceiveStatus");
        return new NvxStreamStatus("Stream Started", "", false);
    }

    public Task SetPreviewEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default) => Record($"SetPreviewEnabled {isEnabled}");

    public async Task<NvxPreview> GetPreviewAsync(CancellationToken cancellationToken = default)
    {
        await Record("GetPreview");
        return Preview;
    }

    public async Task<NvxImage> DownloadImageAsync(string url, CancellationToken cancellationToken = default)
    {
        await Record($"DownloadImage {url}");
        return new NvxImage(Encoding.UTF8.GetBytes(url), "image/jpeg");
    }

    private Task Record(string call)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Log.Add(call);
        return Task.CompletedTask;
    }
}
