using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;

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

    /// <summary>Identifies the inputs, so running the plan can tell whether anything changed since it was made.</summary>
    public string Fingerprint { get; init; } = "";

    /// <summary>Set when planning itself failed, for example because a save file is locked.</summary>
    public Exception? Error { get; init; }

    public string Title => Stream.Definition.Title;
}

public sealed record GameResult(GameId Game, string Title, SyncAction? Action, GameStatus Status, string Message)
{
    public string? Notice { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public VersionId? NewVersion { get; init; }
}

/// <summary>A restore or upload refused by a safety rule or a damaged file; the game shows Blocked.</summary>
public sealed class BlockedException(string message) : Exception(message);

public sealed record HistoryEntry(VersionRecord Version, bool IsCurrent, bool IsBase, bool Pinned, string? PinLabel);

public sealed record ThinPreview(GameId Game, IReadOnlyList<VersionRecord> Versions, IReadOnlyList<BlobId> Blobs, long Bytes, string Fingerprint);
