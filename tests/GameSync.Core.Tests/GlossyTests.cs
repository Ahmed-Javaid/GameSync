using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Media;
using GameSync.UI.Theming;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// Glossy and Solid (LOOK-17, LOOK-18): the surface kept per PC and when Glossy falls back to Solid, the strength each
/// page takes, and the backdrop keeping every text colour readable over any art.
/// </summary>
public class GlossyTests
{
    private const int PictureWidth = 320;
    private const int PictureHeight = 200;

    /// <summary>Worst cases for a backdrop: flat white and black, saturated colours, a bright sky, hard stripes, noise.</summary>
    private static readonly (string Name, byte[] Bgra)[] Pictures =
    [
        ("white", Fill((_, _) => (255, 255, 255))),
        ("black", Fill((_, _) => (0, 0, 0))),
        ("red", Fill((_, _) => (255, 0, 0))),
        ("green", Fill((_, _) => (0, 255, 0))),
        ("blue", Fill((_, _) => (0, 0, 255))),
        ("yellow", Fill((_, _) => (255, 255, 0))),
        ("cyan", Fill((_, _) => (0, 255, 255))),
        ("magenta", Fill((_, _) => (255, 0, 255))),
        ("sky", Fill((_, y) => ((byte)(135 + 120 * y / PictureHeight), (byte)(206 + 49 * y / PictureHeight), 255))),
        ("white top, black bottom", Fill((_, y) => y < PictureHeight / 2 ? ((byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0))),
        ("stripes", Fill((x, _) => x / 24 % 2 == 0 ? ((byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0))),
        ("noise", Noise()),
    ];

    [Fact]
    public void LOOK_17_the_surface_is_kept_per_pc_and_a_fresh_install_starts_glossy()
    {
        using var world = new TestWorld();
        using var pc = world.Pc("DESKTOP");
        Assert.Equal(Look.Glossy, Look.Load(pc.State).Surface);

        (new Look() with { Surface = Look.Solid }).Save(pc.State);
        Assert.Equal(Look.Solid, Look.Load(pc.State).Surface);

        pc.State.SetSetting("look.surface", "shiny");
        Assert.Equal(Look.Glossy, Look.Load(pc.State).Surface);
    }

    [Fact]
    public void LOOK_18_glossy_goes_solid_in_light_mode_with_pure_black_and_without_transparency_effects()
    {
        var look = new Look();
        var dark = look.Resolve(windowsLight: false, null);
        Assert.True(look.ShowsGlossy(dark, transparencyOn: true));
        Assert.False(look.ShowsGlossy(dark, transparencyOn: false));
        Assert.False(look.ShowsGlossy((look with { Mode = Look.Light }).Resolve(false, null), true));
        Assert.False(look.ShowsGlossy((look with { PureBlack = true }).Resolve(false, null), true));
        Assert.False((look with { Surface = Look.Solid }).ShowsGlossy(dark, true));

        var matchWindows = look with { Mode = Look.MatchWindows };
        Assert.True(matchWindows.ShowsGlossy(matchWindows.Resolve(windowsLight: false, null), true));
        Assert.False(matchWindows.ShowsGlossy(matchWindows.Resolve(windowsLight: true, null), true));

        Assert.Null(ThemeEngine.Glass(new ThemeChoice(Mode: ThemeMode.Light), GlassStrength.Glass));
        Assert.Null(ThemeEngine.Glass(new ThemeChoice(PureBlack: true), GlassStrength.Home));
    }

    [Fact]
    public void LOOK_17_each_page_takes_its_strength_and_the_rail_goes_with_it()
    {
        var shell = new ShellViewModel(id => id switch
        {
            "home" => new HomeViewModel(),
            "library" => new LibraryViewModel(),
            "log" => new ConsoleViewModel(),
            _ => new PlaceholderViewModel("Settings", "settings", "Soon."),
        });

        // Home and the library are full glass, as a game's page is, since 1 Oct 2026 (the owner).
        Assert.Equal(GlassStrength.Glass, shell.Strength);
        shell.Open("library");
        Assert.Equal(GlassStrength.Glass, shell.Strength);
        shell.Open("log");
        Assert.Equal(GlassStrength.Glass, shell.Strength);
        shell.Open("settings");
        Assert.Equal(GlassStrength.Glow, shell.Strength);
        Assert.Equal(GlassStrength.Glow, ShellViewModel.StrengthOf(null));
    }

    [Fact]
    public void Shadows_read_as_the_design_system_writes_them()
    {
        Assert.Equal(0, ThemeService.ParseShadow("none").Count);

        var lift = ThemeService.ParseShadow("inset 0 1px 0 rgba(255, 255, 255, 0.05)")[0];
        Assert.True(lift.IsInset);
        Assert.Equal((0d, 1d, 0d, 0d), (lift.OffsetX, lift.OffsetY, lift.Blur, lift.Spread));
        Assert.Equal(Color.FromArgb(13, 255, 255, 255), lift.Color);

        var dialog = ThemeService.ParseShadow("0 24px 64px rgba(0, 0, 0, 0.6)")[0];
        Assert.False(dialog.IsInset);
        Assert.Equal((0d, 24d, 64d), (dialog.OffsetX, dialog.OffsetY, dialog.Blur));
        Assert.Equal(Color.FromArgb(153, 0, 0, 0), dialog.Color);
    }

    /// <summary>LOOK-18: every dark theme, every strength, every worst-case picture: text keeps 4.5:1 and controls 3:1.</summary>
    [Fact]
    public void LOOK_18_text_keeps_its_contrast_over_any_art()
    {
        // As the app makes them: softened, then their colour tamed.
        var softened = Pictures.Select(p => (p.Name, Soft: Backdrop.Tame(Backdrop.Soften(p.Bgra, PictureWidth, PictureHeight)))).ToList();
        var failures = new ConcurrentBag<string>();
        Parallel.ForEach(DarkChoices(), choice =>
        {
            var theme = ThemeEngine.Build(choice);
            foreach (var strength in Enum.GetValues<GlassStrength>())
            {
                var glass = ThemeEngine.Glass(choice, strength)!;
                foreach (var (name, soft) in softened)
                {
                    var margin = Backdrop.WorstMargin(Backdrop.Bake(soft, glass, Backdrop.Darkening(soft, glass, theme)), glass, theme);
                    if (margin < 1)
                    {
                        failures.Add($"{choice} {strength} over {name}: the weakest text has {margin.ToString("0.000", CultureInfo.InvariantCulture)} of the contrast it needs");
                    }
                }
            }
        });

        Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures.Take(30)));
    }

    /// <summary>
    /// The owner, 1 Oct 2026: Counter-Strike 2's orange flooded the app while Peak's and Risk of Rain 2's colours looked
    /// right. A backdrop's colourfulness eases off above <see cref="Backdrop.ChromaKnee"/> and never passes
    /// <see cref="Backdrop.ChromaLimit"/>; muted art is left as it is, and nothing gets lighter or darker.
    /// </summary>
    [Fact]
    public void LOOK_18_a_vivid_backdrop_is_toned_down_and_a_muted_one_kept()
    {
        var orange = Backdrop.Soften(Fill((_, _) => (235, 110, 20)), PictureWidth, PictureHeight);
        var teal = Backdrop.Soften(Fill((_, _) => (40, 70, 80)), PictureWidth, PictureHeight);
        Assert.True(Backdrop.Chroma(orange).Average > 0.17);

        var tamedOrange = Backdrop.Tame((double[])orange.Clone());
        var (average, highest) = Backdrop.Chroma(tamedOrange);
        Assert.InRange(highest, Backdrop.ChromaKnee, Backdrop.ChromaLimit);
        Assert.True(average < 0.15, $"The orange is still {average:0.000}.");
        Assert.Equal(Backdrop.Chroma(teal).Average, Backdrop.Chroma(Backdrop.Tame((double[])teal.Clone())).Average, 6);

        // Its lightness stays: about the same luminance as before, so the scrim and the contrast see the same art.
        double Luma(double[] rgb) => rgb.Chunk(3).Average(c => 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]);
        Assert.InRange(Luma(tamedOrange) / Luma(orange), 0.85, 1.15);
    }

    [Fact]
    public void LOOK_18_dark_art_keeps_the_designs_gradient_exactly_and_bright_art_is_darkened_more()
    {
        var theme = ThemeEngine.Build(new ThemeChoice());
        var black = Backdrop.Soften(Pictures.Single(p => p.Name == "black").Bgra, PictureWidth, PictureHeight);
        var white = Backdrop.Soften(Pictures.Single(p => p.Name == "white").Bgra, PictureWidth, PictureHeight);
        foreach (var strength in Enum.GetValues<GlassStrength>())
        {
            var glass = ThemeEngine.Glass(new ThemeChoice(), strength)!;
            var onBlack = Backdrop.Darkening(black, glass, theme);
            var onWhite = Backdrop.Darkening(white, glass, theme);
            for (var y = 0; y < Backdrop.Height; y++)
            {
                Assert.Equal(Between(glass.Stops, (y + 0.5) / Backdrop.Height), onBlack[y], 9);
                Assert.True(onWhite[y] >= onBlack[y], $"{strength}: row {y} is lighter over white art than over black.");
            }

            Assert.True(onWhite.Sum() > onBlack.Sum() + 1, $"{strength}: white art isn't darkened more.");
        }
    }

    [Fact]
    public void A_status_badge_on_cover_art_reads_over_any_art_in_both_surfaces()
    {
        (byte, byte, byte)[] arts = [(255, 255, 255), (0, 0, 0), (255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 0), (0, 255, 255), (255, 0, 255), (128, 128, 128)];
        foreach (var choice in DarkChoices().Append(new ThemeChoice(PureBlack: true)))
        {
            var theme = ThemeEngine.Build(choice);
            foreach (var status in new[] { "ok", "warn", "play", "danger", "neutral" })
            {
                var badge = ThemeService.ParseColor(theme[status + "-art"]);
                foreach (var (r, g, b) in arts)
                {
                    var a = badge.A / 255.0;
                    var ground = $"#{Mix(badge.R, r, a):x2}{Mix(badge.G, g, a):x2}{Mix(badge.B, b, a):x2}";
                    var ratio = ThemeEngine.Contrast(theme[status], ground);
                    Assert.True(ratio >= 4.5, $"{choice}: {status} on its badge over art ({r}, {g}, {b}) is {ratio:0.00}:1.");
                }
            }
        }

        static int Mix(byte over, byte under, double alpha) => (int)Math.Round(alpha * over + (1 - alpha) * under);
    }

    /// <summary>Every preset in dark mode (the accent preset with several accents), and Arcade with each secondary swatch.</summary>
    private static IEnumerable<ThemeChoice> DarkChoices()
    {
        foreach (var preset in ThemeEngine.Presets)
        {
            foreach (var accent in preset.FollowsAccent ? new string?[] { "#0078d4", "#e81123", "#107c10", "#ffb900", "#744da9", "#ffffff" } : [null])
            {
                yield return new ThemeChoice(preset.Id, Accent: accent);
            }
        }

        foreach (var swatch in ThemeEngine.Swatches)
        {
            yield return new ThemeChoice(Secondary: swatch.Id);
        }
    }

    private static double Between(IReadOnlyList<(double At, double Alpha)> stops, double at)
    {
        var i = 1;
        while (i < stops.Count - 1 && at > stops[i].At)
        {
            i++;
        }

        var (a, b) = (stops[i - 1], stops[i]);
        return at <= a.At ? a.Alpha : at >= b.At ? b.Alpha : a.Alpha + (b.Alpha - a.Alpha) * (at - a.At) / (b.At - a.At);
    }

    private static byte[] Fill(Func<int, int, (byte R, byte G, byte B)> colourAt)
    {
        var bgra = new byte[PictureWidth * PictureHeight * 4];
        for (var y = 0; y < PictureHeight; y++)
        {
            for (var x = 0; x < PictureWidth; x++)
            {
                var (r, g, b) = colourAt(x, y);
                var o = (y * PictureWidth + x) * 4;
                (bgra[o], bgra[o + 1], bgra[o + 2], bgra[o + 3]) = (b, g, r, 255);
            }
        }

        return bgra;
    }

    private static byte[] Noise()
    {
        var random = new Random(17);
        var bgra = new byte[PictureWidth * PictureHeight * 4];
        random.NextBytes(bgra);
        for (var o = 3; o < bgra.Length; o += 4)
        {
            bgra[o] = 255;
        }

        return bgra;
    }
}
