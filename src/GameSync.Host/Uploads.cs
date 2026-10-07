using GameSync.Core.Model;
using GameSync.Core.Storage;
using GameSync.Core.Sync;

namespace GameSync.Host;

/// <summary>
/// KAN-88: the uploads that follow the app's syncs and the person's actions. What waits in the outbox goes up under the
/// upload lock, never the engine's, so whatever the person does next runs beside it. The app's agent runs this after
/// each round that synced something and after each action; a terminal's commands still upload within their run.
/// </summary>
public static class Uploads
{
    /// <param name="isRunning">The games being played now, whose uploads wait for their session to end (BG-08).</param>
    /// <param name="progress">How far each game's upload is, as it goes (KAN-80).</param>
    /// <returns>A result for each game that had something to upload, or that couldn't; none with no cloud connected yet.</returns>
    public static async Task<IReadOnlyList<GameResult>> RunAsync(string dataDir, Func<GameId, bool>? isRunning, CancellationToken ct,
        IProgress<TransferProgress>? progress = null)
    {
        using var engine = Engine.Open(dataDir);
        if (engine.Config.HasNoCloud || engine.Games.Count == 0)
        {
            return [];
        }

        var (service, _) = await engine.OpenServiceAsync(new SyncOptions { IsRunning = isRunning ?? new RunningGames(engine).IsRunning, Progress = progress }, ct,
            recover: false);
        return await service.UploadAsync(null, ct);
    }
}
