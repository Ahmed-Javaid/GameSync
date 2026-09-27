using GameSync.Core.Safety;

namespace GameSync.Core.Games;

public static class GameValidator
{
    /// <summary>Everything wrong with a game definition, including roots no rule may reach (R5). Empty means it's fine.</summary>
    public static IReadOnlyList<string> Problems(GameDefinition game, SensitivePathGuard guard)
    {
        var problems = new List<string>();
        if (game.Id.Value.Length > 40)
        {
            problems.Add($"The id '{game.Id}' is longer than 40 characters.");
        }

        if (string.IsNullOrWhiteSpace(game.Title))
        {
            problems.Add($"'{game.Id}' has no title.");
        }

        if (game.Rules.Count == 0 && game.Registry.Count == 0)
        {
            problems.Add($"{game.Title} has no save rules.");
        }

        foreach (var rule in game.Registry)
        {
            if (RegistryGuard.Check(rule.Key, [rule.Key]) is { } refusal)
            {
                problems.Add($"{game.Title}: {refusal}");
            }
        }

        foreach (var (key, folder) in game.Roots)
        {
            if (!RestorePathGuard.IsSafeRootKey(key))
            {
                problems.Add($"{game.Title}: '{key}' isn't a valid root name (letters, digits, '-' and '_').");
            }

            if (key == GameDefinition.RegistryRoot)
            {
                problems.Add($"{game.Title}: '{key}' is kept for registry saves; give the folder another root name.");
            }

            if (RootResolver.IsUnresolved(folder))
            {
                // A portable folder; the resolved form gets the checks below on each PC.
                problems.AddRange(RootResolver.CheckPlaceholders(folder).Select(p => $"{game.Title}: {p}"));
                var start = RootResolver.FolderPlaceholders.Append(RootResolver.InstallDir)
                    .Any(p => folder.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                if (!start && !Path.IsPathFullyQualified(folder[..folder.IndexOf('<', StringComparison.Ordinal)]))
                {
                    problems.Add($"{game.Title}: '{folder}' must be a full path or start with a folder like <documents>.");
                }
            }
            else if (!Path.IsPathFullyQualified(folder))
            {
                problems.Add($"{game.Title}: '{folder}' must be a full path, like C:\\Users\\you\\Documents\\Game, or start with a folder like <documents>.");
            }
            else if (guard.CheckRoot(folder) is { } refusal)
            {
                problems.Add($"{game.Title}: {refusal}");
            }
        }

        foreach (var rule in game.Rules)
        {
            if (!game.Roots.ContainsKey(rule.Root))
            {
                problems.Add($"{game.Title}: a rule uses the root '{rule.Root}', which isn't defined.");
            }

            foreach (var pattern in rule.Exclude.Prepend(rule.Include))
            {
                if (string.IsNullOrWhiteSpace(pattern) || Path.IsPathRooted(pattern) || pattern.Replace('\\', '/').Split('/').Contains(".."))
                {
                    problems.Add($"{game.Title}: the pattern '{pattern}' must stay inside its root folder.");
                }
            }
        }

        return problems;
    }
}
