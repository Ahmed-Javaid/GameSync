using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;

namespace GameSync.Core.Sync;

/// <summary>
/// What gets synced as one unit: a game's saves, or one PC's own settings and screenshots for that game, which are
/// backed up per PC and never land on another PC by themselves (FIND-09).
/// </summary>
public sealed record SyncStream(GameDefinition Definition, GameDefinition Game, IReadOnlySet<SaveCategory> Categories, bool PerDevice)
{
    public GameId Id => Definition.Id;

    public bool Includes(SaveCategory category) => Categories.Contains(category);
}

/// <summary>What this run knows about the cloud: whether it could be reached, and how far this PC's clock is off.</summary>
public sealed record CloudView(CloudException? Problem, TimeSpan? ClockSkew)
{
    public static CloudView Reachable { get; } = new(null, null);
}

/// <summary>What one game's next sync would do and why, computed without acting (SYNC-14).</summary>
public sealed record GamePlan
{
    public required SyncStream Stream { get; init; }

    public SyncDecision? Decision { get; init; }

    public IReadOnlyList<FileEntry> Local { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public VersionRecord? Base { get; init; }

    public IReadOnlyList<VersionRecord> Versions { get; init; } = [];

    public bool TreatAsInSession { get; init; }

    /// <summary>
    /// KAN-91: planned for a person's Back up now while the game runs: its files are read now, as a named save's are, and
    /// what changed counts as made while playing; its upload waits until the game closes (BG-08).
    /// </summary>
    public bool WhilePlaying { get; init; }

    public CloudView Cloud { get; init; } = CloudView.Reachable;

    /// <summary>Identifies the inputs, so running the plan can tell whether anything changed since it was made.</summary>
    public string Fingerprint { get; init; } = "";

    /// <summary>Set when planning itself failed, for example because a save file is locked.</summary>
    public Exception? Error { get; init; }

    public string Title => Stream.Definition.Title;
}

/// <summary>
/// How far a job on this PC is (KAN-80): its step ("reading" the copies kept by hand, then "keeping" them), how many of
/// how many, and their bytes when they're known.
/// </summary>
public sealed record WorkProgress(string Step, int Done, int Total, long BytesDone = 0, long BytesTotal = 0);

public sealed record GameResult(GameId Game, string Title, SyncAction? Action, GameStatus Status, string Message)
{
    public string? Notice { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public VersionId? NewVersion { get; init; }

    /// <summary>
    /// KAN-40: what moved, for the log's line: "Kept 2.1 MB (3 files), after 3 h 10 min of play." or "Brought down 2.1 MB
    /// (3 files) from LAPTOP."; null when nothing did.
    /// </summary>
    public string? Moved { get; init; }

    /// <summary>Why the cloud couldn't be used for this game this time, if it couldn't: offline, a sign-in to renew, a full Drive. The tray icon tells offline from what needs the person by it (BG-07).</summary>
    public CloudErrorKind? CloudProblem { get; init; }
}

/// <summary>A restore or upload refused by a safety rule or a damaged file; the game shows Blocked.</summary>
public sealed class BlockedException(string message) : Exception(message);

/// <summary>FOLD-05: the backup folder's drive isn't connected, so nothing syncs until it's back.</summary>
public sealed class BackupFolderUnavailableException(string message) : Exception(message);

/// <param name="DeviceName">The PC's current name, which may have changed since the version was made (PC-02).</param>
/// <param name="Uploaded">False while the version waits in this PC's outbox.</param>
public sealed record HistoryEntry(VersionRecord Version, bool IsCurrent, bool IsBase, bool Pinned, string? PinLabel, string DeviceName, bool Uploaded);

/// <summary>
/// PC-04: a game with saves in the cloud that this PC doesn't sync, with its newest save, and the save rules the newest
/// save that recorded any used. Those rules are only an offer: they pass the usual checks and need confirming (R8).
/// </summary>
public sealed record CloudGame(GameId Id, string Title, VersionRecord Newest, PortableRules? Rules);

public sealed record ThinPreview(GameId Game, IReadOnlyList<VersionRecord> Versions, IReadOnlyList<BlobId> Blobs, long Bytes, string Fingerprint);
