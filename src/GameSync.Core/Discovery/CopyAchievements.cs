using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameSync.Core.Discovery;

/// <summary>
/// One achievement as a copy's record has it: its name, whether it's unlocked and when. A record that keeps each
/// achievement by a checksum of its name has no name here, only <paramref name="Crc"/> (CRC-32 of Steam's name).
/// </summary>
public sealed record RecordEntry(string? Name, bool Unlocked, DateTime? At, uint? Crc = null);

/// <summary>What a copy Steam doesn't run left about its achievements, when GameSync can't read what's unlocked in it (design system version 55).</summary>
public enum RecordClueKind
{
    /// <summary>Nothing: no file about its achievements under its Steam numbers or in its own folder.</summary>
    Nothing,

    /// <summary>A file for its achievements that says nothing of any: made, and not written since, or empty so far.</summary>
    Unwritten,

    /// <summary>A file for its achievements that says nothing of any, beside a file of the game's stats that does: it counts, but never unlocks.</summary>
    StatsOnly,

    /// <summary>A settings folder of its Steam files with no list of the game's achievements in it, so it can't record any.</summary>
    NoList,
}

/// <summary>A clue to why GameSync can't see a copy's achievements: its kind, the file or folder it's about, and the stats kept beside an empty record.</summary>
public sealed record RecordClue(RecordClueKind Kind, string? Path = null, string? Stats = null);

/// <summary>
/// KAN-123: what a copy Steam doesn't run has unlocked, from its own record of it. A copy that plays Steam's part keeps such
/// a record, in a folder named by the game's Steam app ID (in AppData, Documents, Public Documents, ProgramData or the
/// game's own folder) or in the game's own folder. Records come in several shapes (ACH-18, the owner,
/// 8 Oct 2026: "Make sure that games have achievements to the best of our ability"), and each is read:
/// <list type="bullet">
/// <item>INI with a section per achievement and its state and time (<c>Achieved</c>, <c>achieved</c>, <c>State</c>,
/// <c>HaveAchieved</c>, <c>unlocked</c>; <c>UnlockTime</c>, <c>timestamp</c>, <c>Time</c>, <c>HaveAchievedTime</c>…), in
/// decimal or as hexadecimal little-endian numbers, with progress (<c>CurProgress</c> reaching <c>MaxProgress</c> is
/// unlocked); the section's name may be prefixed <c>ACHIEVEMENTS.</c>;</item>
/// <item>INI with an <c>[Achievements]</c> list of name and 1 or 0, and their times in <c>[AchievementsUnlockTimes]</c>;</item>
/// <item>INI with <c>[State]</c> (name and <c>0101</c> when unlocked) and <c>[Time]</c> (hexadecimal) lists;</item>
/// <item>JSON keyed by name (<c>{"ACH": {"earned": true, "earned_time": 1600000000}}</c>), or an array of entries naming
/// themselves (<c>apiname</c>, <c>name</c>, <c>id</c>), either maybe under an <c>achievements</c> key;</item>
/// <item>a binary table of 24-byte entries keyed by the CRC-32 of each achievement's Steam name.</item>
/// </list>
/// An entry counts only when it says whether it's unlocked, so a list of a game's achievements kept for a copy to know
/// them (its definitions) is never taken for a record. Times are Unix seconds, or in some records thousands of seconds or
/// milliseconds. The names are Steam's own (<c>internal_name</c>), so Steam's list names each one. Read only; nothing is
/// changed or sent. No list of where such records live comes with GameSync (the safety rules, as with save folders):
/// GameSync finds them by itself, knowing a record by the game's achievements named inside it (<see cref="Candidates"/>,
/// <see cref="GameFolderCandidates"/>, <see cref="Recognizes"/>), and the person can still add a folder for records kept
/// anywhere else.
/// </summary>
public static partial class CopyAchievements
{
    /// <summary>The largest record read: a game's is a few kilobytes.</summary>
    private const long Largest = 1024 * 1024;

    /// <summary>The kinds of file a record is kept in: INI and JSON text, configuration files, and binary tables (or INI text under another name).</summary>
    private static readonly string[] RecordKinds = [".ini", ".json", ".cfg", ".bin", ".dat"];

    private static readonly HashSet<string> StateKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "achieved", "earned", "unlocked", "state", "haveachieved", "isunlocked", "isachieved",
    };

    private static readonly HashSet<string> TimeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "unlocktime", "unlock_time", "unlockedtime", "unlocked_time", "timeunlocked", "earned_time", "earnedtime", "time", "timestamp",
        "haveachievedtime", "havehaveachievedtime", "date",
    };

    private static readonly HashSet<string> ProgressKeys = new(StringComparer.OrdinalIgnoreCase) { "curprogress", "progress", "cur_progress" };

    private static readonly HashSet<string> MaxProgressKeys = new(StringComparer.OrdinalIgnoreCase) { "maxprogress", "max_progress" };

    private static readonly HashSet<string> NameKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "apiname", "api_name", "name", "id", "achievement", "ach_name", "internal_name",
    };

    /// <summary>Sections of an INI record that aren't achievements: the record's own settings and lists.</summary>
    private static readonly HashSet<string> NotAchievements = new(StringComparer.OrdinalIgnoreCase)
    {
        "SteamAchievements", "Steam", "Steam64", "Achievements", "AchievementsUnlockTimes", "UnlockTimes", "State", "Time", "Stats", "Settings",
    };

    private static readonly EnumerationOptions Within = new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = 3,
        MatchCasing = MatchCasing.CaseInsensitive,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>
    /// A record in one of the folders added in Settings (KAN-123): under the game's Steam number, a file named as records
    /// are (achievements, or else stats), the one written last; null when there's none.
    /// </summary>
    public static string? Find(IEnumerable<string> folders, long appId)
    {
        var app = appId.ToString(CultureInfo.InvariantCulture);
        FileInfo? found = null;
        var foundRank = int.MaxValue;
        foreach (var folder in folders)
        {
            var game = Path.Combine(folder, app);
            if (!Directory.Exists(game))
            {
                continue;
            }

            try
            {
                foreach (var file in new DirectoryInfo(game).EnumerateFiles("*", Within))
                {
                    if (RecordName(file.Name) is { } rank && file.Length <= Largest &&
                        (rank < foundRank || (rank == foundRank && file.LastWriteTimeUtc > found!.LastWriteTimeUtc)))
                    {
                        found = file;
                        foundRank = rank;
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

    /// <summary>How a file's name says it's a record: 0 for one about achievements, 1 for stats, null for neither or another kind.</summary>
    private static int? RecordName(string name)
    {
        if (!RecordKinds.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        return stem.Contains("achiev", StringComparison.OrdinalIgnoreCase) ? 0 : stem.Equals("stats", StringComparison.OrdinalIgnoreCase) ? 1 : null;
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

    /// <summary>The most files looked at in one game's folder, and the most folders walked in one look.</summary>
    private const int MostFilesInAGame = 64, MostFolders = 400_000;

    /// <summary>
    /// Design system version 54 (the owner, 8 Oct 2026: "Tracking by itself", then "find them itself"): files that could be
    /// a copy's own record of one of these games, found with no folder added: record files (<c>.ini</c>, <c>.json</c>,
    /// <c>.cfg</c>, <c>.bin</c>, <c>.dat</c>) of a megabyte at most in a folder named by the game's Steam number, anywhere
    /// under <paramref name="roots"/> a few folders down, as such copies keep them (AppData, Documents, Public Documents,
    /// ProgramData). Which of them is the game's record is told by what's in it (<see cref="Recognizes"/>), so no list of
    /// where they live is needed. Folders in <paramref name="leftOut"/> (GameSync's own) and links are never walked into.
    /// </summary>
    public static IReadOnlyDictionary<long, IReadOnlyList<string>> Candidates(IEnumerable<string> roots, IReadOnlySet<long> apps, IEnumerable<string>? leftOut = null,
        int depth = 4, CancellationToken ct = default)
    {
        var found = new Dictionary<long, List<string>>();
        if (apps.Count == 0)
        {
            return new Dictionary<long, IReadOnlyList<string>>();
        }

        var skip = Skipped(leftOut);
        var oneLevel = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = false };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(DirectoryInfo Folder, int Depth)>();
        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(root))
            {
                queue.Enqueue((new DirectoryInfo(root), 0));
            }
        }

        var walked = 0;
        while (queue.Count > 0 && walked < MostFolders)
        {
            ct.ThrowIfCancellationRequested();
            var (folder, level) = queue.Dequeue();
            var full = Path.TrimEndingDirectorySeparator(folder.FullName) + Path.DirectorySeparatorChar;
            if (!seen.Add(full) || skip.Any(s => full.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            walked++;
            if (level > 0 && long.TryParse(folder.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var app) && apps.Contains(app))
            {
                var files = Files(folder);
                if (files.Count > 0)
                {
                    (found.TryGetValue(app, out var list) ? list : found[app] = []).AddRange(files);
                }

                continue;
            }

            if (level >= depth)
            {
                continue;
            }

            try
            {
                foreach (var sub in folder.EnumerateDirectories("*", oneLevel))
                {
                    queue.Enqueue((sub, level + 1));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // A folder that can't be read holds nothing for us.
            }
        }

        return found.ToDictionary(f => f.Key, f => (IReadOnlyList<string>)f.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        static List<string> Files(DirectoryInfo game)
        {
            try
            {
                return game.EnumerateFiles("*", Within)
                    .Where(f => RecordKinds.Contains(f.Extension, StringComparer.OrdinalIgnoreCase) && f.Length <= Largest)
                    .Take(MostFilesInAGame)
                    .Select(f => f.FullName)
                    .ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return [];
            }
        }
    }

    /// <summary>
    /// ACH-18: files in a game's own folder that could be its copy's record, wherever in it they are, as
    /// some copies keep theirs beside the game rather than in a folder named by its Steam number: record files named for
    /// achievements or stats, a megabyte at most, <paramref name="depth"/> folders down, no links followed. Which is the
    /// record is told by what's in it, as for <see cref="Candidates"/>.
    /// </summary>
    public static IReadOnlyList<string> GameFolderCandidates(string? folder, IEnumerable<string>? leftOut = null, int depth = 8)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        var skip = Skipped(leftOut);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = depth,
            MatchCasing = MatchCasing.CaseInsensitive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles("*", options)
                .Where(f => RecordName(f.Name) is not null && f.Length <= Largest &&
                            !skip.Any(s => f.FullName.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
                .Take(MostFilesInAGame * 4)
                .Select(f => f.FullName)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }

    private static List<string> Skipped(IEnumerable<string>? leftOut) =>
        (leftOut ?? []).Where(f => !string.IsNullOrEmpty(f)).Select(f => Path.TrimEndingDirectorySeparator(Path.GetFullPath(f)) + Path.DirectorySeparatorChar).ToList();

    /// <summary>
    /// ACH-18: the Steam app IDs a copy says it is, in its own folder: a <c>steam_appid.txt</c>, or an
    /// <c>AppId</c> line in one of its configuration files (<c>AppId=2215430</c>, <c>app_id = 2215430</c>), within
    /// <paramref name="depth"/> folders. A copy's record is kept under the ID it says, which may not be the one GameSync
    /// took for the game (a game's edition with an ID of its own).
    /// </summary>
    public static IReadOnlyList<long> DeclaredAppIds(string? folder, int depth = 6)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = depth,
            MatchCasing = MatchCasing.CaseInsensitive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        var ids = new List<long>();
        try
        {
            var read = 0;
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                var ext = file.Extension;
                var appIdFile = file.Name.Equals("steam_appid.txt", StringComparison.OrdinalIgnoreCase);
                if (!appIdFile && !ext.Equals(".ini", StringComparison.OrdinalIgnoreCase) || file.Length > 64 * 1024 || read++ > 300)
                {
                    continue;
                }

                try
                {
                    var text = File.ReadAllText(file.FullName);
                    if (appIdFile)
                    {
                        if (long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
                        {
                            ids.Add(id);
                        }

                        continue;
                    }

                    foreach (Match match in AppIdLine().Matches(text))
                    {
                        if (long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
                        {
                            ids.Add(id);
                        }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // The next file.
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // What was read stands.
        }

        return ids.Distinct().ToList();
    }

    /// <summary>A configuration file's line giving a Steam app ID: <c>AppId=123</c>, <c>RealAppId = 123</c>, <c>app_id: "123"</c>.</summary>
    [GeneratedRegex(@"^[ \t]*(?:real)?app_?id[ \t]*[=:][ \t]*""?(\d{3,10})""?[ \t]*(?:[#;].*)?\r?$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex AppIdLine();

    /// <summary>
    /// Every achievement a record says something of, by name, unlocked or not; null when it can't be read. One keyed by
    /// checksums names none (<see cref="Entries"/> has them). What tells a record from any other file (<see cref="Recognizes"/>).
    /// </summary>
    public static IReadOnlySet<string>? Names(string file) =>
        Entries(file) is { } entries ? entries.Where(e => e.Name is not null).Select(e => e.Name!).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;

    /// <summary>
    /// Whether a file is a game's record (design system version 54): of the achievements it says something of, at least one
    /// is the game's by Steam's own name (or the checksum of it), and at least half of what it names are the game's, so a
    /// file that only mentions one in passing isn't taken for it. A list of a game's achievements that says nothing of
    /// whether they're unlocked (its definitions) is no record.
    /// </summary>
    public static bool Recognizes(string file, IEnumerable<string> achievements) => Matches(file, achievements) > 0;

    /// <summary>How many of the game's achievements a record says something of, when it's the game's record (<see cref="Recognizes"/>); else 0.</summary>
    public static int Matches(string file, IEnumerable<string> achievements)
    {
        if (Entries(file) is not { Count: > 0 } entries)
        {
            return 0;
        }

        var ours = new HashSet<string>(achievements, StringComparer.OrdinalIgnoreCase);
        if (entries.Any(e => e.Crc is not null))
        {
            // A table keyed by checksums also holds the game's stats, whose checksums are no achievement's: one match is
            // enough, as a chance match of a 32-bit checksum is one in four billion.
            var sums = ours.Select(Crc32).ToHashSet();
            return entries.Count(e => e.Crc is { } crc && sums.Contains(crc));
        }

        var matching = entries.Count(e => e.Name is { } name && ours.Contains(name));
        return matching > 0 && matching * 2 >= entries.Count ? matching : 0;
    }

    /// <summary>
    /// The game's record among <paramref name="files"/>: of those that are (<see cref="Recognizes"/>), the one saying
    /// something of the most of its achievements, then the one written last. With none, a record with nothing in it yet (a
    /// copy that has unlocked none, as the owner's Sons of the Forest on 8 Oct 2026): an empty file named for
    /// achievements, which can't say anything is unlocked. Null when there's neither.
    /// </summary>
    /// <param name="allowBlank">Whether an empty file may be the record: only in a folder named by the game's Steam number, never anywhere in its own folder.</param>
    public static string? Best(IEnumerable<string> files, IReadOnlyCollection<string> achievements, bool allowBlank = true)
    {
        var all = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return all.Select(f => (File: f, Matches: Matches(f, achievements)))
                .Where(f => f.Matches > 0)
                .OrderByDescending(f => f.Matches)
                .ThenByDescending(f => File.GetLastWriteTimeUtc(f.File))
                .Select(f => f.File)
                .FirstOrDefault()
            ?? (allowBlank
                ? all.Where(f => RecordName(Path.GetFileName(f)) == 0 && IsBlank(f)).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null);
    }

    /// <summary>
    /// Design system version 55 (the owner, 8 Oct 2026: "have a little question mark on the achivement page for the game that
    /// when hovered it explains why the game is prolly not being picked up"): what a copy left about its achievements when
    /// GameSync can't read what's unlocked in it, from the files it found (<paramref name="files"/>: those under the game's
    /// Steam numbers and those named for achievements or stats in its own folder) and the copy's own folder. In order: a
    /// file for its achievements that says nothing of any (empty, or a header and a count of 0), with or without the game's
    /// stats kept beside it; a settings folder of its Steam files (<c>steam_settings</c>) with no list of its achievements
    /// in it, so it can't record any; else nothing. Read only.
    /// </summary>
    public static RecordClue Clue(IEnumerable<string> files, string? gameFolder, IEnumerable<string>? leftOut = null)
    {
        var all = files.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToList();
        var empty = all.Where(f => RecordName(Path.GetFileName(f)) == 0 && SaysNothing(f))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (empty is not null)
        {
            // The game's stats beside it, or one folder up: a copy that counts what you do, but never unlocks for it.
            var near = new[] { Path.GetDirectoryName(empty), Path.GetDirectoryName(Path.GetDirectoryName(empty)) }.OfType<string>().ToList();
            var stats = all.FirstOrDefault(f => RecordName(Path.GetFileName(f)) == 1 && !IsBlank(f) &&
                                                near.Contains(Path.GetDirectoryName(f) ?? "", StringComparer.OrdinalIgnoreCase));
            return stats is null
                ? new RecordClue(RecordClueKind.Unwritten, empty)
                : new RecordClue(RecordClueKind.StatsOnly, empty, stats);
        }

        return SettingsWithoutList(gameFolder, leftOut) is { } settings ? new RecordClue(RecordClueKind.NoList, settings) : new RecordClue(RecordClueKind.Nothing);
    }

    /// <summary>
    /// A record that says nothing of any achievement: empty, or a header and a count of 0 (a few hundred bytes at most). A
    /// list of a game's achievements for its Steam files to know them (in <c>steam_settings</c>, or longer) is no record.
    /// </summary>
    private static bool SaysNothing(string file)
    {
        if (IsBlank(file))
        {
            return true;
        }

        var parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return new FileInfo(file).Length <= 512 && !parts.Contains("steam_settings", StringComparer.OrdinalIgnoreCase) && Entries(file) is { Count: 0 };
    }

    /// <summary>
    /// A settings folder of a copy's Steam files, <c>steam_settings</c>, anywhere in its own folder, that holds no list of
    /// the game's achievements (no file named for them): such a copy records none (version 55). Null when there's none.
    /// </summary>
    public static string? SettingsWithoutList(string? gameFolder, IEnumerable<string>? leftOut = null, int depth = 8)
    {
        if (string.IsNullOrEmpty(gameFolder) || !Directory.Exists(gameFolder))
        {
            return null;
        }

        var skip = Skipped(leftOut);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = depth,
            MatchCasing = MatchCasing.CaseInsensitive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        try
        {
            return new DirectoryInfo(gameFolder).EnumerateDirectories("steam_settings", options)
                .Where(d => !skip.Any(s => (d.FullName + Path.DirectorySeparatorChar).StartsWith(s, StringComparison.OrdinalIgnoreCase)))
                .Take(8)
                .FirstOrDefault(d => !d.EnumerateFiles("*achiev*", Within).Any())?.FullName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>A file with nothing in it: no bytes, only spaces, or an empty JSON object or array.</summary>
    public static bool IsBlank(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > 64)
            {
                return false;
            }

            var text = File.ReadAllText(file).Trim().Trim('﻿').Trim();
            return text is "" or "{}" or "[]";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// What a record holds as unlocked: each achievement's Steam name, and when if the record says; null when it can't be
    /// read (too large, unreadable, or in no shape known). The names compare without regard to case. A record keyed by
    /// checksums is named from <paramref name="achievements"/>, the game's list, and without it names nothing.
    /// </summary>
    public static IReadOnlyDictionary<string, DateTime?>? Read(string file, IEnumerable<string>? achievements = null)
    {
        if (Entries(file) is not { } entries)
        {
            return null;
        }

        var byCrc = entries.Any(e => e.Crc is not null) && achievements is not null
            ? achievements.Distinct(StringComparer.OrdinalIgnoreCase).GroupBy(Crc32).ToDictionary(g => g.Key, g => g.First())
            : null;
        var unlocked = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries.Where(e => e.Unlocked))
        {
            var name = e.Name ?? (e.Crc is { } crc && byCrc is not null ? byCrc.GetValueOrDefault(crc) : null);
            if (name is not null && (!unlocked.TryGetValue(name, out var at) || at is null))
            {
                unlocked[name] = e.At;
            }
        }

        return unlocked;
    }

    /// <summary>Every achievement a record says something of, in whichever shape it's in; null when it can't be read.</summary>
    public static IReadOnlyList<RecordEntry>? Entries(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > Largest)
            {
                return null;
            }

            var bytes = File.ReadAllBytes(file);
            if (Checksummed(bytes) is { } table)
            {
                return table;
            }

            if (Text(bytes) is not { } text)
            {
                return null;
            }

            // JSON: an object, or an array of objects; an INI file starts with "[" too, but its first section's name follows.
            var start = text.TrimStart();
            var json = start.StartsWith('{') || (start.StartsWith('[') && start[1..].TrimStart() is { Length: > 0 } rest && rest[0] is '{' or ']');
            return json ? FromJson(text) : FromIni(text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>A record's text: UTF-8, or UTF-16 with or without its byte-order mark; null for binary in no shape known.</summary>
    private static string? Text(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 4 && bytes.Length % 2 == 0 && bytes[1] == 0 && bytes[3] == 0 && bytes[0] != 0)
        {
            return Encoding.Unicode.GetString(bytes);
        }

        return bytes.AsSpan().IndexOf((byte)0) >= 0 ? null : Encoding.UTF8.GetString(bytes).TrimStart('﻿');
    }

    /// <summary>
    /// A binary table keyed by checksums: a count, then 24 bytes for each achievement or stat: the CRC-32 of its Steam name,
    /// its unlock time at byte 8 and its value at byte 20 (0 or 1 for an achievement; a stat's runs higher). Null when the
    /// bytes aren't that shape.
    /// </summary>
    private static List<RecordEntry>? Checksummed(byte[] bytes)
    {
        const int Size = 24;
        if (bytes.Length < 4 + Size || (bytes.Length - 4) % Size != 0 || BinaryPrimitives.ReadInt32LittleEndian(bytes) != (bytes.Length - 4) / Size)
        {
            return null;
        }

        var entries = new List<RecordEntry>();
        for (var at = 4; at + Size <= bytes.Length; at += Size)
        {
            var entry = bytes.AsSpan(at, Size);
            var value = BinaryPrimitives.ReadInt32LittleEndian(entry[20..]);
            if (value is 0 or 1)
            {
                entries.Add(new RecordEntry(null, value == 1, value == 1 ? When(BinaryPrimitives.ReadInt32LittleEndian(entry[8..])) : null,
                    BinaryPrimitives.ReadUInt32LittleEndian(entry)));
            }
        }

        return entries;
    }

    /// <summary>The INI shapes: lists of names (<c>[Achievements]</c>, <c>[State]</c>) with their times, and a section per achievement.</summary>
    private static List<RecordEntry> FromIni(string text)
    {
        var sections = new Dictionary<string, List<(string Key, string Value)>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        List<(string Key, string Value)>? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[' && line.IndexOf(']') is var close and > 1)
            {
                var name = line[1..close].Trim();
                if (!sections.TryGetValue(name, out current))
                {
                    sections[name] = current = [];
                    order.Add(name);
                }

                continue;
            }

            var equals = line.IndexOf('=');
            if (current is null || equals <= 0)
            {
                continue;
            }

            current.Add((Unquote(line[..equals].Trim()), Value(line[(equals + 1)..])));
        }

        var entries = new List<RecordEntry>();

        // A list of names with 1 or 0, and their times in another list; a count or numbered names are the list's own.
        static bool Listed(string key) => !IsNumber(key) && !key.Equals("Count", StringComparison.OrdinalIgnoreCase);
        if (sections.TryGetValue("Achievements", out var listed) && listed.Any(kv => Listed(kv.Key)))
        {
            var times = (sections.GetValueOrDefault("AchievementsUnlockTimes") ?? sections.GetValueOrDefault("UnlockTimes") ?? [])
                .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in listed.Where(kv => Listed(kv.Key)))
            {
                if (Truthy(value) is { } on)
                {
                    entries.Add(new RecordEntry(key, on, on && times.TryGetValue(key, out var time) ? Time(time) : null));
                }
            }
        }

        // A list of names with their state as bytes (0101 unlocked), and their times in another list, in hexadecimal.
        if (sections.TryGetValue("State", out var states))
        {
            var times = (sections.GetValueOrDefault("Time") ?? [])
                .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in states)
            {
                if (Truthy(value) is { } on)
                {
                    entries.Add(new RecordEntry(key, on, on && times.TryGetValue(key, out var time) ? Time(time) : null));
                }
            }
        }

        // A section per achievement, with its state, time and progress.
        foreach (var name in order.Where(n => !NotAchievements.Contains(n)))
        {
            if (Entry(Unprefixed(name), sections[name].Select(kv => (kv.Key, (object)kv.Value))) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>A section's name without the <c>ACHIEVEMENTS.</c> some records put before each achievement's.</summary>
    private static string Unprefixed(string section)
    {
        var dot = section.IndexOf('.');
        return Unquote(dot > 0 && section[..dot].Equals("achievements", StringComparison.OrdinalIgnoreCase) ? section[(dot + 1)..] : section);
    }

    /// <summary>A value without a comment after it or the quotes round it.</summary>
    private static string Value(string raw)
    {
        var value = raw.Trim();
        if (value.StartsWith('"') && value.IndexOf('"', 1) is var end and > 0)
        {
            return value[1..end];
        }

        var comment = value.IndexOfAny([';', '#']);
        return (comment > 0 && char.IsWhiteSpace(value[comment - 1]) ? value[..comment] : value).Trim();
    }

    private static string Unquote(string text) => text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;

    private static bool IsNumber(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);

    /// <summary>The JSON shapes: an object keyed by name, or an array of entries naming themselves, maybe under an <c>achievements</c> key.</summary>
    private static List<RecordEntry> FromJson(string text)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 16 });
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var wrapper in new[] { "achievements", "ACHIEVE_DATA", "achievement" })
            {
                if (Property(root, wrapper) is { ValueKind: JsonValueKind.Object or JsonValueKind.Array } inner)
                {
                    root = inner;
                    break;
                }
            }
        }

        var entries = new List<RecordEntry>();
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                var entry = property.Value.ValueKind switch
                {
                    JsonValueKind.Object => Entry(property.Name, property.Value.EnumerateObject().Select(f => (f.Name, (object)f.Value))),
                    JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number or JsonValueKind.String when Truthy(property.Value) is { } on =>
                        new RecordEntry(property.Name, on, null),
                    _ => null,
                };
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object))
            {
                var name = item.EnumerateObject().FirstOrDefault(f => NameKeys.Contains(f.Name) && f.Value.ValueKind == JsonValueKind.String).Value;
                if (name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } id &&
                    Entry(id, item.EnumerateObject().Select(f => (f.Name, (object)f.Value))) is { } entry)
                {
                    entries.Add(entry);
                }
            }
        }

        return entries;
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// One achievement from its fields: its state, time and progress (progress at its maximum is unlocked); null when it
    /// says nothing of whether it's unlocked, as a list of definitions doesn't.
    /// </summary>
    private static RecordEntry? Entry(string name, IEnumerable<(string Key, object Value)> fields)
    {
        bool? state = null;
        DateTime? at = null;
        long? progress = null, max = null;
        foreach (var (key, value) in fields)
        {
            if (StateKeys.Contains(key))
            {
                state = Truthy(value) ?? state;
            }
            else if (TimeKeys.Contains(key))
            {
                at = Time(value) ?? at;
            }
            else if (ProgressKeys.Contains(key))
            {
                progress = Number(value);
            }
            else if (MaxProgressKeys.Contains(key))
            {
                max = Number(value);
            }
        }

        if (state is null && (progress is null || max is null))
        {
            return null;
        }

        var unlocked = state == true || (progress is > 0 && max is > 0 && progress >= max);
        return string.IsNullOrWhiteSpace(name) ? null : new RecordEntry(name.Trim(), unlocked, unlocked ? at : null);
    }

    /// <summary>Whether a value says unlocked: true or false, 1 or 0, yes or no, or a little-endian number or bytes in hexadecimal (<c>01000000</c>, <c>0101</c>).</summary>
    private static bool? Truthy(object value)
    {
        switch (value)
        {
            case JsonElement { ValueKind: JsonValueKind.True }:
                return true;
            case JsonElement { ValueKind: JsonValueKind.False }:
                return false;
            case JsonElement { ValueKind: JsonValueKind.Number } number:
                return number.TryGetDouble(out var d) ? d != 0 : null;
            case JsonElement { ValueKind: JsonValueKind.String } text:
                return Truthy(text.GetString() ?? "");
            case string text:
                var word = text.Trim().ToLowerInvariant();
                return word switch
                {
                    "1" or "true" or "yes" or "unlocked" or "achieved" or "on" => true,
                    "0" or "false" or "no" or "locked" or "off" or "" => false,
                    _ when (word.Length is 4 or 8) && word.All(char.IsAsciiHexDigit) => Hex(word) is { } n ? n != 0 : null,
                    _ when long.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n != 0,
                    _ => null,
                };
            default:
                return null;
        }
    }

    /// <summary>A number: decimal, or eight hexadecimal digits read little-endian (as one with a letter, or a leading zero, is).</summary>
    private static long? Number(object value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.Number } number when number.TryGetInt64(out var n) => n,
        JsonElement { ValueKind: JsonValueKind.String } text => Number(text.GetString() ?? ""),
        string text when text.Trim() is { Length: 8 } hex && hex.All(char.IsAsciiHexDigit) && (hex[0] == '0' || !IsNumber(hex)) => Hex(hex),
        string text when long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
        _ => null,
    };

    /// <summary>A time: Unix seconds (or thousands of seconds, or milliseconds) in decimal, or seconds as a little-endian number in hexadecimal.</summary>
    private static DateTime? Time(object value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.Number } number when number.TryGetInt64(out var n) => When(n),
        JsonElement { ValueKind: JsonValueKind.String } text => Time(text.GetString() ?? ""),
        string text when text.Trim() is { Length: 8 } hex && hex.All(char.IsAsciiHexDigit) => Hex(hex) is { } n ? When(n) : null,
        string text when long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => When(n),
        _ => null,
    };

    /// <summary>Bytes in hexadecimal read as a little-endian number (<c>01000000</c> is 1; <c>0101</c> is 257).</summary>
    private static long? Hex(string hex)
    {
        try
        {
            var bytes = Convert.FromHexString(hex);
            long n = 0;
            for (var i = bytes.Length - 1; i >= 0; i--)
            {
                n = (n << 8) | bytes[i];
            }

            return n;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Unix seconds as a time; none for 0 or anything out of reason. Some records keep thousands of seconds (1790181 for
    /// 23 Sep 2026): a time that would be before 2000 is read that way when it then falls after; and some milliseconds.
    /// </summary>
    private static DateTime? When(long seconds) => seconds switch
    {
        >= Y2000 and < Y3000 => DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime,
        >= Y2000 / 1000 and < Y3000 / 1000 => DateTimeOffset.FromUnixTimeSeconds(seconds * 1000).UtcDateTime,
        >= Y2000 * 1000 and < Y3000 * 1000 => DateTimeOffset.FromUnixTimeMilliseconds(seconds).UtcDateTime,
        _ => null,
    };

    /// <summary>1 January 2000 and 3000, in Unix seconds.</summary>
    private const long Y2000 = 946684800, Y3000 = 32503680000;

    /// <summary>The CRC-32 (IEEE 802.3, as zip's) of an achievement's Steam name in UTF-8: how a table keyed by checksums knows it.</summary>
    public static uint Crc32(string name)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in Encoding.UTF8.GetBytes(name))
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(i =>
    {
        var c = (uint)i;
        for (var k = 0; k < 8; k++)
        {
            c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }

        return c;
    }).ToArray();
}
