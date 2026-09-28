using GameSync.Core.Model;

namespace GameSync.Host;

/// <summary>What the app's screens ask of the engine, the same work as the command line's verbs.</summary>
public static class AppActions
{
    /// <summary>
    /// PLAY-02, PLAY-03: Play from the launcher, as <c>gamesync launch</c>: a game that syncs gets the check before playing
    /// (a newer save from another PC, the game open elsewhere), then starts the way its store does; a game found but not
    /// synced just starts. What goes wrong, and what the check warns about, goes to <paramref name="output"/>.
    /// </summary>
    public static async Task PlayAsync(string dataDir, GameId game, IAgentOutput output)
    {
        try
        {
            await Cli.LaunchAsync(dataDir, [game.Value], output);
        }
        catch (Exception e) when (e is UsageException or InvalidOperationException or IOException)
        {
            output.NeedsYou("GameSync", e.Message);
        }
    }
}
