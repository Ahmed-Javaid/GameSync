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
public sealed record AchievementShown(string Id, string Name, string? Description, bool Hidden, DateTime? UnlockedUtc, string? Icon, string? IconFile, double? Percent)
{
    public bool Unlocked => UnlockedUtc is not null;

    /// <summary>When it was unlocked, if Steam kept the time.</summary>
    public DateTime? KnownUnlockUtc => UnlockedUtc is { } at && at > DateTime.UnixEpoch ? at : null;

    /// <summary>Hidden by its game, and still locked: nothing about it shows but how many players have it.</summary>
    public bool Secret => Hidden && !Unlocked;

    /// <summary>Its tier from how many of Steam's players have it (design system version 33); null until that's known.</summary>
    public AchievementTier? Tier => AchievementTiers.Of(Percent);
}

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

/// <summary>Where a game's achievements come from (KAN-111): Steam's own files on this PC, or Steam's public list, every one locked.</summary>
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
    /// A copy Steam doesn't run, from its own record of what's unlocked, in a folder added in Settings (KAN-123; the owner:
    /// "As long as my offline games also have achievements"): counted like Steam's own, each one named from Steam's list.
    /// </summary>
    CopyRecord,
}

/// <summary>
/// ACH-01 to ACH-03: a game's achievements, from Steam's own files on this PC, with what's kept about them; or, for a
/// game Steam here holds none for, Steam's public list with every one locked (<see cref="From"/>; KAN-111).
/// </summary>
public sealed record GameAchievementsView(GameId Game, string Title, long AppId, IReadOnlyList<AchievementShown> All,
    AchievementsFrom From = AchievementsFrom.Steam)
{
    /// <summary>What's unlocked is known: Steam's own files, or a copy's own record (KAN-123). A list at 0 isn't on the Achievements page or Home.</summary>
    public bool IsTracked => From is AchievementsFrom.Steam or AchievementsFrom.CopyRecord;

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

/// <summary>Reads achievements for the screens, keeping each game's Steam files parsed until they change.</summary>
public static class Achievements
{
    private static readonly ConcurrentDictionary<(string Root, long AppId), (DateTime Changed, SteamGameAchievements? Read)> Parsed = new();

    /// <summary>
    /// The game's achievements: Steam's own on this PC for a Steam copy; else a copy's own record in a folder added in
    /// Settings, named from Steam's list (KAN-123); else Steam's list at 0 once it's been asked for (a copy in its own
    /// folder, a game never run here; KAN-111); null for a game with no Steam app ID, or before the list is kept.
    /// </summary>
    public static GameAchievementsView? For(string dataDir, LauncherGame game, string? steamRoot = null, IReadOnlyList<string>? recordFolders = null) =>
        For(dataDir, game.Id, game.Title, game.Store, game.SteamAppId, steamRoot, recordFolders);

    /// <param name="store">Only a Steam copy shows Steam's own files: a copy in its own folder shows its own record or Steam's list at 0 (ACH-01, KAN-111, KAN-123).</param>
    /// <param name="recordFolders">The folders of copies' records, when the caller has them already; read from this PC's settings otherwise.</param>
    public static GameAchievementsView? For(string dataDir, GameId game, string title, StoreKind? store, long? steamAppId, string? steamRoot = null,
        IReadOnlyList<string>? recordFolders = null)
    {
        if (steamAppId is not { } appId)
        {
            return null;
        }

        using var cache = new AchievementCache(dataDir);
        var rarity = cache.Rarity(appId);
        if (store == StoreKind.Steam && (steamRoot ?? StoreLocations.SteamRoot()) is { } root && Read(root, appId) is { } steam)
        {
            return new GameAchievementsView(game, title, appId, Shown(steam.All, cache, appId, rarity));
        }

        var list = cache.List(appId);

        // KAN-123: a copy Steam doesn't run whose own record is in a folder added in Settings: counted like Steam's, each
        // one named from Steam's list. An unlock the record keeps no time for counts with no day, as Steam's do.
        if (store != StoreKind.Steam && list is { } named && Record(recordFolders ?? RecordFolders(dataDir), appId) is { } record)
        {
            var all = named.All.Select(a => record.TryGetValue(a.Id, out var at) ? a with { UnlockedUtc = at ?? DateTime.UnixEpoch } : a).ToList();
            return new GameAchievementsView(game, title, appId, Shown(all, cache, appId, rarity.Count > 0 ? rarity : named.Percents), AchievementsFrom.CopyRecord);
        }

        if (list is not { } listed)
        {
            // Asked, and Steam lists none: the page says 0 rather than waiting for a list that won't come.
            return cache.ListedNone(appId) ? new GameAchievementsView(game, title, appId, [], AchievementsFrom.NoneOnSteam) : null;
        }

        // Every one locked: the list's own percentages until Steam's weekly ones are kept.
        var percents = rarity.Count > 0 ? rarity : listed.Percents;
        return new GameAchievementsView(game, title, appId, Shown(listed.All, cache, appId, percents),
            store == StoreKind.Steam ? AchievementsFrom.ListNotPlayedHere : AchievementsFrom.ListNotSteamCopy);
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

    /// <summary>Takes a folder of copies' records off the list (KAN-123); its games show Steam's list at 0 again.</summary>
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
        return $"Removed {path}: its games show Steam's list at 0 again.";
    }

    /// <summary>KAN-123: the record file of a game's copy, when one of the folders added holds one.</summary>
    public static string? RecordOf(string dataDir, long appId) => CopyAchievements.Find(RecordFolders(dataDir), appId);

    /// <summary>KAN-123: the games whose copy keeps its own record in a folder added in Settings.</summary>
    public static IReadOnlyList<LauncherGame> WithRecords(string dataDir, IEnumerable<LauncherGame> games)
    {
        var folders = RecordFolders(dataDir);
        return folders.Count == 0 ? [] : games.Where(g => g.SteamAppId is { } app && CopyAchievements.Find(folders, app) is not null).ToList();
    }

    private static readonly ConcurrentDictionary<string, (DateTime Changed, IReadOnlyDictionary<string, DateTime?>? Read)> Records = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A copy's own record, read again only when it changes: what it holds as unlocked, by Steam's names.</summary>
    private static IReadOnlyDictionary<string, DateTime?>? Record(IReadOnlyList<string> folders, long appId)
    {
        if (folders.Count == 0 || CopyAchievements.Find(folders, appId) is not { } file)
        {
            return null;
        }

        var changed = File.GetLastWriteTimeUtc(file);
        if (Records.TryGetValue(file, out var known) && known.Changed == changed)
        {
            return known.Read;
        }

        var read = CopyAchievements.Read(file);
        Records[file] = (changed, read);
        return read;
    }

    /// <summary>Each as the screens show it; a hidden one still locked shows no icon at all, so its picture is never looked for either.</summary>
    private static List<AchievementShown> Shown(IEnumerable<SteamAchievement> all, AchievementCache cache, long appId, IReadOnlyDictionary<string, double> percents) =>
        all.Select(a =>
        {
            var file = a.Unlocked ? a.Icon : a.IconLocked ?? a.Icon;
            var secret = a.Hidden && !a.Unlocked;
            return new AchievementShown(a.Id, a.Name, a.Description, a.Hidden, a.UnlockedUtc, secret ? null : cache.Icon(appId, file), secret ? null : file,
                percents.TryGetValue(a.Id, out var percent) ? percent : null);
        }).ToList();

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
    /// (Steam's, and since KAN-123 copies with their own record), counted or left out, with its popup on or off, by name.
    /// </summary>
    public static IReadOnlyList<AchievementGameRow> SettingsRows(string dataDir, IEnumerable<LauncherGame> games, string? steamRoot = null)
    {
        steamRoot ??= StoreLocations.SteamRoot();
        var leftOut = LeftOut(dataDir);
        var popups = PopupChoices(dataDir);
        var records = RecordFolders(dataDir);
        var antiCheat = AntiCheatGames(dataDir);
        string Count(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return games.Where(g => g.Shown && g.SteamAppId is not null && (g.Store == StoreKind.Steam || records.Count > 0))
            .Select(g => (Game: g, View: For(dataDir, g, steamRoot, records)))
            .Where(x => x.View is { IsTracked: true, Total: > 0 })
            .Select(x =>
            {
                // Steam runs a game whose achievements are Steam's and shows its own popup; a copy with its own record has
                // no launcher showing one, so GameSync's is on unless turned off (KAN-123). Never over an anti-cheat (R13).
                var launcher = x.View!.From == AchievementsFrom.Steam ? LauncherOf(x.Game.Store) : null;
                var guarded = antiCheat.Contains(x.Game.Id);
                return new AchievementGameRow(x.Game.Id, x.Game.Title, $"{Count(x.View!.Unlocked)} of {Count(x.View.Total)}", leftOut.Contains(x.Game.Id),
                    x.Game.CoverPath, x.View.IsComplete, launcher, !guarded && PopupOn(popups, x.Game.Id, launcher is not null), guarded);
            })
            .OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
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
        var records = RecordFolders(dataDir);
        var heroLeftOut = hero is not null && leftOut.Contains(hero.Id);
        if (hero is not null && !heroLeftOut && For(dataDir, hero, steamRoot, records) is { IsTracked: true } own)
        {
            return (own, false, false);
        }

        var other = games.Where(g => g.Shown && g.Id != hero?.Id && g.LastPlayedUtc is not null && !leftOut.Contains(g.Id))
            .OrderByDescending(g => g.LastPlayedUtc)
            .Take(20)
            .Select(g => For(dataDir, g, steamRoot, records))
            .FirstOrDefault(v => v is { IsTracked: true, Unlocked: > 0 });
        return (other, hero is not null && other is not null, heroLeftOut);
    }

    /// <summary>
    /// The Achievements page (design system version 33): every game here whose achievements GameSync tracks, from Steam's
    /// own files and since KAN-123 from copies' own records; Steam's lists at 0 (KAN-111) and games left out (KAN-110)
    /// aren't counted there.
    /// </summary>
    public static IReadOnlyList<GameAchievementsView> ForAll(string dataDir, IEnumerable<LauncherGame> games, string? steamRoot = null)
    {
        steamRoot ??= StoreLocations.SteamRoot();
        var leftOut = LeftOut(dataDir);
        var records = RecordFolders(dataDir);
        return games.Where(g => g.Shown && g.SteamAppId is not null && !leftOut.Contains(g.Id) && (g.Store == StoreKind.Steam || records.Count > 0))
            .Select(g => For(dataDir, g, steamRoot, records))
            .OfType<GameAchievementsView>()
            .Where(v => v.IsTracked)
            .ToList();
    }

    /// <summary>
    /// What a game's page needs that isn't kept yet (KAN-111): Steam's list for a game Steam here holds none for, then its
    /// icons and rarity; true when something new was kept.
    /// </summary>
    public static async Task<bool> FetchForAsync(string dataDir, GameId game, string title, StoreKind? store, long? steamAppId, CancellationToken ct,
        HttpMessageHandler? steam = null)
    {
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
    /// <param name="again">Steam's percentages asked again now, however recent: the Achievements page's Refresh rarity.</param>
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
}
