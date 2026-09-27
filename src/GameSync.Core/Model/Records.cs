using System.Text.Json.Serialization;

namespace GameSync.Core.Model;

[JsonConverter(typeof(JsonStringEnumConverter<SaveCategory>))]
public enum SaveCategory
{
    /// <summary>Synced between PCs.</summary>
    Save,

    /// <summary>Settings: backed up per PC, synced only if the user opts in (FIND-09).</summary>
    Config,

    /// <summary>Off unless the user turns them on; backed up per PC.</summary>
    Screenshots,
}

[JsonConverter(typeof(JsonStringEnumConverter<VersionKind>))]
public enum VersionKind
{
    /// <summary>Part of the game's history line; the newest one nobody replaced is the current save.</summary>
    Normal,

    /// <summary>Changed while the game wasn't running: backed up, but never current until approved (BAK-11).</summary>
    Held,

    /// <summary>A copy kept aside, like a conflict's losing side; never current by itself.</summary>
    Kept,
}

[JsonConverter(typeof(JsonStringEnumConverter<VersionOrigin>))]
public enum VersionOrigin
{
    FirstBackup,
    Session,
    OutOfSession,
    Manual,
    Approved,
    Restore,
    Resolve,
    Reupload,
    KeptAtFirstSync,
    KeptInConflict,
    KeptBeforeRestore,
}

public sealed record DeviceInfo(DeviceId Id, string Name);

/// <summary>A play session on one PC. Until the process watcher exists (Milestone 4), sessions are recorded by hand.</summary>
public sealed record SessionInfo(DateTime StartUtc, DateTime EndUtc)
{
    public bool Covers(DateTime utc, TimeSpan slack) => utc >= StartUtc - slack && utc <= EndUtc + slack;
}

/// <summary>One file in a version. <see cref="Path"/> is portable: the rule's root key, then the path below it with '/'.</summary>
public sealed record FileEntry(string Path, long Size, DateTime ModifiedUtc, BlobId Hash, SaveCategory Category = SaveCategory.Save);

/// <summary>An immutable record of one snapshot of a game's saves. File contents live in the blob store under their hash.</summary>
public sealed record VersionRecord
{
    public int Schema { get; init; } = 1;

    public required VersionId Id { get; init; }

    public required GameId Game { get; init; }

    public VersionId? Parent { get; init; }

    /// <summary>Other versions this one replaces, so a resolved conflict or fork leaves a single current version.</summary>
    public IReadOnlyList<VersionId> Supersedes { get; init; } = [];

    public required VersionKind Kind { get; init; }

    public required VersionOrigin Origin { get; init; }

    public required DeviceInfo Device { get; init; }

    public required DateTime CreatedUtc { get; init; }

    public SessionInfo? Session { get; init; }

    public bool Pinned { get; init; }

    public string? Label { get; init; }

    public required IReadOnlyList<FileEntry> Files { get; init; }
}

/// <summary>A pin added after a version was written. <paramref name="FromConflict"/> marks a save that lost a conflict.</summary>
public sealed record PinRecord(VersionId Version, string Label, DateTime CreatedUtc, DeviceInfo Device, bool FromConflict = false);

/// <summary>The cloud's "now playing" marker for a game (used from Milestone 4).</summary>
public sealed record SessionMarker(DeviceInfo Device, DateTime StartedUtc);
