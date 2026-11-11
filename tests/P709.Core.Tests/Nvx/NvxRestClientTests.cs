using P709.Core.Nvx;

namespace P709.Core.Tests.Nvx;

public class NvxRestClientTests : IDisposable
{
    private const string Address = "10.0.0.50";

    private readonly SimulatedNvx _nvx = new();
    private readonly NvxRestClient _client;

    public NvxRestClientTests()
    {
        _client = CreateClient();
    }

    public void Dispose() => _client.Dispose();

    private NvxRestClient CreateClient(string password = SimulatedNvx.Password) =>
        new(Address, SimulatedNvx.Username, password, _nvx);

    [Fact(DisplayName = "TEST1: The first call logs in (GET then POST the login page) before sending the request.")]
    public async Task FirstCall_LogsInFirst()
    {
        await _client.StartReceiveAsync(TestContext.Current.CancellationToken);

        var calls = _nvx.Requests.Select(request => $"{request.Method} {request.Path}").ToList();
        var expected = new[] { "GET /userlogin.html", "POST /userlogin.html", "POST /Device" };
        Assert.True(expected.SequenceEqual(calls), $"Unexpected requests: {string.Join(", ", calls)}.");
    }

    [Fact(DisplayName = "TEST2: The login posts the username and password as a form, with Origin and Referer set.")]
    public async Task Login_PostsFormWithOriginAndReferer()
    {
        await _client.LoginAsync(TestContext.Current.CancellationToken);

        var login = _nvx.Requests.Single(request => request.Method == HttpMethod.Post);
        Assert.True("login=admin&passwd=secret" == login.Body, $"Unexpected login body: {login.Body}.");
        Assert.True($"https://{Address}" == login.Header("Origin"), $"Unexpected Origin: {login.Header("Origin")}.");
        Assert.True($"https://{Address}/" == login.Header("Referer"), $"Unexpected Referer: {login.Header("Referer")}.");
        Assert.True("track-1" == login.Cookies.GetValueOrDefault("TRACKID"), "The login must send back the TRACKID cookie from the login page.");
    }

    [Fact(DisplayName = "TEST3: Every API request carries all the session cookies from the login.")]
    public async Task ApiRequest_CarriesSessionCookies()
    {
        await _client.StartReceiveAsync(TestContext.Current.CancellationToken);

        var cookies = _nvx.ApiRequests.Single().Cookies;
        var missing = new[] { "TRACKID", "AuthByPasswd", "userstr", "userid", "iv", "tag" }.Where(name => !cookies.ContainsKey(name)).ToList();
        Assert.True(missing.Count == 0, $"Missing cookies: {string.Join(", ", missing)}.");
    }

    [Fact(DisplayName = "TEST4: A changed AuthByPasswd cookie is used from the next request on, without logging in again.")]
    public async Task ApiRequests_FollowChangingAuthCookie()
    {
        await _client.StartReceiveAsync(TestContext.Current.CancellationToken);
        await _client.StopReceiveAsync(TestContext.Current.CancellationToken);
        await _client.StartReceiveAsync(TestContext.Current.CancellationToken);

        Assert.True(1 == _nvx.LoginCount, $"Expected a single login, got {_nvx.LoginCount}.");
        Assert.True(3 == _nvx.ApiRequests.Count(), "Every request should have been accepted.");
    }

    [Fact(DisplayName = "TEST5: A wrong password raises NvxException, naming the user.")]
    public async Task Login_WithWrongPassword_Throws()
    {
        using var client = CreateClient(password: "wrong");

        var exception = await Assert.ThrowsAsync<NvxException>(() => client.StartReceiveAsync(TestContext.Current.CancellationToken));

        Assert.True(exception.Message.Contains("rejected the login for \"admin\""), $"Expected the login failure in: {exception.Message}");
        Assert.True(!_nvx.ApiRequests.Any(), "Nothing should be sent after a failed login.");
    }

    [Fact(DisplayName = "TEST6: An expired session (403) logs in again and retries the request once.")]
    public async Task ExpiredSession_LogsInAgainAndRetries()
    {
        await _client.StartReceiveAsync(TestContext.Current.CancellationToken);
        _nvx.ExpireSessionOnce = true;

        await _client.StopReceiveAsync(TestContext.Current.CancellationToken);

        Assert.True(2 == _nvx.LoginCount, $"Expected a second login, got {_nvx.LoginCount} logins.");
        Assert.True(_nvx.Requests.Last().Body == NvxRequests.StopReceive(), "The refused request should have been sent again.");
    }

    [Fact(DisplayName = "TEST7: A property the NVX rejects raises NvxException.")]
    public async Task Post_WithRejectedProperty_Throws()
    {
        _nvx.PostReply = """{"Actions":[{"Results":[{"Path":"Device.StreamReceive.Streams[0]","Property":"MulticastAddress","StatusId":-1,"StatusInfo":"Value out of range"}]}]}""";

        var exception = await Assert.ThrowsAsync<NvxException>(() => _client.SetReceiveMulticastAddressAsync("239.8.0.21", TestContext.Current.CancellationToken));

        Assert.True(exception.Message.Contains("MulticastAddress"), $"Expected the rejected property in: {exception.Message}");
    }

    [Fact(DisplayName = "TEST8: Setting the multicast address posts the StreamReceive body as JSON to /Device.")]
    public async Task SetReceiveMulticastAddress_PostsBody()
    {
        await _client.SetReceiveMulticastAddressAsync("239.8.0.21", TestContext.Current.CancellationToken);

        var post = _nvx.ApiRequests.Single();
        Assert.True("/Device" == post.Path, $"Expected POST /Device, got {post.Path}.");
        Assert.True(NvxRequests.SetReceiveMulticastAddress("239.8.0.21") == post.Body, $"Unexpected body: {post.Body}.");
    }

    [Fact(DisplayName = "TEST9: Read the preview from GET /Device/Preview.")]
    public async Task GetPreview_ReadsPreviewObject()
    {
        _nvx.DeviceObjects["Preview"] = """{"Device":{"Preview":{"IsPreviewOutputEnabled":true,"ImageList":{"Image1":{"Name":"preview_540px.jpeg","IPv4Path":"https://10.0.0.50/preview/preview_540px.jpeg","Width":960,"IsImageAvailable":true}}}}}""";

        var actual = await _client.GetPreviewAsync(TestContext.Current.CancellationToken);

        Assert.True("/Device/Preview" == _nvx.ApiRequests.Single().Path, "Expected GET /Device/Preview.");
        Assert.True("https://10.0.0.50/preview/preview_540px.jpeg" == actual.Largest()?.Url, $"Unexpected URL {actual.Largest()?.Url}.");
    }

    [Fact(DisplayName = "TEST10: Download a preview image with the session cookies.")]
    public async Task DownloadImage_ReturnsBytesAndType()
    {
        var actual = await _client.DownloadImageAsync($"https://{Address}/preview/preview_540px.jpeg", TestContext.Current.CancellationToken);

        Assert.True(_nvx.PreviewImage.SequenceEqual(actual.Content), "Image bytes did not match.");
        Assert.True("image/jpeg" == actual.ContentType, $"Unexpected content type {actual.ContentType}.");
        Assert.True(_nvx.ApiRequests.Single().Cookies.ContainsKey("AuthByPasswd"), "The download must carry the session.");
    }

    [Fact(DisplayName = "TEST11: Refuse to send the session to a URL on another host.")]
    public async Task DownloadImage_FromOtherHost_ThrowsWithoutSending()
    {
        await Assert.ThrowsAsync<NvxException>(() => _client.DownloadImageAsync("https://evil.example.com/preview.jpeg", TestContext.Current.CancellationToken));

        Assert.True(_nvx.Requests.Count == 0, "Nothing should be sent anywhere.");
    }

    [Fact(DisplayName = "TEST12: An NVX that cannot be reached raises NvxUnreachableException.")]
    public async Task Unreachable_ThrowsNvxUnreachableException()
    {
        _nvx.IsUnreachable = true;

        var exception = await Assert.ThrowsAsync<NvxUnreachableException>(() => _client.StartReceiveAsync(TestContext.Current.CancellationToken));

        Assert.True(exception.Message.Contains(Address), $"Expected the address in: {exception.Message}");
    }
}
