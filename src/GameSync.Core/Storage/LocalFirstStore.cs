using GameSync.Core.Model;
using GameSync.Core.Sync;

namespace GameSync.Core.Storage;

public sealed record PullResult(int Fetched, int Requeued);

public sealed record PushResult(int Uploaded, IReadOnlyList<string> Warnings);

/// <summary>
/// What the sync engine reads and writes: the backup folder on this PC, with the cloud behind it. New versions, pins
/// and pin removals land in the backup folder plus its outbox; <see cref="PushAsync"/> uploads them, each version's
/// files first and its record last (BAK-12), and <see cref="PullAsync"/> copies records this PC hasn't seen. Offline,
/// syncs keep working from the backup folder and the outbox waits (PC-05).
/// </summary>
public sealed class LocalFirstStore(LocalHistory history, ICloud cloud) : IBlobStore, IVersionLog
{
    private const int Parallelism = 4;

    public LocalHistory History => history;

    public ICloud Cloud => cloud;

    // ---- file contents ----

    public Task<bool> ExistsAsync(GameId game, BlobId id, CancellationToken ct) => history.Blobs.ExistsAsync(game, id, ct);

    public Task PutAsync(GameId game, BlobId id, Stream content, CancellationToken ct) => history.Blobs.PutAsync(game, id, content, ct);

    /// <summary>From the backup folder, or fetched from the cloud into it first, checked against its hash on the way in.</summary>
    public async Task<Stream> GetAsync(GameId game, BlobId id, CancellationToken ct)
    {
        if (!await history.Blobs.ExistsAsync(game, id, ct))
        {
            await FetchOneAsync(game, id, ct);
        }

        return await history.Blobs.GetAsync(game, id, ct);
    }

    /// <summary>
    /// KAN-80: the files of a version this PC doesn't have yet, fetched from the cloud before a restore writes anything,
    /// four at a time, saying how far it is; each is checked against its hash on the way in.
    /// </summary>
    public async Task FetchAsync(GameId game, IEnumerable<FileEntry> files, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var missing = new List<FileEntry>();
        foreach (var file in files.DistinctBy(f => f.Hash))
        {
            if (!await history.Blobs.ExistsAsync(game, file.Hash, ct))
            {
                missing.Add(file);
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        var meter = new TransferMeter(game, TransferDirection.Down, progress);
        meter.Add(missing.Count, missing.Sum(f => f.Size));
        meter.Start();
        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct },
            async (file, token) => await meter.MoveAsync(file.Size, () => FetchOneAsync(game, file.Hash, token)));
    }

    private async Task FetchOneAsync(GameId game, BlobId id, CancellationToken ct)
    {
        await using var remote = await cloud.Blobs.GetAsync(game, id, ct);
        try
        {
            await history.Blobs.PutAsync(game, id, remote, ct);
        }
        catch (BlobMismatchException)
        {
            throw new BlobMismatchException($"The cloud's copy of a file is damaged: its contents don't match its hash ({id.Value[..12]}…).");
        }
    }

    /// <summary>Thinning only, which needs the cloud: its trash first, then this PC's copy.</summary>
    public async Task TrashAsync(GameId game, BlobId id, CancellationToken ct)
    {
        await cloud.Blobs.TrashAsync(game, id, ct);
        history.Blobs.Delete(game, id);
    }

    // ---- records ----

    public Task<IReadOnlyList<VersionRecord>> ListAsync(GameId game, CancellationToken ct) => history.Log.ListAsync(game, ct);

    public Task<IReadOnlySet<VersionId>> ListIdsAsync(GameId game, CancellationToken ct) => history.Log.ListIdsAsync(game, ct);

    public Task<VersionRecord?> GetAsync(GameId game, VersionId id, CancellationToken ct) => history.Log.GetAsync(game, id, ct);

    /// <summary>
    /// The outbox entry is written first: a crash between the two leaves an entry without a record, which the next push
    /// drops, never a record nobody uploads.
    /// </summary>
    public async Task AppendAsync(GameId game, VersionRecord version, CancellationToken ct)
    {
        await history.AddPendingVersionAsync(game, version.Id, ct);
        await history.Log.AppendAsync(game, version, ct);
    }

    public Task<IReadOnlyList<PinRecord>> ListPinsAsync(GameId game, CancellationToken ct) => history.Log.ListPinsAsync(game, ct);

    public async Task SetPinAsync(GameId game, PinRecord pin, CancellationToken ct)
    {
        // A name given again after it was taken away: the removal still waiting mustn't undo it in the cloud.
        history.RemovePendingUnpin(game, pin.Version);
        await history.AddPendingPinAsync(game, pin.Version, ct);
        await history.Log.SetPinAsync(game, pin, ct);
    }

    /// <summary>
    /// KAN-88: gone on this PC at once, and from the cloud with the next upload, so taking a name away never waits on
    /// the network. The removal is in the outbox first, so a crash between the two can't let the cloud's pin come back,
    /// and a pull leaves it out until the cloud has it gone too.
    /// </summary>
    public async Task RemovePinAsync(GameId game, VersionId version, CancellationToken ct)
    {
        await history.AddPendingUnpinAsync(game, version, ct);
        history.RemovePendingPin(game, version);
        await history.Log.RemovePinAsync(game, version, ct);
    }

    public async Task MarkThinnedAsync(GameId game, VersionId version, CancellationToken ct)
    {
        await cloud.Log.MarkThinnedAsync(game, version, ct);
        await history.Log.MarkThinnedAsync(game, version, ct);
    }

    public Task<IReadOnlySet<VersionId>> ListThinnedAsync(GameId game, CancellationToken ct) => history.Log.ListThinnedAsync(game, ct);

    public Task<SessionMarker?> GetMarkerAsync(GameId game, CancellationToken ct) => cloud.Log.GetMarkerAsync(game, ct);

    public Task SetMarkerAsync(GameId game, SessionMarker? marker, CancellationToken ct) => cloud.Log.SetMarkerAsync(game, marker, ct);

    // ---- moving things between this PC and the cloud ----

    /// <summary>
    /// Uploads the game's outbox, oldest first: each version's files, then its record, then pins. How far it is goes to
    /// <paramref name="progress"/> as it goes, for all of it at once (KAN-80): the files the cloud doesn't have yet, each
    /// once however many versions share it, and every record and pin.
    /// </summary>
    public async Task<PushResult> PushAsync(GameId game, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var warnings = new List<string>();
        var uploaded = 0;
        var records = new List<VersionRecord>();
        var pending = history.PendingVersions(game);
        if (pending.Count > 0)
        {
            var inCloud = await cloud.Log.ListIdsAsync(game, ct);
            foreach (var id in pending)
            {
                if (inCloud.Contains(id))
                {
                    history.RemovePendingVersion(game, id);
                }
                else if (await history.Log.GetAsync(game, id, ct) is { } record)
                {
                    records.Add(record);
                }
                else
                {
                    // A crash came between the outbox entry and the record: there's nothing to upload.
                    history.RemovePendingVersion(game, id);
                }
            }
        }

        var pendingPins = history.PendingPins(game);
        var pendingUnpins = history.PendingUnpins(game);
        var meter = new TransferMeter(game, TransferDirection.Up, progress);
        if (progress is not null)
        {
            var counted = new HashSet<BlobId>();
            foreach (var file in records.SelectMany(r => r.Files))
            {
                if (counted.Add(file.Hash) && !await cloud.Blobs.ExistsAsync(game, file.Hash, ct))
                {
                    meter.Add(1, file.Size);
                }
            }

            meter.Add(records.Count + pendingPins.Count + pendingUnpins.Count, 0);
            meter.Start();
        }

        foreach (var record in records.OrderBy(r => r.CreatedUtc).ThenBy(r => r.Id))
        {
            if (!await UploadFilesAsync(game, record, meter, ct))
            {
                await history.AbandonAsync(game, record.Id, ct);
                warnings.Add($"Version {record.Id} can't be uploaded: some of its files are gone from this PC and from the cloud.");
                continue;
            }

            CrashPoints.Hit(CrashPoints.PushAfterBlobs);
            await cloud.Log.AppendAsync(game, record, ct);
            history.RemovePendingVersion(game, record.Id);
            meter.Done();
            uploaded++;
        }

        if (pendingPins.Count > 0)
        {
            var pins = (await history.Log.ListPinsAsync(game, ct)).ToDictionary(p => p.Version);
            foreach (var version in pendingPins)
            {
                if (pins.TryGetValue(version, out var pin))
                {
                    await cloud.Log.SetPinAsync(game, pin, ct);
                }

                history.RemovePendingPin(game, version);
                meter.Done();
            }
        }

        foreach (var version in pendingUnpins)
        {
            await cloud.Log.RemovePinAsync(game, version, ct);
            history.RemovePendingUnpin(game, version);
            meter.Done();
        }

        return new PushResult(uploaded, warnings);
    }

    /// <summary>
    /// Copies records this PC hasn't seen, and the cloud's pins and thinning marks. Versions this PC has that the cloud
    /// lost (its folder was deleted, or it's a new account) go back in the outbox to upload again.
    /// </summary>
    public async Task<PullResult> PullAsync(GameId game, CancellationToken ct)
    {
        var inCloud = await cloud.Log.ListIdsAsync(game, ct);
        var here = await history.Log.ListIdsAsync(game, ct);
        var fetched = 0;
        foreach (var id in inCloud.Where(i => !here.Contains(i)).Order())
        {
            if (await cloud.Log.GetAsync(game, id, ct) is { } record)
            {
                await history.Log.AppendAsync(game, record, ct);
                fetched++;
            }
        }

        var waiting = history.PendingVersions(game).Concat(history.AbandonedVersions(game)).ToHashSet();
        var lost = here.Where(i => !inCloud.Contains(i) && !waiting.Contains(i)).ToList();
        foreach (var id in lost)
        {
            await history.AddPendingVersionAsync(game, id, ct);
        }

        var cloudPins = (await cloud.Log.ListPinsAsync(game, ct)).ToDictionary(p => p.Version);
        var localPins = (await history.Log.ListPinsAsync(game, ct)).ToDictionary(p => p.Version);
        var pendingPins = history.PendingPins(game).ToHashSet();
        var pendingUnpins = history.PendingUnpins(game).ToHashSet();
        foreach (var pin in cloudPins.Values.Where(p => !localPins.ContainsKey(p.Version) && !pendingUnpins.Contains(p.Version)))
        {
            await history.Log.SetPinAsync(game, pin, ct);
        }

        foreach (var version in localPins.Keys.Where(v => !cloudPins.ContainsKey(v) && !pendingPins.Contains(v)))
        {
            // Another PC removed it.
            await history.Log.RemovePinAsync(game, version, ct);
        }

        var thinnedHere = await history.Log.ListThinnedAsync(game, ct);
        foreach (var id in (await cloud.Log.ListThinnedAsync(game, ct)).Where(i => !thinnedHere.Contains(i)))
        {
            await history.Log.MarkThinnedAsync(game, id, ct);
        }

        return new PullResult(fetched, lost.Count);
    }

    /// <summary>Uploads the version's files the cloud doesn't have yet. False when one is missing on both sides.</summary>
    private async Task<bool> UploadFilesAsync(GameId game, VersionRecord record, TransferMeter meter, CancellationToken ct)
    {
        var files = record.Files.DistinctBy(f => f.Hash).ToList();
        var missing = new List<FileEntry>();
        foreach (var file in files)
        {
            if (!await cloud.Blobs.ExistsAsync(game, file.Hash, ct))
            {
                missing.Add(file);
            }
        }

        foreach (var file in missing)
        {
            if (!await history.Blobs.ExistsAsync(game, file.Hash, ct))
            {
                return false;
            }
        }

        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct },
            async (file, token) => await meter.MoveAsync(file.Size, async () =>
            {
                await using var content = await history.Blobs.GetAsync(game, file.Hash, token);
                await cloud.Blobs.PutAsync(game, file.Hash, content, token);
            }));

        return true;
    }
}
