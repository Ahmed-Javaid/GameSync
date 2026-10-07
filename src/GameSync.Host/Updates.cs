using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameSync.Core.State;

namespace GameSync.Host;

/// <summary>A newer GameSync on GitHub Releases: its version, what's new, its page, and where its installer and signature are.</summary>
public sealed record UpdateRelease(Version Version, string Page, IReadOnlyList<string> Notes, Uri Installer, Uri Signature);

/// <summary>A newer GameSync downloaded and checked, waiting for Restart to update.</summary>
public sealed record ReadyUpdate(Version Version, string Installer, string Signature, string Page, IReadOnlyList<string> Notes);

/// <summary>What a check found: a newer release, or nothing newer, or why it couldn't tell (<see cref="Offline"/> when GitHub couldn't be reached).</summary>
public sealed record UpdateCheck(UpdateRelease? Newer, string? Problem = null, bool Offline = false);

/// <summary>Why the last check or download didn't get through.</summary>
public sealed record UpdateProblem(string Reason, bool Offline, DateTime AtUtc);

/// <summary>R19: an update that came down but didn't pass GameSync's own check, so it wasn't kept.</summary>
public sealed record UpdateRefusal(Version Version, string Reason, DateTime AtUtc);

/// <summary>A newer GameSync coming down: which, and how much of how much.</summary>
public readonly record struct UpdateProgress(Version Version, long Done, long Total);

/// <summary>Settings → Updates: everything it says, read from this PC alone.</summary>
public sealed record UpdatesView(Version Current, bool Installed, bool Daily, DateTime? CheckedUtc, ReadyUpdate? Ready, UpdateRefusal? Refused, UpdateProblem? Problem);

/// <summary>
/// PKG-03, R19 (design system version 53, Settings → Updates): GameSync asks GitHub for its latest release once a day,
/// downloads a newer one into the data folder's <c>updates</c> in the background, and installs it only when the person
/// chooses Restart to update. Nothing is kept, and nothing runs, unless GameSync's own release signature checks out
/// (<see cref="ReleaseManifest"/>): the signature comes first, the installer is hashed as it comes and never allowed past
/// the size it gives, and it's checked again, held so nothing can change it, just before it starts. Releases come from
/// GitHub alone; <c>GAMESYNC_UPDATE_FEED</c> points at another feed shaped like GitHub's, on this PC, for trying an update
/// without GitHub, and the signature still decides.
/// </summary>
public sealed partial class Updates : IDisposable
{
    public static readonly Uri GitHubFeed = new("https://api.github.com/repos/Ahmed-Javaid/GameSync/releases/latest");
    public const string FeedVariable = "GAMESYNC_UPDATE_FEED";
    public const string DailyKey = "updates.daily";
    public const string CheckedKey = "updates.checked";
    public const string AttemptKey = "updates.attempt";
    public const string ProblemKey = "updates.problem";
    public const string RefusedKey = "updates.refused";
    public const string ToldKey = "updates.told";

    /// <summary>How often GameSync asks, and how soon it asks again after GitHub couldn't be reached.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromDays(1);
    public static readonly TimeSpan AgainAfterProblem = TimeSpan.FromHours(6);

    private const int MostFeed = 1 << 20;
    private const int MostSignature = 16 * 1024;
    private const int MostHops = 5;

    private readonly string _dataDir;
    private readonly HttpClient _http;
    private readonly string _publicKey;
    private readonly Uri _feed;
    private readonly bool _ownFeed;
    private readonly Func<DateTime> _utcNow;

    public Updates(string dataDir, HttpMessageHandler? handler = null, string? publicKey = null, Version? current = null, Uri? feed = null, Func<DateTime>? utcNow = null)
    {
        _dataDir = dataDir;
        _publicKey = publicKey ?? ReleaseKey.Public;
        Current = current ?? Running;
        var variable = Environment.GetEnvironmentVariable(FeedVariable);
        _feed = feed ?? (Uri.TryCreate(variable, UriKind.Absolute, out var own) ? own : GitHubFeed);
        _ownFeed = _feed != GitHubFeed;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) }, disposeHandler: handler is null)
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameSync", Current.ToString(3)));
    }

    /// <summary>The version this GameSync is: Directory.Build.props' VersionPrefix.</summary>
    public static Version Running => typeof(Updates).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 0, 0);

    public Version Current { get; }

    /// <summary>Where GameSync's installer puts it, for this person alone (PKG-01).</summary>
    public static string InstallFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "GameSync");

    /// <summary>
    /// This copy is the one GameSync's installer put there. A build, or the zip, runs from anywhere else, and an update
    /// would be installed beside it rather than over it, so those only say a newer one is out.
    /// </summary>
    public static bool IsInstalledCopy => SameFolder(AppContext.BaseDirectory, InstallFolder);

    /// <summary>Where downloads wait: the data folder's <c>updates</c>.</summary>
    public string Folder => Path.Combine(_dataDir, "updates");

    public void Dispose() => _http.Dispose();

    /// <summary>Settings → Updates, read from this PC alone.</summary>
    public UpdatesView Read()
    {
        using var state = new StateStore(_dataDir);
        return new UpdatesView(Current, IsInstalledCopy, IsDaily(state), Time(state.GetSetting(CheckedKey)), FindReady(), Refusal(state), Problem(state));
    }

    public static bool IsDaily(StateStore state) => state.GetSetting(DailyKey) != "off";

    public static void SetDaily(string dataDir, bool on)
    {
        using var state = new StateStore(dataDir);
        state.SetSetting(DailyKey, on ? "" : "off");
    }

    /// <summary>Whether the daily check is due: on, and a day since the last try (six hours when GitHub couldn't be reached).</summary>
    public bool Due()
    {
        using var state = new StateStore(_dataDir);
        if (!IsDaily(state))
        {
            return false;
        }

        var last = Time(state.GetSetting(AttemptKey));
        return last is null || _utcNow() - last >= (Problem(state) is null ? Every : AgainAfterProblem);
    }

    /// <summary>Asks GitHub for the latest release; <see cref="UpdateCheck.Newer"/> when it's newer than this one.</summary>
    public async Task<UpdateCheck> CheckAsync(CancellationToken ct)
    {
        UpdateCheck check;
        try
        {
            using var response = await GetAsync(_feed, "application/vnd.github+json", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // No release yet.
                check = new UpdateCheck(null);
            }
            else
            {
                response.EnsureSuccessStatusCode();
                check = Parse(await ReadAsync(response, MostFeed, ct));
            }
        }
        catch (HttpRequestException e)
        {
            check = new UpdateCheck(null, Offline(e) ? "GitHub couldn't be reached; this PC seems to be offline" : $"GitHub didn't answer as it should ({e.Message})", Offline(e));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            check = new UpdateCheck(null, "GitHub didn't answer in time", Offline: true);
        }
        catch (Exception e) when (e is JsonException or ReleaseRefusedException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            check = new UpdateCheck(null, $"GitHub's answer couldn't be read ({e.Message})");
        }

        using var state = new StateStore(_dataDir);
        var now = Stamp(_utcNow());
        state.SetSetting(AttemptKey, now);
        if (check.Problem is null)
        {
            state.SetSetting(CheckedKey, now);
            state.SetSetting(ProblemKey, "");
        }
        else
        {
            state.SetSetting(ProblemKey, $"{(check.Offline ? "offline" : "failed")}\t{now}\t{check.Problem}");
        }

        return check;
    }

    /// <summary>
    /// Downloads <paramref name="release"/>: its signature first, then its installer, hashed as it comes. Throws
    /// <see cref="ReleaseRefusedException"/>, keeping nothing, when it isn't exactly what GameSync's release key signed
    /// (and remembers why, for Settings); <see cref="HttpRequestException"/> when the download didn't get through.
    /// </summary>
    public async Task<ReadyUpdate> DownloadAsync(UpdateRelease release, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Folder);
        var part = Path.Combine(Folder, ReleaseManifest.NameFor(release.Version) + ".part");
        try
        {
            string signed;
            using (var response = await GetAsync(release.Signature, "application/octet-stream", ct))
            {
                response.EnsureSuccessStatusCode();
                signed = await ReadAsync(response, MostSignature, ct);
            }

            var manifest = ReleaseManifest.Verify(signed, _publicKey);
            if (manifest.Version != release.Version)
            {
                throw new ReleaseRefusedException($"its signature is for {manifest.Version.ToString(3)}, not {release.Version.ToString(3)}");
            }

            if (manifest.Version <= Current)
            {
                throw new ReleaseRefusedException("it isn't newer than the GameSync running");
            }

            using (var response = await GetAsync(release.Installer, "application/octet-stream", ct))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    done += read;
                    if (done > manifest.Size)
                    {
                        throw new ReleaseRefusedException("it's bigger than its signature says");
                    }

                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    progress?.Report((done, manifest.Size));
                }

                if (done != manifest.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != manifest.Sha256)
                {
                    throw new ReleaseRefusedException("it isn't the installer its signature describes, so it may have been changed on the way");
                }
            }

            var installer = Path.Combine(Folder, manifest.File);
            File.Move(part, installer, overwrite: true);
            var signature = Path.Combine(Folder, ReleaseManifest.SignatureNameFor(manifest.Version));
            File.WriteAllText(signature, signed, new UTF8Encoding(false));
            File.WriteAllText(NotesPath(manifest.Version), JsonSerializer.Serialize(new Notes(release.Page, [.. release.Notes])));
            using (var state = new StateStore(_dataDir))
            {
                state.SetSetting(RefusedKey, "");
            }

            return new ReadyUpdate(manifest.Version, installer, signature, release.Page, release.Notes);
        }
        catch (ReleaseRefusedException e)
        {
            using var state = new StateStore(_dataDir);
            state.SetSetting(RefusedKey, $"{release.Version.ToString(3)}\t{Stamp(_utcNow())}\t{e.Message}");
            throw;
        }
        finally
        {
            TryDelete(part);
        }
    }

    /// <summary>The daily check, or Check now: asks GitHub, and gets a newer release ready.</summary>
    public async Task<UpdatesView> CheckAndGetAsync(IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        var check = await CheckAsync(ct);
        if (check.Newer is { } newer && FindReady()?.Version != newer.Version)
        {
            try
            {
                await DownloadAsync(newer, progress is null ? null : new Forward<(long Done, long Total)>(p => progress.Report(new UpdateProgress(newer.Version, p.Done, p.Total))), ct);
            }
            catch (HttpRequestException e)
            {
                using var state = new StateStore(_dataDir);
                state.SetSetting(ProblemKey, $"{(Offline(e) ? "offline" : "failed")}\t{Stamp(_utcNow())}\tGameSync {newer.Version.ToString(3)} didn't finish coming down ({e.Message})");
            }
            catch (ReleaseRefusedException)
            {
                // Remembered for Settings, which says it wasn't installed and why.
            }
            catch (IOException e)
            {
                using var state = new StateStore(_dataDir);
                state.SetSetting(ProblemKey, $"failed\t{Stamp(_utcNow())}\tGameSync {newer.Version.ToString(3)} couldn't be kept on this PC ({e.Message})");
            }
        }

        return Read();
    }

    /// <summary>
    /// The newest update downloaded and newer than this GameSync, whose signature checks out and whose installer is
    /// there at its size; its fingerprint is checked again just before it starts.
    /// </summary>
    public ReadyUpdate? FindReady()
    {
        if (!Directory.Exists(Folder))
        {
            return null;
        }

        ReadyUpdate? best = null;
        foreach (var signature in Directory.EnumerateFiles(Folder, "GameSync-Setup-*.sig"))
        {
            try
            {
                var manifest = ReleaseManifest.Verify(File.ReadAllText(signature), _publicKey);
                var installer = Path.Combine(Folder, manifest.File);
                if (manifest.Version <= Current || !File.Exists(installer) || new FileInfo(installer).Length != manifest.Size || manifest.Version <= best?.Version)
                {
                    continue;
                }

                var notes = File.Exists(NotesPath(manifest.Version)) ? JsonSerializer.Deserialize<Notes>(File.ReadAllText(NotesPath(manifest.Version))) : null;
                best = new ReadyUpdate(manifest.Version, installer, signature, notes?.Page ?? "", notes?.Lines ?? []);
            }
            catch (Exception e) when (e is ReleaseRefusedException or IOException or UnauthorizedAccessException or JsonException)
            {
                // Not one to install: left for CleanUp.
            }
        }

        return best;
    }

    /// <summary>
    /// Restart to update: checks the update once more, holding its installer so nothing can change it meanwhile, and
    /// starts it to install over this GameSync and open it again (<paramref name="showWindow"/>: with its window). The
    /// app then quits; the installer waits for it.
    /// </summary>
    public Process StartInstall(ReadyUpdate ready, bool showWindow)
    {
        ReleaseManifest manifest;
        FileStream? held = null;
        try
        {
            manifest = ReleaseManifest.Verify(File.ReadAllText(ready.Signature), _publicKey);
            if (manifest.Version <= Current)
            {
                throw new ReleaseRefusedException("it isn't newer than the GameSync running");
            }

            held = new FileStream(Path.Combine(Folder, manifest.File), FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!manifest.Describes(held))
            {
                throw new ReleaseRefusedException("it isn't the installer its signature describes");
            }
        }
        catch (ReleaseRefusedException e)
        {
            held?.Dispose();
            using var state = new StateStore(_dataDir);
            state.SetSetting(RefusedKey, $"{ready.Version.ToString(3)}\t{Stamp(_utcNow())}\t{e.Message}");
            TryDelete(ready.Installer);
            TryDelete(ready.Signature);
            throw;
        }

        using (held)
        {
            var start = new ProcessStartInfo(held.Name) { UseShellExecute = false, WorkingDirectory = Folder };
            foreach (var argument in InstallerArguments(showWindow, _dataDir))
            {
                start.ArgumentList.Add(argument);
            }

            return Process.Start(start) ?? throw new IOException("Windows didn't start the installer.");
        }
    }

    /// <summary>What the installer is given: quietly, closing GameSync if it's still open, and opening it again after, on this data folder.</summary>
    public static IReadOnlyList<string> InstallerArguments(bool showWindow, string dataDir)
    {
        List<string> arguments = ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", $"/RELAUNCH={(showWindow ? "window" : "background")}"];
        if (!SameFolder(dataDir, Engine.DefaultDataDir))
        {
            arguments.Add($"/DATA={Path.GetFullPath(dataDir).TrimEnd('\\')}");
        }

        return arguments;
    }

    /// <summary>After an update, or a refusal: removes downloads that aren't newer than this GameSync, and any left half done.</summary>
    public void CleanUp()
    {
        if (!Directory.Exists(Folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(Folder))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || (VersionIn(name) is { } version && version <= Current))
            {
                TryDelete(file);
            }
        }
    }

    /// <summary>The "What's new" lines of a release's notes: its bullet points, as plain text, six at most.</summary>
    public static IReadOnlyList<string> NotesOf(string? body) =>
        (body ?? "").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            .Select(line => Plain(line[2..]))
            .Where(line => line.Length > 0)
            .Take(6)
            .ToList();

    /// <summary>A version tag, <c>v1.0.1</c> or <c>1.0.1</c>; null for anything else.</summary>
    public static Version? VersionOfTag(string? tag) =>
        tag is not null && Version.TryParse(tag.StartsWith('v') ? tag[1..] : tag, out var version) && version.Build >= 0 && version.Revision < 0 ? version : null;

    private UpdateCheck Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True
            || root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
        {
            return new UpdateCheck(null);
        }

        var version = VersionOfTag(root.GetProperty("tag_name").GetString()) ?? throw new FormatException("its version tag isn't one");
        if (version <= Current)
        {
            return new UpdateCheck(null);
        }

        Uri? installer = null, signature = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = new Uri(asset.GetProperty("browser_download_url").GetString() ?? throw new FormatException("an asset has no address"));
            if (name == ReleaseManifest.NameFor(version))
            {
                installer = url;
            }
            else if (name == ReleaseManifest.SignatureNameFor(version))
            {
                signature = url;
            }
        }

        if (installer is null || signature is null)
        {
            return new UpdateCheck(null, $"GameSync {version.ToString(3)} is on GitHub without its installer and signature, so it can't be installed from here");
        }

        var page = root.TryGetProperty("html_url", out var html) && html.GetString() is { } pageUrl && Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri) && Allowed(pageUri) ? pageUrl : "";
        return new UpdateCheck(new UpdateRelease(version, page, NotesOf(root.TryGetProperty("body", out var body) ? body.GetString() : null), installer, signature));
    }

    /// <summary>
    /// A GET that follows redirects by hand, each to GitHub over HTTPS: its release downloads go from github.com to its
    /// own download servers. With <c>GAMESYNC_UPDATE_FEED</c> set, this PC's own addresses too.
    /// </summary>
    private async Task<HttpResponseMessage> GetAsync(Uri uri, string accept, CancellationToken ct)
    {
        for (var hop = 0; ; hop++)
        {
            if (!Allowed(uri))
            {
                throw new ReleaseRefusedException($"it would come from {uri.Host}, which isn't GitHub");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd(accept);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } next)
            {
                response.Dispose();
                if (hop >= MostHops)
                {
                    throw new HttpRequestException("GitHub sent it on too many times");
                }

                uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                continue;
            }

            return response;
        }
    }

    /// <summary>GitHub over HTTPS: its API, github.com and its download servers; with an own feed, this PC's addresses too.</summary>
    internal bool Allowed(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && (uri.Host is "api.github.com" or "github.com" || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        || _ownFeed && uri.IsLoopback && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static async Task<string> ReadAsync(HttpResponseMessage response, int most, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var text = new MemoryStream();
        var buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (text.Length + read > most)
            {
                throw new ReleaseRefusedException("GitHub's answer is far bigger than it should be");
            }

            text.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(text.ToArray());
    }

    private static bool Offline(HttpRequestException e) =>
        e.HttpRequestError is HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError || e.InnerException is System.Net.Sockets.SocketException;

    private string NotesPath(Version version) => Path.Combine(Folder, $"GameSync-Setup-{version.ToString(3)}.json");

    private static Version? VersionIn(string name) =>
        SetupName().Match(name) is { Success: true } match && Version.TryParse(match.Groups[1].Value, out var version) ? version : null;

    private static UpdateRefusal? Refusal(StateStore state) =>
        state.GetSetting(RefusedKey)?.Split('\t') is [var version, var at, var reason] && Version.TryParse(version, out var v) && Time(at) is { } when
            ? new UpdateRefusal(v, reason, when)
            : null;

    private static UpdateProblem? Problem(StateStore state) =>
        state.GetSetting(ProblemKey)?.Split('\t') is [var kind, var at, var reason] && Time(at) is { } when
            ? new UpdateProblem(reason, kind == "offline", when)
            : null;

    private static string Stamp(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime? Time(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at.ToUniversalTime() : null;

    private static string Plain(string markdown) =>
        Link().Replace(markdown, "$1").Replace("**", "", StringComparison.Ordinal).Replace("`", "", StringComparison.Ordinal).Trim();

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Tried again at the next clean-up.
        }
    }

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"^GameSync-Setup-([0-9]+\.[0-9]+\.[0-9]+)\.(?:exe|sig|json)(?:\.part)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SetupName();

    /// <summary>What's new in a download, kept beside it for Settings.</summary>
    private sealed record Notes(string Page, string[] Lines);

    /// <summary>Progress passed on as it's reported, on the thread reporting it.</summary>
    private sealed class Forward<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
