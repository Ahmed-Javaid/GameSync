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

    /// <summary>Null until the game has synced once.</summary>
    public GameStatus? Status { get; init; }

    public string? StatusDetail { get; init; }

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
    /// LIB-13: one of the person's own with no program on this PC, such as a server's world: nothing to play, and it syncs
    /// once quiet. Its spells of changes aren't play, so play time and the activity calendar leave them out.
    /// </summary>
    public bool IsFolder { get; init; }

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
public sealed record LauncherHome(
    LauncherGame? Hero,
    IReadOnlyList<LauncherGame> NeedsYou,
    IReadOnlyList<LauncherGame> JumpBackIn,
    IReadOnlyList<int> MonthDays,
    int MonthStartWeekday,
    string MonthName,
    int Synced,
    int Syncing);

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

        var first = new DateTime(nowLocal.Year, nowLocal.Month, 1);
        var days = new TimeSpan[DateTime.DaysInMonth(nowLocal.Year, nowLocal.Month)];
        foreach (var game in playable)
        {
            foreach (var session in state.GetSessions(game.Id))
            {
                var start = session.StartUtc.ToLocalTime();
                if (start >= first && start < first.AddMonths(1))
                {
                    days[start.Day - 1] += session.EndUtc - session.StartUtc;
                }
            }
        }

        return new LauncherHome(
            hero,
            games.Where(g => g.NeedsYou).Take(3).ToList(),
            jump,
            days.Select(ActivityLevel).ToList(),
            ((int)first.DayOfWeek + 6) % 7,
            first.ToString("MMMM", CultureInfo.InvariantCulture),
            games.Count(g => g.Syncs && g.Status is GameStatus.Synced or GameStatus.BackupOnly),
            games.Count(g => g.Syncs));
    }

    /// <summary>The setting that hides a game from the launcher on this PC.</summary>
    public static string HiddenKey(GameId game) => $"{HiddenPrefix}{game}";

    /// <summary>The setting that makes a game a favourite on this PC (LIB-17).</summary>
    public static string FavouriteKey(GameId game) => $"{FavouritePrefix}{game}";

    /// <summary>The setting that keeps the library's order on this PC (LIB-16).</summary>
    public const string SortKey = "library.sort";

    /// <summary>The setting that keeps the library's view on this PC: all games, installed, local and the rest (LIB-22).</summary>
    public const string ViewKey = "library.view";

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
            Status = gameState.Status,
            StatusDetail = gameState.Detail,
            Syncs = syncs,
            Installed = installed,
            Store = store,
            SteamAppId = steamId,
            Playtime = steam is null || steam.Playtime < sessionTime ? sessionTime : steam.Playtime,
            LastPlayedUtc = Latest(lastSession, steam?.LastPlayedUtc),
            // The person's own image first (ART-06), then Steam's.
            CoverPath = art?.FindOwn(id, ArtKind.Cover) ?? (steamId is { } a ? art?.Find(a, ArtKind.Cover) : null),
            HeroPath = art?.FindOwn(id, ArtKind.Hero) ?? (steamId is { } h ? art?.Find(h, ArtKind.Hero) : null),
            LogoPath = art?.FindOwn(id, ArtKind.Logo) ?? (steamId is { } l ? art?.Find(l, ArtKind.Logo) : null),
            IsSoftware = steamId is { } s && art?.IsSoftware(s) == true,
            IsHidden = marks.Hidden.ContainsKey(HiddenKey(id)),
            IsFavourite = marks.Favourites.ContainsKey(FavouriteKey(id)),
        };
    }

    private static DateTime? Latest(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : a > b ? a : b;
}
