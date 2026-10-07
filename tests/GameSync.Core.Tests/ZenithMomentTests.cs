using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Host;
using GameSync.UI.ViewModels;
using static GameSync.Core.Tests.AchievementTests;

namespace GameSync.Core.Tests;

/// <summary>
/// Design system version 49 (the owner, 5 Oct 2026: "can the 100% be animated? or glowy or something to show its zenith. I
/// really want players to feel awarded when they hit 100%"): the first time a game's 100% is seen on this PC, on its page or
/// on Home, its moment plays, once; it's remembered per game on the PC. The tiers are banners (Fuji, the Matterhorn, K2,
/// Everest), the Zenith's in red and gold.
/// </summary>
public class ZenithMomentTests
{
    private static readonly GameId Sekiro = GameId.Parse("sekiro");

    [Fact]
    public void A_games_Zenith_once_seen_on_this_PC_is_remembered()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        Assert.Empty(Achievements.ZenithsSeen(data));
        using (var state = new Core.State.StateStore(data))
        {
            Assert.False(Achievements.ZenithSeen(state, Sekiro));
        }

        Achievements.SeeZenith(data, Sekiro);
        Assert.Equal([Sekiro], Achievements.ZenithsSeen(data));
        using (var state = new Core.State.StateStore(data))
        {
            Assert.True(Achievements.ZenithSeen(state, Sekiro));
        }
    }

    [Fact]
    public void The_first_time_a_games_100_percent_is_seen_its_moment_plays_once()
    {
        using var world = new TestWorld();
        var now = DateTime.Now;
        var seen = new List<GameId>();
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { }) { SeeZenith = seen.Add };
        var game = new LauncherGame { Id = Sekiro, Title = "Sekiro: Shadows Die Twice", Store = StoreKind.Steam, SteamAppId = 814380, Installed = true, LastPlayedUtc = DateTime.UtcNow };
        var finished = View(("A", true, false, 50, 2), ("B", true, false, 3, 1)) with { Game = Sekiro, Title = game.Title };
        var unfinished = View(("A", true, false, 50, 2), ("B", false, false, 3, 0)) with { Game = Sekiro, Title = game.Title };
        GameViewModel Page(GameAchievementsView view, bool zenithSeen, bool leftOut = false)
        {
            var page = new GameViewModel(game, HomeViewModel.Tile(game, now), actions);
            page.Show(new GameDetail { Id = Sekiro, Achievements = view, AchievementsLeftOut = leftOut, ZenithSeen = zenithSeen }, now);
            return page;
        }

        // A game's page: a finished game not seen yet plays its moment; saying it played remembers it, and it doesn't again.
        var first = Page(finished, zenithSeen: false);
        Assert.True(first.CelebratesZenith);
        first.SeeZenithCommand.Execute(null);
        Assert.False(first.CelebratesZenith);
        Assert.Equal([Sekiro], seen);
        first.Show(new GameDetail { Id = Sekiro, Achievements = finished, ZenithSeen = false }, now);
        Assert.False(first.CelebratesZenith);

        // Never for one seen before, one not finished, or one left out of the achievements.
        Assert.False(Page(finished, zenithSeen: true).CelebratesZenith);
        Assert.False(Page(unfinished, zenithSeen: false).CelebratesZenith);
        Assert.False(Page(finished, zenithSeen: false, leftOut: true).CelebratesZenith);

        // Home's card: the same, for the game the banner shows; once.
        Assert.True(HomeAchievements.Of(finished, game, false, [game], now, zenithSeen: false).CelebratesZenith);
        Assert.False(HomeAchievements.Of(finished, game, false, [game], now).CelebratesZenith);
        Assert.False(HomeAchievements.Of(unfinished, game, false, [game], now, zenithSeen: false).CelebratesZenith);
        using var state = new Core.State.StateStore(Path.Combine(world.Root, "data"));
        var home = HomeViewModel.From(Launcher.Home([game], state, now), [game], now, actions, achievements: (finished, false, false), zenithsSeen: new HashSet<GameId>());
        Assert.True(home.CelebratesZenith);
        home.SeeZenithCommand.Execute(null);
        Assert.False(home.CelebratesZenith);
        Assert.Equal([Sekiro, Sekiro], seen);
        home.SeeZenithCommand.Execute(null);
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void Each_tier_is_a_banner_and_the_Zeniths_words_are_gold_to_red()
    {
        // The banner's box is the design's 120 x 190; under 40px tall it keeps its cloth, one border and its mountain.
        Assert.Equal(120.0 / 190, UI.Controls.GsTierBanner.Aspect);
        Assert.Equal(40, UI.Controls.GsTierBanner.FullFrom);
        var banner = new UI.Controls.GsTierBanner();
        Assert.Equal("zenith", banner.Tier);

        // Zenith's word: gold to red in dark, a deep gold to the Zenith's red in light, readable on white.
        foreach (var (mode, light) in new[] { ("dark", false), ("light", true) })
        {
            var tokens = UI.Theming.ThemeEngine.Build(new UI.Theming.ThemeChoice(Mode: light ? UI.Theming.ThemeMode.Light : UI.Theming.ThemeMode.Dark));
            Assert.Equal(light ? "#8f6214" : "#ffd76e", tokens["zenith-ink"]);
            Assert.Equal(light ? "#a51d32" : "#ef6a5a", tokens["zenith-ink-deep"]);
            if (light)
            {
                Assert.True(UI.Theming.ThemeEngine.Contrast(tokens["zenith-ink"], "#ffffff") >= 4.5, mode);
                Assert.True(UI.Theming.ThemeEngine.Contrast(tokens["zenith-ink-deep"], "#ffffff") >= 4.5, mode);
            }
        }
    }

    [Fact]
    public void ACH_12_the_Zeniths_dot_runs_orange_to_red_as_the_design_draws_it()
    {
        // Design system version 52 (the owner, 7 Oct 2026: "have dot version for zenith, not the flag. Use a red orange
        // gradient, similar to the text color right now"): the app's dot is the design's, light at its top left as the
        // metals' dots are, fixed in every theme.
        var root = RepoRoot();
        var css = File.ReadAllText(Path.Combine(root, "design", "system", "components", "bundle.css"));
        var design = System.Text.RegularExpressions.Regex.Match(css, @"\.gs-tier-dot\.gs-tier--zenith \{ background: linear-gradient\(135deg, (#\w+), (#\w+)\)");
        Assert.True(design.Success, "the design's Zenith dot");
        var axaml = File.ReadAllText(Path.Combine(root, "src", "GameSync.UI", "Styles", "Achievements.axaml"));
        var app = System.Text.RegularExpressions.Regex.Match(axaml,
            @"<LinearGradientBrush x:Key=""zenith-dot"" StartPoint=""0%,0%"" EndPoint=""100%,100%"">\s*<GradientStop Color=""(#\w+)"" Offset=""0"" />\s*<GradientStop Color=""(#\w+)"" Offset=""1"" />");
        Assert.True(app.Success, "the app's zenith-dot");
        Assert.Equal(design.Groups[1].Value, app.Groups[1].Value, ignoreCase: true);
        Assert.Equal(design.Groups[2].Value, app.Groups[2].Value, ignoreCase: true);
    }

    private static string RepoRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "GameSync.sln")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The repository wasn't found above the tests.");
    }
}
