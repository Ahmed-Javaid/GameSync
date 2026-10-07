using System.Globalization;
using System.Text.Json;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.State;

namespace GameSync.Host;

/// <summary>Where learn mode is for a game on this PC (FIND-04).</summary>
public enum LearnState
{
    /// <summary>It doesn't watch this game: saves were found another way, or the person turned it off.</summary>
    Off,

    /// <summary>It watches the next time the game is played.</summary>
    Waiting,

    /// <summary>The game is being played and learn mode is watching.</summary>
    Watching,

    /// <summary>It watched and found places, waiting for the person to pick.</summary>
    Found,

    /// <summary>It watched and saw nothing like a save; it watches again next time.</summary>
    NothingFound,

    /// <summary>Never for this game: it ships an anti-cheat (R15).</summary>
    AntiCheat,
}

/// <summary>Learn mode for one game, as its pages show it.</summary>
/// <param name="WatchingSinceUtc">While it watches: since when.</param>
/// <param name="Finds">What the last session it watched came to; null before any.</param>
public sealed record LearnView(LearnState State, DateTime? WatchingSinceUtc = null, LearnFinds? Finds = null)
{
    public static LearnView None { get; } = new(LearnState.Off);
}

/// <summary>One session learn mode is watching: the game, where it watches, and what it has heard.</summary>
internal sealed record LearnWatch(GameId Game, LearnScope Scope, LearnRecorder Recorder, DateTime StartUtc) : IDisposable
{
    public void Dispose() => Recorder.Dispose();
}

/// <summary>
/// FIND-04, learn mode's watcher (Milestone 6; design system version 43): for a game GameSync hasn't found saves for, the
/// next session is watched with no admin, the files written in the usual save folders, Steam's <c>userdata</c>, the
/// person's extra save folders and the game's own folder noted (<see cref="LearnRecorder"/>), and when it closes the places
/// they went are kept for the person to pick (<see cref="LearnPlaces"/>). Nothing syncs until they do. It's on by itself
/// for a game not syncing that nothing was found for, as first run promised ("watched the first time you play"); a game's
/// saves page turns it on or off (<c>learn.&lt;game&gt;</c>: <c>on</c> or <c>off</c>); a game with an anti-cheat never gets
/// it (R15). The admin tracer (FIND-05) comes later.
/// </summary>
public static class LearnMode
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The usual save folders learn mode watches, as name search looks in them; <c>&lt;documents&gt;</c> holds My Games.</summary>
    private static readonly string[] Usual = ["<documents>", "<savedGames>", "<roaming>", "<localAppData>", "<localLow>", "<programData>", "<publicDocuments>"];

    /// <summary><c>on</c>: watch its next session; <c>off</c>: never; none: on by itself while nothing is found for it.</summary>
    public static string Key(GameId game) => "learn." + game.Value;

    /// <summary>Since when it watches, while it does.</summary>
    public static string WatchingKey(GameId game) => "learn.watching." + game.Value;

    /// <summary>Its finds, kept on this PC until the person picks or turns learn mode off.</summary>
    public static string FindsFile(string dataDir, GameId game) => Path.Combine(dataDir, "learn", game.Value + ".json");

    /// <summary>Whether learn mode watches this game's next session.</summary>
    /// <param name="syncs">Its saves sync already: then only when it was turned on (a game whose saves went missing).</param>
    /// <param name="found">Places it found before, still waiting for the person: it doesn't watch again unless asked to.</param>
    internal static bool Wants(StateStore state, LibraryEntry? entry, bool syncs, bool found)
    {
        if (entry is null || entry.HasAntiCheat)
        {
            return false;
        }

        return state.GetSetting(Key(entry.Id)) switch
        {
            "on" => true,
            "off" => false,
            _ => !syncs && entry.Proposals.Count == 0 && entry.RegistryProposals.Count == 0 && !found,
        };
    }

    /// <summary>Learn mode for a game, for its pages.</summary>
    public static LearnView View(string dataDir, StateStore state, LibraryEntry? entry, bool syncs)
    {
        if (entry is null)
        {
            return LearnView.None;
        }

        if (entry.HasAntiCheat)
        {
            return new LearnView(LearnState.AntiCheat);
        }

        // Steam's software (Wallpaper Engine) is never taken for a game, so it's never watched (PLAY-12).
        if (IsSoftware(dataDir, entry))
        {
            return LearnView.None;
        }

        var finds = ReadFinds(dataDir, entry.Id);
        if (DateTime.TryParse(state.GetSetting(WatchingKey(entry.Id)), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var since))
        {
            return new LearnView(LearnState.Watching, since, finds);
        }

        // Places found wait for the person, whether or not Watch again asked for the next session too.
        var found = finds is { Places.Count: > 0 };
        if (found && state.GetSetting(Key(entry.Id)) != "off")
        {
            return new LearnView(LearnState.Found, null, finds);
        }

        if (Wants(state, entry, syncs, found))
        {
            return new LearnView(finds is null ? LearnState.Waiting : LearnState.NothingFound, null, finds);
        }

        return new LearnView(LearnState.Off, null, finds);
    }

    private static bool IsSoftware(string dataDir, LibraryEntry entry)
    {
        if (entry.Store != StoreKind.Steam || !long.TryParse(entry.StoreId, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out var app))
        {
            return false;
        }

        using var art = new GameSync.Core.Art.ArtCache(dataDir);
        return art.IsSoftware(app);
    }

    public static LearnFinds? ReadFinds(string dataDir, GameId game)
    {
        try
        {
            var file = FindsFile(dataDir, game);
            return File.Exists(file) ? JsonSerializer.Deserialize<LearnFinds>(File.ReadAllText(file), Json) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Turns learn mode on for the game's next session (Watch again; Turn on learn mode), or off, which forgets what it
    /// found (Not these; Turn off learn mode). Either way the game's saves are where Add a place puts them.
    /// </summary>
    public static void Set(string dataDir, GameId game, bool on)
    {
        using var state = new StateStore(dataDir);
        state.SetSetting(Key(game), on ? "on" : "off");
        if (!on)
        {
            Forget(dataDir, game);
        }

        state.Log(game, "info", on ? "Learn mode watches the next time it's played." : "Learn mode is off for it.", EventTags.Missing);
    }

    /// <summary>The finds are dropped: the person picked, or said not these.</summary>
    private static void Forget(string dataDir, GameId game)
    {
        try
        {
            File.Delete(FindsFile(dataDir, game));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left on disk, the finds are read again and offered again: nothing is lost.
        }
    }

    /// <summary>
    /// A session starts: if learn mode wants it, the watching starts now, over the folders saves usually live in, Steam's
    /// <c>userdata</c>, the extra save folders and the game's own folder. Null when it doesn't.
    /// </summary>
    internal static LearnWatch? Begin(string dataDir, GameId game, string? installDir, DateTime startUtc)
    {
        using var engine = Engine.Open(dataDir);
        var entry = engine.Library.All().FirstOrDefault(e => e.Id == game && e.MergedInto is null);
        var syncs = engine.Games.Any(g => g.Id == game);
        if (!Wants(engine.State, entry, syncs, ReadFinds(dataDir, game) is { Places.Count: > 0 }))
        {
            return null;
        }

        var scope = ScopeOf(engine, installDir ?? entry!.InstallDir);
        var recorder = new LearnRecorder(scope.Roots.Select(r => r.Path), path => LearnPlaces.SkipAtOnce(path, scope));
        engine.State.SetSetting(WatchingKey(game), startUtc.ToString("O", CultureInfo.InvariantCulture));
        engine.State.Log(game, "info", $"Learn mode is watching where it saves, in {recorder.Watching} folders.", EventTags.Session);
        return new LearnWatch(game, scope, recorder, startUtc);
    }

    /// <summary>
    /// The session ended: what it wrote becomes places, added to any it found before (Watch again), and kept for the person.
    /// Learn mode asked for once (<c>on</c>) goes back to its own way after: it keeps watching only while nothing is found.
    /// </summary>
    internal static LearnFinds Finish(string dataDir, LearnWatch watch, SessionInfo session)
    {
        watch.Recorder.Dispose();
        var finds = LearnPlaces.From(watch.Recorder.Written, watch.Scope, session.StartUtc, session.EndUtc, watch.Recorder.Overflowed);
        if (ReadFinds(dataDir, watch.Game) is { } earlier)
        {
            finds = LearnPlaces.Merge(earlier, finds);
        }

        var file = FindsFile(dataDir, watch.Game);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(finds, Json));
        using var state = new StateStore(dataDir);
        state.SetSetting(WatchingKey(watch.Game), "");
        if (state.GetSetting(Key(watch.Game)) == "on")
        {
            state.SetSetting(Key(watch.Game), "");
        }

        state.Log(watch.Game, "info", finds.Places.Count == 0
            ? "Learn mode saw nothing written that looks like a save; it watches again next time."
            : $"Learn mode found {Count(finds.Places.Count, "place")} it saved to: {string.Join(", ", finds.Places.Take(3).Select(p => p.Portable))}.", EventTags.Missing);
        return finds;
    }

    /// <summary>The session can't finish (GameSync is stopping): the watching ends and nothing is kept, so the next session is watched again.</summary>
    internal static void Abandon(string dataDir, LearnWatch watch)
    {
        watch.Recorder.Dispose();
        using var state = new StateStore(dataDir);
        state.SetSetting(WatchingKey(watch.Game), "");
    }

    /// <summary>
    /// Sync these saves: each place picked is added as Add a place adds one (FOLD-01), so the game starts syncing with the
    /// first and takes the others after; then the finds are dropped and learn mode goes back to its own way. Returns what
    /// it came to, in words.
    /// </summary>
    public static async Task<string> AddAsync(string dataDir, GameId game, IReadOnlyList<string> paths, Action waiting, CancellationToken ct)
    {
        if (paths.Count == 0)
        {
            throw new UsageException("Pick a place first.");
        }

        var said = new List<string>();
        foreach (var path in paths)
        {
            said.Add(await GameSettings.ApplyAsync(dataDir, game, new GamePropertiesChange { AddPlace = path, AddPlaceCategory = SaveCategory.Save }, waiting, ct));
        }

        Forget(dataDir, game);
        using (var state = new StateStore(dataDir))
        {
            state.SetSetting(Key(game), "");
        }

        return string.Join(" ", said.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
    }

    /// <summary>Where learn mode watches on this PC, and what it leaves out.</summary>
    internal static LearnScope ScopeOf(Engine engine, string? installDir)
    {
        var folders = new Dictionary<string, string>(engine.Here.Folders, StringComparer.OrdinalIgnoreCase);
        var roots = new List<LearnRoot>();
        void Add(string? path, string portable, int depth = 1, string? tag = null)
        {
            if (path is { Length: > 0 } && Directory.Exists(path) && roots.All(r => !string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                roots.Add(new LearnRoot(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), portable, depth, tag));
            }
        }

        foreach (var usual in Usual)
        {
            Add(folders.GetValueOrDefault(usual), usual);
        }

        if (folders.GetValueOrDefault("<steamRoot>") is { } steam)
        {
            Add(Path.Combine(steam, "userdata"), "<steamRoot>/userdata", 2, LearnPlaces.SteamCloudTag);
        }

        var discoverer = new Discoverer(null, engine.Here.Folders);
        foreach (var extra in Cli.SaveFolders(engine.State))
        {
            if (discoverer.Resolve(extra.Path, "") is { } path)
            {
                Add(path, extra.Path);
            }
        }

        if (installDir is { Length: > 0 } && Directory.Exists(installDir))
        {
            Add(installDir, "<installDir>", 1, LearnPlaces.OwnFolderTag);
            folders["<installDir>"] = installDir;
        }

        var leftOut = new[] { engine.DataDir, Cli.HistoryFolder(engine.State, engine.DataDir) }.Where(f => !string.IsNullOrEmpty(f)).ToList();
        return new LearnScope(roots, folders, leftOut, engine.Here.Guard);
    }

    private static string Count(int n, string word) => $"{n.ToString(CultureInfo.InvariantCulture)} {word}{(n == 1 ? "" : "s")}";
}
