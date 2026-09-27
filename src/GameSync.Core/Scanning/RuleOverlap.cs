using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;

namespace GameSync.Core.Scanning;

/// <summary>
/// FOLD-11: two games whose rules take the same file, or the same registry key. That's an error on both games, since a
/// restore of either would change the other's save. Only folders where one game's root sits in the other's are read.
/// </summary>
public static class RuleOverlap
{
    private const int MaxFilesChecked = 20_000;

    /// <summary>Each game that shares a save with another, and what it shares.</summary>
    public static IReadOnlyDictionary<GameId, string> Find(IReadOnlyList<GameDefinition> games)
    {
        var problems = new Dictionary<GameId, string>();
        for (var i = 0; i < games.Count; i++)
        {
            for (var j = i + 1; j < games.Count; j++)
            {
                if (Shared(games[i], games[j]) is { } what)
                {
                    problems.TryAdd(games[i].Id, $"{games[j].Title} takes {what} too, and two games can't share a save. Give each its own files.");
                    problems.TryAdd(games[j].Id, $"{games[i].Title} takes {what} too, and two games can't share a save. Give each its own files.");
                }
            }
        }

        return problems;
    }

    private static string? Shared(GameDefinition a, GameDefinition b)
    {
        foreach (var keyA in a.Registry)
        {
            if (b.Registry.FirstOrDefault(keyB => Nested(keyA.Key, keyB.Key)) is not null)
            {
                return $"the registry key {keyA.Key}";
            }
        }

        foreach (var ruleA in a.Rules)
        {
            foreach (var ruleB in b.Rules)
            {
                if (!a.Roots.TryGetValue(ruleA.Root, out var rootA) || !b.Roots.TryGetValue(ruleB.Root, out var rootB) ||
                    RootResolver.IsUnresolved(rootA) || RootResolver.IsUnresolved(rootB))
                {
                    continue;
                }

                var inner = Inside(rootA, rootB) ? rootA : Inside(rootB, rootA) ? rootB : null;
                if (inner is null || !Directory.Exists(inner))
                {
                    continue;
                }

                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in Directory.EnumerateFiles(inner, "*", options).Take(MaxFilesChecked))
                {
                    if (Takes(ruleA, rootA, file) && Takes(ruleB, rootB, file))
                    {
                        return file;
                    }
                }
            }
        }

        return null;
    }

    private static bool Takes(SaveRule rule, string root, string file)
    {
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        return new Glob(rule.Include).IsMatch(relative) &&
            !rule.Exclude.Any(e => new Glob(e).IsMatch(relative)) &&
            !(rule.UseDefaultExcludes && DefaultExcludes.All.Any(e => e.IsMatch(relative)));
    }

    private static bool Inside(string folder, string outer)
    {
        var inner = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var around = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outer));
        return inner.Equals(around, StringComparison.OrdinalIgnoreCase) || inner.StartsWith(around + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Nested(string keyA, string keyB) =>
        RegistryGuard.Check(keyA, [keyB]) is null || RegistryGuard.Check(keyB, [keyA]) is null;
}
