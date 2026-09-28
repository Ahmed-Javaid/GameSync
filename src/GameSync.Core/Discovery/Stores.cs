using System.Text.Json;
using System.Xml.Linq;

namespace GameSync.Core.Discovery;

public enum StoreKind
{
    Steam,
    Epic,
    Ea,
    Loose,
}

/// <summary>What a game's status says when its store's own cloud moves its saves (LIB-10).</summary>
public static class StoreNames
{
    /// <summary>"Synced by Steam", or "Synced by its store" when the store isn't known; never "Backup only" (28 Sep 2026).</summary>
    public static string SyncedBy(StoreKind? store) => store switch
    {
        StoreKind.Steam => "Synced by Steam",
        StoreKind.Epic => "Synced by Epic",
        StoreKind.Ea => "Synced by EA",
        _ => "Synced by its store",
    };
}

/// <summary>A game a store says is installed, or a folder that looks like one (LIB-01 to LIB-05).</summary>
public sealed record InstalledGame
{
    public required StoreKind Store { get; init; }

    /// <summary>The store's name for it, or for a loose game the best name its folder gives.</summary>
    public required string Title { get; init; }

    public required string InstallDir { get; init; }

    /// <summary>Steam's app ID, Epic's app name or EA's content ID.</summary>
    public string? StoreId { get; init; }

    /// <summary>The installed build (Steam's build ID, Epic's version), for spotting updates later (BAK-06).</summary>
    public string? Build { get; init; }
}

public sealed record SteamAccount(string AccountId, string SteamId64, bool MostRecent);

public sealed record SteamInstall(string Root, IReadOnlyList<string> Libraries, IReadOnlyList<InstalledGame> Games, IReadOnlyList<SteamAccount> Accounts);

/// <summary>LIB-01: Steam's libraries from <c>libraryfolders.vdf</c>, and each library's <c>appmanifest_*.acf</c>.</summary>
public static class SteamReader
{
    private const long SteamId64Base = 76561197960265728;

    /// <summary>Steam's own packages that aren't games.</summary>
    private static readonly HashSet<string> NotGames = ["228980"];

    public static SteamInstall? Read(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var libraries = new List<string> { root };
        if (Vdf.TryReadRoot(Path.Combine(root, "steamapps", "libraryfolders.vdf")) is { } folders)
        {
            foreach (var (_, library) in folders.Blocks())
            {
                if (library["path"] is { Length: > 0 } path)
                {
                    var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                    if (!libraries.Contains(full, StringComparer.OrdinalIgnoreCase))
                    {
                        libraries.Add(full);
                    }
                }
            }
        }

        var games = new List<InstalledGame>();
        foreach (var library in libraries)
        {
            var apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps))
            {
                continue;
            }

            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                if (Vdf.TryReadRoot(acf) is not { } app || app["appid"] is not { Length: > 0 } id || NotGames.Contains(id) ||
                    app["installdir"] is not { Length: > 0 } folder || app["name"] is not { Length: > 0 } name)
                {
                    continue;
                }

                var installDir = Path.Combine(apps, "common", folder);
                if (Directory.Exists(installDir) && !games.Any(g => g.StoreId == id))
                {
                    games.Add(new InstalledGame { Store = StoreKind.Steam, Title = name, InstallDir = installDir, StoreId = id, Build = app["buildid"] });
                }
            }
        }

        return new SteamInstall(root, libraries, games, ReadAccounts(root));
    }

    /// <summary>Accounts from <c>loginusers.vdf</c>, with the short ID <c>userdata</c> folders use.</summary>
    private static List<SteamAccount> ReadAccounts(string root)
    {
        var accounts = new List<SteamAccount>();
        if (Vdf.TryReadRoot(Path.Combine(root, "config", "loginusers.vdf")) is { } users)
        {
            foreach (var (key, user) in users.Blocks())
            {
                if (long.TryParse(key, out var id64) && id64 > SteamId64Base)
                {
                    accounts.Add(new SteamAccount((id64 - SteamId64Base).ToString(System.Globalization.CultureInfo.InvariantCulture), key, user["MostRecent"] == "1"));
                }
            }
        }

        return accounts;
    }
}

/// <summary>LIB-02: Epic's install manifests (<c>*.item</c>, JSON), one per installed app.</summary>
public static class EpicReader
{
    public static IReadOnlyList<InstalledGame> Read(string manifestsFolder)
    {
        var games = new List<InstalledGame>();
        if (!Directory.Exists(manifestsFolder))
        {
            return games;
        }

        foreach (var file in Directory.EnumerateFiles(manifestsFolder, "*.item"))
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllBytes(file));
                var item = json.RootElement;
                string? Text(string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                bool Flag(string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

                var appName = Text("AppName");
                var mainGame = Text("MainGameAppName");
                var categories = item.TryGetProperty("AppCategories", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Select(c => c.GetString()).ToList()
                    : null;
                if (Flag("bIsIncompleteInstall") || Text("DisplayName") is not { Length: > 0 } title || Text("InstallLocation") is not { Length: > 0 } location ||
                    (categories is not null && !categories.Contains("games")) || (mainGame is { Length: > 0 } && mainGame != appName) || !Directory.Exists(location))
                {
                    continue;
                }

                games.Add(new InstalledGame { Store = StoreKind.Epic, Title = title, InstallDir = location, StoreId = appName, Build = Text("AppVersionString") });
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                // A damaged or locked manifest only hides that one game.
            }
        }

        return games;
    }
}

/// <summary>LIB-03: EA app games, from <c>__Installer\installerdata.xml</c> in each game folder.</summary>
public static class EaReader
{
    /// <param name="eaFolders">Game folders, or folders holding them such as <c>C:\Program Files\EA Games</c>.</param>
    public static IReadOnlyList<InstalledGame> Read(IEnumerable<string> eaFolders)
    {
        var games = new List<InstalledGame>();
        foreach (var folder in eaFolders.Where(Directory.Exists))
        {
            var candidates = File.Exists(Path.Combine(folder, "__Installer", "installerdata.xml")) ? [folder] : Directory.EnumerateDirectories(folder);
            foreach (var game in candidates)
            {
                var data = Path.Combine(game, "__Installer", "installerdata.xml");
                if (!File.Exists(data) || games.Any(g => g.InstallDir.Equals(game, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                try
                {
                    var xml = XDocument.Load(data);
                    string? English(string element) =>
                        xml.Descendants(element).FirstOrDefault(e => (string?)e.Attribute("locale") == "en_US")?.Value ?? xml.Descendants(element).FirstOrDefault()?.Value;
                    var title = English("gameTitle") ?? xml.Descendants("localeInfo").FirstOrDefault(e => (string?)e.Attribute("locale") == "en_US")?.Element("title")?.Value;
                    games.Add(new InstalledGame
                    {
                        Store = StoreKind.Ea,
                        Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileName(game) : title.Trim(),
                        InstallDir = game,
                        StoreId = xml.Descendants("contentID").FirstOrDefault()?.Value,
                        Build = xml.Descendants("buildMetaData").FirstOrDefault()?.Element("gameVersion")?.Attribute("version")?.Value,
                    });
                }
                catch (Exception e) when (e is System.Xml.XmlException or IOException or UnauthorizedAccessException)
                {
                    // Unreadable: this game is left out, the rest carry on.
                }
            }
        }

        return games;
    }
}

/// <summary>Where this PC's stores keep their records, and the game folders the person added (FOLD-07).</summary>
public sealed record StoreSources
{
    public string? SteamRoot { get; init; }

    public string? EpicManifests { get; init; }

    public IReadOnlyList<string> EaFolders { get; init; } = [];

    public IReadOnlyList<string> GameFolders { get; init; } = [];
}

public sealed record Detection(IReadOnlyList<InstalledGame> Games, SteamInstall? Steam);

public static class Detector
{
    /// <summary>
    /// Every installed game this PC has, each install folder once: a store's record beats a loose folder, and store
    /// folders inside the game folders you added are left to their store.
    /// </summary>
    public static Detection Detect(StoreSources sources)
    {
        var steam = sources.SteamRoot is { } root ? SteamReader.Read(root) : null;
        var games = new List<InstalledGame>(steam?.Games ?? []);
        if (sources.EpicManifests is { } manifests)
        {
            games.AddRange(EpicReader.Read(manifests));
        }

        games.AddRange(EaReader.Read(sources.EaFolders));
        var storeFolders = (steam?.Libraries ?? []).Concat(games.Select(g => g.InstallDir)).ToList();
        games.AddRange(LooseScanner.Scan(sources.GameFolders, storeFolders));
        return new Detection(games.DistinctBy(g => Path.TrimEndingDirectorySeparator(Path.GetFullPath(g.InstallDir)).ToLowerInvariant()).ToList(), steam);
    }
}
