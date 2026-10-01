using System.Text;
using System.Text.Json.Serialization;

namespace GameSync.Core.Discovery;

/// <summary>A path from the save list: a file, a folder or a pattern, and which stores it applies to (null for all).</summary>
public sealed record SaveListPath(string Path, bool Save, bool Config, IReadOnlyList<string>? Stores = null);

/// <summary>One game in the save list, reduced to what Windows needs.</summary>
public sealed record SaveListGame
{
    public required string Title { get; init; }

    public IReadOnlyList<long> SteamIds { get; init; } = [];

    public IReadOnlyList<long> GogIds { get; init; } = [];

    /// <summary>Names the game's install folder usually has.</summary>
    public IReadOnlyList<string> InstallDirs { get; init; } = [];

    public IReadOnlyList<SaveListPath> Files { get; init; } = [];

    public IReadOnlyList<SaveListPath> Registry { get; init; } = [];

    /// <summary>Stores whose own cloud syncs this game: steam, epic, gog, origin, uplay.</summary>
    public IReadOnlyList<string> Cloud { get; init; } = [];

    /// <summary>Other titles that point here, like a game's earlier name.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
}

/// <summary>
/// FIND-01: the PCGamingWiki save list (the Ludusavi manifest), indexed for looking a game up by store ID, install
/// folder name or title. Names compare as lowercase letters and digits only.
/// </summary>
public sealed class SaveList
{
    private readonly Dictionary<long, SaveListGame> _bySteamId = [];
    private readonly Dictionary<long, SaveListGame> _byGogId = [];
    private readonly Dictionary<string, List<SaveListGame>> _byInstallDir = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SaveListGame> _byTitle = new(StringComparer.Ordinal);

    [JsonConstructor]
    public SaveList(IReadOnlyList<SaveListGame> games, string source, DateTime updatedUtc)
    {
        Games = games;
        Source = source;
        UpdatedUtc = updatedUtc;
        foreach (var game in games)
        {
            foreach (var id in game.SteamIds)
            {
                _bySteamId.TryAdd(id, game);
            }

            foreach (var id in game.GogIds)
            {
                _byGogId.TryAdd(id, game);
            }

            foreach (var dir in game.InstallDirs.Select(Normalize).Where(d => d.Length > 0).Distinct())
            {
                if (!_byInstallDir.TryGetValue(dir, out var list))
                {
                    _byInstallDir[dir] = list = [];
                }

                list.Add(game);
            }

            foreach (var title in game.Aliases.Prepend(game.Title).Select(Normalize).Where(t => t.Length > 0))
            {
                _byTitle.TryAdd(title, game);
            }
        }
    }

    public IReadOnlyList<SaveListGame> Games { get; }

    /// <summary>Where the list came from: a URL or a file.</summary>
    public string Source { get; }

    public DateTime UpdatedUtc { get; }

    public SaveListGame? BySteamId(long id) => _bySteamId.GetValueOrDefault(id);

    public SaveListGame? ByGogId(long id) => _byGogId.GetValueOrDefault(id);

    /// <summary>The games whose usual install folder has this name; several games can share one.</summary>
    public IReadOnlyList<SaveListGame> ByInstallDir(string folderName) =>
        _byInstallDir.TryGetValue(Normalize(folderName), out var games) ? games : [];

    public SaveListGame? ByTitle(string title) => _byTitle.GetValueOrDefault(Normalize(title));

    /// <summary>
    /// KAN-69: the one game whose title ends with this name's words, Roman and plain numbers alike, for a game in its own
    /// folder named without its series: "Black Ops 3" is "Call of Duty: Black Ops III", "Dark Souls 3" is "Dark Souls
    /// III". Only a name of two words or more, with six letters and digits at least, and only when exactly one title
    /// ends so ("Warfare 2" ends four); null otherwise, as a guess is worse than no match (LIB-06).
    /// </summary>
    public SaveListGame? ByNameEnd(string name)
    {
        var words = Words(name);
        if (words.Length < 2 || words.Sum(w => w.Length) < 6)
        {
            return null;
        }

        var endings = LazyInitializer.EnsureInitialized(ref _endings, () => Games.SelectMany(g => g.Aliases.Prepend(g.Title).Select(t => (Words: Words(t), Game: g))).ToList());
        var found = endings.Where(e => e.Words.Length >= words.Length && e.Words.AsSpan(e.Words.Length - words.Length).SequenceEqual(words))
            .Select(e => e.Game)
            .Distinct()
            .Take(2)
            .ToList();
        return found is [var only] ? only : null;
    }

    private List<(string[] Words, SaveListGame Game)>? _endings;

    /// <summary>
    /// A name's words in lower case, without marks, notes in brackets or accents, and Roman numbers II to X as digits:
    /// "Call of Duty(R): Black Ops III" is call, of, duty, black, ops, 3.
    /// </summary>
    public static string[] Words(string name)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var c in Fingerprinter.Plain(name).Normalize(NormalizationForm.FormD).Append(' '))
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(char.ToLowerInvariant(c));
            }
            else if (char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark && word.Length > 0)
            {
                words.Add(Roman.GetValueOrDefault(word.ToString(), word.ToString()));
                word.Clear();
            }
        }

        return [.. words];
    }

    private static readonly Dictionary<string, string> Roman = new(StringComparer.Ordinal)
    {
        ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5", ["vi"] = "6", ["vii"] = "7", ["viii"] = "8", ["ix"] = "9", ["x"] = "10",
    };

    /// <summary>
    /// "Slay the Spire 2" and "SlayTheSpire2" both become "slaythespire2" (LIB-06). Accents go too, since decomposing
    /// splits them off as marks that aren't letters: "Ragnarök" becomes "ragnarok".
    /// </summary>
    public static string Normalize(string name)
    {
        var text = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c))
            {
                text.Append(char.ToLowerInvariant(c));
            }
        }

        return text.ToString();
    }
}
