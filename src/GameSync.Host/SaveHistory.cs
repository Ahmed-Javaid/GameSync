using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;

namespace GameSync.Host;

/// <summary>Why a version was kept, as the Versions tab's icon shows it (MGR-08).</summary>
public enum KeptFor
{
    /// <summary>Synced after the game was played.</summary>
    Play,

    /// <summary>A folder of the person's own, synced once it was quiet for 5 minutes (LIB-13).</summary>
    Quiet,

    /// <summary>Made by the daily backup.</summary>
    Daily,

    /// <summary>The game's first backup.</summary>
    First,

    /// <summary>Changed while the game wasn't running, and held for the person (BAK-11).</summary>
    Held,

    /// <summary>Pinned: a named save, the save that lost a conflict, one kept before an update or a restore.</summary>
    Kept,

    /// <summary>The save a restore made current.</summary>
    Restored,

    /// <summary>Anything else: a conflict chosen by hand, a backup by hand, a save backed up again.</summary>
    Other,
}

/// <summary>A version of any game, from any PC, as the Versions tab lists it (MGR-08).</summary>
/// <param name="SavedUtc">When the save itself was made: its newest file, as the tab shows and orders them.</param>
/// <param name="CreatedUtc">When GameSync kept it, for the tab's This week.</param>
/// <param name="Why">Why it was kept, in the table's words: "After play · 3 h 10 min", "Named: Before Erlang Shen".</param>
/// <param name="Kept">Pinned, so it stays whatever is thinned: a named save, a conflict's other side, one kept before an update or a restore.</param>
public sealed record SavedVersion(GameId Game, string Title, VersionId Id, DateTime SavedUtc, DateTime CreatedUtc, string Pc, string Why, KeptFor For,
    bool Named, bool Kept, int Files, long Bytes);

/// <summary>A game the Versions tab can be filtered to: every game that syncs or has versions kept.</summary>
public sealed record HistoryGame(GameId Id, string Title);

/// <summary>Every version of every game from every PC, newest first, and what the tab says about keeping them (MGR-08).</summary>
/// <param name="Games">Every game that syncs or has versions kept, by name, for the tab's game filter.</param>
/// <param name="ThisPc">This PC's name.</param>
/// <param name="Pcs">Every PC a version came from, by name.</param>
/// <param name="KeptOnPc">How many of each game's newest versions keep their files on this PC; null when all of them do (BAK-17).</param>
/// <param name="HasCloud">A cloud is connected; without one (Skip for now), every version waits on this PC (ONB-06).</param>
public sealed record VersionsView(IReadOnlyList<SavedVersion> Versions, IReadOnlyList<HistoryGame> Games, string ThisPc, IReadOnlyList<string> Pcs,
    int? KeptOnPc, bool HasCloud);

/// <summary>A line of GameSync's activity log, with its game's name, as the Log tab lists it (MGR-09).</summary>
/// <param name="Tag">What it was about (<see cref="EventTags"/>); null in lines logged before tags were kept.</param>
public sealed record LogEntry(DateTime AtUtc, GameId Game, string Title, string Level, string? Tag, string Message);

/// <summary>
/// The save manager's Versions and Log tabs (MGR-08, MGR-09): every version of every game from every PC, read from the
/// backup folder's copy of the records, and everything GameSync did, from this PC's activity log, kept for good. Both
/// are read from this PC alone, so they open at once, offline too; the agent brings other PCs' records down every 15 minutes.
/// </summary>
public static class SaveHistory
{
    public static async Task<VersionsView> ReadVersionsAsync(string dataDir, CancellationToken ct)
    {
        using var engine = Engine.Open(dataDir);
        var history = new LocalHistory(Cli.HistoryFolder(engine.State, dataDir));
        var pcs = history.LoadDevices().ToDictionary(d => d.Id, d => d.Name);
        var titles = Titles(engine);
        var folders = engine.Library.All().Where(e => e.IsOwnFolder).Select(e => e.Id).ToHashSet();
        var versions = new List<SavedVersion>();
        var games = history.Games().Concat(engine.Games.Select(g => g.Id)).Distinct().ToList();
        foreach (var game in games)
        {
            ct.ThrowIfCancellationRequested();
            var thinned = await history.Log.ListThinnedAsync(game, ct);
            var records = (await history.Log.ListAsync(game, ct)).Where(v => !thinned.Contains(v.Id)).ToList();
            var pins = (await history.Log.ListPinsAsync(game, ct))
                .GroupBy(p => p.Version)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedUtc).First());
            versions.AddRange(Describe(game, titles.GetValueOrDefault(game, game.Value), records, pins, pcs, folders.Contains(game)));
        }

        var limits = Cli.KeepLimits(engine.State);
        return new VersionsView(
            // Newest save first, by the time each row shows: a save named or imported later sits where its own time puts it.
            versions.OrderByDescending(v => v.SavedUtc).ThenByDescending(v => v.CreatedUtc).ThenByDescending(v => v.Id).ToList(),
            games.Select(g => new HistoryGame(g, titles.GetValueOrDefault(g, g.Value))).OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
            engine.Device.Name,
            versions.Select(v => v.Pc).Append(engine.Device.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(),
            limits.VersionsPerGame == int.MaxValue ? null : limits.VersionsPerGame,
            !engine.Config.HasNoCloud);
    }

    /// <summary>Everything in this PC's activity log, newest first, each line with its game's name.</summary>
    public static IReadOnlyList<LogEntry> ReadLog(string dataDir)
    {
        using var engine = Engine.Open(dataDir);
        var titles = Titles(engine);
        return engine.State.GetEvents(null, int.MaxValue)
            .Select(e => new LogEntry(e.AtUtc, e.Game, titles.GetValueOrDefault(e.Game, e.Game.Value), e.Level, e.Tag, e.Message))
            .ToList();
    }

    /// <summary>A game's versions as the tab lists them, apart from where they're read, so every line of it is tested.</summary>
    /// <param name="pins">Each version's newest pin.</param>
    /// <param name="isFolder">A folder of the person's own with no program (LIB-13): its sessions are quiet spells, not play.</param>
    internal static IEnumerable<SavedVersion> Describe(GameId game, string title, IReadOnlyList<VersionRecord> versions,
        IReadOnlyDictionary<VersionId, PinRecord> pins, IReadOnlyDictionary<DeviceId, string> pcs, bool isFolder) =>
        versions.Select(v =>
        {
            var pin = pins.GetValueOrDefault(v.Id);
            var (why, kind) = Why(v, pin, isFolder);
            return new SavedVersion(game, title, v.Id, GameDetails.SavedAt(v), v.CreatedUtc, pcs.GetValueOrDefault(v.Device.Id, v.Device.Name), why, kind,
                pin?.Named == true, v.Pinned || pin is not null, v.Files.Count, FileSet.TotalSize(v.Files));
        });

    /// <summary>
    /// Why a version was kept, in the Versions tab's words: its name, the note it was kept with ("LAPTOP's save, replaced
    /// by DESKTOP's in a conflict"), or how it came about (after play and for how long, the daily backup, the first backup).
    /// </summary>
    internal static (string Why, KeptFor For) Why(VersionRecord version, PinRecord? pin, bool isFolder)
    {
        if (pin is { Named: true })
        {
            return ($"Named: {pin.Label}", KeptFor.Kept);
        }

        if (version.Kind == VersionKind.Held)
        {
            return ("Changed outside play, held for review", KeptFor.Held);
        }

        // The note a kept save was made or pinned with says the most: whose save it was, and what replaced it.
        var note = pin?.Label ?? version.Label;
        if (pin is not null || version.Kind == VersionKind.Kept || version.Pinned)
        {
            return (note is null ? version.Origin switch
            {
                VersionOrigin.KeptBeforeRestore => "Kept before a restore",
                VersionOrigin.KeptAtFirstSync => "Kept before this PC's first sync",
                VersionOrigin.BeforeUpdate => "Kept before a game update",
                VersionOrigin.Imported => "Imported",
                _ => "Lost a conflict, kept",
            } : note.StartsWith("before ", StringComparison.Ordinal) ? $"Kept {note}" : Sentence(note), KeptFor.Kept);
        }

        if (note == Daily.VersionNote)
        {
            return (note, KeptFor.Daily);
        }

        return version.Origin switch
        {
            VersionOrigin.FirstBackup => ("First backup", KeptFor.First),
            VersionOrigin.Session when isFolder => ("After 5 quiet minutes", KeptFor.Quiet),
            VersionOrigin.Session => (version.Session is { } s ? $"After play · {Launcher.DurationText(s.EndUtc - s.StartUtc)}" : "After play", KeptFor.Play),
            VersionOrigin.Restore => (note is null ? "Restored from history" : Sentence(note), KeptFor.Restored),
            VersionOrigin.Approved => ("Held, then kept by you", KeptFor.Other),
            VersionOrigin.Resolve => (note is null ? "Chosen in a conflict" : Sentence(note), KeptFor.Other),
            VersionOrigin.Reupload => ("Backed up again", KeptFor.Other),
            VersionOrigin.Manual => ("Backed up by hand", KeptFor.Other),
            VersionOrigin.OutOfSession => ("Changed outside play", KeptFor.Other),
            _ => (note is null ? "Backed up" : Sentence(note), KeptFor.Other),
        };
    }

    /// <summary>Every game's name this PC knows: the ones that sync, then the library's.</summary>
    private static Dictionary<GameId, string> Titles(Engine engine)
    {
        var titles = new Dictionary<GameId, string>();
        foreach (var game in engine.Games)
        {
            titles.TryAdd(game.Id, game.Title);
        }

        foreach (var entry in engine.Library.All().Where(e => e.MergedInto is null))
        {
            titles.TryAdd(entry.Id, entry.DisplayTitle);
        }

        return titles;
    }

    private static string Sentence(string note) => note.Length == 0 ? note : char.ToUpperInvariant(note[0]) + note[1..];
}
