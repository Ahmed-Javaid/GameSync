using System.Collections.Concurrent;
using GameSync.Core.Art;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>One achievement as GameSync shows it: its words, when it was unlocked, its icon kept on this PC, and its rarity.</summary>
/// <param name="UnlockedUtc">Null while it's locked; <see cref="DateTime.UnixEpoch"/> when Steam kept no time for it.</param>
/// <param name="Icon">The icon kept on this PC (unlocked or locked, as it is); null until it's been fetched.</param>
/// <param name="IconFile">The icon's file name on Steam's image server, for fetching it.</param>
/// <param name="Percent">The share of Steam's players who have it, once asked.</param>
/// <param name="Marked">Marked unlocked by the person (design system version 54), for a copy that writes its unlocks nowhere GameSync can find.</param>
/// <param name="OnPc">The PC it was unlocked on, when GameSync didn't see it on this one (version 54); null when it did.</param>
public sealed record AchievementShown(string Id, string Name, string? Description, bool Hidden, DateTime? UnlockedUtc, string? Icon, string? IconFile, double? Percent,
    bool Marked = false, string? OnPc = null)
{
    public bool Unlocked => UnlockedUtc is not null;

    /// <summary>When it was unlocked, if Steam kept the time.</summary>
    public DateTime? KnownUnlockUtc => UnlockedUtc is { } at && at > DateTime.UnixEpoch ? at : null;

    /// <summary>Hidden by its game, and still locked: nothing about it shows but how many players have it.</summary>
    public bool Secret => Hidden && !Unlocked;

    /// <summary>Its tier from how many of Steam's players have it (design system version 33); null until that's known.</summary>
    public AchievementTier? Tier => AchievementTiers.Of(Percent);
}

/// <summary>Why GameSync can't see what's unlocked in a copy (design system version 55): the words, and the file or folder they're about.</summary>
public sealed record AchievementsWhy(string Text, string? Path);

/// <summary>An achievement's metal, from its rarity: Gold under 5% of Steam's players, Silver under 20%, Bronze the rest.</summary>
public enum AchievementTier
{
    Bronze,
    Silver,
    Gold,
}

/// <summary>The tiers and rarity's words of design system version 33 (the owner, 3 Oct 2026).</summary>
public static class AchievementTiers
{
    public static AchievementTier? Of(double? percent) => percent switch
    {
        null => null,
        < 5 => AchievementTier.Gold,
        < 20 => AchievementTier.Silver,
        _ => AchievementTier.Bronze,
    };

    /// <summary>"Ultra rare" under 1% of players, "Very rare" under 5%, "Rare" under 20%, "Uncommon" under 50%, else "Common".</summary>
    public static string? Rarity(double? percent) => percent switch
    {
        null => null,
        < 1 => "Ultra rare",
        < 5 => "Very rare",
        < 20 => "Rare",
        < 50 => "Uncommon",
        _ => "Common",
    };

    public static string Word(AchievementTier tier) => tier switch
    {
        AchievementTier.Gold => "Gold",
        AchievementTier.Silver => "Silver",
        _ => "Bronze",
    };
}

/// <summary>
/// A game in Achievements by game (KAN-110, KAN-131): its name, how far it is ("123 of 171"), whether it's left out, its
/// cover, whether it has its Zenith, the launcher that runs it and shows its own popup ("Steam"; null for a copy of its
/// own), and whether GameSync shows its popup for it.
/// </summary>
/// <param name="AntiCheat">R13 (design system version 51): it ships an anti-cheat, so GameSync draws nothing over it: no popup, ever.</param>
public sealed record AchievementGameRow(GameId Game, string Title, string Line, bool LeftOut, string? Cover, bool Zenith = false, string? Launcher = null,
    bool Popup = true, bool AntiCheat = false);

/// <summary>
/// Where a game's achievements come from (KAN-111): what's unlocked is GameSync's own record since design system version
/// 54, and this says what fills it on this PC: Steam's own files, a copy's own record, neither (what's kept came from
/// another PC or the person's marks), or nothing yet, Steam's public list with every one locked.
/// </summary>
public enum AchievementsFrom
{
    /// <summary>Steam's files on this PC: which are unlocked and when (ACH-01).</summary>
    Steam,

    /// <summary>Steam's list at 0: a copy that isn't Steam's, so what's unlocked in it doesn't show (the owner's Cuphead).</summary>
    ListNotSteamCopy,

    /// <summary>Steam's list at 0: a Steam game Steam on this PC has never run, so it holds nothing unlocked here.</summary>
    ListNotPlayedHere,

    /// <summary>Steam was asked and lists none: the game's page says 0 (the owner's Child of Light, whose achievements are Ubisoft's).</summary>
    NoneOnSteam,

    /// <summary>
    /// A copy Steam doesn't run, from its own record of what's unlocked (KAN-123; the owner: "As long as my offline games
    /// also have achievements"), found by GameSync itself since version 54 or in a folder added in Settings: counted like
    /// Steam's own, each one named from Steam's list.
    /// </summary>
    CopyRecord,

    /// <summary>
    /// Nothing on this PC writes its unlocks down, but GameSync's record has some (version 54): unlocked on another PC,
    /// seen here before (Steam since gone, a record since deleted), or marked by the person.
    /// </summary>
    Kept,
}

/// <summary>
/// ACH-01 to ACH-03: a game's achievements, Steam's list naming them and GameSync's own record saying which are unlocked
/// (design system version 54), with what's kept about them; or, for a game with nothing unlocked anywhere, Steam's public
/// list with every one locked (<see cref="From"/>; KAN-111).
/// </summary>
/// <param name="CanMark">
/// Version 54 (the owner: "Yes but only for games that write nothing down"): a copy Steam doesn't run with no record of its
/// own found, whose achievements the person marks by hand.
/// </param>
/// <param name="KeptOn">The other PCs some of its unlocks were seen on, by name (version 54).</param>
public sealed record GameAchievementsView(GameId Game, string Title, long AppId, IReadOnlyList<AchievementShown> All,
    AchievementsFrom From = AchievementsFrom.Steam, bool CanMark = false, IReadOnlyList<string>? KeptOn = null)
{
    /// <summary>
    /// What's unlocked is known: Steam's own files, a copy's own record (KAN-123), or GameSync's record from elsewhere
    /// (version 54). A list at 0 isn't on the Achievements page or Home.
    /// </summary>
    public bool IsTracked => From is AchievementsFrom.Steam or AchievementsFrom.CopyRecord or AchievementsFrom.Kept;

    /// <summary>How many it has marked by hand (version 54).</summary>
    public int Marked => All.Count(a => a is { Unlocked: true, Marked: true });

    /// <summary>Version 55: why GameSync can't see what's unlocked in this copy, for the ? on its achievements page; null when it can.</summary>
    public AchievementsWhy? Why { get; init; }

    public int Unlocked => All.Count(a => a.Unlocked);

    public int Total => All.Count;

    public double Percent => Total == 0 ? 0 : 100.0 * Unlocked / Total;

    /// <summary>Zenith: every one unlocked.</summary>
    public bool IsComplete => Total > 0 && Unlocked == Total;

    /// <summary>When Zenith was earned: the last one's unlock, if Steam kept its time.</summary>
    public DateTime? CompletedUtc => IsComplete ? All.Max(a => a.KnownUnlockUtc) : null;

    /// <summary>When the latest was unlocked, if Steam kept the time.</summary>
    public DateTime? LastUnlockUtc => All.Max(a => a.KnownUnlockUtc);

    /// <summary>How many unlocked are of a tier.</summary>
    public int Count(AchievementTier tier) => All.Count(a => a.Unlocked && a.Tier == tier);

    /// <summary>The latest unlocked first; those with no time kept last.</summary>
    public IReadOnlyList<AchievementShown> Latest(int count) =>
        All.Where(a => a.Unlocked).OrderByDescending(a => a.UnlockedUtc).Take(count).ToList();

    /// <summary>The unlocked one the fewest players have; null until Steam's percentages are kept.</summary>
    public AchievementShown? Rarest => All.Where(a => a is { Unlocked: true, Percent: not null }).MinBy(a => a.Percent);

    /// <summary>The icons Home, the game's page and the Achievements page show: the latest five, the rarest, and the three within reach.</summary>
    public IEnumerable<string?> ShownIcons => Latest(5).Append(Rarest).Concat(WithinReach(3)).OfType<AchievementShown>().Select(a => a.IconFile);

    /// <summary>The locked ones most of Steam's players have, never a hidden one: the Achievements page's Within reach (KAN-105).</summary>
    public IReadOnlyList<AchievementShown> WithinReach(int count) =>
        All.Where(a => a is { Unlocked: false, Hidden: false, Percent: not null }).OrderByDescending(a => a.Percent).Take(count).ToList();

    /// <summary>Every icon the list of all of them shows: a hidden one's is never asked for until it's unlocked.</summary>
    public IEnumerable<string?> EveryIcon => All.Where(a => !a.Secret).Select(a => a.IconFile);
}

/// <summary>
/// Reads achievements for the screens. Since design system version 54 (the owner, 8 Oct 2026: "The achievement system needs
/// to work even without steam. It can get information about WHAT achievements are there to begin with, but after that it
/// needs to work without it. Tracking by itself or something") what's unlocked is GameSync's own record: Steam's list names
/// the achievements, and every unlock GameSync sees on this PC (Steam's files, a copy's own record) is kept in it as it's
/// read, with what came from other PCs and what the person marked, so nothing seen is lost when Steam or the copy is gone.
/// Each game's Steam files and records are parsed again only when they change.
/// </summary>
public static class Achievements
{
    private static readonly ConcurrentDictionary<(string Root, long AppId), (DateTime Changed, SteamGameAchievements? Read)> Parsed = new();

    /// <summary>What was last added to the record from each witness, so a file seen before isn't added again at every look.</summary>
    private static readonly ConcurrentDictionary<(string DataDir, long AppId, UnlockSeen Seen), string> Observed = new();

    /// <summary>
    /// The game's achievements: Steam's list (Steam's own files on this PC for a Steam copy, else the public list kept once
    /// asked) with GameSync's record of what's unlocked, filled first from what this PC holds: Steam's files for a Steam
    /// copy, else the copy's own record (KAN-123), found by GameSync or in a folder added in Settings. With nothing unlocked
    /// anywhere, Steam's list at 0 (KAN-111); null for a game with no Steam app ID, or before any list is kept.
    /// </summary>
    /// <remarks>A copy whose achievements are marked by hand says why GameSync can't see them (<see cref="GameAchievementsView.Why"/>, version 55).</remarks>
    public static GameAchievementsView? For(string dataDir, LauncherGame game, string? steamRoot = null, IReadOnlyList<string>? recordFolders = null)
    {
        var view = For(dataDir, game.Id, game.Title, game.Store, game.SteamAppId, steamRoot, recordFolders);
        if (view is not { CanMark: true })
        {
            return view;
        }

        using var state = new GameSync.Core.State.StateStore(dataDir);
        return view with { Why = WhyNot(state, game, view.AppId) };
    }

    /// <param name="store">Only a Steam copy reads Steam's own files: a copy in its own folder reads its own record (ACH-01, KAN-111, KAN-123).</param>
    /// <param name="recordFolders">The folders of copies' records, when the caller has them already; read from this PC's settings otherwise.</param>
    public static GameAchievementsView? For(string dataDir, GameId game, string title, StoreKind? store, long? steamAppId, string? steamRoot = null,
        IReadOnlyList<string>? recordFolders = null)
    {
        using var look = new Look(dataDir, steamRoot ?? StoreLocations.SteamRoot(), recordFolders);

        // ACH-18: a copy's record found under the ID the copy says it is names its achievements by that ID's list.
        return AppIdOf(look.State, game, steamAppId) is { } appId ? For(look, game, title, store, appId) : null;
    }

    private static GameAchievementsView? For(Look look, GameId game, string title, StoreKind? store, long appId)
    {
        var cache = look.Cache;
        var rarity = cache.Rarity(appId);
        var steam = store == StoreKind.Steam && look.SteamRoot is { } root ? Read(root, appId) : null;
        var listed = cache.List(appId);
        var list = steam?.All ?? listed?.All;

        // Steam's own files on this PC: what's unlocked goes into GameSync's record, and the names are kept, so both stay
        // should Steam go.
        if (steam is not null)
        {
            if (listed is null)
            {
                cache.KeepList(appId, steam.All);
            }

            Observe(look, appId, UnlockSeen.Steam, $"{look.SteamRoot}|{Stamp(look.SteamRoot!, appId)}",
                steam.All.Where(a => a.Unlocked).Select(a => (a.Id, Sane(a.UnlockedUtc))));
        }

        // KAN-123: a copy Steam doesn't run that keeps its own record: what it holds goes into GameSync's record, each by
        // the name Steam's list gives it; a name the list doesn't have is never made up. No time kept, no day.
        var recordFile = store != StoreKind.Steam ? RecordOf(look, appId) : null;
        var record = recordFile is null ? null : Record(recordFile, list?.Select(a => a.Id).ToList());
        if (record is not null && list is not null)
        {
            var named = list.GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
            Observe(look, appId, UnlockSeen.Record, $"{recordFile}|{File.GetLastWriteTimeUtc(recordFile!).Ticks}",
                record.Where(r => named.ContainsKey(r.Key)).Select(r => (named[r.Key], Sane(r.Value))));
        }

        var kept = look.State.GetUnlocks(appId).Where(e => !e.Removed).GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        if (list is null)
        {
            // Asked, and Steam lists none: the page says 0 rather than waiting for a list that won't come.
            return cache.ListedNone(appId) ? new GameAchievementsView(game, title, appId, [], AchievementsFrom.NoneOnSteam) : null;
        }

        // Design system version 55 (the owner, 8 Oct 2026, of Sons of the Forest's record, which keeps its stats but never an
        // achievement: "If the marking exists we should be able to do it"): a record that has never held an unlock writes
        // nothing down, as far as anyone can tell, so its achievements are marked by hand until it does.
        var writes = record is not null && (record.Count > 0 || kept.Values.Any(e => e.Seen == UnlockSeen.Record));
        var from = steam is not null ? AchievementsFrom.Steam
            : writes ? AchievementsFrom.CopyRecord
            : list.Any(a => kept.ContainsKey(a.Id)) ? AchievementsFrom.Kept
            : store == StoreKind.Steam ? AchievementsFrom.ListNotPlayedHere
            : AchievementsFrom.ListNotSteamCopy;

        // The list's own percentages until Steam's weekly ones are kept.
        var percents = rarity.Count > 0 ? rarity : listed?.Percents ?? new Dictionary<string, double>();
        var shown = list.Select(a =>
        {
            var e = kept.GetValueOrDefault(a.Id);
            var file = e is not null ? a.Icon : a.IconLocked ?? a.Icon;

            // A hidden one still locked shows no icon at all, so its picture is never looked for either.
            var secret = a.Hidden && e is null;
            return new AchievementShown(a.Id, a.Name, a.Description, a.Hidden, e is null ? null : e.UnlockedUtc ?? DateTime.UnixEpoch,
                secret ? null : cache.Icon(appId, file), secret ? null : file, percents.TryGetValue(a.Id, out var percent) ? percent : null,
                e?.Seen == UnlockSeen.Hand, e is { Witnessed: true } && e.Device != look.Device ? look.NameOf(e.Device) : null);
        }).ToList();
        var keptOn = shown.Where(a => a.OnPc is not null).Select(a => a.OnPc!).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList();

        // Marking by hand (the owner: "Yes but only for games that write nothing down"): a copy Steam doesn't run with no
        // record of its own found, or one whose record has never held an unlock (version 55).
        return new GameAchievementsView(game, title, appId, shown, from, CanMark: store != StoreKind.Steam && !writes, KeptOn: keptOn);
    }

    /// <summary>Adds what a witness on this PC holds to GameSync's record, once for each time its file changes.</summary>
    private static void Observe(Look look, long appId, UnlockSeen seen, string stamp, IEnumerable<(string Id, DateTime? At)> unlocked)
    {
        var key = (look.DataDir, appId, seen);
        if (Observed.TryGetValue(key, out var last) && last == stamp)
        {
            return;
        }

        var now = DateTime.UtcNow;
        look.State.KeepUnlocks(unlocked.Where(u => KeptAchievements.IsName(u.Id)).Select(u => new KeptUnlock(appId, u.Id, u.At, seen, look.Device, now, now)));
        Observed[key] = stamp;
    }

    /// <summary>
    /// Version 54: unlocks seen while a game plays (the popup's watch), kept in GameSync's record at once, each with its time
    /// or, when the witness keeps none, now.
    /// </summary>
    public static void KeepSeen(string dataDir, long appId, UnlockSeen seen, IEnumerable<(string Id, DateTime? At)> unlocked)
    {
        var now = DateTime.UtcNow;
        var entries = unlocked.Where(u => KeptAchievements.IsName(u.Id)).ToList();
        if (entries.Count == 0)
        {
            return;
        }

        using var state = new GameSync.Core.State.StateStore(dataDir);
        var device = state.GetOrCreateDevice(Environment.MachineName).Id.Value;
        state.KeepUnlocks(entries.Select(u => new KeptUnlock(appId, u.Id, Sane(u.At) ?? now, seen, device, now, now)));
    }

    /// <summary>An unlock's time when it holds up: none for one Steam kept no time for, or one before 2000 or days ahead.</summary>
    private static DateTime? Sane(DateTime? at) => at is { } t && t > new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc) && t < DateTime.UtcNow.AddDays(2) ? t : null;

    /// <summary>
    /// One look at the achievements of one game or many: what they share, opened once (this PC's state, the cache, Steam's
    /// folder, the folders of records, this PC's device ID and the names of the others).
    /// </summary>
    private sealed class Look : IDisposable
    {
        private IReadOnlyDictionary<string, string>? _names;

        public Look(string dataDir, string? steamRoot, IReadOnlyList<string>? recordFolders)
        {
            DataDir = dataDir;
            State = new GameSync.Core.State.StateStore(dataDir);
            Cache = new AchievementCache(dataDir);
            SteamRoot = steamRoot;
            Folders = recordFolders ?? RecordFolders(State);
            Device = State.GetOrCreateDevice(Environment.MachineName).Id.Value;
        }

        public string DataDir { get; }

        public GameSync.Core.State.StateStore State { get; }

        public AchievementCache Cache { get; }

        public string? SteamRoot { get; }

        public IReadOnlyList<string> Folders { get; }

        public string Device { get; }

        /// <summary>Another PC's name, as the cloud's list of devices last said; "another PC" until it's known.</summary>
        public string NameOf(string device)
        {
            if (_names is null)
            {
                try
                {
                    _names = new GameSync.Core.Storage.LocalHistory(Cli.HistoryFolder(State, DataDir)).LoadDevices()
                        .GroupBy(d => d.Id.Value).ToDictionary(g => g.Key, g => g.First().Name);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    _names = new Dictionary<string, string>();
                }
            }

            return _names.GetValueOrDefault(device) ?? "another PC";
        }

        public void Dispose()
        {
            Cache.Dispose();
            State.Dispose();
        }
    }

    /// <summary>
    /// KAN-123: the folders added in Settings, Achievements where copies Steam doesn't run keep their records of what's
    /// unlocked, one folder per game named by its Steam app ID. Kept on this PC only, one a line.
    /// </summary>
    public const string RecordFoldersKey = "achievements.records";

    public static IReadOnlyList<string> RecordFolders(GameSync.Core.State.StateStore state) =>
        (state.GetSetting(RecordFoldersKey) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static IReadOnlyList<string> RecordFolders(string dataDir)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        return RecordFolders(state);
    }

    /// <summary>Adds a folder of copies' records (KAN-123), and says what's in it.</summary>
    public static string AddRecordFolder(string dataDir, string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim().Trim('"')));
        if (!Directory.Exists(full))
        {
            throw new UsageException($"{full} isn't there.");
        }

        using var state = new GameSync.Core.State.StateStore(dataDir);
        var folders = RecordFolders(state).ToList();
        if (folders.Contains(full, StringComparer.OrdinalIgnoreCase))
        {
            return $"{full} is already one of the folders.";
        }

        folders.Add(full);
        state.SetSetting(RecordFoldersKey, string.Join('\n', folders));
        var count = CopyAchievements.CountIn(full);
        return count == 0
            ? $"Added {full}, but it holds no records yet: it should have one folder per game, named by its Steam number, with the copy's record inside."
            : $"Added {full}: records for {count} {(count == 1 ? "game" : "games")}, whose achievements count like Steam's.";
    }

    /// <summary>Takes a folder of copies' records off the list (KAN-123); what GameSync saw in it stays in its record (version 54).</summary>
    public static string RemoveRecordFolder(string dataDir, string folder)
    {
        var path = folder.Trim().Trim('"');
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        using var state = new GameSync.Core.State.StateStore(dataDir);
        var folders = RecordFolders(state).ToList();
        if (folders.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase) || string.Equals(f, full, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            throw new UsageException($"{path} isn't one of the folders.");
        }

        state.SetSetting(RecordFoldersKey, string.Join('\n', folders));
        return $"Removed {path}: GameSync no longer reads it, and keeps what it saw there.";
    }

    /// <summary>The setting holding the record GameSync found by itself for a game's copy (version 54), by its Steam app ID.</summary>
    public static string FoundKey(long appId) => FoundPrefix + appId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private const string FoundPrefix = "achievements.found.";

    /// <summary>The name of the game a record was found for, for Settings.</summary>
    public static string FoundGameKey(long appId) => "achievements.foundgame." + appId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// ACH-18: the setting holding the Steam app ID a copy's own record is kept under, when it isn't the
    /// one GameSync took for the game: the ID the copy says it is in its own folder (a game's edition with an ID of its own,
    /// as Dark Souls II's Scholar of the First Sin).
    /// </summary>
    public static string AppIdKey(GameId game) => "achievements.appid." + game.Value;

    /// <summary>The Steam app ID a game's achievements are kept and named by: the one its copy's record was found under, else GameSync's for the game.</summary>
    public static long? AppIdOf(GameSync.Core.State.StateStore state, GameId game, long? steamAppId) =>
        state.GetSetting(AppIdKey(game)) is { Length: > 0 } text &&
        long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) && id > 0 ? id : steamAppId;

    public static long? AppIdOf(string dataDir, LauncherGame game)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        return AppIdOf(state, game.Id, game.SteamAppId);
    }

    /// <summary>Valve's own test app, which some copies say they are to play together: never taken for a game's ID.</summary>
    private const long Spacewar = 480;

    /// <summary>When GameSync last looked for copies' own records (version 54): Settings' "Looked 2 minutes ago".</summary>
    public const string LookedKey = "achievements.looked";

    /// <summary>The setting holding what a copy left about its achievements when GameSync can't read them (version 55), as JSON.</summary>
    public static string ClueKey(GameId game) => "achievements.clue." + game.Value;

    private static readonly System.Text.Json.JsonSerializerOptions ClueJson = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static RecordClue? ReadClue(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<RecordClue>(json, ClueJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Design system version 55 (the owner, 8 Oct 2026: "have a little question mark on the achivement page for the game that
    /// when hovered it explains why the game is prolly not being picked up"): why GameSync can't see what's unlocked in a
    /// copy Steam doesn't run, from what it found when it last looked, with the file or folder that says so; null for a
    /// Steam copy. Copies are never named by who made them, and GameSync never changes a game's files to make it record
    /// (the owner: fixes like that are theirs, "separate than gamesync").
    /// </summary>
    public static AchievementsWhy? WhyNot(GameSync.Core.State.StateStore state, LauncherGame game, long appId)
    {
        if (game.Store == StoreKind.Steam)
        {
            return null;
        }

        const string Mark = " Mark the ones you've earned; GameSync looks again each time you play.";
        if (state.GetSetting(LookedKey) is not { Length: > 0 })
        {
            return new AchievementsWhy("GameSync hasn't looked for this copy's record of its achievements yet. It looks a minute after it starts, and while the game plays.", null);
        }

        var clue = ReadClue(state.GetSetting(ClueKey(game.Id)));
        switch (clue)
        {
            case { Kind: RecordClueKind.NoList }:
                return new AchievementsWhy(
                    $"This copy's Steam files have a settings folder with no list of {game.Title}'s achievements in it, so it can't record any.{Mark}", clue.Path);
            case { Kind: RecordClueKind.StatsOnly, Path: { } empty }:
                return new AchievementsWhy(
                    $"This copy keeps {game.Title}'s stats but never its achievements: on Steam, Steam itself unlocks the ones your stats earn, and this copy doesn't.{Mark}",
                    Path.GetDirectoryName(empty));
            case { Kind: RecordClueKind.Unwritten, Path: { } file } when File.Exists(file):
                var written = File.GetLastWriteTimeUtc(file);
                return game.LastPlayedUtc is { } played && played > written.AddHours(1)
                    ? new AchievementsWhy(
                        $"This copy keeps a file for its achievements, last written on {Day(written)}, though you've played since, last on {Day(played)}: it isn't recording them.{Mark}", file)
                    : new AchievementsWhy($"This copy's file for its achievements holds none yet: nothing unlocked so far, or the copy doesn't record them.{Mark}", file);
            default:
                return new AchievementsWhy(
                    $"GameSync found nothing this copy writes about achievements: no file in a folder named by its Steam number ({appId.ToString(System.Globalization.CultureInfo.InvariantCulture)}) " +
                    "in AppData, Documents, Saved Games, Public Documents or ProgramData, and none in its own folder. Some copies keep none; if you know where this one keeps it, add that folder in Settings, Achievements." +
                    Mark, null);
        }

        // "11 Jul", or "11 Jul 2025" before this year.
        static string Day(DateTime utc)
        {
            var local = utc.ToLocalTime();
            return local.ToString(local.Year == DateTime.Now.Year ? "d MMM" : "d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>KAN-123: the record file of a game's copy: in a folder added in Settings, or found by GameSync itself (version 54).</summary>
    public static string? RecordOf(string dataDir, long appId)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        return RecordOf(RecordFolders(state), state, appId);
    }

    private static string? RecordOf(Look look, long appId) => RecordOf(look.Folders, look.State, appId);

    private static string? RecordOf(IReadOnlyList<string> folders, GameSync.Core.State.StateStore state, long appId) =>
        (folders.Count > 0 ? CopyAchievements.Find(folders, appId) : null)
        ?? (state.GetSetting(FoundKey(appId)) is { Length: > 0 } found && File.Exists(found) ? found : null);

    /// <summary>The records GameSync found by itself and still there, by the game's Steam app ID (version 54).</summary>
    public static IReadOnlyDictionary<long, string> FoundRecords(GameSync.Core.State.StateStore state)
    {
        var found = new Dictionary<long, string>();
        foreach (var (key, value) in state.GetSettings(FoundPrefix))
        {
            if (long.TryParse(key[FoundPrefix.Length..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var app) &&
                File.Exists(value))
            {
                found[app] = value;
            }
        }

        return found;
    }

    /// <summary>KAN-123: the games whose copy keeps its own record, found by GameSync or in a folder added in Settings.</summary>
    public static IReadOnlyList<LauncherGame> WithRecords(string dataDir, IEnumerable<LauncherGame> games)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        var folders = RecordFolders(state);
        return games.Where(g => g.Store != StoreKind.Steam && AppIdOf(state, g.Id, g.SteamAppId) is { } app && RecordOf(folders, state, app) is not null).ToList();
    }

    private static readonly ConcurrentDictionary<string, (DateTime Changed, int Named, IReadOnlyDictionary<string, DateTime?>? Read)> Records = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A copy's own record, read again only when it changes: what it holds as unlocked, by Steam's names; a record keyed by
    /// checksums is named from <paramref name="achievements"/>, the game's list, read again once the list is known.
    /// </summary>
    private static IReadOnlyDictionary<string, DateTime?>? Record(string file, IReadOnlyCollection<string>? achievements)
    {
        var changed = File.GetLastWriteTimeUtc(file);
        var named = achievements?.Count ?? 0;
        if (Records.TryGetValue(file, out var known) && known.Changed == changed && known.Named == named)
        {
            return known.Read;
        }

        var read = CopyAchievements.Read(file, achievements);
        Records[file] = (changed, named, read);
        return read;
    }

    /// <summary>
    /// The setting that says a game's Zenith has been seen on this PC (design system version 49): its moment played, so it
    /// doesn't again; the time it was seen.
    /// </summary>
    public static string ZenithSeenKey(GameId game) => ZenithSeenPrefix + game.Value;

    private const string ZenithSeenPrefix = "achievements.zenith.seen.";

    /// <summary>Whether a game's Zenith has been seen on this PC.</summary>
    public static bool ZenithSeen(GameSync.Core.State.StateStore state, GameId game) => state.GetSetting(ZenithSeenKey(game)) is { Length: > 0 };

    /// <summary>The games whose Zenith has been seen on this PC.</summary>
    public static IReadOnlySet<GameId> ZenithsSeen(string dataDir)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        var games = new HashSet<GameId>();
        foreach (var (key, value) in state.GetSettings(ZenithSeenPrefix))
        {
            if (value is { Length: > 0 } && GameId.TryParse(key[ZenithSeenPrefix.Length..], out var game))
            {
                games.Add(game);
            }
        }

        return games;
    }

    /// <summary>A game's Zenith seen on this PC: its moment has played and won't again here.</summary>
    public static void SeeZenith(string dataDir, GameId game)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        state.SetSetting(ZenithSeenKey(game), DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The setting that leaves a game out of the achievements on this PC (KAN-110): no popups, not counted anywhere.</summary>
    public static string LeftOutKey(GameId game) => "achievements.off." + game.Value;

    /// <summary>Whether a game is left out of the achievements on this PC (Settings, Achievements).</summary>
    public static bool IsLeftOut(GameSync.Core.State.StateStore state, GameId game) => state.GetSetting(LeftOutKey(game)) == "1";

    /// <summary>The games left out of the achievements on this PC.</summary>
    public static IReadOnlySet<GameId> LeftOut(string dataDir)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        var games = new HashSet<GameId>();
        foreach (var (key, value) in state.GetSettings("achievements.off."))
        {
            if (value == "1" && GameId.TryParse(key["achievements.off.".Length..], out var game))
            {
                games.Add(game);
            }
        }

        return games;
    }

    /// <summary>
    /// Settings, Achievements, Achievements by game (KAN-110, KAN-131): every game here whose achievements GameSync tracks
    /// (Steam's, copies with their own record since KAN-123, and any GameSync's record has unlocks for since version 54),
    /// counted or left out, with its popup on or off, by name.
    /// </summary>
    public static IReadOnlyList<AchievementGameRow> SettingsRows(string dataDir, IEnumerable<LauncherGame> games, string? steamRoot = null)
    {
        var leftOut = LeftOut(dataDir);
        var popups = PopupChoices(dataDir);
        var antiCheat = AntiCheatGames(dataDir);
        string Count(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var look = new Look(dataDir, steamRoot ?? StoreLocations.SteamRoot(), null);
        return Tracked(look, games)
            .Where(x => x.View.Total > 0)
            .Select(x =>
            {
                // A launcher runs a game of its store and shows its own popup; a copy with its own record, or whose unlocks
                // are marked by hand, has no launcher showing one, so GameSync's is on unless turned off (KAN-123). Never
                // over an anti-cheat (R13).
                var launcher = x.View.From == AchievementsFrom.CopyRecord ? null : LauncherOf(x.Game.Store);
                var guarded = antiCheat.Contains(x.Game.Id);
                return new AchievementGameRow(x.Game.Id, x.Game.Title, $"{Count(x.View.Unlocked)} of {Count(x.View.Total)}", leftOut.Contains(x.Game.Id),
                    x.Game.CoverPath, x.View.IsComplete, launcher, !guarded && PopupOn(popups, x.Game.Id, launcher is not null), guarded);
            })
            .OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The games whose achievements GameSync tracks, with their views (version 54): Steam copies, copies whose own record
    /// is found, and any game GameSync's record holds unlocks for, from this PC or another.
    /// </summary>
    private static List<(LauncherGame Game, GameAchievementsView View)> Tracked(Look look, IEnumerable<LauncherGame> games)
    {
        var kept = look.State.GetUnlocks().Where(e => !e.Removed).Select(e => e.AppId).ToHashSet();
        var tracked = new List<(LauncherGame, GameAchievementsView)>();
        foreach (var g in games)
        {
            if (!g.Shown || AppIdOf(look.State, g.Id, g.SteamAppId) is not { } app || (g.Store != StoreKind.Steam && !kept.Contains(app) && RecordOf(look, app) is null))
            {
                continue;
            }

            if (For(look, g.Id, g.Title, g.Store, app) is { IsTracked: true } view)
            {
                tracked.Add((g, view));
            }
        }

        return tracked;
    }

    /// <summary>
    /// The launcher that runs a game of this store and shows its own popup when an achievement unlocks (the owner, 4 Oct
    /// 2026: "games on steam/epic/official external launchers have their own achievement popups, so those games will have
    /// their popups turned off by default on gamesync"); null for a copy of its own, which has no popup but GameSync's.
    /// </summary>
    public static string? LauncherOf(StoreKind? store) => store switch
    {
        StoreKind.Steam => "Steam",
        StoreKind.Epic => "Epic",
        StoreKind.Ea => "The EA app",
        _ => null,
    };

    /// <summary>The setting that turns GameSync's popup on or off for a game on this PC (KAN-131): <c>on</c>, <c>off</c>, or none for its default.</summary>
    public static string PopupKey(GameId game) => "achievements.popup." + game.Value;

    /// <summary>The games whose popup was turned on or off on this PC; a game not here has its default.</summary>
    public static IReadOnlyDictionary<GameId, bool> PopupChoices(string dataDir)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        var choices = new Dictionary<GameId, bool>();
        foreach (var (key, value) in state.GetSettings("achievements.popup."))
        {
            if (value is "on" or "off" && GameId.TryParse(key["achievements.popup.".Length..], out var game))
            {
                choices[game] = value == "on";
            }
        }

        return choices;
    }

    /// <summary>
    /// Whether GameSync shows its popup for a game: as turned on or off for it, else off for a game a launcher runs, which
    /// shows its own, and on for any other.
    /// </summary>
    public static bool PopupOn(IReadOnlyDictionary<GameId, bool> choices, GameId game, bool launcherShowsOwn) =>
        choices.TryGetValue(game, out var on) ? on : !launcherShowsOwn;

    /// <summary>
    /// R13 (design system version 51): the games that ship an anti-cheat, over which GameSync draws nothing: a window on
    /// top of a game, however harmless, is what some anti-cheats look for. Their unlocks still count and chime.
    /// </summary>
    public static IReadOnlySet<GameId> AntiCheatGames(string dataDir)
    {
        using var library = new LibraryStore(dataDir);
        return library.All().Where(e => e.HasAntiCheat && e.MergedInto is null).Select(e => e.Id).ToHashSet();
    }

    /// <summary>Turns GameSync's popup on or off for a game on this PC.</summary>
    public static void SetPopup(string dataDir, GameId game, bool on)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        state.SetSetting(PopupKey(game), on ? "on" : "off");
    }

    /// <summary>Leaves a game out of the achievements on this PC, or counts it again.</summary>
    public static void SetLeftOut(string dataDir, GameId game, bool leftOut)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        state.SetSetting(LeftOutKey(game), leftOut ? "1" : "");
    }

    /// <summary>
    /// Home's Achievements card (the owner, 3 Oct 2026): the hero game's, or when it keeps none, the last game played that
    /// does. <c>HeroHasNone</c> says the card is about another game than the hero; <c>HeroLeftOut</c> that the hero has
    /// some but is left out of the achievements (KAN-110), so the card doesn't say it has none.
    /// </summary>
    public static (GameAchievementsView? View, bool HeroHasNone, bool HeroLeftOut) ForHome(string dataDir, LauncherGame? hero, IEnumerable<LauncherGame> games,
        string? steamRoot = null)
    {
        // Only what's tracked, and never a game left out (KAN-110).
        var leftOut = LeftOut(dataDir);
        using var look = new Look(dataDir, steamRoot ?? StoreLocations.SteamRoot(), null);
        GameAchievementsView? Of(LauncherGame g) => AppIdOf(look.State, g.Id, g.SteamAppId) is { } app ? For(look, g.Id, g.Title, g.Store, app) : null;
        var heroLeftOut = hero is not null && leftOut.Contains(hero.Id);
        if (hero is not null && !heroLeftOut && Of(hero) is { IsTracked: true } own)
        {
            return (own, false, false);
        }

        var other = games.Where(g => g.Shown && g.Id != hero?.Id && g.LastPlayedUtc is not null && !leftOut.Contains(g.Id))
            .OrderByDescending(g => g.LastPlayedUtc)
            .Take(20)
            .Select(Of)
            .FirstOrDefault(v => v is { IsTracked: true, Unlocked: > 0 });
        return (other, hero is not null && other is not null, heroLeftOut);
    }

    /// <summary>
    /// The Achievements page (design system version 33): every game here whose achievements GameSync tracks, kept in its own
    /// record since version 54 (Steam's files, copies' own records, other PCs, marks by hand); Steam's lists at 0 (KAN-111)
    /// and games left out (KAN-110) aren't counted there.
    /// </summary>
    public static IReadOnlyList<GameAchievementsView> ForAll(string dataDir, IEnumerable<LauncherGame> games, string? steamRoot = null)
    {
        var leftOut = LeftOut(dataDir);
        using var look = new Look(dataDir, steamRoot ?? StoreLocations.SteamRoot(), null);
        return Tracked(look, games.Where(g => !leftOut.Contains(g.Id))).Select(x => x.View).ToList();
    }

    /// <summary>
    /// What a game's page needs that isn't kept yet (KAN-111): Steam's list for a game Steam here holds none for, then its
    /// icons and rarity; true when something new was kept.
    /// </summary>
    public static async Task<bool> FetchForAsync(string dataDir, GameId game, string title, StoreKind? store, long? steamAppId, CancellationToken ct,
        HttpMessageHandler? steam = null)
    {
        // ACH-18: the ID the copy's record was found under, when it isn't GameSync's for the game.
        using (var state = new GameSync.Core.State.StateStore(dataDir))
        {
            steamAppId = AppIdOf(state, game, steamAppId);
        }

        if (steamAppId is not { } appId)
        {
            return false;
        }

        var before = For(dataDir, game, title, store, appId);
        var listed = false;
        if (before is not { IsTracked: true })
        {
            using var cache = new AchievementCache(dataDir, steam);
            listed = await cache.FetchListAsync(appId, ct);
        }

        // Steam saying for the first time that it lists none changes the page too: it says 0 rather than waiting.
        var after = For(dataDir, game, title, store, appId);
        var learned = listed || (before is null && after is not null);
        return after is { Total: > 0 } view ? await FetchAsync(dataDir, view, ct, steam: steam) | learned : learned;
    }

    /// <summary>Icons and rarity Steam's files don't hold, asked of Steam once (ACH-02, ACH-03); true when something new was kept.</summary>
    /// <param name="every">Every icon the list of all of them shows, not only those Home and the game's page show.</param>
    /// <param name="again">Steam's percentages asked again now, however recent.</param>
    /// <param name="steam">For tests: answers in place of Steam.</param>
    public static async Task<bool> FetchAsync(string dataDir, GameAchievementsView view, CancellationToken ct, bool every = false, bool again = false,
        HttpMessageHandler? steam = null)
    {
        using var cache = new AchievementCache(dataDir, steam);

        // Rarity first: the rarest one unlocked is shown with its icon, and which it is comes from the percentages.
        var rarity = await cache.FetchRarityAsync(view.AppId, ct, again);
        var percents = cache.Rarity(view.AppId);
        var rarest = view.All.Where(a => a.Unlocked && percents.ContainsKey(a.Id)).MinBy(a => percents[a.Id]);
        var reach = view.All.Where(a => !a.Unlocked && !a.Hidden && percents.ContainsKey(a.Id)).OrderByDescending(a => percents[a.Id]).Take(3);
        var icons = (every ? view.EveryIcon : view.Latest(5).Append(rarest).Concat(reach).OfType<AchievementShown>().Select(a => a.IconFile)).ToList();
        return rarity | (cache.WantsIcons(view.AppId, icons) && await cache.FetchIconsAsync(view.AppId, icons, ct));
    }

    /// <summary>The Achievements page's games, a few at a time: what each shows that isn't kept yet; true when something new was kept.</summary>
    public static async Task<bool> FetchAllAsync(string dataDir, IReadOnlyList<GameAchievementsView> views, CancellationToken ct, bool again = false)
    {
        var changed = 0;
        await Parallel.ForEachAsync(views, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct }, async (view, token) =>
        {
            if (await FetchAsync(dataDir, view, token, again: again))
            {
                Interlocked.Increment(ref changed);
            }
        });
        return changed > 0;
    }

    /// <summary>
    /// Settings, Achievements, Rarity's Refresh now (design system version 55; the owner, 8 Oct 2026: "Refresh rarity
    /// shouldn't be on the main achievements page, but in settings"): Steam asked again now how rare each achievement is,
    /// for every game here with achievements, a few at a time. Returns how many were asked and how many answered.
    /// </summary>
    public static async Task<(int Asked, int Answered)> RefreshRarityAsync(string dataDir, IEnumerable<LauncherGame> games, CancellationToken ct,
        HttpMessageHandler? steam = null)
    {
        var apps = ForAll(dataDir, games).Select(v => v.AppId).Distinct().ToList();
        var answered = 0;
        using var cache = new AchievementCache(dataDir, steam);
        await Parallel.ForEachAsync(apps, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct }, async (app, token) =>
        {
            if (await cache.FetchRarityAsync(app, token, again: true))
            {
                Interlocked.Increment(ref answered);
            }
        });
        return (apps.Count, answered);
    }

    private static SteamGameAchievements? Read(string steamRoot, long appId)
    {
        var stats = Path.Combine(steamRoot, "appcache", "stats");
        var changed = Directory.Exists(stats)
            ? new DirectoryInfo(stats).EnumerateFiles($"*_{appId}.bin").Select(f => f.LastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max()
            : DateTime.MinValue;
        if (Parsed.TryGetValue((steamRoot, appId), out var known) && known.Changed == changed)
        {
            return known.Read;
        }

        var read = changed == DateTime.MinValue ? null : SteamAchievements.Read(steamRoot, appId);
        Parsed[(steamRoot, appId)] = (changed, read);
        return read;
    }

    /// <summary>When Steam's files of a game last changed, as <see cref="Read"/> last saw them: what's added to the record once per change.</summary>
    private static long Stamp(string steamRoot, long appId) => Parsed.TryGetValue((steamRoot, appId), out var known) ? known.Changed.Ticks : 0;

    // ---- marking by hand (design system version 54) ----

    /// <summary>
    /// Version 54 (the owner: "Yes but only for games that write nothing down"): marks one of a game's achievements unlocked
    /// now, by hand, or takes a mark off again, for a copy that writes its unlocks nowhere GameSync can find. What a
    /// witness saw is never taken off, and a game whose unlocks something writes down can't be marked: Steam's or the
    /// record's say. Marks go into GameSync's record like any unlock, on every PC once it's in the cloud.
    /// </summary>
    /// <param name="achievement">Steam's name for it, or its name as shown.</param>
    public static string Mark(string dataDir, LauncherGame game, string achievement, bool unlocked, string? steamRoot = null)
    {
        if (AppIdOf(dataDir, game) is not { } appId || For(dataDir, game, steamRoot) is not { Total: > 0 } view)
        {
            throw new UsageException($"{game.Title} has no list of achievements to mark: GameSync names them from Steam's lists.");
        }

        var a = view.All.FirstOrDefault(x => string.Equals(x.Id, achievement, StringComparison.OrdinalIgnoreCase))
            ?? view.All.FirstOrDefault(x => string.Equals(x.Name, achievement, StringComparison.CurrentCultureIgnoreCase))
            ?? throw new UsageException($"{game.Title} has no achievement called {achievement}.");
        using var state = new GameSync.Core.State.StateStore(dataDir);
        var device = state.GetOrCreateDevice(Environment.MachineName).Id.Value;
        var kept = state.GetUnlocks(appId).FirstOrDefault(e => !e.Removed && string.Equals(e.Id, a.Id, StringComparison.OrdinalIgnoreCase));
        var now = DateTime.UtcNow;
        if (unlocked)
        {
            if (kept is not null)
            {
                return $"{a.Name} is already unlocked.";
            }

            if (!view.CanMark)
            {
                throw new UsageException($"{game.Title}'s unlocks are written down where GameSync reads them, so they can't be marked by hand.");
            }

            state.KeepUnlocks([new KeptUnlock(appId, a.Id, now, UnlockSeen.Hand, device, now, now)]);
            return $"Marked {a.Name} unlocked in {game.Title}.";
        }

        if (kept is null)
        {
            return $"{a.Name} isn't unlocked.";
        }

        if (kept.Witnessed)
        {
            throw new UsageException($"{a.Name} was seen unlocked, not marked by hand, so it stays.");
        }

        state.KeepUnlocks([kept with { Removed = true, ChangedUtc = now > kept.ChangedUtc ? now : kept.ChangedUtc.AddTicks(1), Device = device }]);
        return $"Took the mark off {a.Name} in {game.Title}.";
    }

    // ---- finding copies' own records (design system version 54) ----

    /// <summary>
    /// Version 54 (the owner: "Tracking by itself", then "find them itself"): looks for the records the copies Steam doesn't
    /// run keep of what's unlocked, in the usual places (AppData, Documents, Saved Games, Public Documents, ProgramData) and
    /// each copy's own folder, and keeps the one found for each game. A record is told by the game's achievements named
    /// inside it, so each candidate's game needs Steam's list, asked once when it isn't kept. Returns how many games have
    /// a record found. GameSync's own folders are never looked in.
    /// </summary>
    public static async Task<int> FindRecordsAsync(string dataDir, IEnumerable<LauncherGame> games, CancellationToken ct, HttpMessageHandler? steam = null,
        IEnumerable<string>? roots = null)
    {
        var copies = games.Where(g => g.Store != StoreKind.Steam).ToList();
        using var state = new GameSync.Core.State.StateStore(dataDir);
        var leftOut = OwnFolders(dataDir, state);
        var dirs = InstallDirs(dataDir, state, copies.Select(g => g.Id).ToHashSet());

        // ACH-18: each copy's Steam app IDs to look under: GameSync's for the game first, then any the copy says it is
        // in its own folder (its edition may have an ID of its own).
        var ids = new Dictionary<GameId, List<long>>();
        var all = games.ToList();
        foreach (var g in copies)
        {
            // An ID another game here is known by is that game's: a copy pointed at the wrong folder (the owner's friend's Dark
            // Souls, 8 Oct 2026, its III located in Remastered's folder) never takes the other's achievements.
            var others = all.Where(o => o.Id != g.Id && o.SteamAppId is not null && o.SteamAppId != g.SteamAppId).Select(o => o.SteamAppId!.Value).ToHashSet();
            var declared = dirs.GetValueOrDefault(g.Id) is { } dir ? await Task.Run(() => CopyAchievements.DeclaredAppIds(dir), ct) : [];
            var mine = new[] { g.SteamAppId }.OfType<long>().Concat(declared.Where(id => !others.Contains(id))).Where(id => id != Spacewar).Distinct().ToList();
            if (mine.Count > 0)
            {
                ids[g.Id] = mine;
            }
        }

        var looked = roots?.ToList() ?? WindowsRoots();
        var apps = ids.Values.SelectMany(i => i).ToHashSet();
        var candidates = await Task.Run(() => CopyAchievements.Candidates(looked, apps, leftOut, ct: ct), ct);
        using var cache = new AchievementCache(dataDir, steam);
        var found = 0;
        foreach (var g in copies.Where(c => ids.ContainsKey(c.Id)))
        {
            // ACH-18: a record kept beside the game, anywhere in its own folder.
            var dir = dirs.GetValueOrDefault(g.Id);
            var own = dir is not null ? await Task.Run(() => CopyAchievements.GameFolderCandidates(dir, leftOut), ct) : [];
            var seen = new List<string>(own);
            string? kept = null;
            foreach (var app in ids[g.Id])
            {
                var named = candidates.GetValueOrDefault(app) ?? [];
                seen.AddRange(named);
                if (named.Count == 0 && own.Count == 0)
                {
                    continue;
                }

                if (cache.List(app) is null)
                {
                    await cache.FetchListAsync(app, ct);
                }

                if (cache.List(app) is not { } list)
                {
                    continue;
                }

                var achievements = list.All.Select(a => a.Id).ToList();
                if ((CopyAchievements.Best(named.Concat(own), achievements, allowBlank: false) ?? CopyAchievements.Best(named, achievements)) is { } best)
                {
                    state.SetSetting(FoundKey(app), best);
                    state.SetSetting(FoundGameKey(app), g.Title);

                    // Under another ID than GameSync's for the game: its achievements are that ID's from now on.
                    state.SetSetting(AppIdKey(g.Id), app == g.SteamAppId ? "" : app.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    found++;
                    kept = best;
                    break;
                }
            }

            // Design system version 55: with no record that says anything, what the copy left instead, for the ? on its
            // achievements page.
            var clue = kept is null || CopyAchievements.IsBlank(kept) ? await Task.Run(() => CopyAchievements.Clue(seen, dir, leftOut), ct) : null;
            state.SetSetting(ClueKey(g.Id), clue is null ? "" : System.Text.Json.JsonSerializer.Serialize(clue, ClueJson));
        }

        // A record found before that's gone now is forgotten; what GameSync saw in it stays in its record.
        foreach (var (key, value) in state.GetSettings(FoundPrefix))
        {
            if (!File.Exists(value))
            {
                state.SetSetting(key, "");
            }
        }

        state.SetSetting(LookedKey, DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        return found;
    }

    /// <summary>
    /// Version 54: watches for the record a copy Steam doesn't run writes while it plays (<see cref="RecordWatch"/>), when
    /// none is known yet, under GameSync's ID for the game or one the copy says it is (ACH-18); null for a Steam copy,
    /// a copy whose record is known, one with an anti-cheat (R13, as learn mode), or one with no list of achievements to
    /// tell its record by (each is asked of Steam first). A record found is kept for the game, and <paramref name="found"/> told.
    /// </summary>
    public static async Task<RecordWatch?> WatchForRecordAsync(string dataDir, LauncherGame game, Action<string>? found = null, IEnumerable<string>? roots = null,
        TimeSpan? every = null, HttpMessageHandler? steam = null, CancellationToken ct = default)
    {
        if (game.Store == StoreKind.Steam || AntiCheatGames(dataDir).Contains(game.Id))
        {
            return null;
        }

        using var state = new GameSync.Core.State.StateStore(dataDir);
        var dir = InstallDirs(dataDir, state, new HashSet<GameId> { game.Id }).GetValueOrDefault(game.Id);
        var ids = new[] { AppIdOf(state, game.Id, game.SteamAppId), game.SteamAppId }.OfType<long>()
            .Concat(dir is null ? [] : CopyAchievements.DeclaredAppIds(dir))
            .Where(id => id != Spacewar)
            .Distinct()
            .ToList();
        var folders = RecordFolders(state);
        if (ids.Count == 0 || ids.Any(id => RecordOf(folders, state, id) is not null))
        {
            return null;
        }

        using var cache = new AchievementCache(dataDir, steam);
        var lists = new List<(long AppId, IReadOnlyCollection<string> Achievements)>();
        foreach (var id in ids)
        {
            if (cache.List(id) is null)
            {
                await cache.FetchListAsync(id, ct);
            }

            if (cache.List(id) is { } list)
            {
                lists.Add((id, list.All.Select(a => a.Id).ToList()));
            }
        }

        if (lists.Count == 0)
        {
            return null;
        }

        var watched = roots?.ToList() ?? [.. WindowsRoots(), .. dir is null ? Array.Empty<string>() : [dir]];
        return new RecordWatch(lists, watched, OwnFolders(dataDir, state), (path, app) =>
        {
            using (var kept = new GameSync.Core.State.StateStore(dataDir))
            {
                kept.SetSetting(FoundKey(app), path);
                kept.SetSetting(FoundGameKey(app), game.Title);
                kept.SetSetting(AppIdKey(game.Id), app == game.SteamAppId ? "" : app.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            found?.Invoke(path);
        }, every);
    }

    /// <summary>GameSync's own folders, never looked in for a record: its data, its backups, and a cloud folder on this PC.</summary>
    private static List<string> OwnFolders(string dataDir, GameSync.Core.State.StateStore state)
    {
        var own = new List<string> { dataDir, Cli.HistoryFolder(state, dataDir) };
        if (AppConfig.Load(dataDir) is { UsesDrive: false, HasNoCloud: false } config && Directory.Exists(config.Remote))
        {
            own.Add(config.Remote);
        }

        return own;
    }

    /// <summary>Where copies keep their records, without any list of who puts them where: Windows' usual folders.</summary>
    private static List<string> WindowsRoots()
    {
        var folders = Cli.FoldersForThisPc();
        var roots = new List<string>();
        foreach (var usual in new[] { "<roaming>", "<localAppData>", "<localLow>", "<documents>", "<savedGames>", "<publicDocuments>", "<programData>" })
        {
            if (folders.GetValueOrDefault(usual) is { Length: > 0 } path)
            {
                roots.Add(path);
            }
        }

        return roots;
    }

    /// <summary>Each copy's own folder on this PC: where the person put it by hand, else where GameSync found it.</summary>
    private static Dictionary<GameId, string> InstallDirs(string dataDir, GameSync.Core.State.StateStore state, IReadOnlySet<GameId> copies)
    {
        var dirs = new Dictionary<GameId, string>();
        using var library = new LibraryStore(dataDir);
        foreach (var entry in library.All().Where(e => copies.Contains(e.Id) && e.MergedInto is null))
        {
            if ((state.GetSetting($"installDir.{entry.Id}") is { Length: > 0 } byHand ? byHand : entry.InstallDirByHand ?? entry.InstallDir) is { Length: > 0 } dir &&
                Directory.Exists(dir))
            {
                dirs[entry.Id] = dir;
            }
        }

        return dirs;
    }
}
