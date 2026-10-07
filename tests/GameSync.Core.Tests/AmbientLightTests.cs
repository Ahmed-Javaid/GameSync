using GameSync.UI.Controls;

namespace GameSync.Core.Tests;

/// <summary>
/// Design system version 50 (the owner, 7 Oct 2026: "Animate the light rays"): the Zeniths monument's light moves, and
/// light that keeps moving while a page waits ticks slowly on one clock: a 100% ring's light on Avalonia's own animations
/// was repainting the window 142 times a second, half a core.
/// </summary>
public class AmbientLightTests
{
    [Fact]
    public void Light_that_keeps_moving_ticks_at_most_15_times_a_second_and_30_for_a_glint()
    {
        Assert.True(Ambient.Slow >= TimeSpan.FromMilliseconds(1000.0 / 15));
        Assert.True(Ambient.Quick >= TimeSpan.FromMilliseconds(1000.0 / 30));
        Assert.True(Ambient.Waiting > Ambient.Slow);
    }

    [Fact]
    public void A_lights_time_moves_only_while_it_moves_and_carries_on_where_it_stopped()
    {
        var time = new AmbientTime(360);
        Assert.True(time.Advance(10.0, moves: true));
        Assert.Equal(0, time.Seconds);
        time.Advance(10.1, moves: true);
        Assert.Equal(0.1, time.Seconds, 6);

        // Held still (its window not active): no time passes for it.
        Assert.False(time.Advance(12.0, moves: false));
        Assert.False(time.Advance(30.0, moves: false));
        Assert.Equal(0.1, time.Seconds, 6);

        // Moving again: a step at most a quarter of a second, so the rays never jump.
        time.Advance(40.0, moves: true);
        Assert.Equal(0.1 + Ambient.LongestStep, time.Seconds, 6);

        // Back on screen after a while: it starts from where it was.
        time.Resume();
        time.Advance(500.0, moves: true);
        Assert.Equal(0.1 + Ambient.LongestStep, time.Seconds, 6);

        // Round its cycle.
        var round = new AmbientTime(1);
        for (var t = 0.0; t <= 1.5; t += 0.05)
        {
            round.Advance(t, moves: true);
        }

        Assert.InRange(round.Seconds, 0, 1);
    }

    [Fact]
    public void A_light_off_screen_holds_still_and_the_monuments_rays_turn_both_ways()
    {
        // Not in a window, so never the active one: it waits and its time stays.
        var monument = new GsZenithMonument { IsEarned = true };
        Assert.Equal(Ambient.Waiting, ((IAmbientLight)monument).Advance(1));
        Assert.Equal(Ambient.Waiting, ((IAmbientLight)monument).Advance(2));
        Assert.Equal(0, monument.Time);
        var glow = new GsZenithGlow();
        Assert.Equal(Ambient.Waiting, ((IAmbientLight)glow).Advance(1));
        Assert.Equal(0, glow.Time);

        // The rays once a minute one way, the finer ones every 90 seconds the other; both, and the glow's eight-second
        // breaths, come back to the start together in the six minutes of its time.
        Assert.Equal(60, 360 / GsZenithMonument.Turn);
        Assert.Equal(-90, 360 / GsZenithMonument.FineTurn);
        foreach (var period in new[] { 60.0, 90.0, 8.0 })
        {
            Assert.Equal(0, 360 % period);
        }
    }
}
