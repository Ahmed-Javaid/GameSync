using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameSync.Core.Art;

/// <summary>
/// ACH-02, ACH-03: what Steam's own files on the PC don't hold about achievements, asked of Steam with no key and kept on
/// this PC: each icon shown, from Steam's image server by the file name Steam's list gives (checked like cover art,
/// ART-08), and how many players have each one, from Steam's public percentages, asked again after a week. Nothing about
/// achievements is uploaded or synced.
/// </summary>
public sealed partial class AchievementCache : IDisposable
{
    /// <summary>
    /// Steam's image servers for achievement icons, asked in turn: the shared one holds every game's (Valheim's icons are
    /// only there, 3 Oct 2026), the older one the icons of games from before it.
    /// </summary>
    public static readonly IReadOnlyList<string> IconBases =
    [
        "https://shared.akamai.steamstatic.com/community_assets/images/apps/",
        "https://cdn.akamai.steamstatic.com/steamcommunity/public/images/apps/",
    ];

    public const string RarityApi = "https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v0002/";

    /// <summary>
    /// Every achievement of any game by its app ID, with no key (design system version 35; KAN-111): names, descriptions,
    /// icons, which are hidden and how many players have each. For a game Steam on this PC keeps no list of: a copy that
    /// isn't Steam's (the owner's Cuphead and Child of Light) or a Steam game never run here.
    /// </summary>
    public const string ListApi = "https://api.steampowered.com/IPlayerService/GetGameAchievements/v1/";

    private static readonly TimeSpan RarityAskAgain = TimeSpan.FromDays(7);
    private static readonly TimeSpan ListAskAgain = TimeSpan.FromDays(30);

    private readonly Lazy<HttpClient> _http;
    private readonly Func<DateTime> _utcNow;

    /// <param name="handler">For tests: answers in place of Steam.</param>
    public AchievementCache(string dataDir, HttpMessageHandler? handler = null, Func<DateTime>? utcNow = null)
    {
        Folder = Path.Combine(dataDir, "art", "achievements");
        _utcNow = utcNow ?? (() => DateTime.UtcNow);

        // No redirects: an icon comes from Steam's image server or not at all. Made only when something is asked, as the
        // screens open the cache for every game just to read what's kept.
        _http = new Lazy<HttpClient>(() =>
        {
            // A handler given (tests) stays its giver's: it may answer for more than one cache.
            var http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) },
                disposeHandler: handler is null)
            {
                Timeout = TimeSpan.FromSeconds(15),
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GameSync");
            return http;
        });
    }

    public string Folder { get; }

    public void Dispose()
    {
        if (_http.IsValueCreated)
        {
            _http.Value.Dispose();
        }
    }

    /// <summary>An icon's file as Steam's list names it: its hash and an image extension, nothing that could be a path.</summary>
    [GeneratedRegex(@"^[0-9a-f]{16,64}\.(jpg|png)$", RegexOptions.IgnoreCase)]
    private static partial Regex IconName();

    /// <summary>The icon kept on this PC, or null until it's been fetched.</summary>
    public string? Icon(long appId, string? file) =>
        file is not null && IconName().IsMatch(file) && File.Exists(IconPath(appId, file)) ? IconPath(appId, file) : null;

    /// <summary>The share of players who have each achievement (by Steam's own name for it), as last asked; empty until then.</summary>
    public IReadOnlyDictionary<string, double> Rarity(long appId)
    {
        try
        {
            return File.Exists(RarityPath(appId)) ? ParseRarity(File.ReadAllText(RarityPath(appId))) : new Dictionary<string, double>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, double>();
        }
    }

    /// <summary>Whether there's something to ask Steam for: an icon not kept yet, or rarity older than a week.</summary>
    public bool Wants(long appId, IEnumerable<string?> icons) => WantsIcons(appId, icons) || RarityStale(appId);

    /// <summary>Whether an icon isn't kept yet.</summary>
    public bool WantsIcons(long appId, IEnumerable<string?> icons) =>
        icons.Any(i => i is not null && IconName().IsMatch(i) && !File.Exists(IconPath(appId, i)));

    /// <summary>Whether Steam's percentages are older than a week, or were never asked for.</summary>
    public bool RarityStale(long appId) => !File.Exists(AskedPath(appId)) || _utcNow() - File.GetLastWriteTimeUtc(AskedPath(appId)) > RarityAskAgain;

    /// <summary>Fetches the icons not kept yet and the rarity when it's stale. Steam not answering leaves what's kept as it is.</summary>
    /// <returns>Whether anything new was kept.</returns>
    public async Task<bool> FetchAsync(long appId, IEnumerable<string?> icons, CancellationToken ct) =>
        await FetchRarityAsync(appId, ct) | await FetchIconsAsync(appId, icons, ct);

    /// <summary>Fetches the icons not kept yet, four at a time; one Steam doesn't give is left for another time.</summary>
    /// <returns>Whether any was kept.</returns>
    public async Task<bool> FetchIconsAsync(long appId, IEnumerable<string?> icons, CancellationToken ct)
    {
        var changed = 0;
        Directory.CreateDirectory(Path.Combine(Folder, appId.ToString(CultureInfo.InvariantCulture)));
        var wanted = icons.OfType<string>().Distinct().Where(i => IconName().IsMatch(i) && !File.Exists(IconPath(appId, i))).ToList();
        await Parallel.ForEachAsync(wanted, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (icon, token) =>
        {
            try
            {
                foreach (var server in IconBases)
                {
                    using var reply = await _http.Value.GetAsync(new Uri($"{server}{appId.ToString(CultureInfo.InvariantCulture)}/{icon}"), token);
                    if (!reply.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    var content = await reply.Content.ReadAsByteArrayAsync(token);
                    if (ArtCheck.ExtensionOf(content) is not null)
                    {
                        await File.WriteAllBytesAsync(IconPath(appId, icon), content, token);
                        Interlocked.Increment(ref changed);
                        return;
                    }
                }
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException && !token.IsCancellationRequested)
            {
                // Offline or slow: the icon comes another time, and the name shows meanwhile.
            }
        });

        return changed > 0;
    }

    /// <summary>Asks Steam how many players have each achievement, when what's kept is a week old or <paramref name="again"/> says so.</summary>
    /// <param name="again">Asked now whatever its age: the Achievements page's Refresh rarity.</param>
    /// <returns>Whether new percentages were kept.</returns>
    public async Task<bool> FetchRarityAsync(long appId, CancellationToken ct, bool again = false)
    {
        if (!again && !RarityStale(appId))
        {
            return false;
        }

        Directory.CreateDirectory(Path.Combine(Folder, appId.ToString(CultureInfo.InvariantCulture)));
        try
        {
            var changed = false;
            using var reply = await _http.Value.GetAsync(new Uri($"{RarityApi}?gameid={appId.ToString(CultureInfo.InvariantCulture)}&format=json"), ct);
            if (reply.IsSuccessStatusCode)
            {
                var json = await reply.Content.ReadAsStringAsync(ct);
                if (ParseRarity(json).Count > 0)
                {
                    await File.WriteAllTextAsync(RarityPath(appId), json, ct);
                    changed = true;
                }
            }

            // Asked: a game Steam has no percentages for isn't asked again for a week either.
            File.SetLastWriteTimeUtc(TouchRarity(appId), _utcNow());
            return changed;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Offline: asked again next time.
            return false;
        }
    }

    /// <summary>
    /// Steam's list of a game's achievements as last asked, every one locked, with how many players have each; null until
    /// it's been asked (KAN-111). A hidden one's name and description are in it as Steam gives them: the screens keep them
    /// secret, as they do Steam's own files' (ACH-06).
    /// </summary>
    public (IReadOnlyList<Discovery.SteamAchievement> All, IReadOnlyDictionary<string, double> Percents)? List(long appId)
    {
        try
        {
            return File.Exists(ListPath(appId)) && ParseList(File.ReadAllText(ListPath(appId))) is { All.Count: > 0 } list ? list : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Steam was asked for the game's list and has none: a game whose achievements aren't Steam's (Child of Light's are Ubisoft's).</summary>
    public bool ListedNone(long appId) => File.Exists(ListAskedPath(appId)) && List(appId) is null;

    /// <summary>Whether Steam's list is a month old, or was never asked for.</summary>
    public bool ListStale(long appId) => !File.Exists(ListAskedPath(appId)) || _utcNow() - File.GetLastWriteTimeUtc(ListAskedPath(appId)) > ListAskAgain;

    /// <summary>Asks Steam for a game's list of achievements, when what's kept is a month old or was never asked for.</summary>
    /// <returns>Whether a new list was kept.</returns>
    public async Task<bool> FetchListAsync(long appId, CancellationToken ct, bool again = false)
    {
        if (!again && !ListStale(appId))
        {
            return false;
        }

        Directory.CreateDirectory(Path.Combine(Folder, appId.ToString(CultureInfo.InvariantCulture)));
        try
        {
            var changed = false;
            var app = appId.ToString(CultureInfo.InvariantCulture);
            using var reply = await _http.Value.GetAsync(new Uri($"{ListApi}?appid={app}&language=english"), ct);
            if (reply.IsSuccessStatusCode)
            {
                var json = await reply.Content.ReadAsStringAsync(ct);
                if (ParseList(json).All.Count > 0)
                {
                    await File.WriteAllTextAsync(ListPath(appId), json, ct);
                    changed = true;
                }
            }

            // Asked: a game with none isn't asked again for a month either.
            var asked = ListAskedPath(appId);
            if (!File.Exists(asked))
            {
                await File.WriteAllTextAsync(asked, "", ct);
            }

            File.SetLastWriteTimeUtc(asked, _utcNow());
            return changed;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Offline: asked again next time.
            return false;
        }
    }

    /// <summary>
    /// Steam's answer: <c>{"response":{"achievements":[{"internal_name":"ACH_X","localized_name":"…","localized_desc":"…",
    /// "icon":"<hash>.jpg","icon_gray":"<hash>.jpg","hidden":false,"player_percent_unlocked":"76.6"}]}}</c>. Words are
    /// cleaned as the store's are; an entry with no name is left out.
    /// </summary>
    public static (IReadOnlyList<Discovery.SteamAchievement> All, IReadOnlyDictionary<string, double> Percents) ParseList(string json)
    {
        var all = new List<Discovery.SteamAchievement>();
        var percents = new Dictionary<string, double>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("response", out var response) || !response.TryGetProperty("achievements", out var list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                return (all, percents);
            }

            foreach (var entry in list.EnumerateArray())
            {
                string? Text(string name) => entry.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                if (Text("internal_name") is not { Length: > 0 } id || SteamArt.Clean(Text("localized_name"), 200) is not { Length: > 0 } title)
                {
                    continue;
                }

                var hidden = entry.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.True;
                all.Add(new Discovery.SteamAchievement(id, title, SteamArt.Clean(Text("localized_desc"), 500), hidden, Text("icon"), Text("icon_gray"), null));
                if (entry.TryGetProperty("player_percent_unlocked", out var percent) &&
                    (percent.ValueKind == JsonValueKind.Number ? percent.GetDouble()
                        : double.TryParse(percent.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1) is var value and >= 0 and <= 100)
                {
                    percents[id] = value;
                }
            }
        }
        catch (JsonException)
        {
            // Not Steam's answer: no list.
        }

        return (all, percents);
    }

    /// <summary>Steam's answer: <c>{"achievementpercentages":{"achievements":[{"name":"ACH_X","percent":12.3}]}}</c>, the percent a number or a text.</summary>
    public static IReadOnlyDictionary<string, double> ParseRarity(string json)
    {
        var found = new Dictionary<string, double>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("achievementpercentages", out var root) || !root.TryGetProperty("achievements", out var list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                return found;
            }

            foreach (var entry in list.EnumerateArray())
            {
                if (entry.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } id && entry.TryGetProperty("percent", out var percent) &&
                    (percent.ValueKind == JsonValueKind.Number ? percent.GetDouble()
                        : double.TryParse(percent.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1) is var value and >= 0 and <= 100)
                {
                    found[id] = value;
                }
            }
        }
        catch (JsonException)
        {
            // Not Steam's answer: no rarity.
        }

        return found;
    }

    private string IconPath(long appId, string file) => Path.Combine(Folder, appId.ToString(CultureInfo.InvariantCulture), file.ToLowerInvariant());

    private string RarityPath(long appId) => Path.Combine(Folder, appId.ToString(CultureInfo.InvariantCulture), "rarity.json");

    private string AskedPath(long appId) => Path.Combine(Folder, appId.ToString(CultureInfo.InvariantCulture), "rarity.asked");

    private string ListPath(long appId) => Path.Combine(Folder, appId.ToString(CultureInfo.InvariantCulture), "list.json");

    private string ListAskedPath(long appId) => Path.Combine(Folder, appId.ToString(CultureInfo.InvariantCulture), "list.asked");

    private string TouchRarity(long appId)
    {
        var path = AskedPath(appId);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "");
        }

        return path;
    }
}
