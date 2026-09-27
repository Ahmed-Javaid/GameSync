using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GameSync.Core.Discovery;

/// <summary>What GameSync takes over from Ludusavi's settings (ONB-04): where its backups are, the games it ignores, and the games added by hand.</summary>
public sealed record LudusaviConfig(string? BackupFolder, IReadOnlyList<string> IgnoredGames, IReadOnlyList<LudusaviCustomGame> CustomGames);

/// <param name="Extends">Adds to the save list's entry for the game; otherwise it replaces it, like Ludusavi's "override".</param>
public sealed record LudusaviCustomGame(string Name, bool Extends, IReadOnlyList<string> Files, IReadOnlyList<string> Registry);

/// <summary>One game's folder in Ludusavi's backup folder, from its <c>mapping.yaml</c>.</summary>
/// <param name="Drives">Backup folder name to drive, like "drive-C" to "C:".</param>
public sealed record LudusaviBackupSet(string Folder, string Title, IReadOnlyDictionary<string, string> Drives, IReadOnlyList<LudusaviBackup> Backups)
{
    public LudusaviBackup? Latest => Backups.MaxBy(b => b.WhenUtc);

    /// <summary>Where an unzipped backup's files are: the game's folder for ".", or its subfolder.</summary>
    public string CopyOf(LudusaviBackup backup) => backup.Name == "." ? Folder : Path.Combine(Folder, backup.Name);

    /// <summary>
    /// Where a folder on this PC sits in a backup's copy, by the drive folders the mapping names:
    /// <c>C:\Users\you\Documents\Game</c> is <c>&lt;copy&gt;\drive-C\Users\you\Documents\Game</c>. Null when the backup has no such drive.
    /// </summary>
    public string? Locate(string folder, string copy)
    {
        if (folder.Contains('<', StringComparison.Ordinal) || Path.GetPathRoot(folder) is not { Length: > 0 } drive)
        {
            return null;
        }

        var letter = drive.TrimEnd('\\', '/');
        var name = Drives.FirstOrDefault(d => d.Value.TrimEnd('\\', '/').Equals(letter, StringComparison.OrdinalIgnoreCase)).Key;
        return name is null ? null : Path.Combine(copy, name, folder[drive.Length..]);
    }
}

/// <summary>A full backup: "." (the files sit in the game's folder), a subfolder, or a zip.</summary>
public sealed record LudusaviBackup(string Name, DateTime WhenUtc, int Files)
{
    public bool IsZip => Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Reads Ludusavi's own files. They're data from outside, read as plain values only, never as types.</summary>
public static partial class LudusaviReader
{
    public static string DefaultConfig =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ludusavi", "config.yaml");

    public static LudusaviConfig ReadConfig(string configPath)
    {
        using var reader = new StreamReader(configPath);
        var config = Deserializer.Deserialize<Config?>(reader) ?? new Config();
        var custom = (config.CustomGames ?? [])
            .Where(g => !string.IsNullOrWhiteSpace(g.Name) && g.Ignore != true)
            .Select(g => new LudusaviCustomGame(
                g.Name!.Trim(),
                Extends: string.Equals(g.Integration, "extend", StringComparison.OrdinalIgnoreCase),
                (g.Files ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).ToList(),
                (g.Registry ?? []).Where(r => !string.IsNullOrWhiteSpace(r)).ToList()))
            .ToList();
        return new LudusaviConfig(
            string.IsNullOrWhiteSpace(config.Backup?.Path) ? null : config.Backup!.Path,
            (config.Backup?.IgnoredGames ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList(),
            custom);
    }

    /// <summary>Every game folder in the backup folder with a readable <c>mapping.yaml</c>; the rest go into <paramref name="problems"/>.</summary>
    public static IReadOnlyList<LudusaviBackupSet> ReadBackups(string backupFolder, List<string> problems)
    {
        var sets = new List<LudusaviBackupSet>();
        foreach (var folder in Directory.EnumerateDirectories(backupFolder).Order(StringComparer.OrdinalIgnoreCase))
        {
            var mappingPath = Path.Combine(folder, "mapping.yaml");
            if (!File.Exists(mappingPath))
            {
                if (Directory.EnumerateDirectories(folder, "drive-*").Any())
                {
                    problems.Add($"{Path.GetFileName(folder)}: has backed-up files but no mapping.yaml, so they can't be placed.");
                }

                continue;
            }

            try
            {
                using var reader = new StreamReader(mappingPath);
                var mapping = Deserializer.Deserialize<Mapping?>(reader);
                if (mapping?.Name is not { Length: > 0 } title)
                {
                    problems.Add($"{Path.GetFileName(folder)}: its mapping.yaml has no game name.");
                    continue;
                }

                var backups = (mapping.Backups ?? [])
                    .Where(b => !string.IsNullOrWhiteSpace(b.Name) && ParseTime(b.When) is not null)
                    .Select(b => new LudusaviBackup(b.Name!, ParseTime(b.When)!.Value, b.Files?.Count ?? 0))
                    .ToList();
                sets.Add(new LudusaviBackupSet(folder, title, mapping.Drives ?? [], backups));
            }
            catch (Exception e) when (e is YamlDotNet.Core.YamlException or IOException)
            {
                problems.Add($"{Path.GetFileName(folder)}: couldn't read its mapping.yaml ({e.Message}).");
            }
        }

        return sets;
    }

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Ludusavi writes nanoseconds, like "2026-06-21T16:02:12.362240900Z"; .NET reads up to seven digits.</summary>
    private static DateTime? ParseTime(string? text) =>
        text is not null && DateTime.TryParse(ExtraDigits().Replace(text, "$1"), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when)
            ? when
            : null;

    [GeneratedRegex(@"(\.\d{7})\d+")]
    private static partial Regex ExtraDigits();

    private sealed class Config
    {
        public BackupSection? Backup { get; set; }

        public List<CustomGame>? CustomGames { get; set; }
    }

    private sealed class BackupSection
    {
        public string? Path { get; set; }

        public List<string>? IgnoredGames { get; set; }
    }

    private sealed class CustomGame
    {
        public string? Name { get; set; }

        public string? Integration { get; set; }

        public bool? Ignore { get; set; }

        public List<string>? Files { get; set; }

        public List<string>? Registry { get; set; }
    }

    private sealed class Mapping
    {
        public string? Name { get; set; }

        public Dictionary<string, string>? Drives { get; set; }

        public List<Backup>? Backups { get; set; }
    }

    private sealed class Backup
    {
        public string? Name { get; set; }

        public string? When { get; set; }

        public Dictionary<string, object?>? Files { get; set; }
    }
}
