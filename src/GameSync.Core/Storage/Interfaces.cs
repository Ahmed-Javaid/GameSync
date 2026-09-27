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

public sealed class BlobMismatchException(string message) : Exception(message);

/// <summary>A record from the cloud that fails validation; it's ignored and reported on its game.</summary>
public sealed record StorageProblem(GameId Game, string Item, string Message);
