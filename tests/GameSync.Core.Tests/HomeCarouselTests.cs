using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// The owner's review of 3 Oct 2026, late (design system version 37): Home's banner moves between the games played last
/// (KAN-125) and a click on it opens the game's page (KAN-126); and Glossy's colours change in place, so a change of mode
/// doesn't make the window look every colour up twice (KAN-124).
/// </summary>
public class HomeCarouselTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 22, 0, 0, DateTimeKind.Local);

    [Fact]
    public void KAN_125_Homes_banner_moves_between_the_games_played_last_round_and_round_its_card_following()
    {
        // The owner: "I want the main screens hero card to be scrollable ... Recently played games?"
        var games = new[]
        {
            Game("apex", "Apex Legends", hoursAgo: 1),
            Game("ror2", "Risk of Rain 2", hoursAgo: 30),
            Game("valheim", "Valheim", hoursAgo: 50),
            Game("core", "Core Keeper", hoursAgo: 70),
            Game("bloons", "Bloons TD 6", hoursAgo: 90),
            Game("cs2", "Counter-Strike 2", hoursAgo: 110),
            Game("world", "Server world", hoursAgo: 2) with { IsFolder = true },
            Game("never", "Never played", hoursAgo: null),
        };
        var opened = new List<GameId>();
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { }) { OpenGame = opened.Add };
        var card = Achievements(games[0]);
        var home = HomeViewModel.From(Home(games[0]), games, Now, actions, achievements: (card, false, false),
            achievementsOf: game => (Achievements(game), false, false));

        // The hero first, then the next four played; a folder of your own isn't played, and a game never played isn't one.
        Assert.Equal(["Apex Legends", "Risk of Rain 2", "Valheim", "Core Keeper", "Bloons TD 6"], home.HeroSlides.Select(s => s.Title));
        Assert.True(home.HasHeroPages);
        Assert.Equal(("Apex Legends", "Apex Legends"), (home.HeroTitle, home.Achievements!.Title));
        Assert.Equal("Apex Legends, 1 of 5. Enter opens its page; Left and Right show the other games you played last.", home.HeroSpoken);

        // The next game: everything on the banner, the card, and the dots follow, and the window is told, for its colours.
        var shown = 0;
        home.HeroShown += () => shown++;
        home.NextHeroCommand.Execute(null);
        Assert.Equal(("Risk of Rain 2", "Risk of Rain 2", 1), (home.HeroTitle, home.Achievements!.Title, shown));
        Assert.Equal([false, true, false, false, false], home.HeroDots.Select(d => d.IsCurrent));
        Assert.Equal("Risk of Rain 2, 2 of 5", home.HeroDots[1].Name);

        // Round from the first to the last and back; a dot shows its game.
        home.PreviousHeroCommand.Execute(null);
        home.PreviousHeroCommand.Execute(null);
        Assert.Equal("Bloons TD 6", home.HeroTitle);
        home.NextHeroCommand.Execute(null);
        Assert.Equal("Apex Legends", home.HeroTitle);
        home.HeroDots[2].Show.Execute(null);
        Assert.Equal("Valheim", home.HeroTitle);

        // KAN-126: the banner opens the shown game's page.
        home.OpenHeroCommand.Execute(null);
        Assert.Equal([GameId.Parse("valheim")], opened);

        // A refresh keeps the game shown; unless another game is first now (one started playing), which is then shown.
        var again = HomeViewModel.From(Home(games[0]), games, Now, actions);
        again.KeepShowing(home);
        Assert.Equal("Valheim", again.HeroTitle);
        var playing = HomeViewModel.From(Home(games[5] with { RunningSinceUtc = DateTime.UtcNow }), games, Now, actions);
        playing.KeepShowing(home);
        Assert.Equal("Counter-Strike 2", playing.HeroTitle);

        // One game: no pager, and nothing to move to.
        var one = HomeViewModel.From(Home(games[0]), [games[0]], Now, actions);
        Assert.False(one.HasHeroPages);
        Assert.Equal("Apex Legends. Enter opens its page.", one.HeroSpoken);
        Assert.False(one.NextHeroCommand.CanExecute(null));
    }

    [Fact]
    public void KAN_126_a_banner_that_opens_its_games_page_takes_focus_and_shows_the_hand()
    {
        var banner = new UI.Controls.GsHeroBanner();
        Assert.False(banner.Focusable);
        banner.OpenCommand = new RelayCommand(() => { });
        Assert.True(banner.Focusable);
        Assert.Contains(":openable", banner.Classes);
    }

    [Fact]
    public void KAN_124_Glossys_colours_change_in_place_so_the_window_doesnt_look_them_all_up_again()
    {
        var scope = new Avalonia.Controls.ResourceDictionary();
        UI.Theming.ThemeService.Scope(scope, new Dictionary<string, string> { ["bg-100"] = "#101214", ["bg-200"] = "rgba(16, 18, 20, 0.5)", ["lift-card"] = "none" });
        var brush = (SolidColorBrush)scope["bg-100"]!;

        // Dark to light: the same brush, recoloured, so what's bound to it only repaints; a token the new look lacks goes.
        UI.Theming.ThemeService.Scope(scope, new Dictionary<string, string> { ["bg-100"] = "#e6e9ee", ["lift-card"] = "0 1px 3px rgba(20, 25, 30, 0.08)" });
        Assert.Same(brush, scope["bg-100"]);
        Assert.Equal(Color.Parse("#e6e9ee"), brush.Color);
        Assert.True(scope.TryGetResource("bg-100-color", null, out var color));
        Assert.Equal(Color.Parse("#e6e9ee"), color);
        Assert.False(scope.ContainsKey("bg-200"));
        Assert.False(scope.TryGetResource("bg-200-color", null, out _));

        // The colours are swapped in whole, one dictionary, not written one by one.
        Assert.Single(scope.MergedDictionaries);

        // Solid: none of it.
        UI.Theming.ThemeService.Scope(scope, null);
        Assert.Empty(scope);
        Assert.Empty(scope.MergedDictionaries);
    }

    private static LauncherGame Game(string id, string title, double? hoursAgo) => new()
    {
        Id = GameId.Parse(id),
        Title = title,
        Store = StoreKind.Steam,
        Installed = true,
        LastPlayedUtc = hoursAgo is { } hours ? DateTime.UtcNow.AddHours(-hours) : null,
    };

    private static LauncherHome Home(LauncherGame hero) =>
        new(hero, [], [], [new ActivityMonth(new DateTime(2026, 10, 1), [0, 0, 0], 3, "October", [[], [], []])], 0, 0);

    /// <summary>A game's achievements, one of them unlocked, so its card names it.</summary>
    private static GameAchievementsView Achievements(LauncherGame game) =>
        new(game.Id, game.Title, 1, [new AchievementShown("A", "First", null, false, DateTime.UtcNow.AddDays(-1), null, null, 50), new AchievementShown("B", "Second", null, false, null, null, null, 20)]);
}
