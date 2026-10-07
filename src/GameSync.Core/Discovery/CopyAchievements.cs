using System.Globalization;
using System.Text.Json;

namespace GameSync.Core.Discovery;

/// <summary>
/// KAN-123: what a copy Steam doesn't run has unlocked, from its own record of it. A copy that plays Steam's part keeps such
/// a record, and the person adds the folder holding them (Settings, Achievements): one folder per game, named by its Steam
/// app ID, with the record somewhere inside, <c>achievements.ini</c> or <c>achievements.json</c>. An INI record has a
/// section per achievement with <c>achieved=true</c> and when (<c>timestamp</c>, <c>UnlockTime</c> or <c>TimeUnlocked</c>,
/// Unix seconds, or in some thousands of seconds); a JSON one is <c>{"ACH_NAME": {"earned": true, "earned_time":
/// 1600000000}}</c>. A record with nothing in it yet is a game with none unlocked. The names are Steam's own
/// (<c>internal_name</c>), so Steam's list names each one. Read only; nothing is changed or sent. Where such folders are
/// is the person's to add: no list of them comes with GameSync (the safety rules, as with save folders).
/// </summary>
public static class CopyAchievements
{
    /// <summary>The largest record read: a game's is a few kilobytes.</summary>
    private const long Largest = 1024 * 1024;

    private static readonly string[] Names = ["achievements.ini", "achievements.json"];

    private static readonly string[] Achieved = ["achieved", "earned", "unlocked"];

    private static readonly string[] Times = ["timestamp", "unlocktime", "unlock_time", "timeunlocked", "earned_time", "time"];

    private static readonly EnumerationOptions Within = new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = 3,
        MatchCasing = MatchCasing.CaseInsensitive,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>The game's record in one of <paramref name="folders"/>, the one written last; null when there's none.</summary>
    public static string? Find(IEnumerable<string> folders, long appId)
    {
        var app = appId.ToString(CultureInfo.InvariantCulture);
        FileInfo? found = null;
        foreach (var folder in folders)
        {
            var game = Path.Combine(folder, app);
            if (!Directory.Exists(game))
            {
                continue;
            }

            try
            {
                foreach (var name in Names)
                {
                    foreach (var file in new DirectoryInfo(game).EnumerateFiles(name, Within))
                    {
                        if (found is null || file.LastWriteTimeUtc > found.LastWriteTimeUtc)
                        {
                            found = file;
                        }
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A folder that can't be read has no record for us.
            }
        }

        return found?.FullName;
    }

    /// <summary>How many games have a record in a folder: its subfolders named by a Steam app ID with a record inside.</summary>
    public static int CountIn(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? new DirectoryInfo(folder).EnumerateDirectories()
                    .Count(d => long.TryParse(d.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var app) && Find([folder], app) is not null)
                : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// What a record holds as unlocked: each achievement's Steam name, and when if the record says; null when it can't be
    /// read (too large, unreadable, or in neither shape). The names compare without regard to case.
    /// </summary>
    public static IReadOnlyDictionary<string, DateTime?>? Read(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > Largest)
            {
                return null;
            }

            var text = File.ReadAllText(file).TrimStart('\uFEFF');
            return text.TrimStart().StartsWith('{') ? FromJson(text) : FromIni(text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, DateTime?> FromIni(string text)
    {
        var unlocked = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
        string? section = null;
        var achieved = false;
        DateTime? at = null;

        void End()
        {
            if (section is not null && achieved)
            {
                unlocked[section] = at;
            }
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                End();
                section = line[1..^1].Trim();
                achieved = false;
                at = null;
                continue;
            }

            var equals = line.IndexOf('=');
            if (section is null || equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (Achieved.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                achieved = value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
            }
            else if (Times.Contains(key, StringComparer.OrdinalIgnoreCase) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            {
                at = When(seconds);
            }
        }

        End();
        return unlocked;
    }

    private static Dictionary<string, DateTime?>? FromJson(string text)
    {
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var unlocked = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var achieved = false;
            DateTime? at = null;
            foreach (var field in entry.Value.EnumerateObject())
            {
                if (Achieved.Contains(field.Name, StringComparer.OrdinalIgnoreCase))
                {
                    achieved = field.Value.ValueKind == JsonValueKind.True || (field.Value.ValueKind == JsonValueKind.Number && field.Value.GetDouble() != 0);
                }
                else if (Times.Contains(field.Name, StringComparer.OrdinalIgnoreCase) && field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt64(out var seconds))
                {
                    at = When(seconds);
                }
            }

            if (achieved)
            {
                unlocked[entry.Name] = at;
            }
        }

        return unlocked;
    }

    /// <summary>
    /// Unix seconds as a time; none for 0 or anything out of reason. Some records keep thousands of seconds (1790181 for
    /// 23 Sep 2026): a time that would be before 2000 is read that way when it then falls after.
    /// </summary>
    private static DateTime? When(long seconds) => seconds switch
    {
        >= Y2000 and < Y3000 => DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime,
        >= Y2000 / 1000 and < Y3000 / 1000 => DateTimeOffset.FromUnixTimeSeconds(seconds * 1000).UtcDateTime,
        _ => null,
    };

    /// <summary>1 January 2000 and 3000, in Unix seconds.</summary>
    private const long Y2000 = 946684800, Y3000 = 32503680000;
}
