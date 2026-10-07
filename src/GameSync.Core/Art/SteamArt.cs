using System.Globalization;
using System.Text.Json;

namespace GameSync.Core.Art;

/// <summary>Which image fills which slot (ART-03).</summary>
public enum ArtKind
{
    /// <summary>Tiles: Steam's 600×900 library capsule (<c>library_capsule_2x</c>).</summary>
    Cover,

    /// <summary>The hero banner: Steam's library hero, which never contains text.</summary>
    Hero,

    /// <summary>The game's transparent logo, shown over the hero in place of its title.</summary>
    Logo,
}

/// <summary>
/// The image names Steam's store API gives for one app, and when they last changed (ART-02, ART-07); and what kind of
/// app it is, since Steam also installs software such as Wallpaper Engine, which the launcher doesn't show as a game.
/// </summary>
public sealed record SteamAssets(
    long AppId, string UrlFormat, long LastModified, IReadOnlyDictionary<string, string> Files, int? StoreType = null, long? ParentAppId = null)
{
    /// <summary>Steam's store type for software and tools; games are 0.</summary>
    public const int Software = 6;

    /// <summary>Steam's store type for a demo, which is software when its parent app is (3DMark Demo).</summary>
    public const int Demo = 1;
}

/// <summary>
/// What a game's Steam store page says about it, for its page's About (ART-09): the short description, who made and
/// published it, when it came out, and its top tags (as Steam's tag IDs, named from <see cref="SteamArt.TagListRequest"/>).
/// </summary>
public sealed record SteamStoreInfo(long AppId, string? Description, IReadOnlyList<string> Developers, IReadOnlyList<string> Publishers,
    DateTime? ReleasedUtc, IReadOnlyList<int> Tags);

/// <summary>
/// Steam's official art for an app ID, with no key (ART-01, ART-02): the store API (<c>IStoreBrowseService/GetItems</c>
/// with <c>include_assets</c>) names each image, because newer games keep theirs under hashed paths that can't be
/// guessed; the images come from Steam's image server. The same request brings the store page's basics for the game's
/// About (ART-09), and Steam's list of tag names is asked for once a month. Only these two hosts are ever asked.
/// </summary>
public static class SteamArt
{
    public const string StoreApi = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/";
    public const string TagApi = "https://api.steampowered.com/IStoreService/GetTagList/v1/?language=english";
    public const string ImageBase = "https://shared.steamstatic.com/store_item_assets/";

    /// <summary>The store API takes this many apps a call.</summary>
    public const int Batch = 50;

    /// <summary>How many of its top tags a game's About shows, as its genres.</summary>
    public const int TagCount = 4;

    /// <summary>A description longer than this is cut; the store's short one is a few lines.</summary>
    public const int DescriptionMax = 1200;

    public static Uri AssetsRequest(IEnumerable<long> appIds) => Request(appIds, assets: true);

    /// <summary>Only what kind of app each is, for a demo's parent.</summary>
    public static Uri TypesRequest(IEnumerable<long> appIds) => Request(appIds, assets: false);

    /// <summary>Steam's names for its tags, in English: the same list for every game, so it's asked for once.</summary>
    public static Uri TagListRequest() => new(TagApi);

    private static Uri Request(IEnumerable<long> appIds, bool assets)
    {
        var request = new
        {
            ids = appIds.Select(id => new { appid = id }),
            context = new { language = "english", country_code = "US" },
            data_request = new
            {
                include_assets = assets,
                include_related_items = assets,
                include_basic_info = assets,
                include_release = assets,
                include_tag_count = assets ? TagCount : 0,
            },
        };
        return new Uri(StoreApi + "?input_json=" + Uri.EscapeDataString(JsonSerializer.Serialize(request)));
    }

    /// <summary>
    /// Each app's store page basics from a GetItems reply. Steam's words are untrusted: control characters go, and a
    /// description, a name or a list is cut to a sensible length.
    /// </summary>
    public static IReadOnlyList<SteamStoreInfo> ParseInfo(string json)
    {
        using var document = JsonDocument.Parse(json);
        var found = new List<SteamStoreInfo>();
        if (!document.RootElement.TryGetProperty("response", out var response) || !response.TryGetProperty("store_items", out var items))
        {
            return found;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("appid", out var appId) || !appId.TryGetInt64(out var id) ||
                (item.TryGetProperty("success", out var success) && success.GetInt32() != 1))
            {
                continue;
            }

            string? description = null;
            IReadOnlyList<string> developers = [], publishers = [];
            if (item.TryGetProperty("basic_info", out var basic))
            {
                description = basic.TryGetProperty("short_description", out var text) && text.ValueKind == JsonValueKind.String ? Clean(text.GetString(), DescriptionMax) : null;
                developers = Names(basic, "developers");
                publishers = Names(basic, "publishers");
            }

            DateTime? released = null;
            if (item.TryGetProperty("release", out var release))
            {
                foreach (var key in new[] { "original_release_date", "steam_release_date" })
                {
                    if (release.TryGetProperty(key, out var stamp) && stamp.TryGetInt64(out var seconds) && seconds > 0)
                    {
                        released = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
                        break;
                    }
                }
            }

            var tags = new List<(int Id, long Weight)>();
            if (item.TryGetProperty("tags", out var tagList) && tagList.ValueKind == JsonValueKind.Array)
            {
                foreach (var tag in tagList.EnumerateArray())
                {
                    if (tag.TryGetProperty("tagid", out var tagId) && tagId.TryGetInt32(out var tid))
                    {
                        tags.Add((tid, tag.TryGetProperty("weight", out var weight) && weight.TryGetInt64(out var w) ? w : 0));
                    }
                }
            }

            found.Add(new SteamStoreInfo(id, description, developers, publishers, released,
                tags.OrderByDescending(t => t.Weight).Select(t => t.Id).Distinct().Take(TagCount).ToList()));
        }

        return found;
    }

    /// <summary>Steam's tag names by ID, from a GetTagList reply.</summary>
    public static IReadOnlyDictionary<int, string> ParseTags(string json)
    {
        using var document = JsonDocument.Parse(json);
        var names = new Dictionary<int, string>();
        if (document.RootElement.TryGetProperty("response", out var response) && response.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tags.EnumerateArray())
            {
                if (tag.TryGetProperty("tagid", out var id) && id.TryGetInt32(out var tid) && tag.TryGetProperty("name", out var name) &&
                    Clean(name.GetString(), 40) is { } clean)
                {
                    names[tid] = clean;
                }
            }
        }

        return names;
    }

    private static IReadOnlyList<string> Names(JsonElement basic, string key) =>
        basic.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
                .Select(p => p.TryGetProperty("name", out var name) ? Clean(name.GetString(), 80) : null)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList()
            : [];

    /// <summary>Text from Steam, fit to show: no control characters, runs of white space made one, and at most <paramref name="max"/> characters.</summary>
    public static string? Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = string.Join(' ', new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length <= max ? cleaned : cleaned[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>Each app's store type, from a GetItems reply.</summary>
    public static IReadOnlyDictionary<long, int> ParseTypes(string json)
    {
        using var document = JsonDocument.Parse(json);
        var types = new Dictionary<long, int>();
        if (document.RootElement.TryGetProperty("response", out var response) && response.TryGetProperty("store_items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("appid", out var appId) && item.TryGetProperty("type", out var type) && type.TryGetInt32(out var kind))
                {
                    types[appId.GetInt64()] = kind;
                }
            }
        }

        return types;
    }

    /// <summary>The apps Steam knows, from a GetItems reply; apps it doesn't know are left out.</summary>
    public static IReadOnlyList<SteamAssets> ParseAssets(string json)
    {
        using var document = JsonDocument.Parse(json);
        var found = new List<SteamAssets>();
        if (!document.RootElement.TryGetProperty("response", out var response) || !response.TryGetProperty("store_items", out var items))
        {
            return found;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("appid", out var appId) || !item.TryGetProperty("assets", out var assets) ||
                (item.TryGetProperty("success", out var success) && success.GetInt32() != 1) ||
                !assets.TryGetProperty("asset_url_format", out var format) || format.GetString() is not { } urlFormat || !SafeFormat(urlFormat))
            {
                continue;
            }

            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in assets.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } name && SafeName(name))
                {
                    files[property.Name] = name;
                }
            }

            var lastModified = assets.TryGetProperty("last_modified", out var modified) && modified.TryGetInt64(out var stamp) ? stamp : 0;
            int? storeType = item.TryGetProperty("type", out var type) && type.TryGetInt32(out var kind) ? kind : null;
            long? parent = item.TryGetProperty("related_items", out var related) && related.TryGetProperty("parent_appid", out var parentId) &&
                parentId.TryGetInt64(out var id) ? id : null;
            found.Add(new SteamAssets(appId.GetInt64(), urlFormat, lastModified, files, storeType, parent));
        }

        return found;
    }

    /// <summary>Where one image is, or null when Steam has none of that kind for the app.</summary>
    public static Uri? ImageUrl(SteamAssets assets, ArtKind kind)
    {
        if (kind == ArtKind.Logo)
        {
            return new Uri(ImageBase + $"steam/apps/{assets.AppId.ToString(CultureInfo.InvariantCulture)}/logo.png");
        }

        var file = kind switch
        {
            ArtKind.Cover => assets.Files.GetValueOrDefault("library_capsule_2x") ?? assets.Files.GetValueOrDefault("library_capsule"),
            _ => assets.Files.GetValueOrDefault("library_hero"),
        };
        return file is null ? null : new Uri(ImageBase + assets.UrlFormat.Replace("${FILENAME}", file, StringComparison.Ordinal));
    }

    // What Steam sends is untrusted too: a path under its own image folder, never a host or a way out of it.
    private static bool SafeFormat(string format) =>
        format.StartsWith("steam/apps/", StringComparison.Ordinal) && format.Contains("${FILENAME}", StringComparison.Ordinal) &&
        !format.Contains("..", StringComparison.Ordinal) && !format.Contains("://", StringComparison.Ordinal) && !format.Contains('\\');

    private static bool SafeName(string name) =>
        name.Length is > 0 and < 200 && !name.Contains("..", StringComparison.Ordinal) && !name.StartsWith('/') &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '/');
}

/// <summary>ART-08: downloaded images are untrusted, so only JPEG, PNG or WebP under a size cap are kept, checked by their content.</summary>
public static class ArtCheck
{
    public const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>The file extension the content really is, or null when it isn't an image GameSync shows.</summary>
    public static string? ExtensionOf(ReadOnlySpan<byte> content)
    {
        if (content.Length > MaxBytes || content.Length < 12)
        {
            return null;
        }

        if (content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return ".jpg";
        }

        if (content[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return ".png";
        }

        return content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8) ? ".webp" : null;
    }
}
