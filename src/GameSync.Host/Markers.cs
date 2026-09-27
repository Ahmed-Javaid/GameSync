using System.Globalization;
using GameSync.Core.Model;
using GameSync.Core.Sync;

namespace GameSync.Host;

/// <summary>
/// PLAY-07 around a session: the now-playing marker goes up at its start and comes down after its sync. This PC notes
/// each marker it put up, so one it couldn't take down (offline at the game's exit, or stopped mid-session) comes down
/// at a later sync, instead of telling the other PCs for good that this PC never synced back.
/// </summary>
internal static class Markers
{
    private const string Prefix = "session.marker.";

    /// <summary>Tells the other PCs the game is playing here; nothing is read or recovered while the game starts.</summary>
    public static async Task SetAsync(string dataDir, GameId game, DateTime startUtc, CancellationToken ct)
    {
        using var engine = Engine.Open(dataDir);
        engine.State.SetSetting(Prefix + game, startUtc.ToString("O", CultureInfo.InvariantCulture));
        var (service, _) = await engine.OpenServiceAsync(new SyncOptions { IsRunning = _ => true }, ct, recover: false);
        await service.MarkPlayingAsync(game, startUtc, ct);
    }

    /// <summary>After a sync: takes down the markers this PC put up for games that aren't playing now.</summary>
    public static async Task ClearAsync(Engine engine, SyncService service, Func<GameId, bool> isRunning, CancellationToken ct)
    {
        foreach (var key in engine.State.GetSettings(Prefix).Keys)
        {
            if (!GameId.TryParse(key[Prefix.Length..], out var game) || engine.Games.All(g => g.Id != game))
            {
                engine.State.SetSetting(key, "");
            }
            else if (!isRunning(game) && await service.ClearPlayingAsync(game, ct))
            {
                engine.State.SetSetting(key, "");
            }
        }
    }
}
