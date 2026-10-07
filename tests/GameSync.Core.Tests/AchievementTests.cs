using System.Text;
using GameSync.Core.Discovery;

namespace GameSync.Core.Tests;

/// <summary>ACH-01: a Steam game's achievements from the Steam client's own files on this PC, with no key.</summary>
public class AchievementTests
{
    internal const long Valheim = 892970;

    [Fact]
    public void ACH_01_Steams_own_files_give_each_achievement_and_when_it_was_unlocked()
    {
        using var world = new TestWorld();
        var steam = Path.Combine(world.Root, "Steam");
        var stats = Directory.CreateDirectory(Path.Combine(steam, "appcache", "stats")).FullName;
        Write(Path.Combine(stats, $"UserGameStatsSchema_{Valheim}.bin"), Kv.Block("892970",
            Kv.Text("gamename", "Valheim"),
            Kv.Block("stats",
                Kv.Block("1",
                    Kv.Text("type", "ACHIEVEMENTS"),
                    Kv.Block("bits",
                        Achievement(0, "ArrivedHere", Kv.Text("name", "I Have Arrived!"), "Arrive in Valheim", hidden: false, "arrived.jpg"),
                        Achievement(1, "Eikthyr", Kv.Text("name", "Hunter"), "Defeat Eikthyr", hidden: false, "hunter.jpg"),
                        Achievement(2, "Yagluth", Kv.Block("name", Kv.Text("german", "Fliegen"), Kv.Text("english", "Fly")), "Secret", hidden: true, "fly.jpg"))),
                Kv.Block("2", Kv.Int("type", 1), Kv.Text("name", "Deaths")))));

        // Two accounts played it; the one written last is the one shown. Steam keeps the bits as a signed number.
        var unlocked = new DateTimeOffset(2026, 9, 22, 15, 54, 0, TimeSpan.Zero);
        var older = Path.Combine(stats, $"UserGameStats_111_{Valheim}.bin");
        Write(older, Kv.Block("cache", Kv.Block("1", Kv.Int("data", 7))));
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-30));
        Write(Path.Combine(stats, $"UserGameStats_281062582_{Valheim}.bin"), Kv.Block("cache",
            Kv.Int("crc", -1385083358),
            Kv.Block("1", Kv.Int("data", unchecked((int)0x80000005)), Kv.Block("AchievementTimes", Kv.Int("0", (int)unlocked.ToUnixTimeSeconds())))));

        var game = SteamAchievements.Read(steam, Valheim)!;

        Assert.Equal(["I Have Arrived!", "Hunter", "Fly"], game.All.Select(a => a.Name));
        Assert.Equal(2, game.Unlocked);
        var arrived = game.All[0];
        Assert.Equal(("ArrivedHere", "Arrive in Valheim", "arrived.jpg", "arrived_gray.jpg"), (arrived.Id, arrived.Description, arrived.Icon, arrived.IconLocked));
        Assert.Equal(unlocked.UtcDateTime, arrived.UnlockedUtc);
        Assert.False(game.All[1].Unlocked);
        Assert.True(game.All[2].Hidden);

        // Unlocked, with no time kept: it counts, at an unknown time.
        Assert.Equal(DateTime.UnixEpoch, game.All[2].UnlockedUtc);

        // A game Steam has no list for, and a file cut short, read as none rather than wrong ones.
        Assert.Null(SteamAchievements.Read(steam, 730));
        File.WriteAllBytes(Path.Combine(stats, "UserGameStatsSchema_440.bin"), [0, (byte)'4', (byte)'4', (byte)'0', 0, 1, (byte)'x']);
        Assert.Null(SteamAchievements.Read(steam, 440));
    }

    [Fact]
    public async Task ACH_02_ACH_03_icons_and_rarity_are_asked_of_Steam_once_kept_and_checked()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var asked = new List<string>();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3];
        using var handler = new Steam(url =>
        {
            asked.Add(url);
            return url.Contains("GetGlobalAchievementPercentagesForApp", StringComparison.Ordinal)
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"achievementpercentages":{"achievements":[{"name":"ArrivedHere","percent":"15.9"},{"name":"Eikthyr","percent":2.4}]}}""") }
                : url.EndsWith("aaaaaaaaaaaaaaaaaaaa.jpg", StringComparison.Ordinal)
                    ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(png) }
                    : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("<html>not an image</html>") };
        });
        using var cache = new Core.Art.AchievementCache(data, handler);

        Assert.True(cache.Wants(Valheim, ["aaaaaaaaaaaaaaaaaaaa.jpg"]));
        Assert.True(await cache.FetchAsync(Valheim, ["aaaaaaaaaaaaaaaaaaaa.jpg", "bbbbbbbbbbbbbbbbbbbb.jpg", "../../evil.jpg", null!], CancellationToken.None));

        // The icon kept is the image; what wasn't one, and a name that could be a path, never are.
        Assert.NotNull(cache.Icon(Valheim, "aaaaaaaaaaaaaaaaaaaa.jpg"));
        Assert.Null(cache.Icon(Valheim, "bbbbbbbbbbbbbbbbbbbb.jpg"));
        Assert.Null(cache.Icon(Valheim, "../../evil.jpg"));
        Assert.DoesNotContain(asked, a => a.Contains("evil", StringComparison.Ordinal));
        Assert.Equal(15.9, cache.Rarity(Valheim)["ArrivedHere"]);
        Assert.Equal(2.4, cache.Rarity(Valheim)["Eikthyr"]);

        // Asked once: rarity waits a week, a kept icon is never asked for again.
        var before = asked.Count;
        await cache.FetchAsync(Valheim, ["aaaaaaaaaaaaaaaaaaaa.jpg"], CancellationToken.None);
        Assert.Equal(before, asked.Count);
    }

    [Fact]
    public void Home_shows_the_hero_games_achievements_or_the_last_played_game_with_some_when_the_hero_keeps_none()
    {
        // The owner, 3 Oct 2026: Home's Needs you card becomes the hero game's achievements. Their hero is Bloodborne on
        // shadPS4, a copy in its own folder, so the card shows the last Steam game played instead, saying so.
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Path.Combine(world.Root, "Steam");
        var stats = Directory.CreateDirectory(Path.Combine(steam, "appcache", "stats")).FullName;
        Write(Path.Combine(stats, $"UserGameStatsSchema_{Valheim}.bin"), Kv.Block("892970", Kv.Block("stats", Kv.Block("1", Kv.Text("type", "ACHIEVEMENTS"),
            Kv.Block("bits", Achievement(0, "ArrivedHere", Kv.Text("name", "I Have Arrived!"), "Arrive", hidden: false, "arrived.jpg"),
                Achievement(1, "Eikthyr", Kv.Text("name", "Hunter"), "Defeat Eikthyr", hidden: false, "hunter.jpg"))))));
        Write(Path.Combine(stats, $"UserGameStats_281062582_{Valheim}.bin"), Kv.Block("cache", Kv.Block("1", Kv.Int("data", 1),
            Kv.Block("AchievementTimes", Kv.Int("0", (int)new DateTimeOffset(2026, 9, 22, 15, 54, 0, TimeSpan.Zero).ToUnixTimeSeconds())))));

        var bloodborne = new Host.LauncherGame { Id = Model.GameId.Parse("bloodborne-goty"), Title = "Bloodborne GOTY", Store = StoreKind.Loose, Installed = true, LastPlayedUtc = DateTime.UtcNow };
        var valheim = new Host.LauncherGame { Id = Model.GameId.Parse("valheim"), Title = "Valheim", Store = StoreKind.Steam, SteamAppId = Valheim, Installed = true, LastPlayedUtc = DateTime.UtcNow.AddDays(-1) };
        var copy = valheim with { Id = Model.GameId.Parse("valheim-copy"), Store = StoreKind.Loose, LastPlayedUtc = DateTime.UtcNow.AddHours(-1) };

        var (card, heroHasNone, _) = Host.Achievements.ForHome(data, bloodborne, [bloodborne, copy, valheim], steam);
        Assert.Equal(("Valheim", 1, 2, true), (card!.Title, card.Unlocked, card.Total, heroHasNone));
        var home = UI.ViewModels.HomeAchievements.Of(card, bloodborne, heroHasNone, [bloodborne, valheim], DateTime.Now);
        Assert.Equal("Valheim, played yesterday. Bloodborne GOTY has none GameSync can read.", home.Subtitle);

        // A copy of a Steam game in its own folder, run without Steam (the owner's Sons of the Forest): Steam keeps nothing of
        // it, and the card says so rather than that GameSync couldn't read it.
        var sons = new Host.LauncherGame { Id = Model.GameId.Parse("sons-of-the-forest"), Title = "Sons of the Forest", Store = StoreKind.Loose, SteamAppId = 1326470, Installed = true, LastPlayedUtc = DateTime.UtcNow };
        var (sonsCard, sonsHasNone, _) = Host.Achievements.ForHome(data, sons, [sons, valheim], steam);
        Assert.Equal(("Valheim", true), (sonsCard!.Title, sonsHasNone));
        Assert.Equal("Valheim, played yesterday. Sons of the Forest isn't run by Steam here, so Steam keeps none of its achievements.",
            UI.ViewModels.HomeAchievements.Of(sonsCard, sons, sonsHasNone, [sons, valheim], DateTime.Now).Subtitle);
        Assert.Equal(["I Have Arrived!"], home.Latest.Select(a => a.Name));
        Assert.Equal(("50%", 1, false), (home.Progress.PercentText, home.Progress.ToGo, home.Progress.IsZenith));

        // A Steam hero shows its own, and a copy in its own folder never shows Steam's (ACH-01).
        Assert.Equal((card.Title, false), (Host.Achievements.ForHome(data, valheim, [valheim], steam).View!.Title, Host.Achievements.ForHome(data, valheim, [valheim], steam).HeroHasNone));
        Assert.Null(Host.Achievements.For(data, copy, steam));
    }

    [Fact]
    public void Tiers_come_from_rarity_and_a_Zenith_is_every_one_unlocked()
    {
        // Design system version 33 (the owner, 3 Oct 2026): Gold under 5% of Steam's players, Silver under 20%, Bronze the rest.
        Assert.Equal(new Host.AchievementTier?[] { Host.AchievementTier.Gold, Host.AchievementTier.Gold, Host.AchievementTier.Silver, Host.AchievementTier.Silver, Host.AchievementTier.Bronze, null },
            new double?[] { 0.4, 4.99, 5, 19.9, 20, null }.Select(Host.AchievementTiers.Of));
        Assert.Equal(["Ultra rare", "Very rare", "Rare", "Uncommon", "Common"], new double?[] { 0.9, 1, 5, 20, 50 }.Select(p => Host.AchievementTiers.Rarity(p)!));

        var view = View(("A", true, false, 2.0, 1), ("B", true, false, 12, 2), ("C", true, false, 60, 3), ("D", false, false, 30, 0));
        var progress = UI.ViewModels.AchievementsProgress.Of(view, DateTime.Now);
        Assert.Equal((1, 1, 1, 1), (progress.Gold, progress.Silver, progress.Bronze, progress.ToGo));
        Assert.False(progress.IsZenith);

        // 170 of 171 is 99%, never 100%: only every one unlocked is Zenith, on the day the last one was.
        Assert.Equal("99%", new UI.ViewModels.AchievementsProgress(170, 171, 0, 0, 0, null).PercentText);
        var all = View(("A", true, false, 2.0, 1), ("B", true, false, 12, 5));
        Assert.True(all.IsComplete);
        Assert.Equal(all.All[1].UnlockedUtc, all.CompletedUtc);
        Assert.True(UI.ViewModels.AchievementsProgress.Of(all, DateTime.Now).IsZenith);
    }

    [Fact]
    public void A_hidden_achievement_stays_hidden_until_it_is_unlocked()
    {
        // The owner, 3 Oct 2026: "Hidden achievements stay hidden until they're unlocked."
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Path.Combine(world.Root, "Steam");
        var stats = Directory.CreateDirectory(Path.Combine(steam, "appcache", "stats")).FullName;
        Write(Path.Combine(stats, $"UserGameStatsSchema_{Valheim}.bin"), Kv.Block("892970", Kv.Block("stats", Kv.Block("1", Kv.Text("type", "ACHIEVEMENTS"),
            Kv.Block("bits",
                Achievement(0, "ArrivedHere", Kv.Text("name", "I Have Arrived!"), "Arrive", hidden: false, "aaaaaaaaaaaaaaaaaaaa.jpg"),
                Achievement(1, "Moder", Kv.Text("name", "Dragon Slayer"), "Defeat Moder", hidden: true, "bbbbbbbbbbbbbbbbbbbb.jpg"),
                Achievement(2, "Queen", Kv.Text("name", "Royal Secret"), "Defeat the Queen", hidden: true, "cccccccccccccccccccc.jpg"))))));
        Write(Path.Combine(stats, $"UserGameStats_281062582_{Valheim}.bin"), Kv.Block("cache", Kv.Block("1", Kv.Int("data", 0b101),
            Kv.Block("AchievementTimes", Kv.Int("0", 1_758_556_440), Kv.Int("2", 1_758_642_840)))));
        var valheim = new Host.LauncherGame { Id = Model.GameId.Parse("valheim"), Title = "Valheim", Store = StoreKind.Steam, SteamAppId = Valheim, Installed = true };

        var view = Host.Achievements.For(data, valheim, steam)!;
        var secret = view.All.Single(a => a.Id == "Moder");
        Assert.True(secret.Secret);

        // Not even its picture is looked for; the one unlocked is like any other.
        Assert.Null(secret.IconFile);
        Assert.DoesNotContain("bbbbbbbbbbbbbbbbbbbb.jpg", view.EveryIcon);
        Assert.Contains("cccccccccccccccccccc.jpg", view.EveryIcon);
        var shown = UI.ViewModels.AchievementItem.Of(secret, DateTime.Now);
        Assert.Equal(("Hidden achievement", "Keep playing to reveal it.", "hidden"), (shown.Name, shown.Description, shown.State));
        Assert.DoesNotContain("Dragon", shown.Tip, StringComparison.Ordinal);
        Assert.DoesNotContain("Moder", shown.ToString(), StringComparison.Ordinal);

        var page = new UI.ViewModels.AchievementsViewModel(valheim.Id, "Valheim", null, () => { }, []);
        page.Show(view, DateTime.Now);
        Assert.Equal(["All 3", "Unlocked 2", "Hidden 1"], page.Tabs.Select(t => $"{t.Label} {t.Count}"));
        Assert.Equal(["Royal Secret", "I Have Arrived!", "Hidden achievement"], page.Shown.Select(a => a.Name));

        // A search never finds it, by its name or by what it asks: whether a word would find it is a secret too.
        page.Search = "dragon";
        Assert.Empty(page.Shown);
        Assert.Contains("Hidden ones aren't searched", page.Nothing, StringComparison.Ordinal);
        page.Search = "Moder";
        Assert.Empty(page.Shown);
        page.Search = "queen";
        Assert.Equal(["Royal Secret"], page.Shown.Select(a => a.Name));

        page.Search = "";
        page.Tab = UI.ViewModels.AchievementsViewModel.HiddenTab;
        Assert.Equal("Its name and what it asks stay secret until you unlock it.", page.PickedText);
        Assert.Equal("Not yet", page.PickedWhen);
    }

    [Fact]
    public void The_Achievements_page_tracks_Zeniths_and_what_is_closest_to_one()
    {
        // The owner, 3 Oct 2026: "a way to track platinum / 100% achievements".
        var now = DateTime.Now;
        var apex = View(("A", true, false, 2.0, 28), ("B", true, false, 40, 29)) with { Game = Model.GameId.Parse("apex"), Title = "Apex Legends" };
        var ror2 = View(("A", true, false, 1.0, 1), ("B", true, false, 10, 2), ("C", true, false, 50, 3), ("D", false, false, 3, 0)) with { Game = Model.GameId.Parse("ror2"), Title = "Risk of Rain 2" };
        var valheim = View(("A", true, false, 70, 4), ("B", false, false, 5, 0), ("C", false, false, 5, 0), ("D", false, false, 5, 0)) with { Game = Model.GameId.Parse("valheim"), Title = "Valheim" };
        var unplayed = View(("A", false, false, 70, 0)) with { Game = Model.GameId.Parse("deadlock"), Title = "Deadlock" };

        var room = new UI.ViewModels.TrophyRoomViewModel();
        room.Show([unplayed, valheim, ror2, apex], now);

        Assert.Equal(["Apex Legends"], room.Zeniths.Select(g => g.Title));
        Assert.Equal(("1", "Latest: Apex Legends"), (room.ZenithCount, room.ZenithSub));

        // KAN-128: the monument says it in two lines, so a long name isn't broken at its hyphen, and in full for screen readers.
        Assert.Equal(("Latest:\nApex Legends", "Zeniths: 1, the latest Apex Legends"), (room.ZenithSubLines, room.ZenithSpoken));

        // Closest: started and not finished, by how far; a game nobody here has unlocked one of is only in Every game.
        Assert.Equal(["Risk of Rain 2", "Valheim"], room.Closest.Select(g => g.Title));
        Assert.Equal("75% · 1 to go", room.Closest[0].ToGoText);
        Assert.Equal(4, room.Rows.Count);
        Assert.Equal("Deadlock", room.Rows[^1].Title);

        // The average is of the games started, as Steam's is: (100 + 75 + 25) / 3.
        Assert.Equal("67%", room.AverageLabel);
        Assert.Equal(("6", "across 3 games"), (room.UnlockedText, room.UnlockedSub));
        Assert.Equal("Risk of Rain 2", room.Rarest!.GameTitle);
        Assert.Equal(["Apex Legends B", "Apex Legends A", "Valheim A", "Risk of Rain 2 C", "Risk of Rain 2 B"], room.Recent.Select(r => $"{r.GameTitle} {r.Achievement.Id}"));

        // A heading sorts Every game, and again reverses it.
        room.SortCommand.Execute("Game");
        Assert.Equal(["Apex Legends", "Deadlock", "Risk of Rain 2", "Valheim"], room.Rows.Select(g => g.Title));
        room.SortCommand.Execute("Game");
        Assert.Equal("Valheim", room.Rows[0].Title);

        // Nothing read yet, and no percentages kept: the page is empty, not broken.
        room.Show([], now);
        Assert.Equal((false, null), (room.HasGames, room.Rarest));
        room.Show([View(("A", true, false, null, 1))], now);
        Assert.Null(room.Rarest);

        // None finished: the shelf says which is closest.
        room.Show([ror2, valheim], now);
        Assert.Equal("None yet. Risk of Rain 2 is closest: 1 to go.", room.ZenithNote);
        Assert.Equal(("None yet", "Zeniths: none yet"), (room.ZenithSubLines, room.ZenithSpoken));

        // A game opens its achievements here, and Back comes back.
        room.OpenGame(ror2.Game);
        Assert.Equal(("Risk of Rain 2", false), (room.Game!.GameTitle, room.ShowsRoom));
        room.Game.BackCommand.Execute(null);
        Assert.True(room.ShowsRoom);
    }

    [Fact]
    public async Task Rarity_is_asked_first_so_the_rarest_ones_icon_comes_with_the_latest()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var asked = new List<string>();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3];
        using var handler = new Steam(url =>
        {
            lock (asked)
            {
                asked.Add(url);
            }

            return url.Contains("GetGlobalAchievementPercentagesForApp", StringComparison.Ordinal)
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"achievementpercentages":{"achievements":[{"name":"Old","percent":0.5},{"name":"N1","percent":50}]}}"""),
                }
                : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(png) };
        });

        // Six unlocked: the oldest is the rarest, so it isn't among the latest five.
        var view = View(("Old", true, false, null, 1), ("N1", true, false, null, 2), ("N2", true, false, null, 3), ("N3", true, false, null, 4),
            ("N4", true, false, null, 5), ("N5", true, false, null, 6));
        Assert.True(await Host.Achievements.FetchAsync(data, view, CancellationToken.None, steam: handler));
        Assert.Contains(asked, a => a.EndsWith("/" + IconOf("Old"), StringComparison.Ordinal));
        Assert.True(asked.FindIndex(a => a.Contains("Percentages", StringComparison.Ordinal)) < asked.FindIndex(a => a.EndsWith(".jpg", StringComparison.Ordinal)));

        // Refresh rarity asks again however recent; otherwise it waits a week.
        asked.Clear();
        await Host.Achievements.FetchAsync(data, view, CancellationToken.None, steam: handler);
        Assert.Empty(asked);
        await Host.Achievements.FetchAsync(data, view, CancellationToken.None, again: true, steam: handler);
        Assert.Single(asked);
    }

    [Fact]
    public void The_metals_are_the_design_systems_fixed_tokens_in_every_theme()
    {
        // Bronze, silver, gold and platinum never follow the theme, as the status colours don't (design system version 33).
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "GameSync.sln")))
        {
            folder = folder.Parent;
        }

        var axaml = File.ReadAllText(Path.Combine(folder!.FullName, "src", "GameSync.UI", "Styles", "Achievements.axaml"));
        foreach (var choice in new[] { new UI.Theming.ThemeChoice(), new UI.Theming.ThemeChoice(Mode: UI.Theming.ThemeMode.Light) })
        {
            var tokens = UI.Theming.ThemeEngine.Build(choice);
            foreach (var metal in new[] { "bronze", "silver", "gold", "platinum" })
            {
                var brush = System.Text.RegularExpressions.Regex.Match(axaml,
                    $@"x:Key=""metal-{metal}""[^>]*>\s*<GradientStop Color=""(#\w+)"" Offset=""0"" />\s*<GradientStop Color=""(#\w+)"" Offset=""1"" />");
                Assert.True(brush.Success, metal);
                Assert.Equal(tokens[metal], brush.Groups[1].Value, ignoreCase: true);
                Assert.Equal(tokens[metal + "-deep"], brush.Groups[2].Value, ignoreCase: true);
            }

            // Zenith's word on a page minds the mode (design system version 36): the metal in dark, a deep steel in light.
            var mode = choice.Mode == UI.Theming.ThemeMode.Light ? "Light" : "Dark";
            var ink = System.Text.RegularExpressions.Regex.Match(axaml,
                $@"<ResourceDictionary x:Key=""{mode}"">\s*<LinearGradientBrush x:Key=""zenith-ink-text""[^>]*>\s*<GradientStop Color=""(#\w+)"" Offset=""0"" />\s*<GradientStop Color=""(#\w+)"" Offset=""1"" />");
            Assert.True(ink.Success, mode);
            Assert.Equal(tokens["zenith-ink"], ink.Groups[1].Value, ignoreCase: true);
            Assert.Equal(tokens["zenith-ink-deep"], ink.Groups[2].Value, ignoreCase: true);
        }

        // In light, 4.5:1 or better on a white card where it starts.
        var light = UI.Theming.ThemeEngine.Build(new UI.Theming.ThemeChoice(Mode: UI.Theming.ThemeMode.Light));
        Assert.True(UI.Theming.ThemeEngine.Contrast(light["zenith-ink"], "#ffffff") >= 4.5);
    }

    /// <summary>A game's achievements as read: each one's id, whether it's unlocked and hidden, its percent, and the day of September 2026 it was unlocked.</summary>
    internal static Host.GameAchievementsView View(params (string Id, bool Unlocked, bool Hidden, double? Percent, int Day)[] items) =>
        new(Model.GameId.Parse("game"), "Game", Valheim, items.Select(i => new Host.AchievementShown(i.Id, i.Id, $"Do {i.Id}", i.Hidden,
            i.Unlocked ? new DateTime(2026, 9, Math.Max(1, i.Day), 12, 0, 0, DateTimeKind.Utc) : null, null, IconOf(i.Id), i.Percent)).ToList());

    /// <summary>An icon's name as Steam's list gives it: a hash, here made from the achievement's id.</summary>
    internal static string IconOf(string id) => Convert.ToHexString(Encoding.UTF8.GetBytes(id)).ToLowerInvariant().PadRight(20, '0') + ".jpg";

    internal sealed class Steam(Func<string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request.RequestUri!.ToString()));
    }

    internal static byte[] Achievement(int bit, string id, byte[] name, string description, bool hidden, string icon) =>
        Kv.Block(bit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Kv.Text("name", id),
            Kv.Block("display", name, Kv.Block("desc", Kv.Text("english", description)), Kv.Text("hidden", hidden ? "1" : "0"),
                Kv.Text("icon", icon), Kv.Text("icon_gray", icon.Replace(".jpg", "_gray.jpg", StringComparison.Ordinal))),
            Kv.Int("bit", bit));

    internal static void Write(string path, byte[] root) => File.WriteAllBytes(path, [.. root, 8]);

    /// <summary>Valve's binary KeyValues, written as the Steam client writes its caches.</summary>
    internal static class Kv
    {
        public static byte[] Block(string key, params byte[][] items) => [0, .. Z(key), .. items.SelectMany(i => i), 8];

        public static byte[] Text(string key, string value) => [1, .. Z(key), .. Z(value)];

        public static byte[] Int(string key, int value) => [2, .. Z(key), .. BitConverter.GetBytes(value)];

        private static byte[] Z(string text) => [.. Encoding.UTF8.GetBytes(text), 0];
    }
}
