using System.Net;
using System.Text.Json;

namespace GameSync.Core.Art;

/// <summary>What a refresh did: images taken from the Steam client's own cache, apps asked about online, and images downloaded.</summary>
public sealed record ArtRefresh(int Copied, int Asked, int Downloaded);

/// <summary>
/// Cover art on this PC (ART-07): each Steam app's cover, hero and logo in the data folder's <c>art</c>, shown offline.
/// Art is decoration, not data: it never goes to the cloud or into a shared zip.
/// <list type="bullet">
/// <item>First from the Steam client's own cache on this PC, with no network: games in the person's Steam library have their
/// art there already.</item>
/// <item>Steam's store is asked once per new game (by app ID, nothing else), for what kind of app it is, its store page's
/// basics for its About (ART-09), and any art the Steam client hasn't cached, such as for Epic and loose copies of Steam games.</item>
/// <item>After that it's asked again only a week on while art is missing, and a month on for art it gave, in case Steam changed it.</item>
/// <item>Steam's list of tag names, the same for every game, is asked for once and again a month on.</item>
/// </list>
/// </summary>
public sealed class ArtCache : IDisposable
{
    private static readonly TimeSpan MissingAskAgain = TimeSpan.FromDays(7);
    private static readonly TimeSpan DownloadedAskAgain = TimeSpan.FromDays(30);
    private static readonly TimeSpan TagsAskAgain = TimeSpan.FromDays(30);
    private static readonly ArtKind[] Kinds = [ArtKind.Cover, ArtKind.Hero, ArtKind.Logo];
    private static readonly string[] Extensions = [".jpg", ".png", ".webp"];

    private readonly HttpClient _http;
    private readonly Func<DateTime> _utcNow;
    private readonly string? _steamCache;

    /// <param name="handler">For tests: answers in place of Steam.</param>
    /// <param name="steamRoot">The Steam client's folder, whose <c>appcache\librarycache</c> holds art it already downloaded.</param>
    public ArtCache(string dataDir, HttpMessageHandler? handler = null, Func<DateTime>? utcNow = null, string? steamRoot = null)
    {
        Folder = Path.Combine(dataDir, "art", "steam");
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _steamCache = steamRoot is null ? null : Path.Combine(steamRoot, "appcache", "librarycache");
        // No redirects: an image comes from Steam's image server or not at all.
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GameSync");
    }

    public string Folder { get; }

    public void Dispose() => _http.Dispose();

    /// <summary>The cached image of this kind for the app, if there is one.</summary>
    public string? Find(long appId, ArtKind kind)
    {
        var folder = AppFolder(appId);
        return Extensions.Select(e => Path.Combine(folder, Name(kind) + e)).FirstOrDefault(File.Exists);
    }

    /// <summary>What the app's Steam store page says about it, once Steam's store has been asked (ART-09).</summary>
    public SteamStoreInfo? Info(long appId)
    {
        var file = Path.Combine(AppFolder(appId), "info.json");
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<SteamStoreInfo>(File.ReadAllText(file)) : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Steam's names for its tags, as far as they've been asked for; empty before.</summary>
    public IReadOnlyDictionary<int, string> TagNames()
    {
        try
        {
            return File.Exists(TagsFile) ? JsonSerializer.Deserialize<Dictionary<int, string>>(File.ReadAllText(TagsFile)) ?? [] : new Dictionary<int, string>();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new Dictionary<int, string>();
        }
    }

    /// <summary>A game's top tags by name, as its About shows them (ART-09); the ones Steam hasn't named are left out.</summary>
    public IReadOnlyList<string> TagsOf(SteamStoreInfo info)
    {
        var names = TagNames();
        return info.Tags.Select(t => names.GetValueOrDefault(t)).OfType<string>().ToList();
    }

    private string TagsFile => Path.Combine(Folder, "tags.json");

    /// <summary>
    /// Whether Steam's store lists the app as software (Wallpaper Engine, Lossless Scaling), or as the demo of software
    /// (3DMark Demo), rather than a game, as far as it's been asked.
    /// </summary>
    public bool IsSoftware(long appId) =>
        Record(appId) is { } record && (record.StoreType == SteamAssets.Software || (record.StoreType == SteamAssets.Demo && record.ParentType == SteamAssets.Software));

    /// <summary>
    /// Brings each app's art up to date: first from the Steam client's cache, then, for the apps that are due, from
    /// Steam's store. Offline or refused, what's cached stays and the rest waits for the next try.
    /// </summary>
    /// <param name="askAll">Ask Steam's store about every app now, as the person's Refresh art does.</param>
    public async Task<ArtRefresh> RefreshAsync(IEnumerable<long> appIds, CancellationToken ct, bool askAll = false)
    {
        var ids = appIds.Distinct().ToList();
        var copied = ids.Sum(CopyFromSteamClient);
        var due = ids.Where(id => askAll || Due(id)).ToList();
        int asked = 0, downloaded = 0;
        foreach (var batch in due.Chunk(SteamArt.Batch))
        {
            IReadOnlyList<SteamAssets> known;
            IReadOnlyList<SteamStoreInfo> infos;
            try
            {
                using var reply = await _http.GetAsync(SteamArt.AssetsRequest(batch), ct);
                if (reply.StatusCode != HttpStatusCode.OK)
                {
                    continue;
                }

                var json = await reply.Content.ReadAsStringAsync(ct);
                known = SteamArt.ParseAssets(json);
                infos = SteamArt.ParseInfo(json);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                break;
            }

            asked += batch.Length;
            var parentTypes = await ParentTypesAsync(known, ct);
            // A few apps at a time, so one slow image doesn't hold up the rest.
            await Parallel.ForEachAsync(batch, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (appId, token) =>
            {
                var assets = known.FirstOrDefault(a => a.AppId == appId);
                var parentType = assets?.ParentAppId is { } parent ? parentTypes.GetValueOrDefault(parent, -1) : (int?)null;
                Interlocked.Add(ref downloaded, await SaveAsync(appId, assets, parentType, infos.FirstOrDefault(i => i.AppId == appId), token));
            });
        }

        // Steam's tag names, only once some game has tags to name.
        if (ids.Any(id => Info(id) is { Tags.Count: > 0 }))
        {
            await RefreshTagNamesAsync(ct);
        }

        return new ArtRefresh(copied, asked, downloaded);
    }

    /// <summary>Steam's tag names, when there are none yet or they're a month old; offline, the old ones stay.</summary>
    private async Task RefreshTagNamesAsync(CancellationToken ct)
    {
        if (File.Exists(TagsFile) && _utcNow() - File.GetLastWriteTimeUtc(TagsFile) < TagsAskAgain && TagNames().Count > 0)
        {
            return;
        }

        try
        {
            using var reply = await _http.GetAsync(SteamArt.TagListRequest(), ct);
            if (reply.StatusCode != HttpStatusCode.OK)
            {
                return;
            }

            var names = SteamArt.ParseTags(await reply.Content.ReadAsStringAsync(ct));
            if (names.Count > 0)
            {
                Directory.CreateDirectory(Folder);
                await File.WriteAllTextAsync(TagsFile, JsonSerializer.Serialize(names), ct);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            // The About shows no genres until the next try.
        }
    }

    /// <summary>
    /// Takes the app's art from the Steam client's cache when this cache lacks it, or when the Steam client's copy is newer;
    /// art that came from Steam's store stays. Each file is checked like a download (ART-08). No network.
    /// </summary>
    private int CopyFromSteamClient(long appId)
    {
        var source = _steamCache is null ? null : Path.Combine(_steamCache, Id(appId));
        if (source is null || !Directory.Exists(source))
        {
            return 0;
        }

        var files = new DirectoryInfo(source).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true })
            .ToList();
        var downloaded = Record(appId)?.Downloaded ?? [];
        var copied = 0;
        foreach (var kind in Kinds.Where(k => !downloaded.Contains(k)))
        {
            if (Candidate(files, kind) is not { } found || found.Length > ArtCheck.MaxBytes)
            {
                continue;
            }

            if (Find(appId, kind) is { } existing && File.GetLastWriteTimeUtc(existing) >= found.LastWriteTimeUtc)
            {
                continue;
            }

            try
            {
                var content = File.ReadAllBytes(found.FullName);
                if (ArtCheck.ExtensionOf(content) is { } extension)
                {
                    File.SetLastWriteTimeUtc(Write(appId, kind, content, extension), found.LastWriteTimeUtc);
                    copied++;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Steam may be rewriting its cache; the next refresh takes it.
            }
        }

        return copied;
    }

    /// <summary>The Steam client's file for a kind of art: the sharpest it has, newest first when a hashed folder holds an older one.</summary>
    private static FileInfo? Candidate(IReadOnlyList<FileInfo> files, ArtKind kind)
    {
        string[] names = kind switch
        {
            ArtKind.Cover => ["library_600x900_2x.jpg", "library_capsule_2x.jpg", "library_600x900.jpg", "library_capsule.jpg"],
            ArtKind.Hero => ["library_hero_2x.jpg", "library_hero.jpg"],
            _ => ["logo_2x.png", "logo.png"],
        };
        return names.Select(name => files.Where(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).MaxBy(f => f.LastWriteTimeUtc))
            .FirstOrDefault(f => f is not null);
    }

    /// <summary>The store type of each demo's parent app, asked in one call; offline, none.</summary>
    private async Task<IReadOnlyDictionary<long, int>> ParentTypesAsync(IReadOnlyList<SteamAssets> known, CancellationToken ct)
    {
        var parents = known.Where(a => a.StoreType == SteamAssets.Demo).Select(a => a.ParentAppId).OfType<long>().Distinct().ToList();
        if (parents.Count == 0)
        {
            return new Dictionary<long, int>();
        }

        try
        {
            using var reply = await _http.GetAsync(SteamArt.TypesRequest(parents), ct);
            return reply.StatusCode == HttpStatusCode.OK ? SteamArt.ParseTypes(await reply.Content.ReadAsStringAsync(ct)) : new Dictionary<long, int>();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            return new Dictionary<long, int>();
        }
    }

    /// <summary>
    /// Downloads the kinds of art this cache doesn't have, and the ones it downloaded before that Steam has changed since;
    /// keeps what the store page says about the app for its About.
    /// </summary>
    private async Task<int> SaveAsync(long appId, SteamAssets? assets, int? parentType, SteamStoreInfo? info, CancellationToken ct)
    {
        Directory.CreateDirectory(AppFolder(appId));
        var previous = Record(appId);
        var downloaded = new HashSet<ArtKind>(previous?.Downloaded ?? []);
        var changed = previous?.LastModified != assets?.LastModified;
        var saved = 0;
        if (assets is not null)
        {
            foreach (var kind in Kinds)
            {
                if (Find(appId, kind) is not null && !(changed && downloaded.Contains(kind)))
                {
                    continue;
                }

                if (SteamArt.ImageUrl(assets, kind) is { } url && await DownloadAsync(url, ct) is { } image)
                {
                    Write(appId, kind, image.Content, image.Extension);
                    downloaded.Add(kind);
                    saved++;
                }
            }
        }

        if (info is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(AppFolder(appId), "info.json"), JsonSerializer.Serialize(info), ct);
        }

        // -1: Steam didn't say what kind of app it is, which isn't worth asking again about.
        var record = new ArtRecord(assets?.LastModified, _utcNow(), assets is null ? null : assets.StoreType ?? -1, parentType, downloaded.Order().ToList(), AskedInfo: true);
        await File.WriteAllTextAsync(Path.Combine(AppFolder(appId), "art.json"), JsonSerializer.Serialize(record), ct);
        return saved;
    }

    private string Write(long appId, ArtKind kind, byte[] content, string extension)
    {
        var folder = Directory.CreateDirectory(AppFolder(appId)).FullName;
        foreach (var old in Extensions.Select(e => Path.Combine(folder, Name(kind) + e)).Where(File.Exists))
        {
            File.Delete(old);
        }

        var target = Path.Combine(folder, Name(kind) + extension);
        var temporary = target + ".part";
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, target, overwrite: true);
        return target;
    }

    private async Task<(byte[] Content, string Extension)?> DownloadAsync(Uri url, CancellationToken ct)
    {
        try
        {
            using var reply = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (reply.StatusCode != HttpStatusCode.OK || reply.Content.Headers.ContentLength > ArtCheck.MaxBytes)
            {
                return null;
            }

            await using var stream = await reply.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > ArtCheck.MaxBytes)
                {
                    return null;
                }
            }

            var content = buffer.ToArray();
            return ArtCheck.ExtensionOf(content) is { } extension ? (content, extension) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether to ask Steam's store about the app: never asked; asked before GameSync noted what kind of app each is, or
    /// before it kept the store page's basics (once more, for the About); a demo whose parent isn't known; still missing its
    /// cover a week on; or holding art Steam gave, a month on. An app whose art all came from the Steam client isn't asked again.
    /// </summary>
    private bool Due(long appId)
    {
        if (Record(appId) is not { } record)
        {
            return true;
        }

        var age = _utcNow() - record.CheckedUtc;
        return (record.LastModified is not null && record.StoreType is null) ||
            (record.StoreType is not null && !record.AskedInfo) ||
            (record.StoreType == SteamAssets.Demo && record.ParentType is null) ||
            (Find(appId, ArtKind.Cover) is null && age > MissingAskAgain) ||
            (record.Downloaded is { Count: > 0 } && age > DownloadedAskAgain);
    }

    private ArtRecord? Record(long appId)
    {
        var file = Path.Combine(AppFolder(appId), "art.json");
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<ArtRecord>(File.ReadAllText(file)) : null;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return null;
        }
    }

    private string AppFolder(long appId) => Path.Combine(Folder, Id(appId));

    private static string Id(long appId) => appId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Name(ArtKind kind) => kind.ToString().ToLowerInvariant();

    /// <param name="ParentType">A demo's parent app's store type; -1 when Steam didn't say.</param>
    /// <param name="Downloaded">The kinds of art that came from Steam's store rather than the Steam client.</param>
    /// <param name="AskedInfo">Asked with the store page's basics, which records made before ART-09 weren't.</param>
    private sealed record ArtRecord(long? LastModified, DateTime CheckedUtc, int? StoreType = null, int? ParentType = null, IReadOnlyList<ArtKind>? Downloaded = null,
        bool AskedInfo = false);
}
