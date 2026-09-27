using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Storage;
using Google.Apis.Http;

namespace GameSync.Storage.Drive;

/// <summary>GameSync's OAuth client, from the JSON Google Cloud gives out for a "Desktop app" client.</summary>
public sealed record GoogleClient(string ClientId, string ClientSecret)
{
    public static GoogleClient Parse(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("installed", out var installed))
        {
            throw new FormatException("That file isn't for a Desktop app client: it has no \"installed\" section.");
        }

        var id = installed.TryGetProperty("client_id", out var i) ? i.GetString() : null;
        var secret = installed.TryGetProperty("client_secret", out var s) ? s.GetString() : null;
        return id is { Length: > 0 } && secret is { Length: > 0 }
            ? new GoogleClient(id, secret)
            : throw new FormatException("That file is missing the client id or secret.");
    }
}

/// <summary>The stored sign-in: the refresh token, which lasts, and the current access token, which doesn't.</summary>
internal sealed record GoogleToken(string RefreshToken, string AccessToken, DateTime ExpiresUtc, string Scope);

/// <summary>
/// Signing in to Google Drive (CLOUD-02): in the browser, with PKCE and a redirect to 127.0.0.1 that listens only while
/// sign-in waits (R11). Only the <c>drive.file</c> scope is ever asked for (R9). The token is stored encrypted for this
/// Windows user (R10), refreshed as it expires, and revoked at Google on sign-out (CLOUD-07).
/// </summary>
public sealed class GoogleAuth : IConfigurableHttpClientInitializer, IHttpExecuteInterceptor, IHttpUnsuccessfulResponseHandler
{
    public const string Scope = "https://www.googleapis.com/auth/drive.file";

    private static readonly Uri AuthorizeEndpoint = new("https://accounts.google.com/o/oauth2/v2/auth");
    private static readonly Uri TokenEndpoint = new("https://oauth2.googleapis.com/token");
    private static readonly Uri RevokeEndpoint = new("https://oauth2.googleapis.com/revoke");

    private readonly GoogleClient _client;
    private readonly string _tokenFile;
    private readonly ISecretProtector _protector;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private GoogleToken? _token;

    public GoogleAuth(GoogleClient client, string tokenFile, ISecretProtector protector, HttpClient? http = null)
    {
        _client = client;
        _tokenFile = tokenFile;
        _protector = protector;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public bool IsSignedIn => Load() is not null;

    /// <summary>What Google granted, for checking that it's <c>drive.file</c> alone (R9).</summary>
    public string? GrantedScope => Load()?.Scope;

    /// <summary>
    /// Opens Google's sign-in page through <paramref name="openBrowser"/>, waits on 127.0.0.1 for the redirect, and
    /// stores the token. Nothing listens once this returns.
    /// </summary>
    public async Task SignInAsync(Action<Uri> openBrowser, TimeSpan timeout, CancellationToken ct)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        IReadOnlyDictionary<string, string> answer;
        string redirectUri;
        using (var redirect = new LoopbackRedirect())
        {
            redirectUri = redirect.Uri;
            var query = await new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _client.ClientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = Scope,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = state,
                ["access_type"] = "offline",
                ["prompt"] = "consent",
            }).ReadAsStringAsync(ct);
            openBrowser(new Uri($"{AuthorizeEndpoint}?{query}"));
            answer = await redirect.WaitAsync(timeout, ct);
        }

        if (answer.GetValueOrDefault("state") != state)
        {
            throw new InvalidOperationException("The answer from the browser didn't match this sign-in, so it was ignored. Run 'gamesync signin' again.");
        }

        if (answer.TryGetValue("error", out var error))
        {
            throw new InvalidOperationException(error == "access_denied"
                ? "Sign-in was cancelled in the browser."
                : $"Google didn't sign GameSync in ({error}).");
        }

        var code = answer.GetValueOrDefault("code") ?? throw new InvalidOperationException("Google's answer had no sign-in code. Run 'gamesync signin' again.");
        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _client.ClientId,
            ["client_secret"] = _client.ClientSecret,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        }, keepRefreshToken: null, ct);

        // R9: GameSync asked for drive.file alone, and keeps nothing broader.
        if (token.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(s => s != Scope))
        {
            await RevokeAsync(token.RefreshToken, ct);
            throw new InvalidOperationException($"Google granted more than GameSync asked for ({token.Scope}), so the sign-in was cancelled.");
        }

        Save(token);
    }

    /// <summary>A current access token, refreshed when it's about to expire or when <paramref name="force"/> is set.</summary>
    public async Task<string> GetAccessTokenAsync(bool force, CancellationToken ct)
    {
        var token = Load() ?? throw new CloudException(CloudErrorKind.SignInExpired, "GameSync isn't signed in to Google Drive.");
        if (!force && token.ExpiresUtc > DateTime.UtcNow.AddMinutes(1))
        {
            return token.AccessToken;
        }

        await _refresh.WaitAsync(ct);
        try
        {
            var current = Load() ?? throw new CloudException(CloudErrorKind.SignInExpired, "GameSync isn't signed in to Google Drive.");
            if (current.AccessToken != token.AccessToken || (!force && current.ExpiresUtc > DateTime.UtcNow.AddMinutes(1)))
            {
                return current.AccessToken;
            }

            var fresh = await RequestTokenAsync(new Dictionary<string, string>
            {
                ["client_id"] = _client.ClientId,
                ["client_secret"] = _client.ClientSecret,
                ["refresh_token"] = current.RefreshToken,
                ["grant_type"] = "refresh_token",
            }, current.RefreshToken, ct);
            Save(fresh with { Scope = current.Scope });
            return fresh.AccessToken;
        }
        finally
        {
            _refresh.Release();
        }
    }

    /// <summary>CLOUD-07: revokes the sign-in at Google, then forgets it here. Offline, nothing changes.</summary>
    public async Task SignOutAsync(CancellationToken ct)
    {
        if (Load() is not { } token)
        {
            return;
        }

        await RevokeAsync(token.RefreshToken, ct);
        File.Delete(_tokenFile);
        _token = null;
    }

    public void Initialize(ConfigurableHttpClient httpClient)
    {
        httpClient.MessageHandler.AddExecuteInterceptor(this);
        httpClient.MessageHandler.AddUnsuccessfulResponseHandler(this);
    }

    public async Task InterceptAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(false, cancellationToken));

    /// <summary>A 401 means the access token went stale early: refresh once and let the request try again.</summary>
    public async Task<bool> HandleResponseAsync(HandleUnsuccessfulResponseArgs args)
    {
        if (args.Response.StatusCode != HttpStatusCode.Unauthorized || !args.SupportsRetry || args.CurrentFailedTry > 1)
        {
            return false;
        }

        await GetAccessTokenAsync(true, args.CancellationToken);
        return true;
    }

    private async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            using var response = await _http.PostAsync(RevokeEndpoint, new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refreshToken }), ct);

            // 400 means the token is already invalid, which is as good as revoked.
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.BadRequest)
            {
                throw new CloudException(CloudErrorKind.Other, $"Google didn't cancel the sign-in ({(int)response.StatusCode}). Nothing was changed; try again.");
            }
        }
        catch (HttpRequestException e)
        {
            throw new CloudException(CloudErrorKind.Offline, "You're offline, so Google couldn't be told to cancel the sign-in. Nothing was changed; try again when you're online.", e);
        }
    }

    private async Task<GoogleToken> RequestTokenAsync(Dictionary<string, string> form, string? keepRefreshToken, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new CloudException(CloudErrorKind.Offline, "You're offline, or Google can't be reached.", e);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            using var json = JsonDocument.Parse(body.Length > 0 ? body : "{}");
            var root = json.RootElement;
            if (!response.IsSuccessStatusCode)
            {
                var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
                throw error == "invalid_grant"
                    ? new CloudException(CloudErrorKind.SignInExpired, "The sign-in to Google Drive expired.")
                    : new CloudException(CloudErrorKind.Other, $"Google refused the sign-in ({error ?? ((int)response.StatusCode).ToString()}).");
            }

            var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
            var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : keepRefreshToken;
            if (access is null || refresh is null)
            {
                throw new CloudException(CloudErrorKind.Other, "Google's answer had no lasting sign-in in it. Run 'gamesync signin' again.");
            }

            var seconds = root.TryGetProperty("expires_in", out var x) && x.TryGetInt32(out var s) ? s : 3600;
            var scope = root.TryGetProperty("scope", out var sc) ? sc.GetString() ?? Scope : Scope;
            return new GoogleToken(refresh, access, DateTime.UtcNow.AddSeconds(seconds), scope);
        }
    }

    private GoogleToken? Load()
    {
        if (_token is not null)
        {
            return _token;
        }

        if (!File.Exists(_tokenFile))
        {
            return null;
        }

        try
        {
            return _token = JsonSerializer.Deserialize<GoogleToken>(_protector.Unprotect(File.ReadAllBytes(_tokenFile)), Json.Options);
        }
        catch (Exception e) when (e is CryptographicException or JsonException)
        {
            // Another Windows user's file, or a damaged one: as good as signed out (R10).
            return null;
        }
    }

    private void Save(GoogleToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_tokenFile))!);
        var temp = $"{_tokenFile}.tmp-{Guid.NewGuid():N}";
        File.WriteAllBytes(temp, _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(token, Json.Options)));
        File.Move(temp, _tokenFile, overwrite: true);
        _token = token;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
