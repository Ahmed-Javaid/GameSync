using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using GameSync.Core.Storage;
using GameSync.Storage.Drive;
using GameSync.Windows;

namespace GameSync.Core.Tests;

/// <summary>Signing in to Google, against a fake Google and a simulated browser: nothing here reaches the internet.</summary>
public class GoogleAuthTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task CLOUD_02_R9_R11_sign_in_uses_the_browser_PKCE_drive_file_only_and_a_127_0_0_1_redirect_that_then_closes()
    {
        using var world = new TestWorld();
        var google = new FakeGoogle();
        var auth = Auth(world, google);
        Uri? opened = null;

        await auth.SignInAsync(url => Browser(opened = url, favicon: true), TimeSpan.FromSeconds(30), Ct);

        var query = HttpUtility.ParseQueryString(opened!.Query);
        var redirect = new Uri(query["redirect_uri"]!);
        Assert.Equal(GoogleAuth.Scope, query["scope"]);
        Assert.Equal("127.0.0.1", redirect.Host);
        Assert.Equal("S256", query["code_challenge_method"]);
        var verifier = google.TokenRequests.Single()["code_verifier"];
        Assert.Equal(query["code_challenge"], Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
        Assert.True(auth.IsSignedIn);
        Assert.Equal(GoogleAuth.Scope, auth.GrantedScope);
        Assert.Equal("access-1", await auth.GetAccessTokenAsync(false, Ct));

        // R11: nothing listens once sign-in is over.
        using var probe = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => probe.ConnectAsync(IPAddress.Loopback, redirect.Port));
    }

    [Fact]
    public async Task R10_the_stored_token_is_encrypted_for_this_Windows_user()
    {
        using var world = new TestWorld();
        var auth = Auth(world, new FakeGoogle());
        await auth.SignInAsync(url => Browser(url), TimeSpan.FromSeconds(30), Ct);

        var stored = await File.ReadAllBytesAsync(TokenFile(world));
        Assert.DoesNotContain("the-refresh-token", Encoding.UTF8.GetString(stored));
        Assert.DoesNotContain("the-refresh-token", Encoding.Unicode.GetString(stored));

        // Without the same user's key (here, other entropy), it can't be read back.
        Assert.ThrowsAny<CryptographicException>(() => ProtectedData.Unprotect(stored, "someone else"u8.ToArray(), DataProtectionScope.CurrentUser));

        // A damaged or foreign file reads as signed out, never as a crash.
        await File.WriteAllBytesAsync(TokenFile(world), [1, 2, 3]);
        Assert.False(Auth(world, new FakeGoogle()).IsSignedIn);
    }

    [Fact]
    public async Task An_answer_for_another_sign_in_is_ignored()
    {
        using var world = new TestWorld();
        var auth = Auth(world, new FakeGoogle());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            auth.SignInAsync(url => Browser(url, state: "forged"), TimeSpan.FromSeconds(30), Ct));

        Assert.False(auth.IsSignedIn);
    }

    [Fact]
    public async Task Cancelling_in_the_browser_says_so_and_stores_nothing()
    {
        using var world = new TestWorld();
        var auth = Auth(world, new FakeGoogle());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            auth.SignInAsync(url => Browser(url, error: "access_denied"), TimeSpan.FromSeconds(30), Ct));

        Assert.Contains("cancelled", error.Message);
        Assert.False(File.Exists(TokenFile(world)));
    }

    [Fact]
    public async Task R9_a_sign_in_that_grants_more_than_drive_file_is_revoked_and_refused()
    {
        using var world = new TestWorld();
        var google = new FakeGoogle { Scope = $"{GoogleAuth.Scope} https://www.googleapis.com/auth/drive" };
        var auth = Auth(world, google);

        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.SignInAsync(url => Browser(url), TimeSpan.FromSeconds(30), Ct));

        Assert.False(auth.IsSignedIn);
        Assert.Equal("the-refresh-token", Assert.Single(google.Revoked));
    }

    [Fact]
    public async Task R11_a_sign_in_nobody_finishes_times_out_and_stops_listening()
    {
        using var world = new TestWorld();
        var auth = Auth(world, new FakeGoogle());
        Uri? opened = null;

        await Assert.ThrowsAsync<TimeoutException>(() => auth.SignInAsync(url => opened = url, TimeSpan.FromMilliseconds(300), Ct));

        var port = new Uri(HttpUtility.ParseQueryString(opened!.Query)["redirect_uri"]!).Port;
        using var probe = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => probe.ConnectAsync(IPAddress.Loopback, port));
    }

    [Fact]
    public async Task CLOUD_04_an_expired_sign_in_is_told_apart()
    {
        using var world = new TestWorld();
        var google = new FakeGoogle();
        var auth = Auth(world, google);
        await auth.SignInAsync(url => Browser(url), TimeSpan.FromSeconds(30), Ct);

        Assert.Equal("access-2", await auth.GetAccessTokenAsync(force: true, Ct));

        google.RefreshError = "invalid_grant";
        var expired = await Assert.ThrowsAsync<CloudException>(() => auth.GetAccessTokenAsync(force: true, Ct));
        Assert.Equal(CloudErrorKind.SignInExpired, expired.Kind);
    }

    [Fact]
    public async Task CLOUD_07_sign_out_revokes_the_token_at_Google_and_offline_changes_nothing()
    {
        using var world = new TestWorld();
        var google = new FakeGoogle();
        var auth = Auth(world, google);
        await auth.SignInAsync(url => Browser(url), TimeSpan.FromSeconds(30), Ct);

        google.Offline = true;
        var offline = await Assert.ThrowsAsync<CloudException>(() => auth.SignOutAsync(Ct));
        Assert.Equal(CloudErrorKind.Offline, offline.Kind);
        Assert.True(auth.IsSignedIn);

        google.Offline = false;
        await auth.SignOutAsync(Ct);
        Assert.Equal("the-refresh-token", Assert.Single(google.Revoked));
        Assert.False(auth.IsSignedIn);
        Assert.False(File.Exists(TokenFile(world)));
    }

    private static GoogleAuth Auth(TestWorld world, FakeGoogle google) =>
        new(new GoogleClient("client-id", "client-secret"), TokenFile(world), new DpapiProtector(), new HttpClient(google));

    private static string TokenFile(TestWorld world) => Path.Combine(world.Root, "google-token.bin");

    /// <summary>The browser: after the user signs in, Google sends it back to GameSync's redirect.</summary>
    private static void Browser(Uri opened, string? state = null, string? error = null, bool favicon = false)
    {
        var query = HttpUtility.ParseQueryString(opened.Query);
        var redirect = query["redirect_uri"]!;
        var answer = error is null ? $"code=the-code&state={state ?? query["state"]}" : $"error={error}&state={query["state"]}";
        _ = Task.Run(async () =>
        {
            using var http = new HttpClient();
            if (favicon)
            {
                using var _ = await http.GetAsync($"{redirect}favicon.ico");
            }

            await http.GetStringAsync($"{redirect}?{answer}");
        });
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Google's token and revoke endpoints.</summary>
    private sealed class FakeGoogle : HttpMessageHandler
    {
        public string Scope { get; set; } = GoogleAuth.Scope;

        public string? RefreshError { get; set; }

        public bool Offline { get; set; }

        public List<Dictionary<string, string>> TokenRequests { get; } = [];

        public List<string> Revoked { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline)
            {
                throw new HttpRequestException("No route to host.");
            }

            var parsed = HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken));
            var form = parsed.AllKeys.OfType<string>().ToDictionary(k => k, k => parsed[k]!);
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/revoke":
                    Revoked.Add(form["token"]);
                    return Json(HttpStatusCode.OK, "{}");
                case "/token" when form["grant_type"] == "authorization_code":
                    TokenRequests.Add(form);
                    return Json(HttpStatusCode.OK,
                        $$"""{"access_token":"access-1","expires_in":3600,"refresh_token":"the-refresh-token","scope":"{{Scope}}","token_type":"Bearer"}""");
                case "/token" when RefreshError is not null:
                    return Json(HttpStatusCode.BadRequest, $$"""{"error":"{{RefreshError}}"}""");
                case "/token":
                    return Json(HttpStatusCode.OK, """{"access_token":"access-2","expires_in":3600,"token_type":"Bearer"}""");
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
