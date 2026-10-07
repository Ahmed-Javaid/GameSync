using System.Runtime.InteropServices;

namespace GameSync.Windows;

/// <summary>
/// ACH-09: the achievement popup's sound (the owner, 3 Oct 2026: "a nice sound to show they've unlocked an
/// achievement"). GameSync makes each chime itself, no sound file from anywhere, in one of a few sounds at one of a few
/// volumes the person picks in Settings (KAN-120, the owner: "The popup sound is too loud, it needs to be softer. Give me
/// options i can choose from"). Each sound has a short run of notes per tier: Bronze two, Silver three, Gold three and a
/// higher one, and a Zenith its own rising run over a soft chord. Played by Windows under its own volume, never waiting.
/// </summary>
public static class Chime
{
    public const int SampleRate = 44_100;

    public const string DefaultSound = "bell";

    public const string DefaultVolume = "soft";

    /// <summary>The sounds to choose from, by id and name.</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> Sounds =
        [("bell", "Soft bell"), ("marimba", "Marimba"), ("glass", "Glass"), ("harp", "Harp"), ("pop", "Pop")];

    /// <summary>
    /// The volumes to choose from, by id and name, each as how loud the chime is on average over its notes (its RMS, a
    /// fraction of full scale), so every sound is about as loud as the others at the same volume. Loud is still below the
    /// first chime, which the owner found too loud.
    /// </summary>
    public static readonly IReadOnlyList<(string Id, string Name, double Level)> Volumes =
        [("quiet", "Quiet", 0.025), ("soft", "Soft", 0.045), ("medium", "Medium", 0.075), ("loud", "Loud", 0.11)];

    /// <summary>No chime goes past this, however loud its volume: clear but never harsh over a game.</summary>
    public const double Ceiling = 0.6;

    private const uint SndAsync = 0x1;
    private const uint SndNoDefault = 0x2;
    private const uint SndMemory = 0x4;

    private static readonly Dictionary<string, GCHandle> Pinned = [];
    private static readonly object Gate = new();

    /// <summary>
    /// Plays a tier's chime (<c>bronze</c>, <c>silver</c>, <c>gold</c> or <c>zenith</c>; anything else Bronze's) in a sound
    /// and at a volume from <see cref="Sounds"/> and <see cref="Volumes"/> (the defaults for anything else).
    /// </summary>
    public static void Play(string? tier, string? sound = null, string? volume = null)
    {
        try
        {
            IntPtr wave;
            lock (Gate)
            {
                var key = $"{Tier(tier)}/{SoundOf(sound)}/{VolumeOf(volume).Id}";
                if (!Pinned.TryGetValue(key, out var handle))
                {
                    // Kept for as long as GameSync runs: Windows reads it while it plays, after this returns.
                    handle = GCHandle.Alloc(Wave(tier, sound, volume), GCHandleType.Pinned);
                    Pinned[key] = handle;
                }

                wave = handle.AddrOfPinnedObject();
            }

            PlaySound(wave, IntPtr.Zero, SndMemory | SndAsync | SndNoDefault);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>A tier's chime as a WAV file in memory: 16-bit mono at <see cref="SampleRate"/>.</summary>
    public static byte[] Wave(string? tier, string? sound = null, string? volume = null)
    {
        var samples = Samples(Tier(tier), SoundOf(sound), VolumeOf(volume).Level);
        var data = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Round(Math.Clamp(samples[i], -1, 1) * short.MaxValue);
            data[i * 2] = (byte)value;
            data[i * 2 + 1] = (byte)(value >> 8);
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + data.Length);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(data.Length);
            writer.Write(data);
        }

        return stream.ToArray();
    }

    private static string Tier(string? tier) => tier is "silver" or "gold" or "zenith" ? tier : "bronze";

    private static string SoundOf(string? sound) => Sounds.Any(s => s.Id == sound) ? sound! : DefaultSound;

    private static (string Id, string Name, double Level) VolumeOf(string? volume) =>
        Volumes.FirstOrDefault(v => v.Id == volume) is { Id: not null } found ? found : Volumes.First(v => v.Id == DefaultVolume);

    /// <summary>A tier's notes: when each starts (seconds) and its pitch (Hz), from A major, brighter for rarer ones.</summary>
    private static (double At, double Hz)[] Notes(string tier) => tier switch
    {
        "silver" => [(0, 659.26), (0.09, 880.0), (0.18, 1108.73)],
        "gold" => [(0, 880.0), (0.08, 1108.73), (0.16, 1318.51), (0.26, 1760.0)],
        "zenith" => [(0, 440.0), (0.07, 554.37), (0.14, 659.26), (0.21, 880.0), (0.28, 1108.73), (0.35, 1318.51)],
        _ => [(0, 659.26), (0.11, 880.0)],
    };

    private static double[] Samples(string tier, string sound, double level)
    {
        var length = (tier, sound) switch
        {
            (_, "pop") => tier == "zenith" ? 1.2 : 0.7,
            ("zenith", _) => 2.4,
            ("gold", _) => 1.6,
            ("silver", _) => 1.3,
            _ => 1.1,
        };
        var samples = new double[(int)(length * SampleRate)];
        var notes = Notes(tier);
        var random = new Random(7);
        foreach (var (at, hz) in notes)
        {
            switch (sound)
            {
                case "marimba":
                    // Wood: a low octave, its fundamental and the bar's bright overtone dying fast.
                    Partials(samples, at, hz / 2, [(1, 1, 0.42), (3.93, 0.35, 0.07), (10.2, 0.05, 0.02)], 0.002);
                    break;
                case "glass":
                    // Glass: a struck goblet's partials, which aren't harmonic, ringing long, eased in.
                    Partials(samples, at, hz, [(1, 1, 1.1), (2.76, 0.32, 0.5), (5.4, 0.1, 0.25)], 0.012);
                    break;
                case "harp":
                    Pluck(samples, at, hz / 2, random);
                    break;
                case "pop":
                    Pop(samples, at, hz);
                    break;
                default:
                    // A soft bell: its tone and two quiet partials, eased in so the strike isn't sharp.
                    Partials(samples, at, hz, [(1, 1, tier == "zenith" ? 0.42 : 0.32), (2, 0.3, 0.2), (3, 0.08, 0.13)], 0.008);
                    break;
            }
        }

        if (tier == "zenith" && sound != "pop")
        {
            // A soft chord under the run, swelling in as it ends.
            foreach (var hz in new[] { 440.0, 659.26, 880.0 })
            {
                Pad(samples, 0.3, sound == "marimba" ? hz / 2 : hz);
            }
        }

        // As loud as the volume asks, measured as the run's average loudness so every sound matches the others, never past
        // the ceiling, then a short fade at the very end so it never clicks.
        var loud = (int)(Math.Min(length, notes[^1].At + 0.35) * SampleRate);
        var rms = Math.Sqrt(samples.Take(loud).Sum(v => v * v) / loud);
        var loudest = samples.Max(Math.Abs);
        var scale = rms > 0 ? Math.Min(level / rms, Ceiling / loudest) : 0;
        var fade = (int)(0.05 * SampleRate);
        for (var i = 0; i < samples.Length; i++)
        {
            var tail = samples.Length - 1 - i;
            samples[i] *= scale * (tail < fade ? (double)tail / fade : 1);
        }

        return samples;
    }

    /// <summary>A struck note: partials of the given ratios, amplitudes and decay times (seconds), after an attack of <paramref name="attack"/> seconds.</summary>
    private static void Partials(double[] samples, double at, double hz, (double Ratio, double Amp, double Decay)[] partials, double attack)
    {
        var start = (int)(at * SampleRate);
        for (var i = start; i < samples.Length; i++)
        {
            var t = (double)(i - start) / SampleRate;
            var envelope = Math.Min(1, t / attack);
            var value = 0.0;
            foreach (var (ratio, amp, decay) in partials)
            {
                value += amp * Math.Sin(2 * Math.PI * hz * ratio * t) * Math.Exp(-t / decay);
            }

            samples[i] += envelope * value;
        }
    }

    /// <summary>A plucked string (Karplus and Strong): a soft burst of noise round a loop as long as the note's period, losing its brightness as it rings.</summary>
    private static void Pluck(double[] samples, double at, double hz, Random random)
    {
        var period = Math.Max(2, (int)Math.Round(SampleRate / hz));
        var loop = new double[period];
        var smooth = 0.0;
        for (var i = 0; i < period; i++)
        {
            // Smoothed noise: a gentle pluck rather than a bright one.
            smooth = 0.5 * smooth + 0.5 * (random.NextDouble() * 2 - 1);
            loop[i] = smooth;
        }

        var start = (int)(at * SampleRate);
        for (var i = start; i < samples.Length; i++)
        {
            var p = (i - start) % period;
            var next = loop[(p + 1) % period];
            var value = loop[p];
            samples[i] += value;
            loop[p] = 0.996 * 0.5 * (value + next);
        }
    }

    /// <summary>A soft pop: a short rounded note that slides up into its pitch.</summary>
    private static void Pop(double[] samples, double at, double hz)
    {
        var start = (int)(at * SampleRate);
        var phase = 0.0;
        for (var i = start; i < samples.Length; i++)
        {
            var t = (double)(i - start) / SampleRate;
            var pitch = hz * (0.62 + 0.38 * Math.Min(1, t / 0.035));
            phase += 2 * Math.PI * pitch / SampleRate;
            samples[i] += Math.Sin(phase) * Math.Min(1, t / 0.003) * Math.Exp(-t / 0.07);
        }
    }

    private static void Pad(double[] samples, double at, double hz)
    {
        var start = (int)(at * SampleRate);
        for (var i = start; i < samples.Length; i++)
        {
            var t = (double)(i - start) / SampleRate;
            var swell = Math.Min(1, t / 0.25) * Math.Exp(-t / 0.9);
            samples[i] += 0.22 * swell * (Math.Sin(2 * Math.PI * hz * t) + 0.3 * Math.Sin(2 * Math.PI * hz * 2 * t));
        }
    }

    [DllImport("winmm.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
}
