using GameSync.Core.Model;

namespace GameSync.Core.Storage;

/// <summary>File contents, stored once per game under their SHA-256.</summary>
public interface IBlobStore
{
    Task<bool> ExistsAsync(GameId game, BlobId id, CancellationToken ct);

    /// <summary>Stores <paramref name="content"/> and throws <see cref="BlobMismatchException"/> if it doesn't hash to <paramref name="id"/>.</summary>
    Task PutAsync(GameId game, BlobId id, Stream content, CancellationToken ct);

    Task<Stream> GetAsync(GameId game, BlobId id, CancellationToken ct);

    /// <summary>Moves a blob to the backend's trash. Only manual thinning calls this.</summary>
    Task TrashAsync(GameId game, BlobId id, CancellationToken ct);
}

/// <summary>Each game's versions, pins and "now playing" marker. Versions are immutable once appended.</summary>
public interface IVersionLog
{
    Task<IReadOnlyList<VersionRecord>> ListAsync(GameId game, CancellationToken ct);

    /// <summary>Every version's id, without reading the records, so a pull only downloads records it hasn't seen.</summary>
    Task<IReadOnlySet<VersionId>> ListIdsAsync(GameId game, CancellationToken ct);

    /// <summary>One version's record, or null when it's missing or unreadable (unreadable ones are reported as problems).</summary>
    Task<VersionRecord?> GetAsync(GameId game, VersionId id, CancellationToken ct);

    /// <summary>Appends a new version; appending an id that exists throws.</summary>
    Task AppendAsync(GameId game, VersionRecord version, CancellationToken ct);

    Task<IReadOnlyList<PinRecord>> ListPinsAsync(GameId game, CancellationToken ct);

    Task SetPinAsync(GameId game, PinRecord pin, CancellationToken ct);

    Task RemovePinAsync(GameId game, VersionId version, CancellationToken ct);

    /// <summary>
    /// Marks a version thinned: it leaves the history the user sees and its unshared files go to the trash. The small
    /// record stays, so which version replaced which is never lost. Only manual thinning calls this (BAK-05).
    /// </summary>
    Task MarkThinnedAsync(GameId game, VersionId version, CancellationToken ct);

    Task<IReadOnlySet<VersionId>> ListThinnedAsync(GameId game, CancellationToken ct);

    Task<SessionMarker?> GetMarkerAsync(GameId game, CancellationToken ct);

    Task SetMarkerAsync(GameId game, SessionMarker? marker, CancellationToken ct);
}

/// <summary>A cloud backend: the two stores above, plus what it keeps for the whole account.</summary>
public interface ICloud
{
    IBlobStore Blobs { get; }

    IVersionLog Log { get; }

    /// <summary>Where the cloud folder is, who is signed in, how full it is, and the server's clock when it has one.</summary>
    Task<CloudInfo> GetInfoAsync(CancellationToken ct);

    /// <summary>Every game with a folder in the cloud, including ones this PC doesn't sync (PC-04).</summary>
    Task<IReadOnlyList<GameId>> ListGamesAsync(CancellationToken ct);

    Task<IReadOnlyList<DeviceRecord>> ListDevicesAsync(CancellationToken ct);

    Task SaveDeviceAsync(DeviceRecord device, CancellationToken ct);

    /// <summary>Makes the game's plain <c>latest/</c> copy hold exactly <paramref name="version"/>'s files (CLOUD-03).</summary>
    Task WriteLatestAsync(GameId game, VersionRecord version, Func<FileEntry, CancellationToken, Task<Stream>> open, CancellationToken ct);

    /// <summary>Writes <c>HOW-TO-RESTORE.txt</c> and <c>restore.ps1</c> at the top of the cloud folder when they're missing or out of date.</summary>
    Task WriteRestoreKitAsync(CancellationToken ct);
}

/// <summary><paramref name="Where"/> is a folder path or a link to open; the rest is null when the backend doesn't know it.</summary>
public sealed record CloudInfo(string Where, string? Account, long? UsedBytes, long? TotalBytes, DateTime? ServerTimeUtc);

public enum CloudErrorKind
{
    /// <summary>No connection; everything waits in the outbox.</summary>
    Offline,

    SignInExpired,

    StorageFull,

    /// <summary>Still rate limited after retrying with backoff.</summary>
    RateLimited,

    /// <summary>The backend flagged a file as malware; it's never downloaded (R4, CLOUD-06).</summary>
    Flagged,

    Other,
}

/// <summary>A typed cloud failure, so each game can say what went wrong (CLOUD-04).</summary>
public sealed class CloudException(CloudErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public CloudErrorKind Kind { get; } = kind;
}

public sealed class BlobMismatchException(string message) : Exception(message);

/// <summary>A record from the cloud that fails validation; it's ignored and reported on its game.</summary>
public sealed record StorageProblem(GameId Game, string Item, string Message);
