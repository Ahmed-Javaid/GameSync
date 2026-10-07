using System.Globalization;
using GameSync.Core.Model;
using GameSync.Core.Sessions;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>
/// BAK-10: whether a game runs now: one of its programs is running, or the agent still has its session open, which
/// covers the seconds after it closes while its saves settle. A game's programs are looked up the first time it's asked
/// about, and the answer holds for a second.
/// </summary>
internal sealed class RunningGames(Engine engine)
{
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(1);

    private readonly ProcessWatcher _watcher = new();
    private readonly Dictionary<GameId, GamePrograms?> _programs = [];
    private readonly Dictionary<GameId, (DateTime At, bool Running)> _answers = [];

    // A session the agent left open when it was stopped says nothing; the agent closes it when it starts again.
    private readonly bool _agent = EngineLock.AgentRunning(engine.DataDir);

    /// <summary>The setting the agent keeps while a game's session is open: when it started.</summary>
    public static string OpenSessionKey(GameId game) => $"session.open.{game}";

    public const string QuietOpenPrefix = "quiet.open.";

    /// <summary>
    /// PLAY-06: when the person last said they're done playing the game. Its processes started before then, such as an
    /// emulator still closing, aren't the game any more; a new start of it is.
    /// </summary>
    public static string EndedByHandKey(GameId game) => $"session.byhand.{game}";

    /// <summary>
    /// LIB-13: the setting the agent keeps while a folder of the person's own is changing, from when it started: in use,
    /// so nothing is restored into it meanwhile, but not played, so Home and the play time leave it out.
    /// </summary>
    public static string QuietOpenKey(GameId game) => $"{QuietOpenPrefix}{game}";

    public bool IsRunning(GameId game)
    {
        if (_agent && (engine.State.GetSetting(OpenSessionKey(game)) is { Length: > 0 } || engine.State.GetSetting(QuietOpenKey(game)) is { Length: > 0 }))
        {
            return true;
        }

        if (_answers.TryGetValue(game, out var answer) && DateTime.UtcNow - answer.At < Fresh)
        {
            return answer.Running;
        }

        if (!_programs.TryGetValue(game, out var programs))
        {
            programs = engine.InstallDirs.TryGetValue(game, out var folder) && Directory.Exists(folder) ? GamePrograms.For(game, folder) : null;
            _programs[game] = programs;
        }

        var byHand = engine.State.GetSetting(EndedByHandKey(game)) is { Length: > 0 } said &&
            DateTime.TryParse(said, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at.ToUniversalTime() : (DateTime?)null;
        var running = programs is { Names.Count: > 0 } && _watcher.Find([programs]).Any(p => byHand is null || p.StartedUtc > byHand);
        _answers[game] = (DateTime.UtcNow, running);
        return running;
    }
}
