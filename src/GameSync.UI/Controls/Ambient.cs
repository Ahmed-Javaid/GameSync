using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace GameSync.UI.Controls;

/// <summary>
/// The clock for light that keeps moving while a page waits (design system version 50): a game's 100% ring's rays, glow
/// and glint (<see cref="GsZenithGlow"/>) and the Zeniths monument's turning light (<see cref="GsZenithMonument"/>). It
/// ticks 15 times a second, 30 while a light asks (a glint running round), and gives each light the time on one clock.
/// Avalonia's own animations run at the screen's rate, 142 frames a second on the owner's 144 Hz screen, and drawing on
/// the CPU every frame costs the window about 3 ms whatever moved, so one slowly turning ring took half a core (measured
/// 6 Oct 2026); slow light looks the same at 15. A light holds still while its window isn't the active one, and with
/// Windows' animation effects off.
/// </summary>
internal static class Ambient
{
    /// <summary>The slow tick, for light that turns or breathes: 15 a second.</summary>
    public static readonly TimeSpan Slow = TimeSpan.FromSeconds(1.0 / 15);

    /// <summary>The quick tick, for light running round fast, as a glint does: 30 a second.</summary>
    public static readonly TimeSpan Quick = TimeSpan.FromSeconds(1.0 / 30);

    /// <summary>How often a light that holds still (its window not active) looks again.</summary>
    public static readonly TimeSpan Waiting = TimeSpan.FromMilliseconds(500);

    /// <summary>The longest step a light takes at once, so it carries on where it stopped rather than jumping.</summary>
    public const double LongestStep = 0.25;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly List<IAmbientLight> Lights = [];
    private static DispatcherTimer? _timer;

    /// <summary>Seconds on the clock.</summary>
    public static double Now => Clock.Elapsed.TotalSeconds;

    /// <summary>How many lights the clock is moving now.</summary>
    public static int Count => Lights.Count;

    /// <summary>A light starts taking ticks: it's on screen.</summary>
    public static void Start(IAmbientLight light)
    {
        if (!Lights.Contains(light))
        {
            Lights.Add(light);
        }

        Schedule(Slow);
    }

    /// <summary>A light stops taking ticks: it's off screen. With none left the clock stops.</summary>
    public static void Stop(IAmbientLight light)
    {
        Lights.Remove(light);
        if (Lights.Count == 0)
        {
            _timer?.Stop();
        }
    }

    /// <summary>Whether <paramref name="light"/> moves now: Windows' animation effects on, and its window the active one.</summary>
    public static bool Moves(Visual light) => Motion.On && TopLevel.GetTopLevel(light) is Window { IsActive: true };

    /// <summary>Each light takes the time and says when it wants the next tick; the soonest is the next.</summary>
    internal static void Tick()
    {
        var now = Now;
        var next = Waiting;
        foreach (var light in Lights.ToArray())
        {
            var wants = light.Advance(now);
            if (wants < next)
            {
                next = wants;
            }
        }

        if (Lights.Count > 0)
        {
            Schedule(next);
        }
    }

    private static void Schedule(TimeSpan after)
    {
        if (_timer is null)
        {
            // Below input, so a tick never holds up a click or a key.
            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Tick += (_, _) => Tick();
        }

        _timer.Stop();
        _timer.Interval = after < Quick ? Quick : after;
        _timer.Start();
    }
}

/// <summary>A light on the <see cref="Ambient"/> clock.</summary>
internal interface IAmbientLight
{
    /// <summary>Moves the light to <paramref name="seconds"/> on the clock and says how soon it wants the next tick.</summary>
    TimeSpan Advance(double seconds);
}

/// <summary>
/// A light's own time on the <see cref="Ambient"/> clock: it runs only while the light moves, a step at most
/// <see cref="Ambient.LongestStep"/>, so a light held still carries on from where it was, round a cycle of
/// <see cref="Cycle"/> seconds.
/// </summary>
internal sealed class AmbientTime(double cycle)
{
    private double _last = double.NaN;

    /// <summary>The seconds after which the light looks the same as at 0.</summary>
    public double Cycle { get; } = cycle;

    /// <summary>Seconds on the light's own time, 0 up to <see cref="Cycle"/>.</summary>
    public double Seconds { get; private set; }

    /// <summary>The clock says <paramref name="now"/>: the light's time moves on if it <paramref name="moves"/>, which it returns.</summary>
    public bool Advance(double now, bool moves)
    {
        var step = double.IsNaN(_last) ? 0 : Math.Clamp(now - _last, 0, Ambient.LongestStep);
        _last = now;
        if (moves)
        {
            Seconds = (Seconds + step) % Cycle;
        }

        return moves;
    }

    /// <summary>The light came back on screen: its next step starts from there, not from when it left.</summary>
    public void Resume() => _last = double.NaN;
}
