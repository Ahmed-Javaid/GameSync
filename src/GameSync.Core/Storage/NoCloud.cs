using GameSync.Core.Model;

namespace GameSync.Core.Storage;

/// <summary>
/// The cloud before one is connected (first run's Skip for now, ONB-01): every call answers
/// <see cref="CloudErrorKind.NotConnected"/>, so the sync works as it does offline. Every version is kept in the backup
/// folder on this PC and waits to go up, and goes up in the background once a cloud is connected; nothing is lost.
/// </summary>
public sealed class NoCloud : ICloud, IBlobStore, IVersionLog
{
    public const string Message = "No cloud is connected yet: every version is kept on this PC, and goes up once you connect Google Drive or a folder.";

    public IBlobStore Blobs => this;

    public IVersionLog Log => this;

    public Task<CloudInfo> GetInfoAsync(CancellationToken ct) => Task.FromException<CloudInfo>(NotConnected());

    public Task<IReadOnlyList<GameId>> ListGamesAsync(CancellationToken ct) => Task.FromException<IReadOnlyList<GameId>>(NotConnected());

    public Task<IReadOnlyList<DeviceRecord>> ListDevicesAsync(CancellationToken ct) => Task.FromException<IReadOnlyList<DeviceRecord>>(NotConnected());

    public Task SaveDeviceAsync(DeviceRecord device, CancellationToken ct) => Task.FromException(NotConnected());

    public Task WriteLatestAsync(GameId game, VersionRecord version, Func<FileEntry, CancellationToken, Task<Stream>> open, CancellationToken ct) => Task.FromException(NotConnected());

    public Task WriteRestoreKitAsync(CancellationToken ct) => Task.FromException(NotConnected());

    Task<bool> IBlobStore.ExistsAsync(GameId game, BlobId id, CancellationToken ct) => Task.FromException<bool>(NotConnected());

    Task IBlobStore.PutAsync(GameId game, BlobId id, Stream content, CancellationToken ct) => Task.FromException(NotConnected());

    Task<Stream> IBlobStore.GetAsync(GameId game, BlobId id, CancellationToken ct) => Task.FromException<Stream>(NotConnected());

    Task IBlobStore.TrashAsync(GameId game, BlobId id, CancellationToken ct) => Task.FromException(NotConnected());

    Task<IReadOnlyList<VersionRecord>> IVersionLog.ListAsync(GameId game, CancellationToken ct) => Task.FromException<IReadOnlyList<VersionRecord>>(NotConnected());

    Task<IReadOnlySet<VersionId>> IVersionLog.ListIdsAsync(GameId game, CancellationToken ct) => Task.FromException<IReadOnlySet<VersionId>>(NotConnected());

    Task<VersionRecord?> IVersionLog.GetAsync(GameId game, VersionId id, CancellationToken ct) => Task.FromException<VersionRecord?>(NotConnected());

    Task IVersionLog.AppendAsync(GameId game, VersionRecord version, CancellationToken ct) => Task.FromException(NotConnected());

    Task<IReadOnlyList<PinRecord>> IVersionLog.ListPinsAsync(GameId game, CancellationToken ct) => Task.FromException<IReadOnlyList<PinRecord>>(NotConnected());

    Task IVersionLog.SetPinAsync(GameId game, PinRecord pin, CancellationToken ct) => Task.FromException(NotConnected());

    Task IVersionLog.RemovePinAsync(GameId game, VersionId version, CancellationToken ct) => Task.FromException(NotConnected());

    Task IVersionLog.MarkThinnedAsync(GameId game, VersionId version, CancellationToken ct) => Task.FromException(NotConnected());

    Task<IReadOnlySet<VersionId>> IVersionLog.ListThinnedAsync(GameId game, CancellationToken ct) => Task.FromException<IReadOnlySet<VersionId>>(NotConnected());

    Task<SessionMarker?> IVersionLog.GetMarkerAsync(GameId game, CancellationToken ct) => Task.FromException<SessionMarker?>(NotConnected());

    Task IVersionLog.SetMarkerAsync(GameId game, SessionMarker? marker, CancellationToken ct) => Task.FromException(NotConnected());

    private static CloudException NotConnected() => new(CloudErrorKind.NotConnected, Message);
}
