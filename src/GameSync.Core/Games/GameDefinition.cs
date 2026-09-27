using System.Text.Json.Serialization;
using GameSync.Core.Model;

namespace GameSync.Core.Games;

[JsonConverter(typeof(JsonStringEnumConverter<GameMode>))]
public enum GameMode
{
    /// <summary>Two-way sync between PCs.</summary>
    Sync,

    /// <summary>A store's cloud already syncs it: GameSync keeps history but never downloads by itself.</summary>
    BackupOnly,
}

[JsonConverter(typeof(JsonStringEnumConverter<ConflictPolicy>))]
public enum ConflictPolicy
{
    NewestWins,
    AlwaysAsk,
    ThisPcWins,
}

/// <summary>A set of save files: a root folder (by key, so each PC can put it somewhere else) and patterns below it.</summary>
public sealed record SaveRule
{
    public required string Root { get; init; }

    public string Include { get; init; } = "**";

    public IReadOnlyList<string> Exclude { get; init; } = [];

    public SaveCategory Category { get; init; } = SaveCategory.Save;

    /// <summary>Skip logs, crash dumps, shader and web caches (FIND-11).</summary>
    public bool UseDefaultExcludes { get; init; } = true;
}

/// <summary>
/// FIND-10: a registry key the game keeps saves or settings in, like Unity's PlayerPrefs at
/// <c>HKEY_CURRENT_USER/Software/Studio MDHR/Cuphead</c>. It's backed up with everything under it, and restored only there (R7).
/// </summary>
public sealed record RegistryRule
{
    public required string Key { get; init; }

    public SaveCategory Category { get; init; } = SaveCategory.Save;
}

public sealed record GameDefinition
{
    /// <summary>The root key registry exports sit under in versions; no other root may use it.</summary>
    public const string RegistryRoot = "registry";

    public required GameId Id { get; init; }

    public required string Title { get; init; }

    public GameMode Mode { get; init; } = GameMode.Sync;

    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.NewestWins;

    /// <summary>
    /// Root key to folder. The key is what versions store, which keeps paths portable between PCs. A folder may start
    /// with a placeholder such as <c>&lt;documents&gt;</c>, which each PC resolves for itself (FIND-08).
    /// </summary>
    public required IReadOnlyDictionary<string, string> Roots { get; init; }

    public required IReadOnlyList<SaveRule> Rules { get; init; }

    /// <summary>Registry keys, the same on every PC, so they need no placeholders.</summary>
    public IReadOnlyList<RegistryRule> Registry { get; init; } = [];

    /// <summary>Sync <see cref="SaveCategory.Config"/> files between PCs instead of backing them up per PC.</summary>
    public bool SyncConfig { get; init; }

    public bool IncludeScreenshots { get; init; }

    /// <summary>Account IDs this PC filled into the roots, such as <c>steamUser</c> (PC-03). Set when roots are resolved, never saved.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> Accounts { get; init; } = new Dictionary<string, string>();

    /// <summary>The roots as written, placeholders and all, before this PC filled in its folders. Set when roots are resolved, never saved.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string>? PortableRoots { get; init; }
}

/// <summary>
/// A game's save rules in the form every PC reads: roots with their placeholders. Each version records the rules it was
/// taken with, so a PC that doesn't have the game yet is offered them (PC-04), and a PC whose own rules differ is asked
/// before it uses them (R8).
/// </summary>
public sealed record PortableRules
{
    public required string Title { get; init; }

    public GameMode Mode { get; init; } = GameMode.Sync;

    public required IReadOnlyDictionary<string, string> Roots { get; init; }

    public required IReadOnlyList<SaveRule> Rules { get; init; }

    public IReadOnlyList<RegistryRule> Registry { get; init; } = [];

    /// <summary>The rules as written; the folder a PC exports registry keys into is its own, so it's left out.</summary>
    public static PortableRules From(GameDefinition game) => new()
    {
        Title = game.Title,
        Mode = game.Mode,
        Roots = (game.PortableRoots ?? game.Roots).Where(r => r.Key != GameDefinition.RegistryRoot).ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal),
        Rules = game.Rules.Where(r => r.Root != GameDefinition.RegistryRoot).ToList(),
        Registry = game.Registry,
    };

    public GameDefinition ToDefinition(GameId id) => new() { Id = id, Title = Title, Mode = Mode, Roots = Roots, Rules = Rules, Registry = Registry };

    /// <summary>Whether these rules take the file at a version path ("root-key/relative/path").</summary>
    public bool Takes(string path)
    {
        var slash = path.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0)
        {
            return false;
        }

        var (key, relative) = (path[..slash], path[(slash + 1)..]);
        return Roots.ContainsKey(key) && Rules.Any(r =>
            r.Root == key && new Scanning.Glob(r.Include).IsMatch(relative) && !r.Exclude.Any(e => new Scanning.Glob(e).IsMatch(relative)));
    }

    /// <summary>
    /// Whether both take the same files: the same folders under the same keys, and the same rules in any order. A full
    /// path is each PC's own choice, like an install folder, so it matches any folder.
    /// </summary>
    public bool SameFilesAs(PortableRules other) =>
        Roots.Count == other.Roots.Count &&
        Roots.All(r => other.Roots.TryGetValue(r.Key, out var folder) && SameFolder(r.Value, folder)) &&
        Rules.Select(Describe).ToHashSet(StringComparer.Ordinal).SetEquals(other.Rules.Select(Describe)) &&
        Registry.Select(Describe).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(other.Registry.Select(Describe));

    /// <summary>One line per rule, like "&lt;documents&gt;/My Games/Terraria: **" or "... config.json (config)".</summary>
    public IEnumerable<string> Describe()
    {
        foreach (var rule in Rules)
        {
            var folder = Roots.GetValueOrDefault(rule.Root) ?? $"({rule.Root})";
            var excludes = rule.Exclude.Count == 0 ? "" : $", except {string.Join(", ", rule.Exclude)}";
            yield return $"{folder.Replace('\\', '/')}: {rule.Include}{excludes}{CategoryNote(rule.Category)}";
        }

        foreach (var rule in Registry)
        {
            yield return $"registry {rule.Key.Replace('\\', '/')}{CategoryNote(rule.Category)}";
        }
    }

    private static string CategoryNote(SaveCategory category) => category == SaveCategory.Save ? "" : $" ({category.ToString().ToLowerInvariant()})";

    private static string Describe(RegistryRule rule) => $"{rule.Key.Replace('\\', '/').TrimEnd('/')}|{rule.Category}";

    private static string Describe(SaveRule rule) =>
        $"{rule.Root}|{rule.Include}|{string.Join(',', rule.Exclude.Order(StringComparer.Ordinal))}|{rule.Category}|{rule.UseDefaultExcludes}";

    private static bool SameFolder(string a, string b) =>
        !a.StartsWith('<') || !b.StartsWith('<') ||
        string.Equals(a.Replace('\\', '/').TrimEnd('/'), b.Replace('\\', '/').TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
