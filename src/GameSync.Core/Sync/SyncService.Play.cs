using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;

namespace GameSync.Core.Sync;

/// <summary>What the pre-launch check did and found (PLAY-03): a download, if one was safe, and anything to know first.</summary>
public sealed record LaunchCheck(GameResult? Download, IReadOnlyList<string> Warnings);

/// <summary>Playing: the now-playing marker, the pre-launch check, and the save before a game update.</summary>
public sealed partial class SyncService
{
    /// <summary>A marker this old with no upload from its PC since means that PC never synced back (PLAY-07).</summary>
    public static readonly TimeSpan NeverSyncedBack = TimeSpan.FromHours(12);

    private bool IsRunning(GameId game) => _options.IsRunning?.Invoke(game) == true;

    /// <summary>BAK-10: nothing is downloaded or restored while any of the game's programs runs.</summary>
    private void ThrowIfRunning(SyncStream stream, string what)
    {
        if (IsRunning(stream.Game.Id))
        {
            throw new InvalidOperationException($"{stream.Game.Title} is running, so {what} waits until it closes. Nothing was changed.");
        }
    }

    /// <summary>PLAY-07: tells the other PCs the game is being played here, and since when. Offline, they just don't see it.</summary>
    public async Task MarkPlayingAsync(GameId game, DateTime startUtc, CancellationToken ct)
    {
        try
        {
            await _cloud.Log.SetMarkerAsync(Main(game).Id, new SessionMarker(Device, startUtc), ct);
        }
        catch (CloudException)
        {
            // The marker is a courtesy; the conflict rules still protect both PCs.
        }
    }

    /// <summary>
    /// PLAY-07: clears this PC's marker once the session's save is uploaded; another PC's is left alone. False when the
    /// cloud couldn't be reached, so it's tried again later; meanwhile the other PCs see any upload from this PC since the
    /// marker, and know it synced back.
    /// </summary>
    public async Task<bool> ClearPlayingAsync(GameId game, CancellationToken ct)
    {
        try
        {
            var id = Main(game).Id;
            if (await _cloud.Log.GetMarkerAsync(id, ct) is { } marker && marker.Device.Id == Device.Id)
            {
                await _cloud.Log.SetMarkerAsync(id, null, ct);
            }

            return true;
        }
        catch (CloudException)
        {
            return false;
        }
    }

    /// <summary>
    /// PLAY-07: another PC's now-playing marker for the game, said plainly: playing since when, or, after 12 hours
    /// with no upload from it, that it never synced back. Null when no other PC is playing, or it has synced since.
    /// </summary>
    public async Task<string?> PlayingElsewhereAsync(GameId game, CancellationToken ct)
    {
        SessionMarker? marker;
        try
        {
            marker = await _cloud.Log.GetMarkerAsync(Main(game).Id, ct);
        }
        catch (CloudException)
        {
            return null;
        }

        if (marker is null || marker.Device.Id == Device.Id)
        {
            return null;
        }

        var versions = await _log.ListAsync(Main(game).Id, ct);
        if (versions.Any(v => v.Device.Id == marker.Device.Id && v.CreatedUtc > marker.StartedUtc))
        {
            return null;
        }

        var name = DeviceNames().GetValueOrDefault(marker.Device.Id, marker.Device.Name);
        var since = marker.StartedUtc.ToLocalTime();
        return _options.UtcNow() - marker.StartedUtc >= NeverSyncedBack
            ? $"{name} never synced back: it started playing on {since:ddd d MMM HH:mm} and hasn't uploaded since. Playing here is fine; if both saves change, the conflict rules decide."
            : $"{name} has been playing it since {since:HH:mm}, and its save isn't back yet.";
    }

    /// <summary>
    /// PLAY-03: before a launch through GameSync, brings down a newer save from the cloud when that's safe, and says
    /// what else to know first: another PC playing (PLAY-07), a conflict waiting, changes held for review. This PC's own
    /// changes wait for the session's end, so nothing is uploaded here.
    /// </summary>
    public async Task<LaunchCheck> PrepareLaunchAsync(GameId game, CancellationToken ct)
    {
        var stream = Main(game);
        var plan = (await PlanAsync([stream.Id], ct)).Single(p => p.Stream == stream);
        var warnings = plan.Warnings.ToList();
        switch (plan.Error, plan.Decision?.Action)
        {
            case ({ } error, _):
                warnings.Add($"The cloud's save couldn't be checked: {error.Message}");
                return new LaunchCheck(null, warnings);
            case (null, SyncAction.Download or SyncAction.AdoptHead):
                return new LaunchCheck((await ExecuteAsync([plan], ct))[0], warnings);
            case (null, SyncAction.NeedsYou):
                warnings.Add($"A conflict waits for you: {plan.Decision!.Reason} Playing now carries on from this PC's save.");
                break;
            case (null, SyncAction.WaitForCloud):
                warnings.Add(plan.Decision!.Reason);
                break;
        }

        if (plan.Decision?.Held == true || _state.GetState(stream.Id).Status == State.GameStatus.HeldForReview)
        {
            warnings.Add("This PC has changes held for review; approve them to sync them.");
        }

        return new LaunchCheck(null, warnings);
    }

    /// <summary>
    /// BAK-06: keeps the save as it is now, pinned as "before update to build N (date)", before a new build of the game
    /// runs, since an update can convert or overwrite a save. When a version already holds exactly these files, that one
    /// gets the label. Nothing is changed on this PC.
    /// </summary>
    public async Task<GameResult> KeepBeforeUpdateAsync(GameId game, string newBuild, CancellationToken ct)
    {
        var stream = Main(game);
        var label = $"before update to build {newBuild} ({DateTime.Now.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture)})";
        var snapshot = ScanOrThrowIfUnavailable(stream);
        if (snapshot.Files.Count == 0)
        {
            return new GameResult(stream.Id, stream.Definition.Title, SyncAction.None, StatusFor(stream), $"No save to keep before the update to build {newBuild}.");
        }

        await TryPullAsync(stream, ct);
        var (versions, _) = await LoadVersionsAsync(stream, ct);
        var same = versions.Where(v => FileSet.SameContent(v.Files, snapshot.Files)).MaxBy(v => v.CreatedUtc);
        VersionRecord kept;
        if (same is not null)
        {
            await _log.SetPinAsync(stream.Id, new PinRecord(same.Id, label, DateTime.UtcNow, Device), ct);
            kept = same;
        }
        else
        {
            kept = await UploadAsync(stream, snapshot.Files, VersionKind.Kept, VersionOrigin.BeforeUpdate, _state.GetState(stream.Id).Base?.Id, [],
                pinned: true, label, ct);
        }

        _state.Log(stream.Id, "info", $"Kept the save {label}.", EventTags.Backup);
        var result = new GameResult(stream.Id, stream.Definition.Title, SyncAction.Upload, StatusFor(stream), $"Kept the save {label}.")
        {
            NewVersion = kept.Id,
            Warnings = snapshot.Warnings,
        };
        return await AfterManualAsync(stream, result, ct);
    }
}
