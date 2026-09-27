using System.Text.Json;
using GameSync.UI.Theming;

namespace GameSync.Core.Tests;

/// <summary>LOOK-03 to LOOK-09: the theme engine matches the design system's own, and every theme meets contrast.</summary>
public class ThemeTests
{
    private static readonly ThemeMode[] Modes = [ThemeMode.Dark, ThemeMode.Light];

    [Fact]
    public void The_engine_gives_exactly_the_design_systems_colours()
    {
        using var goldens = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "theme-goldens.json")));
        var checkedChoices = 0;
        foreach (var golden in goldens.RootElement.EnumerateArray())
        {
            var c = golden.GetProperty("choice");
            string? Text(string name) => c.TryGetProperty(name, out var p) ? p.GetString() : null;
            var choice = new ThemeChoice(
                Text("preset")!,
                Text("mode") == "light" ? ThemeMode.Light : ThemeMode.Dark,
                c.TryGetProperty("pureBlack", out var black) && black.GetBoolean(),
                Text("primary"),
                Text("secondary"),
                Text("accent"));
            var built = ThemeEngine.Build(choice);
            var expected = golden.GetProperty("tokens").EnumerateObject().ToDictionary(t => t.Name, t => t.Value.GetString()!);

            Assert.Equal(expected.Keys.Order(), built.Keys.Order());
            foreach (var (token, value) in expected)
            {
                Assert.True(value == built[token], $"{choice}: {token} is {built[token]}, the design system's engine gives {value}.");
            }

            checkedChoices++;
        }

        Assert.Equal(105, checkedChoices);
    }

    [Fact]
    public void LOOK_09_every_preset_mode_and_swatch_meets_contrast()
    {
        var failures = new List<string>();
        foreach (var choice in EveryChoice())
        {
            var t = ThemeEngine.Build(choice);
            void Need(string fore, string back, double min, string why)
            {
                var ratio = ThemeEngine.Contrast(t[fore], t[back]);
                if (ratio < min)
                {
                    failures.Add($"{choice}: {fore} on {back} is {ratio:0.00}:1, needs {min}:1 ({why})");
                }
            }

            string[] textGrounds = ["bg-000", "bg-100", "bg-200", "bg-300", "secondary-soft", "primary-soft"];
            foreach (var ground in textGrounds)
            {
                Need("ink", ground, 4.5, "text");
                Need("ink-muted", ground, 4.5, "text");
                Need("ink-faint", ground, 4.5, "text");
            }

            foreach (var ground in new[] { "bg-000", "bg-100", "bg-200", "bg-300" })
            {
                Need("primary", ground, 3, "focus rings and control fills");
                Need("line-200", ground, 3, "control borders");
            }

            Need("line-200", "secondary-soft", 3, "checkbox borders on selected rows");
            Need("on-primary", "primary", 4.5, "text on the primary button");
            Need("on-primary", "primary-strong", 4.5, "text on the primary button, hovered");
            Need("on-secondary", "secondary", 4.5, "text on a secondary fill");
            Need("primary", "primary-soft", 4.5, "the colour on its own soft ground");
            Need("secondary", "bg-200", 3, "icons of the current page and tab");
            Need("secondary", "secondary-soft", 3, "icons on the current page and tab");
            foreach (var status in new[] { "ok", "warn", "danger", "play" })
            {
                Need(status, status + "-soft", 4.5, "a status badge");
                Need(status, "bg-000", 4.5, "a status in the console table");
                Need(status, "bg-200", 4.5, "a status on a card");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(40)));
    }

    [Fact]
    public void LOOK_02_pure_black_makes_the_page_black_and_keeps_cards_visible()
    {
        var black = ThemeEngine.Build(new ThemeChoice(PureBlack: true));
        var light = ThemeEngine.Build(new ThemeChoice(Mode: ThemeMode.Light, PureBlack: true));

        Assert.Equal("#000000", black["bg-100"]);
        Assert.NotEqual(black["bg-100"], black["bg-200"]);
        Assert.NotEqual("#000000", light["bg-100"]);
    }

    [Fact]
    public void LOOK_08_status_colours_ignore_the_theme()
    {
        foreach (var mode in Modes)
        {
            var arcade = ThemeEngine.Build(new ThemeChoice(Mode: mode));
            foreach (var choice in EveryChoice().Where(c => c.Mode == mode))
            {
                var other = ThemeEngine.Build(choice);
                foreach (var token in new[] { "ok", "warn", "danger", "play", "neutral", "on-art", "art-scrim", "glass" })
                {
                    Assert.Equal(arcade[token], other[token]);
                }
            }
        }
    }

    [Fact]
    public void LOOK_04_the_windows_accent_preset_follows_the_accent()
    {
        var blue = ThemeEngine.Build(new ThemeChoice("windows", Accent: "#0078d4"));
        var red = ThemeEngine.Build(new ThemeChoice("windows", Accent: "#e81123"));

        Assert.NotEqual(blue["primary"], red["primary"]);
        Assert.Equal("#61bbff", blue["primary"]);
    }

    private static IEnumerable<ThemeChoice> EveryChoice()
    {
        string?[] swatches = [null, .. ThemeEngine.Swatches.Select(s => s.Id)];
        foreach (var preset in ThemeEngine.Presets)
        {
            foreach (var (mode, black) in new[] { (ThemeMode.Dark, false), (ThemeMode.Dark, true), (ThemeMode.Light, false) })
            {
                foreach (var primary in swatches)
                {
                    foreach (var secondary in swatches)
                    {
                        foreach (var accent in preset.FollowsAccent ? new string?[] { "#0078d4", "#e81123", "#107c10", "#ffb900", "#744da9" } : [null])
                        {
                            yield return new ThemeChoice(preset.Id, mode, black, primary, secondary, accent);
                        }
                    }
                }
            }
        }
    }
}
