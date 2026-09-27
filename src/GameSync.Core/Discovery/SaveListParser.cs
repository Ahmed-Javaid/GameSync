using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GameSync.Core.Discovery;

/// <summary>
/// Reads the Ludusavi manifest's YAML and keeps what Windows needs: paths and registry keys that apply on Windows, the
/// stores each applies to, store IDs, install folder names, store clouds and aliases. The file is data from outside,
/// so it's read as plain values only, never as types.
/// </summary>
public static class SaveListParser
{
    private static readonly string[] CloudStores = ["steam", "epic", "gog", "origin", "uplay"];

    public static SaveList Parse(TextReader yaml, string source, DateTime updatedUtc)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var entries = deserializer.Deserialize<Dictionary<string, Entry?>>(yaml) ?? [];

        var games = new List<SaveListGame>();
        var aliases = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (title, entry) in entries)
        {
            if (entry?.Alias is { Length: > 0 } target)
            {
                if (!aliases.TryGetValue(target, out var list))
                {
                    aliases[target] = list = [];
                }

                list.Add(title);
            }
        }

        foreach (var (title, entry) in entries)
        {
            if (entry is null || entry.Alias is { Length: > 0 })
            {
                continue;
            }

            var files = Windows(entry.Files);
            var registry = Windows(entry.Registry);
            var steamIds = new List<long>();
            if (entry.Steam?.Id is { } steamId)
            {
                steamIds.Add(steamId);
            }

            steamIds.AddRange(entry.Id?.SteamExtra ?? []);
            var gogIds = new List<long>();
            if (entry.Gog?.Id is { } gogId)
            {
                gogIds.Add(gogId);
            }

            gogIds.AddRange(entry.Id?.GogExtra ?? []);
            var installDirs = entry.InstallDir?.Keys.ToList() ?? [];

            // A title with nothing to find it by, and nothing to find, only takes up room.
            if (files.Count == 0 && registry.Count == 0 && steamIds.Count == 0 && gogIds.Count == 0 && installDirs.Count == 0)
            {
                continue;
            }

            games.Add(new SaveListGame
            {
                Title = title,
                SteamIds = steamIds,
                GogIds = gogIds,
                InstallDirs = installDirs,
                Files = files,
                Registry = registry,
                Cloud = CloudStores.Where(s => entry.Cloud?.GetValueOrDefault(s) == true).ToList(),
                Aliases = aliases.GetValueOrDefault(title) ?? [],
            });
        }

        return new SaveList(games, source, updatedUtc);
    }

    /// <summary>The entries that apply on Windows: no condition, or a condition that allows Windows.</summary>
    private static List<SaveListPath> Windows(Dictionary<string, Rule?>? rules)
    {
        var paths = new List<SaveListPath>();
        foreach (var (path, rule) in rules ?? [])
        {
            var tags = rule?.Tags ?? [];
            var when = rule?.When ?? [];
            var windows = when.Count == 0 ? [new When()] : when.Where(w => w.Os is null or "windows").ToList();
            if (windows.Count == 0)
            {
                continue;
            }

            IReadOnlyList<string>? stores = windows.Any(w => w.Store is null)
                ? null
                : windows.Select(w => w.Store!).Distinct().ToList();
            var save = tags.Contains("save") || tags.Count == 0;
            var config = tags.Contains("config");
            paths.Add(new SaveListPath(path, save, config, stores));
        }

        return paths;
    }

    private sealed class Entry
    {
        public Dictionary<string, Rule?>? Files { get; set; }

        public Dictionary<string, Rule?>? Registry { get; set; }

        public Dictionary<string, object?>? InstallDir { get; set; }

        public StoreId? Steam { get; set; }

        public StoreId? Gog { get; set; }

        public Ids? Id { get; set; }

        public Dictionary<string, bool>? Cloud { get; set; }

        public string? Alias { get; set; }
    }

    private sealed class Rule
    {
        public List<string>? Tags { get; set; }

        public List<When>? When { get; set; }
    }

    private sealed class When
    {
        public string? Os { get; set; }

        public string? Store { get; set; }
    }

    private sealed class StoreId
    {
        public long? Id { get; set; }
    }

    private sealed class Ids
    {
        public List<long>? SteamExtra { get; set; }

        public List<long>? GogExtra { get; set; }
    }
}
