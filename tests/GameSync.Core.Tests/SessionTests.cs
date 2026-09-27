using GameSync.Core.Model;
using GameSync.Core.Sessions;

namespace GameSync.Core.Tests;

/// <summary>PLAY-04 and PLAY-06: when play sessions start and end, on a fake clock.</summary>
public class SessionTests
{
    private static readonly DateTime T = new(2026, 9, 28, 19, 0, 0, DateTimeKind.Utc);
    private static readonly GameId Sekiro = GameId.Parse("sekiro");
    private static readonly GameId Terraria = GameId.Parse("terraria");

    [Fact]
    public void PLAY_04_a_session_starts_2_s_before_the_process_and_ends_10_s_after_it_once_saves_are_quiet_for_5_s()
    {
        var tracker = new SessionTracker();
        var game = new GameProcess(Sekiro, 100, T);

        var started = Assert.IsType<SessionStarted>(Assert.Single(tracker.Tick(T.AddSeconds(1), [game])));
        Assert.Equal(T.AddSeconds(-2), started.StartUtc);
        Assert.Empty(tracker.Tick(T.AddSeconds(60), [game]));

        // It quits at about 61 s and writes its last save at 69 s.
        Assert.Empty(tracker.Tick(T.AddSeconds(62), []));
        Assert.Empty(tracker.Tick(T.AddSeconds(70), [], Writes(Sekiro, T.AddSeconds(69))));
        Assert.Empty(tracker.Tick(T.AddSeconds(73), []));
        var ended = Assert.IsType<SessionEnded>(Assert.Single(tracker.Tick(T.AddSeconds(75), [])));

        Assert.Equal(new SessionInfo(T.AddSeconds(-2), T.AddSeconds(69)), ended.Session);
        Assert.False(ended.ByHand);
        Assert.True(ended.Session.Covers(T.AddSeconds(0.5), TimeSpan.Zero), "A save written in the first second lands in the session.");
        Assert.Empty(tracker.Playing);
    }

    [Fact]
    public void PLAY_04_a_launcher_handing_over_to_the_game_within_10_s_is_one_session()
    {
        var tracker = new SessionTracker();
        var launcher = new GameProcess(Sekiro, 100, T);
        var game = new GameProcess(Sekiro, 200, T.AddSeconds(8));

        Assert.Single(tracker.Tick(T.AddSeconds(1), [launcher]));
        Assert.Empty(tracker.Tick(T.AddSeconds(5), []));
        Assert.Empty(tracker.Tick(T.AddSeconds(9), [game]));
        Assert.Empty(tracker.Tick(T.AddSeconds(300), [game]));
        var ended = Assert.IsType<SessionEnded>(Assert.Single(tracker.Tick(T.AddSeconds(311), [])));

        Assert.Equal(T.AddSeconds(-2), ended.Session.StartUtc);
    }

    [Fact]
    public void PLAY_04_a_game_already_running_when_watching_starts_counts_from_its_own_start()
    {
        var tracker = new SessionTracker();

        var started = Assert.IsType<SessionStarted>(Assert.Single(tracker.Tick(T.AddHours(1), [new GameProcess(Sekiro, 100, T)])));

        Assert.Equal(T.AddSeconds(-2), started.StartUtc);
    }

    [Fact]
    public void Two_games_played_at_once_have_their_own_sessions()
    {
        var tracker = new SessionTracker();
        var sekiro = new GameProcess(Sekiro, 100, T);
        var terraria = new GameProcess(Terraria, 200, T.AddMinutes(5));

        tracker.Tick(T.AddSeconds(1), [sekiro]);
        Assert.Single(tracker.Tick(T.AddMinutes(5).AddSeconds(1), [sekiro, terraria]));
        Assert.Empty(tracker.Tick(T.AddMinutes(5).AddSeconds(5), [terraria]));
        var ended = Assert.IsType<SessionEnded>(Assert.Single(tracker.Tick(T.AddMinutes(5).AddSeconds(12), [terraria])));

        Assert.Equal(Sekiro, ended.Game);
        Assert.Equal([Terraria], tracker.Playing.Keys);
    }

    [Fact]
    public void PLAY_06_done_playing_ends_a_stuck_session_and_the_launcher_left_open_starts_no_new_one()
    {
        var tracker = new SessionTracker();
        var launcher = new GameProcess(Sekiro, 100, T);
        tracker.Tick(T.AddSeconds(1), [launcher]);

        var ended = tracker.EndByHand(Sekiro, T.AddHours(2), [launcher])!;

        Assert.True(ended.ByHand);
        Assert.Equal(T.AddHours(2), ended.Session.EndUtc);
        Assert.Empty(tracker.Tick(T.AddHours(2).AddSeconds(2), [launcher]));
        Assert.Empty(tracker.Playing);

        // Once it exits, the next start is a new session.
        Assert.Empty(tracker.Tick(T.AddHours(3), []));
        Assert.Single(tracker.Tick(T.AddHours(4), [new GameProcess(Sekiro, 300, T.AddHours(4))]));
        Assert.Null(tracker.EndByHand(Terraria, T.AddHours(4), []));
    }

    private static Dictionary<GameId, DateTime> Writes(GameId game, DateTime when) => new() { [game] = when };
}
