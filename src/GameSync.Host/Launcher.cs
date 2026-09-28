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

    /// <summary>What Home and the library's game tabs show: games the person hasn't hidden.</summary>
    public bool Shown => !IsSoftware && !IsHidden;

    /// <summary>A status that waits for the person (a conflict, a review, a missing folder, a blocked file).</summary>
    public bool NeedsYou => SyncCounts.NeedsYou(Status);
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
    public static IReadOnlyList<LauncherGame> Games(
        IEnumerable<LibraryEntry> library,
        IEnumerable<(GameId Id, string Title)> handAdded,
        StateStore state,
        SaveList? saveList,
        IReadOnlyDictionary<long, SteamPlay> steamPlay,
        ArtCache? art)
    {
        var games = new List<LauncherGame>();
        foreach (var entry in library.Where(e => e.State != LibraryState.Ignored && e.MergedInto is null))
        {
            var steamId = SteamIdOf(entry, saveList);
            games.Add(Describe(entry.Id, entry.DisplayTitle, entry.State == LibraryState.Synced, entry.Installed, entry.Store, steamId, state, steamPlay, art));
        }

        foreach (var (id, title) in handAdded.Where(g => games.All(x => x.Id != g.Id)))
        {
            var steamId = saveList?.ByTitle(title)?.SteamIds.FirstOrDefault() is > 0 and var listed ? listed : (long?)null;
            games.Add(Describe(id, title, syncs: true, installed: true, store: null, steamId, state, steamPlay, art));
        }

        return games
            .OrderByDescending(g => g.LastPlayedUtc ?? DateTime.MinValue)
            .ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The home screen: the most recently played installed game as the hero, up to three that need you, the next three
    /// played, and this month's play per day from GameSync's sessions (Steam keeps no daily record).
    /// </summary>
    public static LauncherHome Home(IReadOnlyList<LauncherGame> all, StateStore state, DateTime nowLocal)
    {
        // The home is about games; software such as Wallpaper Engine stays in the library's Software tab, and hidden games in Hidden.
        var games = all.Where(g => g.Shown).ToList();
        var played = games.Where(g => g.LastPlayedUtc is not null).ToList();
        var hero = played.FirstOrDefault(g => g.Installed) ?? games.FirstOrDefault(g => g.Installed);
        var jump = played.Where(g => g != hero).Take(3).ToList();
        if (jump.Count < 3)
        {
            jump.AddRange(games.Where(g => g != hero && !jump.Contains(g)).Take(3 - jump.Count));
        }

        var first = new DateTime(nowLocal.Year, nowLocal.Month, 1);
        var days = new TimeSpan[DateTime.DaysInMonth(nowLocal.Year, nowLocal.Month)];
        foreach (var game in games)
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
    public static string HiddenKey(GameId game) => $"hidden.{game}";

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
        string.Join(" · ", new[] { PlaytimeText(game.Playtime), game.Status == GameStatus.Playing ? "Now" : WhenText(game.LastPlayedUtc, nowLocal) }
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

    private static LauncherGame Describe(GameId id, string title, bool syncs, bool installed, StoreKind? store, long? steamId,
        StateStore state, IReadOnlyDictionary<long, SteamPlay> steamPlay, ArtCache? art)
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
            CoverPath = steamId is { } a ? art?.Find(a, ArtKind.Cover) : null,
            HeroPath = steamId is { } h ? art?.Find(h, ArtKind.Hero) : null,
            LogoPath = steamId is { } l ? art?.Find(l, ArtKind.Logo) : null,
            IsSoftware = steamId is { } s && art?.IsSoftware(s) == true,
            IsHidden = state.GetSetting(HiddenKey(id)) is { Length: > 0 },
        };
    }

    private static DateTime? Latest(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : a > b ? a : b;
}
