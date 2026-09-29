using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;

namespace GameSync.Host;

/// <summary>One PC's side of a conflict, as the conflict screen shows it (SYNC-10): its save since the last sync.</summary>
public sealed record ConflictSide
{
    /// <summary>The PC that made it.</summary>
    public required string Pc { get; init; }

    /// <summary>This PC's own copy: keeping it uploads this PC's files, while keeping any other side downloads it.</summary>
    public bool IsThisPc { get; init; }

    /// <summary>The version that holds it: another PC's upload, or the copy this PC kept while the conflict waits.</summary>
    public required VersionId Version { get; init; }

    /// <summary>When its newest change since the last sync was written.</summary>
    public DateTime SavedUtc { get; init; }

    /// <summary>The play session that made it, when one is known.</summary>
    public SessionInfo? Session { get; init; }

    /// <summary>Play sessions on its PC since the last sync, and how long they took together.</summary>
    public int Sessions { get; init; }

    public TimeSpan Played { get; init; }

    public long Bytes { get; init; }

    public int Files { get; init; }

    /// <summary>The files it changed, added or removed since the last sync, by name.</summary>
    public IReadOnlyList<string> Changed { get; init; } = [];

    /// <summary>It lost more than half of its files or size since the last sync, which looks like a reset (SYNC-06).</summary>
    public bool LostHalf { get; init; }

    /// <summary>Changed while the game wasn't running on this PC (SYNC-07); only this PC's own sessions are known.</summary>
    public bool OutsidePlay { get; init; }

    public bool Newest { get; init; }

    /// <summary>The side the screen suggests keeping.</summary>
    public bool Suggested { get; init; }
}

/// <summary>A file that differs between the two sides compared: each side's copy, and what happened to it since the last sync.</summary>
public sealed record ConflictFile(string Path, FileEntry? Here, FileEntry? There, string What)
{
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];
}

/// <summary>
/// A game's conflict, read from this PC alone (the backup folder's copy of the version records and pins, and this PC's
/// sessions and status), so the screen opens at once, offline too. Waiting: the sides to choose between. Otherwise the
/// last conflict GameSync settled, while its winner is still the current save, which Swap reverses (SYNC-04).
/// </summary>
public sealed record ConflictDetail
{
    public required GameId Id { get; init; }

    public required string Title { get; init; }

    /// <summary>It waits for the person; nothing uploads or downloads until they choose (SYNC-11).</summary>
    public bool Waiting { get; init; }

    /// <summary>This PC's side first, then the others, newest first.</summary>
    public IReadOnlyList<ConflictSide> Sides { get; init; } = [];

    /// <summary>Why GameSync asked instead of choosing, as the engine said it when the conflict arose.</summary>
    public string? Reason { get; init; }

    /// <summary>When this PC last agreed with the cloud: the start both sides changed from.</summary>
    public DateTime? LastSyncUtc { get; init; }

    /// <summary>The files that differ between this PC's side and the other one compared.</summary>
    public IReadOnlyList<ConflictFile> Files { get; init; } = [];

    /// <summary>Settled: the PC whose save is current and the one whose save is pinned, which Swap switches to.</summary>
    public string? KeptPc { get; init; }

    public string? PinnedPc { get; init; }

    /// <summary>Settled by the person rather than by newest wins.</summary>
    public bool ByHand { get; init; }

    public ConflictSide? Kept => Waiting ? null : Sides.FirstOrDefault(s => s.Pc == KeptPc);

    public ConflictSide? Pinned => Waiting ? null : Sides.FirstOrDefault(s => s.Pc == PinnedPc && s.Pc != KeptPc) ?? Sides.Skip(1).FirstOrDefault();
}

/// <summary>Reads a game's conflict from a data folder.</summary>
public static class ConflictDetails
{
    /// <summary>A settled conflict stays on offer for Swap while no newer save replaced its winner.</summary>
    private static readonly TimeSpan SettledSlack = TimeSpan.FromMinutes(2);

    /// <summary>Null when the game has no conflict waiting, and none settled since its current save.</summary>
    public static async Task<ConflictDetail?> ReadAsync(string dataDir, GameId game, CancellationToken ct)
    {
        using var engine = Engine.Open(dataDir);
        if (engine.Games.FirstOrDefault(g => g.Id == game) is not { } definition)
        {
            return null;
        }

        var history = new LocalHistory(Cli.HistoryFolder(engine.State, dataDir));
        var thinned = await history.Log.ListThinnedAsync(game, ct);
        var versions = (await history.Log.ListAsync(game, ct)).Where(v => !thinned.Contains(v.Id)).ToList();
        var pins = await history.Log.ListPinsAsync(game, ct);
        var pcs = history.LoadDevices().ToDictionary(d => d.Id, d => d.Name);
        return Describe(definition, versions, pins, engine.State.GetState(game), engine.State.GetSessions(game), engine.Device, pcs);
    }

    /// <param name="sessions">This PC's play sessions of the game.</param>
    /// <param name="pcs">The PCs' names as they are now, which may differ from the names in older records.</param>
    internal static ConflictDetail? Describe(GameDefinition game, IReadOnlyList<VersionRecord> versions, IReadOnlyList<PinRecord> pins, GameState state,
        IReadOnlyList<SessionInfo> sessions, DeviceInfo here, IReadOnlyDictionary<DeviceId, string> pcs)
    {
        var heads = VersionGraph.Heads(versions);
        return state.Status == GameStatus.Conflict
            ? Waiting(game, versions, heads, state, sessions, here, pcs)
            : Settled(game, versions, pins, heads, sessions, here, pcs);
    }

    private static ConflictDetail Waiting(GameDefinition game, IReadOnlyList<VersionRecord> versions, IReadOnlyList<VersionRecord> heads, GameState state,
        IReadOnlyList<SessionInfo> sessions, DeviceInfo here, IReadOnlyDictionary<DeviceId, string> pcs)
    {
        var basis = state.Base;
        var reason = DecisionEngine.WaitingReason(state.Detail);

        // Each PC's current upload is a side; this PC's own, when it has none of those, is the copy kept while the
        // conflict waits (or held before it arose), which is what this PC's files were at its last sync.
        var chosen = heads.ToList();
        if (!chosen.Any(h => h.Device.Id == here.Id) &&
            versions.Where(v => v.Device.Id == here.Id && v.Origin is VersionOrigin.KeptInConflict or VersionOrigin.OutOfSession &&
                    (basis is null || v.CreatedUtc >= basis.CreatedUtc))
                .MaxBy(v => v.CreatedUtc) is { } mine)
        {
            chosen.Add(mine);
        }

        var sides = chosen
            .Select(v => Side(v, basis, versions, sessions, here, pcs))
            .OrderByDescending(s => s.IsThisPc)
            .ThenByDescending(s => s.SavedUtc)
            .ToList();
        sides = Mark(sides, reason);
        var thisPc = sides.FirstOrDefault(s => s.IsThisPc);
        var other = sides.FirstOrDefault(s => !s.IsThisPc && s.Suggested) ?? sides.FirstOrDefault(s => !s.IsThisPc);
        return new ConflictDetail
        {
            Id = game.Id,
            Title = game.Title,
            Waiting = true,
            Sides = sides,
            Reason = reason,
            LastSyncUtc = basis?.CreatedUtc,
            Files = thisPc is null || other is null ? [] : Compare(Find(versions, thisPc.Version), Find(versions, other.Version), basis, thisPc.Pc, other.Pc),
        };
    }

    /// <summary>
    /// The conflict GameSync settled last, while its winner is still the current save: the save that lost is the newest
    /// one kept from a conflict or pinned as its losing side, as Swap finds it.
    /// </summary>
    private static ConflictDetail? Settled(GameDefinition game, IReadOnlyList<VersionRecord> versions, IReadOnlyList<PinRecord> pins,
        IReadOnlyList<VersionRecord> heads, IReadOnlyList<SessionInfo> sessions, DeviceInfo here, IReadOnlyDictionary<DeviceId, string> pcs)
    {
        var byId = versions.ToDictionary(v => v.Id);
        var lost = versions
            .Where(v => v.Origin == VersionOrigin.KeptInConflict)
            .Select(v => (Version: v, When: v.CreatedUtc, Label: v.Label))
            .Concat(pins.Where(p => p.FromConflict && byId.ContainsKey(p.Version)).Select(p => (Version: byId[p.Version], When: p.CreatedUtc, Label: (string?)p.Label)))
            .OrderByDescending(x => x.When)
            .FirstOrDefault();
        // Gone once a newer save replaced the winner, or a restore did (Swap is one): the history has both then.
        if (lost.Version is null || heads.FirstOrDefault() is not { } current || current.CreatedUtc > lost.When + SettledSlack || current.Id == lost.Version.Id ||
            current.Origin == VersionOrigin.Restore)
        {
            return null;
        }

        // Both sides against where they started from: the save the losing side changed from.
        var start = lost.Version.Parent is { } parent ? byId.GetValueOrDefault(parent) : null;
        var kept = Side(current, start, versions, sessions, here, pcs) with { Suggested = true };
        var pinned = Side(lost.Version, start, versions, sessions, here, pcs);
        var newest = kept.SavedUtc >= pinned.SavedUtc ? kept : pinned;
        return new ConflictDetail
        {
            Id = game.Id,
            Title = game.Title,
            Sides = [kept with { Newest = newest == kept }, pinned with { Newest = newest == pinned }],
            LastSyncUtc = start?.CreatedUtc,
            KeptPc = kept.Pc,
            PinnedPc = pinned.Pc,
            ByHand = current.Origin == VersionOrigin.Resolve || lost.Label?.Contains("not chosen", StringComparison.Ordinal) == true,
            Files = Compare(current, lost.Version, start, kept.Pc, pinned.Pc),
        };
    }

    private static ConflictSide Side(VersionRecord version, VersionRecord? basis, IReadOnlyList<VersionRecord> versions, IReadOnlyList<SessionInfo> sessions,
        DeviceInfo here, IReadOnlyDictionary<DeviceId, string> pcs)
    {
        IReadOnlyList<FileEntry> before = basis?.Files ?? [];
        var files = version.Files;
        var since = basis?.CreatedUtc ?? DateTime.MinValue;
        var mine = version.Device.Id == here.Id;

        // This PC knows its own sessions; another PC's come with the versions it uploaded since the last sync, each of
        // which records that PC's latest session, so only the ones that ended since then count.
        var played = (mine
                ? sessions
                : versions.Where(v => v.Device.Id == version.Device.Id && v.CreatedUtc > since && v.Session is not null).Select(v => v.Session!))
            .Where(s => s.EndUtc >= since)
            .Distinct()
            .OrderBy(s => s.StartUtc)
            .ToList();
        var changed = FileSet.ChangedOrAdded(files, before);
        var removed = FileSet.Removed(files, before);
        return new ConflictSide
        {
            Pc = pcs.GetValueOrDefault(version.Device.Id) ?? version.Device.Name,
            IsThisPc = mine,
            Version = version.Id,
            SavedUtc = FileSet.NewestChange(files, before) ?? (files.Count > 0 ? files.Max(f => f.ModifiedUtc) : version.CreatedUtc),
            Session = played.LastOrDefault(),
            Sessions = played.Count,
            Played = played.Aggregate(TimeSpan.Zero, (sum, s) => sum + (s.EndUtc - s.StartUtc)),
            Bytes = FileSet.TotalSize(files),
            Files = files.Count,
            Changed = changed.Concat(removed).Select(f => NameOf(f.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            LostHalf = files.Count == 0 || (before.Count > 0 && (files.Count * 2 < before.Count || FileSet.TotalSize(files) * 2 < FileSet.TotalSize(before))),
            OutsidePlay = mine && changed.Any(f => !sessions.Any(s => s.Covers(f.ModifiedUtc, DecisionEngine.SessionSlack))),
        };
    }

    /// <summary>
    /// Marks the newest side and the one to suggest: the newest, unless it lost more than half of its files or size
    /// (SYNC-06), or it's this PC's and changed while the game wasn't running (SYNC-07); with this PC's clock off, the
    /// times can't be trusted, so the one played longer (SYNC-08).
    /// </summary>
    private static List<ConflictSide> Mark(List<ConflictSide> sides, string? reason)
    {
        if (sides.Count == 0)
        {
            return sides;
        }

        var newest = sides.MaxBy(s => s.SavedUtc)!;
        var fine = sides.Where(s => !s.LostHalf && !s.OutsidePlay).ToList();
        var clockOff = reason?.Contains("'s clock is", StringComparison.Ordinal) == true;
        var suggested = fine.Count == 0 ? newest
            : clockOff ? fine.MaxBy(s => s.Played)!
            : fine.MaxBy(s => s.SavedUtc)!;
        return sides.Select(s => s with { Newest = s == newest, Suggested = s == suggested }).ToList();
    }

    /// <summary>The files that differ between two sides, with what happened to each since <paramref name="basis"/>.</summary>
    private static List<ConflictFile> Compare(VersionRecord? here, VersionRecord? there, VersionRecord? basis, string herePc, string therePc)
    {
        if (here is null || there is null)
        {
            return [];
        }

        var ours = here.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var theirs = there.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var before = (basis?.Files ?? []).ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        return ours.Keys.Union(theirs.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(path => (Path: path, Here: ours.GetValueOrDefault(path), There: theirs.GetValueOrDefault(path), Before: before.GetValueOrDefault(path)))
            .Where(f => f.Here?.Hash != f.There?.Hash)
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => new ConflictFile(f.Path, f.Here, f.There, What(f.Here, f.There, f.Before, herePc, therePc)))
            .ToList();
    }

    private static string What(FileEntry? here, FileEntry? there, FileEntry? before, string herePc, string therePc)
    {
        if (here is null)
        {
            return before is null ? $"Only on {therePc}" : $"Removed on {herePc}";
        }

        if (there is null)
        {
            return before is null ? $"Only on {herePc}" : $"Removed on {therePc}";
        }

        var hereChanged = before is null || here.Hash != before.Hash;
        var thereChanged = before is null || there.Hash != before.Hash;
        return hereChanged && thereChanged ? "Changed on both PCs" : hereChanged ? $"Changed on {herePc}" : $"Changed on {therePc}";
    }

    private static VersionRecord? Find(IReadOnlyList<VersionRecord> versions, VersionId id) => versions.FirstOrDefault(v => v.Id == id);

    private static string NameOf(string path) => path[(path.LastIndexOf('/') + 1)..];
}
