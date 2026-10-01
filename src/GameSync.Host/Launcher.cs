using System.Globalization;
using GameSync.Core.Art;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.State;

namespace GameSync.Host;

/// <summary>One game as the launcher shows it: its status, its art, and when and how long it was played.</summary>
public sealed record LauncherGame
{
    public required GameId Id { get; init; }

    public required string Title { get; init; }

    /// <summary>
    /// Null for a game not syncing yet; a game that syncs but hasn't been backed up yet is Upload pending, with
    /// <see cref="FirstBackupPending"/> (KAN-60), never Synced.
    /// </summary>
    public GameStatus? Status { get; init; }

    public string? StatusDetail { get; init; }

    /// <summary>It syncs, but its first backup hasn't happened yet (KAN-60): it says "Not backed up yet".</summary>
    public bool FirstBackupPending { get; init; }

    /// <summary>Its saves sync: confirmed in the library, or added by hand.</summary>
    public bool Syncs { get; init; }

    public bool Installed { get; init; } = true;

    public StoreKind? Store { get; init; }

    /// <summary>Its Steam app ID, from Steam itself or, for Epic and loose copies, from the save list (ART-01).</summary>
    public long? SteamAppId { get; init; }

    /// <summary>Steam's total for Steam games, or the sessions GameSync recorded, whichever is more (PLAY-09).</summary>
    public TimeSpan Playtime { get; init; }

    public DateTime? LastPlayedUtc { get; init; }

    public string? CoverPath { get; init; }

    public string? HeroPath { get; init; }

    public string? LogoPath { get; init; }

    /// <summary>Steam lists it as software (Wallpaper Engine, 3DMark): its saves can sync, but the launcher doesn't show it as a game.</summary>
    public bool IsSoftware { get; init; }

    /// <summary>The person hid it from the launcher on this PC; it syncs as before.</summary>
    public bool IsHidden { get; init; }

    /// <summary>A favourite on this PC: first in the library's list and covers (LIB-17).</summary>
    public bool IsFavourite { get; init; }

    /// <summary>When GameSync first found it on this PC, for Recently added (LIB-16); unknown for games added by hand.</summary>
    public DateTime? AddedUtc { get; init; }

    /// <summary>Added by hand in games.json, rather than found by a scan.</summary>
    public bool ByHand { get; init; }

    /// <summary>LIB-13: the person added it (Add a game or folder).</summary>
    public bool IsOwn { get; init; }

    /// <summary>
    /// Its store's cloud syncs its saves (LIB-10), so backing up only is the store's doing; a game backed up only without
    /// it is kept by the person's choice, not synced between their PCs (KAN-63).
    /// </summary>
    public bool StoreSyncs { get; init; }

    /// <summary>
    /// LIB-13: one of the person's own with no program on this PC, such as a server's world: nothing to play, and it syncs
    /// once quiet. Its spells of changes aren't play, so play time and the activity calendar leave them out.
    /// </summary>
    public bool IsFolder { get; init; }

    /// <summary>
    /// Where its saves go besides this PC, in a sentence's words: "your Google Drive", or "your cloud folder" for a folder
    /// such as a NAS; null with no cloud yet, after first run's Skip for now (ONB-06).
    /// </summary>
    public string? Cloud { get; init; } = "your Google Drive";

    /// <summary>
    /// PLAY-12: when the session playing it now started, however the game was started and whether its saves sync or
    /// not; null while it isn't running.
    /// </summary>
    public DateTime? RunningSinceUtc { get; init; }

    /// <summary>It's being played now.</summary>
    public bool IsRunning => RunningSinceUtc is not null || Status == GameStatus.Playing;

    /// <summary>LIB-22: installed on this PC in its own folder, started without a store's launcher: the library's Local view.</summary>
    public bool IsLocal => Installed && Store == StoreKind.Loose;

    /// <summary>What Home and the library's game tabs show: games the person hasn't hidden.</summary>
    public bool Shown => !IsSoftware && !IsHidden;

    /// <summary>A status that waits for the person (a conflict, a review, a missing folder, a blocked file).</summary>
    public bool NeedsYou => SyncCounts.NeedsYou(Status);
}

/// <summary>LIB-16: the orders the library's list and covers can take, kept per PC.</summary>
public enum LibrarySort
{
    RecentlyPlayed,
    Name,
    HoursPlayed,
    RecentlyAdded,
}

/// <summary>What the launcher home shows (PLAY-01): the last-played game, what needs you, what to jump back into, and this month's play.</summary>
/// <param name="Months">
/// Activity, month by month (KAN-66): from the first month with play, a year back at most, to this month, which is last.
/// </param>
public sealed record LauncherHome(
    LauncherGame? Hero,
    IReadOnlyList<LauncherGame> NeedsYou,
    IReadOnlyList<LauncherGame> JumpBackIn,
    IReadOnlyList<ActivityMonth> Months,
    int Synced,
    int Syncing)
{
    /// <summary>This month's level of play each day, 0 to 3.</summary>
    public IReadOnlyList<int> MonthDays => Months[^1].Levels;

    /// <summary>The weekday this month starts on, 0 for Monday.</summary>
    public int MonthStartWeekday => Months[^1].StartWeekday;

    public string MonthName => Months[^1].Name;

    /// <summary>This month's days, each with the games played that day and for how long, longest first.</summary>
    public IReadOnlyList<IReadOnlyList<DayPlay>> MonthPlay => Months[^1].Play;
}

/// <summary>
/// A month of Activity: each day's level of play (0 none, 1 under 2 hours, 2 two to four, 3 over four), the weekday it
/// starts on (0 for Monday), its name ("September", or "December 2025" in another year), and each day's games (KAN-66).
/// </summary>
public sealed record ActivityMonth(DateTime First, IReadOnlyList<int> Levels, int StartWeekday, string Name, IReadOnlyList<IReadOnlyList<DayPlay>> Play);

/// <summary>A game played on a day, and for how long in all that day (KAN-66).</summary>
public sealed record DayPlay(GameId Id, string Title, TimeSpan Played);

/// <summary>
/// The launcher's view of this PC (PLAY-01, PLAY-09, LIB-11): every game in the library that isn't ignored, and the
/// games added by hand, each with its status, Steam's art from the cache, and its play from GameSync's sessions and
/// Steam's own record.
/// </summary>
public static class Launcher
{
    /// <param name="running">PLAY-12: the games being played now, with when each session started.</param>
    public static IReadOnlyList<LauncherGame> Games(
        IEnumerable<LibraryEntry> library,
        IEnumerable<(GameId Id, string Title)> handAdded,
        StateStore state,
        SaveList? saveList,
        IReadOnlyDictionary<long, SteamPlay> steamPlay,
        ArtCache? art,
        IReadOnlyDictionary<GameId, DateTime>? running = null)
    {
        var marks = new Marks(state.GetSettings(HiddenPrefix), state.GetSettings(FavouritePrefix));
        var games = new List<LauncherGame>();
        foreach (var entry in library.Where(e => e.State != LibraryState.Ignored && e.MergedInto is null))
        {
            var steamId = SteamIdOf(entry, saveList);
            var game = Describe(entry.Id, entry.DisplayTitle, entry.State == LibraryState.Synced, entry.Installed, entry.Store, steamId, state, steamPlay, art, marks) with
            {
                AddedUtc = entry.FirstSeenUtc == default ? null : entry.FirstSeenUtc,
                RunningSinceUtc = running?.GetValueOrDefault(entry.Id) is { } since && since != default ? since : null,
                IsOwn = entry.IsOwn,
                IsFolder = entry.IsOwnFolder,
                StoreSyncs = entry.StoreCloud,
            };
            games.Add(game.IsFolder ? game with { Playtime = TimeSpan.Zero } : game);
        }

        foreach (var (id, title) in handAdded.Where(g => games.All(x => x.Id != g.Id)))
        {
            var steamId = saveList?.ByTitle(title)?.SteamIds.FirstOrDefault() is > 0 and var listed ? listed : (long?)null;
            games.Add(Describe(id, title, syncs: true, installed: true, store: null, steamId, state, steamPlay, art, marks) with
            {
                ByHand = true,
                RunningSinceUtc = running?.GetValueOrDefault(id) is { } since && since != default ? since : null,
            });
        }

        return games
            .OrderByDescending(g => g.LastPlayedUtc ?? DateTime.MinValue)
            .ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The home screen: the game playing now as the hero (PLAY-12), or else the most recently played installed game, up
    /// to three that need you, the next <see cref="JumpBackInAtMost"/> played (Home shows as many as fit), and this month's
    /// play per day from GameSync's sessions (Steam keeps no daily record).
    /// </summary>
    public const int JumpBackInAtMost = 12;

    public static LauncherHome Home(IReadOnlyList<LauncherGame> all, StateStore state, DateTime nowLocal)
    {
        // The home is about games; software such as Wallpaper Engine stays in the library's Software tab, and hidden games in Hidden.
        // A folder of the person's own (LIB-13) syncs and can need them, but there's nothing to play in it.
        var games = all.Where(g => g.Shown).ToList();
        var playable = games.Where(g => !g.IsFolder).ToList();
        var played = playable.Where(g => g.LastPlayedUtc is not null).ToList();
        var hero = playable.Where(g => g.IsRunning).MaxBy(g => g.RunningSinceUtc ?? DateTime.MinValue)
            ?? played.FirstOrDefault(g => g.Installed) ?? playable.FirstOrDefault(g => g.Installed);
        var jump = played.Where(g => g != hero).Take(JumpBackInAtMost).ToList();
        if (jump.Count < JumpBackInAtMost)
        {
            jump.AddRange(playable.Where(g => g != hero && !jump.Contains(g)).Take(JumpBackInAtMost - jump.Count));
        }

        // Activity: each month from the first with play, a year back at most, to this one; a session counts on the day it started.
        var thisMonth = new DateTime(nowLocal.Year, nowLocal.Month, 1);
        var sessions = playable
            .SelectMany(game => state.GetSessions(game.Id).Select(s => (Game: game, Start: s.StartUtc.ToLocalTime(), Length: s.EndUtc - s.StartUtc)))
            .Where(s => s.Start >= thisMonth.AddMonths(-11) && s.Start < thisMonth.AddMonths(1))
            .ToList();
        var months = new List<ActivityMonth>();
        for (var month = sessions.Count == 0 ? thisMonth : sessions.Min(s => new DateTime(s.Start.Year, s.Start.Month, 1)); month <= thisMonth; month = month.AddMonths(1))
        {
            months.Add(Month(month, sessions.Where(s => s.Start >= month && s.Start < month.AddMonths(1)).ToList(), nowLocal));
        }

        return new LauncherHome(
            hero,
            games.Where(g => g.NeedsYou).Take(3).ToList(),
            jump,
            months,
            games.Count(g => g.Syncs && g.Status is GameStatus.Synced or GameStatus.BackupOnly),
            games.Count(g => g.Syncs));
    }

    private static ActivityMonth Month(DateTime first, IReadOnlyList<(LauncherGame Game, DateTime Start, TimeSpan Length)> sessions, DateTime nowLocal)
    {
        var days = new TimeSpan[DateTime.DaysInMonth(first.Year, first.Month)];
        var byGame = days.Select(_ => new Dictionary<LauncherGame, TimeSpan>()).ToArray();
        foreach (var (game, start, length) in sessions)
        {
            days[start.Day - 1] += length;
            byGame[start.Day - 1][game] = byGame[start.Day - 1].GetValueOrDefault(game) + length;
        }

        return new ActivityMonth(
            first,
            days.Select(ActivityLevel).ToList(),
            ((int)first.DayOfWeek + 6) % 7,
            first.ToString(first.Year == nowLocal.Year ? "MMMM" : "MMMM yyyy", CultureInfo.InvariantCulture),
            byGame.Select(day => (IReadOnlyList<DayPlay>)day.OrderByDescending(p => p.Value).ThenBy(p => p.Key.Title, StringComparer.OrdinalIgnoreCase)
                .Select(p => new DayPlay(p.Key.Id, p.Key.Title, p.Value)).ToList()).ToList());
    }

    /// <summary>The setting that hides a game from the launcher on this PC.</summary>
    public static string HiddenKey(GameId game) => $"{HiddenPrefix}{game}";

    /// <summary>The setting that makes a game a favourite on this PC (LIB-17).</summary>
    public static string FavouriteKey(GameId game) => $"{FavouritePrefix}{game}";

    /// <summary>The setting that keeps the library's order on this PC (LIB-16).</summary>
    public const string SortKey = "library.sort";

    /// <summary>The setting that keeps the library's Installed only on this PC (KAN-47).</summary>
    public const string InstalledOnlyKey = "library.installed-only";

    private const string HiddenPrefix = "hidden.";
    private const string FavouritePrefix = "favourite.";

    /// <summary>
    /// LIB-16: the library's order. Recently played puts the newest played first and never-played games last; Name
    /// goes A to Z; Hours played puts the most played first; Recently added puts what GameSync found last first. Ties,
    /// and games with nothing to go by, go by name.
    /// </summary>
    public static IReadOnlyList<LauncherGame> Sort(IEnumerable<LauncherGame> games, LibrarySort sort)
    {
        var byName = StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);
        return (sort switch
        {
            LibrarySort.Name => games.OrderBy(g => g.Title, byName),
            LibrarySort.HoursPlayed => games.OrderByDescending(g => g.Playtime).ThenBy(g => g.Title, byName),
            LibrarySort.RecentlyAdded => games.OrderByDescending(g => g.AddedUtc ?? DateTime.MinValue).ThenBy(g => g.Title, byName),
            _ => games.OrderByDescending(g => g.LastPlayedUtc ?? DateTime.MinValue).ThenBy(g => g.Title, byName),
        }).ToList();
    }

    /// <summary>
    /// LIB-15: whether a title answers a search. Case, accents and punctuation don't count ("ragnarok" finds
    /// "Ragnarök", "black myth" finds "Black Myth: Wukong"), spaces don't either ("slaythe" finds Slay the Spire),
    /// and neither do the first letters of the words ("sts" and "sts2" find Slay the Spire 2, "cs2" Counter-Strike 2).
    /// </summary>
    public static bool Matches(string title, string? query)
    {
        var words = Words(query);
        if (words.Count == 0)
        {
            return true;
        }

        var titleWords = Words(title);
        var spaced = string.Join(' ', titleWords);
        var q = string.Join(' ', words);
        if (spaced.Contains(q, StringComparison.Ordinal))
        {
            return true;
        }

        var joined = string.Concat(words);
        if (string.Concat(titleWords).Contains(joined, StringComparison.Ordinal))
        {
            return true;
        }

        // A number counts whole among the initials: "Slay the Spire 2" is "sts2".
        var initials = string.Concat(titleWords.Select(w => char.IsAsciiDigit(w[0]) ? w : w[..1]));
        return initials.StartsWith(joined, StringComparison.Ordinal);
    }

    /// <summary>A text's words in lower case, letters and digits only, accents dropped.</summary>
    private static List<string> Words(string? text)
    {
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        foreach (var c in (text ?? "").Normalize(System.Text.NormalizationForm.FormD).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(c);
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }

        return words;
    }

    /// <summary>0 none, 1 under 2 hours, 2 two to four, 3 over four, as the activity calendar shows it.</summary>
    public static int ActivityLevel(TimeSpan played) =>
        played <= TimeSpan.Zero ? 0 : played < TimeSpan.FromHours(2) ? 1 : played <= TimeSpan.FromHours(4) ? 2 : 3;

    /// <summary>"61 h", "45 min", or null when never played.</summary>
    public static string? PlaytimeText(TimeSpan playtime) =>
        playtime <= TimeSpan.Zero ? null : playtime.TotalHours >= 1 ? $"{(int)Math.Round(playtime.TotalHours)} h" : $"{Math.Max(1, (int)Math.Round(playtime.TotalMinutes))} min";

    /// <summary>How long something lasted: "1 h 52 min", "47 min", "2 h", "under a minute".</summary>
    public static string DurationText(TimeSpan span)
    {
        var minutes = (int)Math.Round(span.TotalMinutes);
        return minutes < 1 ? "under a minute"
            : minutes < 60 ? $"{minutes.ToString(CultureInfo.InvariantCulture)} min"
            : minutes % 60 == 0 ? $"{(minutes / 60).ToString(CultureInfo.InvariantCulture)} h"
            : $"{(minutes / 60).ToString(CultureInfo.InvariantCulture)} h {(minutes % 60).ToString(CultureInfo.InvariantCulture)} min";
    }

    /// <summary>"Today 21:04", "Yesterday", "20 Sep", or "20 Sep 2025" before this year.</summary>
    public static string? WhenText(DateTime? utc, DateTime nowLocal)
    {
        if (utc is null)
        {
            return null;
        }

        var local = utc.Value.ToLocalTime();
        return local.Date == nowLocal.Date ? $"Today {local:HH:mm}"
            : local.Date == nowLocal.Date.AddDays(-1) ? "Yesterday"
            : local.Year == nowLocal.Year ? local.ToString("d MMM", CultureInfo.InvariantCulture)
            : local.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>A tile's meta line: "61 h · Today 21:04".</summary>
    public static string? Meta(LauncherGame game, DateTime nowLocal) =>
        string.Join(" · ", new[] { PlaytimeText(game.Playtime), game.IsRunning ? "Now" : WhenText(game.LastPlayedUtc, nowLocal) }
            .Where(s => s is not null)) is { Length: > 0 } meta ? meta : null;

    internal static long? SteamIdOf(LibraryEntry entry, SaveList? saveList)
    {
        if (entry.Store == StoreKind.Steam && long.TryParse(entry.StoreId, NumberStyles.None, CultureInfo.InvariantCulture, out var appId))
        {
            return appId;
        }

        var listed = (entry.SaveListTitle is { } listTitle ? saveList?.ByTitle(listTitle) : null) ?? saveList?.ByTitle(entry.Title);
        return listed?.SteamIds.FirstOrDefault() is > 0 and var id ? id : null;
    }

    /// <summary>The games hidden and made favourites on this PC, read once for the whole library.</summary>
    private sealed record Marks(IReadOnlyDictionary<string, string> Hidden, IReadOnlyDictionary<string, string> Favourites);

    private static LauncherGame Describe(GameId id, string title, bool syncs, bool installed, StoreKind? store, long? steamId,
        StateStore state, IReadOnlyDictionary<long, SteamPlay> steamPlay, ArtCache? art, Marks marks)
    {
        var sessions = state.GetSessions(id);
        var steam = steamId is { } appId ? steamPlay.GetValueOrDefault(appId) : null;
        var sessionTime = TimeSpan.FromTicks(sessions.Sum(s => (s.EndUtc - s.StartUtc).Ticks));
        DateTime? lastSession = sessions.Count > 0 ? sessions.Max(s => s.EndUtc) : null;
        var gameState = state.GetState(id);
        return new LauncherGame
        {
            Id = id,
            Title = title,
            // KAN-60: a game that syncs but has never synced waits for its first backup; it isn't Synced yet.
            Status = gameState.Status ?? (syncs ? GameStatus.UploadPending : null),
            StatusDetail = gameState.Detail,
            FirstBackupPending = syncs && gameState.Status is null,
            Syncs = syncs,
            Installed = installed,
            Store = store,
            SteamAppId = steamId,
            Playtime = steam is null || steam.Playtime < sessionTime ? sessionTime : steam.Playtime,
            LastPlayedUtc = Latest(lastSession, steam?.LastPlayedUtc),
            // The person's own image first (ART-06), then Steam's, then what the game carries in its own folder (KAN-59).
            CoverPath = art?.FindOwn(id, ArtKind.Cover) ?? (steamId is { } a ? art?.Find(a, ArtKind.Cover) : null) ?? art?.FindLocal(id, ArtKind.Cover),
            HeroPath = art?.FindOwn(id, ArtKind.Hero) ?? (steamId is { } h ? art?.Find(h, ArtKind.Hero) : null) ?? art?.FindLocal(id, ArtKind.Hero),
            LogoPath = art?.FindOwn(id, ArtKind.Logo) ?? (steamId is { } l ? art?.Find(l, ArtKind.Logo) : null),
            IsSoftware = steamId is { } s && art?.IsSoftware(s) == true,
            IsHidden = marks.Hidden.ContainsKey(HiddenKey(id)),
            IsFavourite = marks.Favourites.ContainsKey(FavouriteKey(id)),
        };
    }

    private static DateTime? Latest(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : a > b ? a : b;
}
