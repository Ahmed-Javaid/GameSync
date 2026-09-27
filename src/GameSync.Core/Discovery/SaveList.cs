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
