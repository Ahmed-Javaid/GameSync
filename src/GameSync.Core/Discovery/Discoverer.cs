using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Scanning;

namespace GameSync.Core.Discovery;

/// <summary>How a save location was found: the design's layers, in order, plus the ways a person adds one.</summary>
public enum FoundBy
{
    SaveList,
    EngineRule,
    NameSearch,

    /// <summary>A subfolder named by the game's Steam app ID, in a folder you marked as named by ID (FOLD-08).</summary>
    IdFolder,

    Ludusavi,
    ByHand,
}

/// <summary>
/// FOLD-08: a save folder you added on this PC. Plain ones join the name search; one named by ID holds a subfolder per
/// Steam app ID. <paramref name="Path"/> starts with a placeholder when it's under one of Windows' folders.
/// </summary>
public sealed record ExtraSaveFolder(string Path, bool ById);

/// <summary>
/// A place a game's saves may be (FIND-06): a portable root and the pattern below it, with what's there now. It stays a
/// proposal until confirmed once.
/// </summary>
public sealed record Proposal(FoundBy FoundBy, string Root, string Include, SaveCategory Category, int Files, long Bytes, DateTime? NewestUtc);

/// <summary>FIND-10: a registry key that may hold a game's saves or settings, with how many values it has now.</summary>
public sealed record RegistryProposal(FoundBy FoundBy, string Key, SaveCategory Category, int Values, DateTime? ChangedUtc);

/// <summary>An installed game with the save list's entry for it, what its folder says, and where its saves may be.</summary>
public sealed record DiscoveredGame
{
    public required InstalledGame Installed { get; init; }

    public SaveListGame? Listed { get; init; }

    /// <summary>The save list's title when it knows the game, which both PCs agree on; otherwise the store's or folder's name.</summary>
    public required string Title { get; init; }

    public required Fingerprint Print { get; init; }

    public IReadOnlyList<Proposal> Proposals { get; init; } = [];

    public IReadOnlyList<RegistryProposal> Registry { get; init; } = [];

    /// <summary>The store's own cloud syncs it, so it's backup only (LIB-10).</summary>
    public bool StoreCloud { get; init; }

    /// <summary>Only settings were found: progress probably lives on the game's servers (LIB-10).</summary>
    public bool ProbablyOnlineOnly => Discoverer.OnlySettings(Proposals, Registry);
}

/// <summary>Saves on this PC for a game in the save list that isn't installed here.</summary>
public sealed record LeftoverGame(SaveListGame Listed, IReadOnlyList<Proposal> Proposals, bool StoreCloud)
{
    public IReadOnlyList<RegistryProposal> Registry { get; init; } = [];

    public bool ProbablyOnlineOnly => Discoverer.OnlySettings(Proposals, Registry);
}

/// <summary>
/// Finds where each installed game keeps its saves: the save list first (FIND-01), with your ID-named folders
/// (FOLD-08), then engine rules (FIND-02), then a search by the game's names (FIND-03). The first layer that finds
/// files wins. Nothing is written anywhere.
/// </summary>
/// <param name="guard">R5: folders no rule may reach are never proposed, whatever layer found them.</param>
/// <param name="registry">For registry keys (FIND-10); without it, none are proposed.</param>
public sealed partial class Discoverer(SaveList? saveList, IReadOnlyDictionary<string, string> folders, IReadOnlyList<ExtraSaveFolder>? extraFolders = null,
    SensitivePathGuard? guard = null, IRegistryStore? registry = null)
{
    private const int MaxFilesCounted = 20_000;

    /// <summary>Where a name search looks, one level deep, to catch <c>&lt;company&gt;\&lt;product&gt;</c> too.</summary>
    private static readonly string[] SearchRoots = ["<documents>", "<documents>/My Games", "<savedGames>", "<roaming>", "<localAppData>", "<localLow>", "<programData>", "<publicDocuments>"];

    private static readonly Dictionary<string, string> ManifestFolders = new(StringComparer.Ordinal)
    {
        ["<base>"] = "<installDir>",
        ["<home>"] = "<home>",
        ["<winAppData>"] = "<roaming>",
        ["<winLocalAppData>"] = "<localAppData>",
        ["<winLocalAppDataLow>"] = "<localLow>",
        ["<winDocuments>"] = "<documents>",
        ["<winPublic>"] = "<public>",
        ["<winProgramData>"] = "<programData>",
    };

    /// <summary>Folders and files that change all the time but never hold saves.</summary>
    [GeneratedRegex(@"(^|/)(Temp|tmp|Cache|Caches|cache2|GPUCache|ShaderCache|DXCache|GLCache|D3DSCache|CrashDumps|Crashes|CrashReportClient|Logs?|INetCache|WebCache|Code Cache|htmlcache|Service Worker|IndexedDB|Local Storage|Session Storage|blob_storage|Packages|Microsoft|Google|Mozilla|BraveSoftware|NVIDIA|NVIDIA Corporation|AMD|discord|Spotify|JetBrains|Code|npm-cache|pip)(/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex NoisePath();

    /// <summary>Directory listings, so each folder is read once per scan however many paths pass through it.</summary>
    private readonly ConcurrentDictionary<string, (string Name, bool IsFolder, bool IsLink)[]> _listings = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, bool> _folderExists = new(StringComparer.OrdinalIgnoreCase);

    public DiscoveredGame Discover(InstalledGame game)
    {
        var print = Fingerprinter.Read(game.InstallDir);
        var listed = Match(game, print);
        var proposals = Allowed(listed is null ? [] : FromSaveList(listed, game.Store, game.StoreId, game.InstallDir), game.InstallDir);
        proposals.AddRange(Allowed(FromIdFolders(game, listed), game.InstallDir));
        if (proposals.Count == 0)
        {
            proposals = Allowed(FromEngine(game, print), game.InstallDir);
        }

        if (proposals.Count == 0)
        {
            proposals = Allowed(FromNames(game, listed, print), game.InstallDir);
        }

        var cloudName = StoreName(game.Store);
        return new DiscoveredGame
        {
            Installed = game,
            Listed = listed,
            Title = listed?.Title ?? game.Title,
            Print = print,
            Proposals = proposals,
            Registry = FromRegistry(listed, print.PredictedRegistry),
            StoreCloud = cloudName is not null && listed?.Cloud.Contains(cloudName) == true,
        };
    }

    public static bool OnlySettings(IReadOnlyList<Proposal> files, IReadOnlyList<RegistryProposal> registry) =>
        files.Count + registry.Count > 0 && files.All(p => p.Category == SaveCategory.Config) && registry.All(r => r.Category == SaveCategory.Config);

    /// <summary>
    /// FIND-10: the registry keys the save list gives, or, when it gives none, the ones the engine predicts (Unity's
    /// PlayerPrefs, as settings); only keys under HKEY_CURRENT_USER\Software that exist now (R7).
    /// </summary>
    private List<RegistryProposal> FromRegistry(SaveListGame? listed, IReadOnlyList<string> predicted)
    {
        var found = new List<RegistryProposal>();
        if (registry is null)
        {
            return found;
        }

        var candidates = listed is { Registry.Count: > 0 }
            ? listed.Registry.Select(r => (FoundBy.SaveList, r.Path, r.Save ? SaveCategory.Save : SaveCategory.Config))
            : predicted.Select(key => (FoundBy.EngineRule, key, SaveCategory.Config));
        foreach (var (by, key, category) in candidates)
        {
            if (key.Contains('<', StringComparison.Ordinal) || RegistryGuard.Check(key, [key]) is not null ||
                found.Any(f => RegistryGuard.SameKey(f.Key, key)) || registry.Export(key) is not { } export)
            {
                continue;
            }

            found.Add(new RegistryProposal(by, key.Replace('\\', '/'), category, CountValues(export.Node), export.ChangedUtc));
        }

        return found;
    }

    private static int CountValues(RegistryNode node) => node.Values.Count + node.Keys.Values.Sum(CountValues);

    /// <summary>
    /// The design's "Saves found, game not installed": every game in the save list whose saves are on this PC although
    /// it isn't installed, from the paths that don't need an install folder. Games found installed are left out, and so
    /// is a game whose saves one of them already takes. Such a game may be backed up by its store's cloud on whichever
    /// store it came from, so it counts as store-cloud whenever the list knows any.
    /// </summary>
    public IReadOnlyList<LeftoverGame> FindUninstalled(IReadOnlyCollection<DiscoveredGame> installed)
    {
        if (saveList is null)
        {
            return [];
        }

        var listed = installed.Select(g => g.Listed?.Title).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var taken = installed.SelectMany(g => g.Proposals).ToList();
        var found = new ConcurrentBag<LeftoverGame>();
        var takenKeys = installed.SelectMany(g => g.Registry).Select(r => r.Key).ToList();
        Parallel.ForEach(saveList.Games.Where(g => (g.Files.Count > 0 || g.Registry.Count > 0) && !listed.Contains(g.Title)), new ParallelOptions { MaxDegreeOfParallelism = 4 }, game =>
        {
            var proposals = game.Files.Count > 0 ? FromListOnly(game) : [];
            var keys = FromRegistry(game, []).Where(r => !takenKeys.Any(t => RegistryGuard.SameKey(t, r.Key))).ToList();
            if (proposals.Count + keys.Count > 0 && !proposals.Any(p => taken.Any(t => Covers(t, p))))
            {
                found.Add(new LeftoverGame(game, proposals, StoreCloud: game.Cloud.Count > 0) { Registry = keys });
            }
        });

        return found.OrderBy(g => g.Listed.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The places the save list gives for a game that aren't in its install folder, whatever store it came from.</summary>
    public List<Proposal> FromListOnly(SaveListGame listed)
    {
        var store = folders.ContainsKey("<steamRoot>") ? StoreKind.Steam : StoreKind.Loose;
        var steamId = listed.SteamIds.Count > 0 ? listed.SteamIds[0].ToString(CultureInfo.InvariantCulture) : null;
        return Allowed(FromSaveList(listed, store, steamId, installDir: null, anyStore: true), installDir: null);
    }

    /// <summary>ONB-04: a game added by hand in Ludusavi, its paths read and checked like the save list's.</summary>
    public List<Proposal> FromLudusavi(LudusaviCustomGame game)
    {
        var listed = new SaveListGame { Title = game.Name, Files = game.Files.Select(f => new SaveListPath(f, Save: true, Config: false)).ToList() };
        return Allowed(FromSaveList(listed, StoreKind.Loose, null, installDir: null, anyStore: true), installDir: null)
            .Select(p => p with { FoundBy = FoundBy.Ludusavi })
            .ToList();
    }

    /// <summary>ONB-04: the registry keys of a game added by hand in Ludusavi that exist here.</summary>
    public List<RegistryProposal> RegistryFromLudusavi(LudusaviCustomGame game)
    {
        var listed = new SaveListGame { Title = game.Name, Registry = game.Registry.Select(r => new SaveListPath(r, Save: true, Config: false)).ToList() };
        return FromRegistry(listed, []).Select(r => r with { FoundBy = FoundBy.Ludusavi }).ToList();
    }

    /// <summary>The proposals whose folder a save rule may reach (R5); a name search can land on an app's sign-in folder.</summary>
    private List<Proposal> Allowed(List<Proposal> proposals, string? installDir) =>
        guard is null ? proposals : proposals.Where(p => Resolve(p.Root, installDir) is { } folder && guard.CheckRoot(folder) is null).ToList();

    /// <summary>Whether a proposal already takes another's files: the same place, or a folder-wide one around it.</summary>
    public static bool Covers(Proposal outer, Proposal inner) =>
        (SameFolder(outer.Root, inner.Root) && (outer.Include == inner.Include || outer.Include == "**")) ||
        (outer.Include == "**" && $"{inner.Root.Replace('\\', '/')}/".StartsWith($"{outer.Root.Replace('\\', '/').TrimEnd('/')}/", StringComparison.OrdinalIgnoreCase));

    /// <summary>LIB-06: store ID, then install folder name, then title and the folder's other names; never a guess.</summary>
    public SaveListGame? Match(InstalledGame game, Fingerprint print)
    {
        if (saveList is null)
        {
            return null;
        }

        if (game.Store == StoreKind.Steam && long.TryParse(game.StoreId, out var steamId) && saveList.BySteamId(steamId) is { } bySteam)
        {
            return bySteam;
        }

        if (saveList.ByInstallDir(Path.GetFileName(game.InstallDir)) is [var byFolder])
        {
            return byFolder;
        }

        foreach (var name in print.Names.Prepend(game.Title))
        {
            if (saveList.ByTitle(name) is { } byTitle)
            {
                return byTitle;
            }
        }

        return null;
    }

    /// <summary>
    /// Turns a save list path into a portable root and a pattern below it: "&lt;winAppData&gt;/Sekiro/&lt;storeUserId&gt;/S0000.sl2"
    /// becomes the root "&lt;roaming&gt;/Sekiro" and the pattern "*/S0000.sl2". Null for paths that don't apply here.
    /// </summary>
    public static (string Root, string Pattern)? ToPortable(string path, StoreKind store, string? storeId)
    {
        var text = path.Replace('\\', '/');
        string? root;
        string rest;
        if (text.Length > 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] == '/')
        {
            // A few entries are full paths, like the Xbox app's C:/XboxGames/GameSave: the drive is the root.
            (root, rest) = (text[..2].ToUpperInvariant() + "\\", text[2..]);
        }
        else
        {
            var close = text.IndexOf('>', StringComparison.Ordinal);
            if (!text.StartsWith('<') || close < 0)
            {
                return null;
            }

            var head = text[..(close + 1)];
            rest = text[(close + 1)..];
            root = head == "<root>" ? (store == StoreKind.Steam ? "<steamRoot>" : null) : ManifestFolders.GetValueOrDefault(head);

            // "<winLocalAppData>Low/Kwalee/Game" is how some entries spell LocalLow.
            if (head == "<winLocalAppData>" && rest.StartsWith("Low/", StringComparison.Ordinal))
            {
                (root, rest) = ("<localLow>", rest[3..]);
            }
        }

        rest = rest
            .Replace("<storeUserId>", "*", StringComparison.Ordinal)
            .Replace("<osUserName>", "*", StringComparison.Ordinal)
            .Replace("<storeGameId>", storeId ?? "*", StringComparison.Ordinal)
            .Replace("<game>", "*", StringComparison.Ordinal);
        if (root is null || (rest.Length > 0 && rest[0] != '/') || rest.Contains('<', StringComparison.Ordinal))
        {
            return null;
        }

        var segments = rest.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        var fixedCount = segments.TakeWhile(s => s.IndexOfAny(['*', '?', '[', ']', '{', '}']) < 0).Count();

        // "<home>/AppData/LocalLow/..." is "<localLow>/...": Windows' own folder, which follows a moved Documents too.
        foreach (var (parent, under, placeholder) in HomeFolders)
        {
            if (root == parent && fixedCount >= under.Length && under.Select((s, i) => s.Equals(segments[i], StringComparison.OrdinalIgnoreCase)).All(same => same))
            {
                root = placeholder;
                segments.RemoveRange(0, under.Length);
                fixedCount -= under.Length;
                break;
            }
        }

        var fixedPart = segments.Take(fixedCount).ToList();
        var pattern = string.Join('/', segments.Skip(fixedCount));
        if (fixedPart.Count == 0)
        {
            return root.StartsWith('<') ? (root, pattern) : null;
        }

        return (root.StartsWith('<') ? $"{root}/{string.Join('/', fixedPart)}" : Path.Combine([root, .. fixedPart]), pattern);
    }

    /// <summary>Folders under the user's home or Public that Windows knows by name, most specific first.</summary>
    private static readonly (string Parent, string[] Under, string Placeholder)[] HomeFolders =
    [
        ("<home>", ["AppData", "LocalLow"], "<localLow>"),
        ("<home>", ["AppData", "Local"], "<localAppData>"),
        ("<home>", ["AppData", "Roaming"], "<roaming>"),
        ("<home>", ["Documents"], "<documents>"),
        ("<home>", ["Saved Games"], "<savedGames>"),
        ("<public>", ["Documents"], "<publicDocuments>"),
    ];

    /// <summary>
    /// A root's key in versions and in the cloud's plain copy, made from the portable root itself so every PC gives the
    /// same folder the same key: "&lt;documents&gt;/My Games/Terraria" is "documents-my-games-terraria".
    /// </summary>
    public static string RootKey(string portableRoot)
    {
        var slug = new StringBuilder();
        foreach (var c in portableRoot.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(c);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var key = slug.ToString().Trim('-');
        if (key.Length is 0 or > 40)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(portableRoot)))[..6];
            key = $"{key[..Math.Min(key.Length, 33)].TrimEnd('-')}-{hash}".Trim('-');
        }

        return key;
    }

    /// <summary>
    /// The game definition confirmed proposals make. Rules for particular files come before rules for whole folders,
    /// so a settings file inside a save folder keeps its category.
    /// </summary>
    public static GameDefinition ToDefinition(GameId id, string title, IEnumerable<Proposal> proposals, GameMode mode = GameMode.Sync)
    {
        var chosen = proposals.OrderBy(p => WholeFolder(p.Include)).ThenBy(p => p.Root, StringComparer.Ordinal).ThenBy(p => p.Include, StringComparer.Ordinal).ToList();
        return new GameDefinition
        {
            Id = id,
            Title = title,
            Mode = mode,
            Roots = chosen.Select(p => p.Root).Distinct(StringComparer.Ordinal).ToDictionary(RootKey, r => r),
            Rules = chosen.Select(p => new SaveRule { Root = RootKey(p.Root), Include = p.Include, Category = p.Category }).ToList(),
        };
    }

    /// <summary>
    /// The definition plus the proposals it doesn't cover yet. Its own roots and rules stay exactly as they are; a new
    /// folder gets a root key that doesn't clash with one already used for another folder.
    /// </summary>
    public static GameDefinition Extend(GameDefinition definition, IEnumerable<Proposal> proposals)
    {
        var roots = new Dictionary<string, string>(definition.Roots, StringComparer.Ordinal);
        var rules = definition.Rules.ToList();
        foreach (var proposal in proposals.OrderBy(p => WholeFolder(p.Include)))
        {
            var key = roots.FirstOrDefault(r => SameFolder(r.Value, proposal.Root)).Key;
            if (key is null)
            {
                var wanted = RootKey(proposal.Root);
                key = wanted;
                for (var n = 2; roots.ContainsKey(key); n++)
                {
                    key = $"{wanted[..Math.Min(wanted.Length, 36)].TrimEnd('-')}-{n}";
                }

                roots[key] = proposal.Root;
            }

            if (!rules.Any(r => r.Root == key && r.Include == proposal.Include))
            {
                rules.Add(new SaveRule { Root = key, Include = proposal.Include, Category = proposal.Category });
            }
        }

        return definition with { Roots = roots, Rules = rules.OrderBy(r => WholeFolder(r.Include)).ToList() };
    }

    /// <summary>
    /// A folder or file on this PC for a portable path or a full one, or null when a placeholder has no value here,
    /// such as &lt;installDir&gt; for a game that isn't installed.
    /// </summary>
    public string? Resolve(string portable, string? installDir)
    {
        if (Path.IsPathFullyQualified(portable))
        {
            return Path.GetFullPath(portable);
        }

        var close = portable.IndexOf('>', StringComparison.Ordinal);
        if (!portable.StartsWith('<') || close < 0)
        {
            return null;
        }

        var head = portable[..(close + 1)];
        var value = head == RootResolver.InstallDir ? installDir : folders.GetValueOrDefault(head);
        return string.IsNullOrEmpty(value) ? null : Path.GetFullPath(value + portable[(close + 1)..].Replace('/', Path.DirectorySeparatorChar));
    }

    /// <param name="anyStore">Also the paths for other stores, for a game whose store isn't known; their store root is never this one's.</param>
    private List<Proposal> FromSaveList(SaveListGame listed, StoreKind store, string? storeId, string? installDir, bool anyStore = false)
    {
        var proposals = new List<Proposal>();
        var storeName = StoreName(store);
        foreach (var path in listed.Files)
        {
            var forThisStore = path.Stores is null || (storeName is not null && path.Stores.Contains(storeName));
            if (!forThisStore && !anyStore)
            {
                continue;
            }

            if (ToPortable(path.Path, forThisStore ? store : StoreKind.Loose, storeId) is not var (root, pattern))
            {
                continue;
            }

            // A full path under one of Windows' folders, such as a game added by hand in Ludusavi, works on every PC this way.
            if (!root.StartsWith('<'))
            {
                root = RootResolver.ToPortable(root, folders);
            }

            if (Resolve(root, installDir) is not { } folder)
            {
                continue;
            }

            // A path without a pattern is either a folder (everything in it) or a single file.
            if (pattern.Length == 0)
            {
                var slash = root.LastIndexOf('/');
                switch (Find(folder))
                {
                    case (true, true):
                        pattern = "**";
                        break;
                    case (true, false) when slash > 0:
                        pattern = root[(slash + 1)..];
                        root = root[..slash];
                        folder = Path.GetDirectoryName(folder)!;
                        break;
                    default:
                        continue;
                }
            }

            var category = path.Save ? SaveCategory.Save : SaveCategory.Config;
            foreach (var include in Forms(path.Path, pattern))
            {
                if (Evidence(folder, include) is { } evidence && !proposals.Any(p => p.Root == root && p.Include == include))
                {
                    proposals.Add(new Proposal(FoundBy.SaveList, root, include, category, evidence.Files, evidence.Bytes, evidence.Newest));
                }
            }
        }

        return proposals;
    }

    /// <summary>
    /// A save list path names files or folders, and a folder means everything in it, so a pattern is tried both ways:
    /// "*/632360/remote" as files, and as the folders' contents. A path ending in an account ID is always a folder.
    /// </summary>
    private static IEnumerable<string> Forms(string manifestPath, string pattern)
    {
        if (pattern.EndsWith("**", StringComparison.Ordinal))
        {
            return [pattern];
        }

        var last = manifestPath.Replace('\\', '/').TrimEnd('/');
        last = last[(last.LastIndexOf('/') + 1)..];
        return last is "<storeUserId>" or "<osUserName>" or "<storeGameId>" ? [$"{pattern}/**"] : [pattern, $"{pattern}/**"];
    }

    /// <summary>FOLD-08: a subfolder named by the game's Steam app ID, from its store or the save list, in each ID-named folder.</summary>
    private List<Proposal> FromIdFolders(InstalledGame game, SaveListGame? listed)
    {
        var proposals = new List<Proposal>();
        var ids = (listed?.SteamIds ?? []).Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        if (game.Store == StoreKind.Steam && game.StoreId is { Length: > 0 } storeId)
        {
            ids.Add(storeId);
        }

        foreach (var folder in (extraFolders ?? []).Where(f => f.ById))
        {
            foreach (var id in ids)
            {
                var root = Join(folder.Path, id);
                if (Resolve(root, game.InstallDir) is { } dir && Evidence(dir, "**") is { } evidence)
                {
                    proposals.Add(new Proposal(FoundBy.IdFolder, root, "**", SaveCategory.Save, evidence.Files, evidence.Bytes, evidence.Newest));
                }
            }
        }

        return proposals;
    }

    private List<Proposal> FromEngine(InstalledGame game, Fingerprint print)
    {
        var proposals = new List<Proposal>();
        foreach (var root in print.PredictedFolders)
        {
            if (Resolve(root, game.InstallDir) is { } folder && Evidence(folder, "**") is { } evidence)
            {
                proposals.Add(new Proposal(FoundBy.EngineRule, root, "**", SaveCategory.Save, evidence.Files, evidence.Bytes, evidence.Newest));
            }
        }

        return proposals;
    }

    /// <summary>FIND-03: folders named like the game, one level deep in the usual save folders; names under 4 letters are too vague.</summary>
    private List<Proposal> FromNames(InstalledGame game, SaveListGame? listed, Fingerprint print)
    {
        var keys = print.Names.Prepend(game.Title).Append(listed?.Title ?? "")
            .Select(SaveList.Normalize)
            .Where(k => k.Length >= 4)
            .ToHashSet(StringComparer.Ordinal);
        var proposals = new List<Proposal>();
        if (keys.Count == 0)
        {
            return proposals;
        }

        foreach (var searchRoot in SearchRoots.Concat((extraFolders ?? []).Where(f => !f.ById).Select(f => f.Path)))
        {
            if (Resolve(searchRoot, game.InstallDir) is not { } baseFolder || !Directory.Exists(baseFolder))
            {
                continue;
            }

            IEnumerable<string> candidates;
            try
            {
                candidates = Directory.EnumerateDirectories(baseFolder, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 1, IgnoreInaccessible = true }).ToList();
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                var relative = Path.GetRelativePath(baseFolder, candidate).Replace('\\', '/');
                if (NoisePath().IsMatch(relative) || !keys.Contains(SaveList.Normalize(Path.GetFileName(candidate))))
                {
                    continue;
                }

                var root = Join(searchRoot, relative);
                if (!proposals.Any(p => p.Root == root) && Evidence(candidate, "**") is { } evidence)
                {
                    proposals.Add(new Proposal(FoundBy.NameSearch, root, "**", SaveCategory.Save, evidence.Files, evidence.Bytes, evidence.Newest));
                }
            }
        }

        // The install folder too, by what save folders are called, since many games keep their saves beside themselves.
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var candidate in Directory.Exists(game.InstallDir) ? Directory.EnumerateDirectories(game.InstallDir, "*", options) : [])
        {
            var relative = Path.GetRelativePath(game.InstallDir, candidate).Replace('\\', '/');
            if (SaveFolderName().IsMatch(Path.GetFileName(candidate)) && !proposals.Any(p => $"{p.Root}/".StartsWith($"<installDir>/{relative}/", StringComparison.OrdinalIgnoreCase)) &&
                Evidence(candidate, "**") is { } evidence)
            {
                proposals.Add(new Proposal(FoundBy.NameSearch, $"<installDir>/{relative}", "**", SaveCategory.Save, evidence.Files, evidence.Bytes, evidence.Newest));
            }
        }

        return proposals;
    }

    [GeneratedRegex(@"^(saves?|save ?data|save ?games?|saved ?games|save ?files?)$", RegexOptions.IgnoreCase)]
    private static partial Regex SaveFolderName();

    /// <summary>
    /// The files a rule would take here, after the usual excludes; null when there are none. The pattern is followed a
    /// folder at a time up to its first "**", so only folders it can reach are read, each once per scan.
    /// </summary>
    private (int Files, long Bytes, DateTime Newest)? Evidence(string folder, string pattern)
    {
        if (Find(folder) is not (true, true))
        {
            return null;
        }

        var segments = pattern.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var deep = Array.FindIndex(segments, s => s.Contains("**", StringComparison.Ordinal));
        var reached = new List<(string Path, bool IsFolder)> { (folder, true) };
        for (var i = 0; i < (deep < 0 ? segments.Length : deep) && reached.Count > 0; i++)
        {
            var segment = segments[i];
            var glob = segment.IndexOfAny(['*', '?']) >= 0 ? new Glob(segment) : null;
            reached = reached.Where(r => r.IsFolder)
                .SelectMany(r => List(r.Path)
                    .Where(e => !e.IsLink && (glob?.IsMatch(e.Name) ?? e.Name.Equals(segment, StringComparison.OrdinalIgnoreCase)))
                    .Select(e => (Path.Combine(r.Path, e.Name), e.IsFolder)))
                .ToList();
        }

        var files = 0;
        long bytes = 0;
        var newest = DateTime.MinValue;
        var matches = deep < 0
            ? reached.Where(r => !r.IsFolder).Select(r => r.Path)
            : reached.Where(r => r.IsFolder).SelectMany(r => Directory.EnumerateFiles(r.Path, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            }));
        var full = new Glob(pattern);
        foreach (var file in matches)
        {
            var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (!full.IsMatch(relative) || DefaultExcludes.All.Any(e => e.IsMatch(relative)))
            {
                continue;
            }

            var info = new FileInfo(file);
            files++;
            bytes += info.Length;
            newest = info.LastWriteTimeUtc > newest ? info.LastWriteTimeUtc : newest;
            if (files >= MaxFilesCounted)
            {
                break;
            }
        }

        return files == 0 ? null : (files, bytes, newest);
    }

    /// <summary>Whether a path exists and is a folder, from its parent's listing, read once per scan.</summary>
    private (bool Exists, bool IsFolder) Find(string path)
    {
        path = Path.TrimEndingDirectorySeparator(path);
        if (Path.GetDirectoryName(path) is not { } parent)
        {
            return _folderExists.GetOrAdd(path, Directory.Exists) ? (true, true) : (false, false);
        }

        if (!_folderExists.GetOrAdd(parent, Directory.Exists))
        {
            return (false, false);
        }

        var name = Path.GetFileName(path);
        foreach (var entry in List(parent))
        {
            if (entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return (true, entry.IsFolder);
            }
        }

        return (false, false);
    }

    /// <summary>A folder's entries, links included: a rule's root may be one, but the scanner never follows one below it.</summary>
    private (string Name, bool IsFolder, bool IsLink)[] List(string folder) =>
        _listings.GetOrAdd(folder, f =>
        {
            try
            {
                return new DirectoryInfo(f)
                    .EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 })
                    .Select(i => (i.Name, i is DirectoryInfo, i.Attributes.HasFlag(FileAttributes.ReparsePoint)))
                    .ToArray();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        });

    private static bool WholeFolder(string include) => include.EndsWith("**", StringComparison.Ordinal);

    /// <summary>A folder below a portable root keeps '/'; below a full path, Windows' separator.</summary>
    private static string Join(string root, string relative)
    {
        root = root.TrimEnd('/', '\\');
        return root.StartsWith('<') ? $"{root}/{relative}" : Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool SameFolder(string a, string b) =>
        a.Replace('\\', '/').TrimEnd('/').Equals(b.Replace('\\', '/').TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static string? StoreName(StoreKind store) => store switch
    {
        StoreKind.Steam => "steam",
        StoreKind.Epic => "epic",
        StoreKind.Ea => "origin",
        _ => null,
    };
}
