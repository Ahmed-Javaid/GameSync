using GameSync.Core.Model;

namespace GameSync.Core.Games;

/// <summary>
/// FIND-08: save folders are stored with placeholders, such as <c>&lt;documents&gt;/My Games/Terraria</c>, and each PC
/// fills in its own folders. A placeholder this PC can't fill (no install folder for the game here, no Steam account)
/// stays in the path; the scanner then reports the game as Not available, never as having no saves.
/// </summary>
public sealed class RootResolver(
    IReadOnlyDictionary<string, string> folders,
    IReadOnlyDictionary<string, string> accounts,
    IReadOnlyDictionary<GameId, string> installDirs)
{
    public const string InstallDir = "<installDir>";

    /// <summary>Folders every Windows user has; one may start a save folder.</summary>
    public static readonly IReadOnlyList<string> FolderPlaceholders =
    [
        "<home>", "<documents>", "<publicDocuments>", "<roaming>", "<localAppData>", "<localLow>", "<savedGames>", "<programData>",
    ];

    /// <summary>Account IDs, which may appear anywhere in a save folder and are recorded with each version (PC-03).</summary>
    public static readonly IReadOnlyList<string> AccountPlaceholders = ["<steamUser>", "<epicUser>"];

    /// <summary>The game with this PC's folders filled in, and the account IDs it used.</summary>
    public GameDefinition Resolve(GameDefinition game)
    {
        var roots = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, folder) in game.Roots)
        {
            roots[key] = ResolveFolder(game.Id, folder, used);
        }

        return game with { Roots = roots, Accounts = used };
    }

    /// <summary>A path Windows could never have: it still holds a placeholder this PC couldn't fill.</summary>
    public static bool IsUnresolved(string folder) => folder.Contains('<', StringComparison.Ordinal);

    /// <summary>Why a save folder can't be used on this PC, for the game's Not available status.</summary>
    public static string DescribeUnresolved(string title, string folder)
    {
        if (folder.Contains(InstallDir, StringComparison.OrdinalIgnoreCase))
        {
            return $"{title} isn't installed on this PC: no install folder is set for it";
        }

        foreach (var placeholder in AccountPlaceholders)
        {
            if (folder.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
            {
                var store = placeholder == "<steamUser>" ? "Steam" : "Epic";
                return $"No {store} account is set on this PC, and {title}'s saves are kept per account";
            }
        }

        return $"This PC doesn't know the folder in '{folder}'";
    }

    /// <summary>Problems with a save folder's placeholders: unknown ones, or a folder placeholder anywhere but the start.</summary>
    public static IReadOnlyList<string> CheckPlaceholders(string folder)
    {
        var problems = new List<string>();
        var start = 0;
        while ((start = folder.IndexOf('<', start)) >= 0)
        {
            var end = folder.IndexOf('>', start);
            if (end < 0)
            {
                problems.Add($"'{folder}' has a '<' without a matching '>'.");
                break;
            }

            var token = folder[start..(end + 1)];
            var isFolder = FolderPlaceholders.Contains(token, StringComparer.OrdinalIgnoreCase) || token.Equals(InstallDir, StringComparison.OrdinalIgnoreCase);
            if (isFolder && start != 0)
            {
                problems.Add($"'{folder}': {token} can only start a save folder.");
            }
            else if (!isFolder && !AccountPlaceholders.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                problems.Add($"'{folder}' uses {token}, which GameSync doesn't know. Known: {string.Join(", ", FolderPlaceholders.Append(InstallDir).Concat(AccountPlaceholders))}.");
            }

            start = end + 1;
        }

        return problems;
    }

    private string ResolveFolder(GameId game, string folder, Dictionary<string, string> used)
    {
        var result = folder;
        foreach (var placeholder in FolderPlaceholders.Append(InstallDir))
        {
            if (!result.StartsWith(placeholder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = placeholder == InstallDir ? installDirs.GetValueOrDefault(game) : folders.GetValueOrDefault(placeholder);
            if (string.IsNullOrEmpty(value))
            {
                return folder;
            }

            result = Path.TrimEndingDirectorySeparator(value) + result[placeholder.Length..];
            break;
        }

        foreach (var placeholder in AccountPlaceholders)
        {
            if (!result.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = placeholder.Trim('<', '>');
            if (!accounts.TryGetValue(key, out var value) || string.IsNullOrEmpty(value))
            {
                return folder;
            }

            result = result.Replace(placeholder, value, StringComparison.OrdinalIgnoreCase);
            used[key] = value;
        }

        return result.Replace('/', Path.DirectorySeparatorChar);
    }
}
