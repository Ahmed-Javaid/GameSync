using System.Globalization;
using System.Text;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Host;
using GameSync.UI.ViewModels;
using static GameSync.Core.Tests.AchievementTests;

namespace GameSync.Core.Tests;

/// <summary>
/// The owner's review of 3 Oct 2026 (design system version 35): the popup and its chime when an achievement unlocks while
/// playing (ACH-09), a game left out of the achievements (KAN-110), every game's page with its achievements, Steam's list
/// at 0 when Steam here keeps none (KAN-111), and the Achievements page's Within reach (KAN-105).
/// </summary>
public class AchievementPopupTests
{
    private const long RiskOfRain = 632360;
    private const long Cuphead = 268910;
    private static readonly GameId ValheimId = GameId.Parse("valheim");

    [Fact]
    public void ACH_09_an_achievement_unlocked_while_its_game_runs_pops_up_once_saying_how_far_the_game_is()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Path.Combine(world.Root, "Steam");
        var stats = Directory.CreateDirectory(Path.Combine(steam, "appcache", "stats")).FullName;
        Write(Path.Combine(stats, $"UserGameStatsSchema_{Valheim}.bin"), Kv.Block("892970", Kv.Block("stats", Kv.Block("1", Kv.Text("type", "ACHIEVEMENTS"),
            Kv.Block("bits",
                Achievement(0, "A", Kv.Text("name", "I Have Arrived!"), "Arrive", hidden: false, "a.jpg"),
                Achievement(1, "B", Kv.Text("name", "Hunter"), "Defeat Eikthyr", hidden: false, "b.jpg"),
                Achievement(2, "C", Kv.Text("name", "Elder"), "Defeat The Elder", hidden: false, "c.jpg"),
                Achievement(3, "D", Kv.Text("name", "Royal"), "Defeat the Queen", hidden: true, "d.jpg"))))));

        // Steam's percentages kept: Hunter is under 5% of players, so its popup and chime are Gold.
        var kept = Directory.CreateDirectory(Path.Combine(data, "art", "achievements", "892970")).FullName;
        File.WriteAllText(Path.Combine(kept, "rarity.json"), """{"achievementpercentages":{"achievements":[{"name":"B","percent":2.5},{"name":"A","percent":80}]}}""");

        var progress = Path.Combine(stats, $"UserGameStats_281062582_{Valheim}.bin");
        var longAgo = (int)new DateTimeOffset(2025, 3, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var written = DateTime.UtcNow.AddMinutes(-10);
        void Progress(int bits, params (int Bit, int At)[] times)
        {
            Write(progress, Kv.Block("cache", Kv.Block("1", Kv.Int("data", bits),
                Kv.Block("AchievementTimes", times.Select(t => Kv.Int(t.Bit.ToString(CultureInfo.InvariantCulture), t.At)).ToArray()))));

            // Each time Steam writes it, the file's time moves on.
            written = written.AddSeconds(5);
            File.SetLastWriteTimeUtc(progress, written);
        }

        Progress(0b0001, (0, longAgo));
        long? running = null;
        using var watch = new AchievementWatch(data, app => app == Valheim ? (ValheimId, "Valheim") : null, steamRoot: () => steam, running: () => running,
            every: TimeSpan.FromHours(1));
        var popped = new List<AchievementUnlock>();
        var ended = new List<long>();
        watch.Unlocked += popped.Add;
        watch.Ended += ended.Add;

        // No game running: nothing watched.
        watch.Tick();
        Assert.Null(watch.Watching);

        // Valheim starts: what it has unlocked already is where it starts from, so nothing pops up.
        running = Valheim;
        watch.Tick();
        watch.Tick();
        Assert.Equal(Valheim, watch.Watching);
        Assert.Empty(popped);

        // Hunter unlocked now: one popup, Gold, 2 of 4.
        Progress(0b0011, (0, longAgo), (1, now));
        watch.Tick();
        var hunter = Assert.Single(popped);
        Assert.Equal((ValheimId, "Valheim", "Hunter", 2, 4, "gold", false), (hunter.Game, hunter.GameTitle, hunter.Achievement.Name, hunter.Done, hunter.Total, hunter.Tier,
            hunter.IsZenith));

        // Looked at again with nothing new, or read half-written by Steam: never a second popup.
        watch.Tick();
        File.WriteAllBytes(progress, [0, (byte)'c', (byte)'a']);
        File.SetLastWriteTimeUtc(progress, written = written.AddSeconds(5));
        watch.Tick();
        Progress(0b0011, (0, longAgo), (1, now));
        watch.Tick();
        Assert.Single(popped);

        // Steam brings down what was unlocked long ago or on another PC as the game starts: never a popup.
        Progress(0b0111, (0, longAgo), (1, now), (2, longAgo));
        watch.Tick();
        Assert.Single(popped);

        // The last one, hidden until now, unlocked: the game's Zenith, with its rarity not known yet.
        Progress(0b1111, (0, longAgo), (1, now), (2, longAgo), (3, now));
        watch.Tick();
        Assert.Equal(2, popped.Count);
        var royal = popped[1];
        Assert.Equal(("Royal", true, 4, 4, true, null), (royal.Achievement.Name, royal.Achievement.Hidden, royal.Done, royal.Total, royal.IsZenith, royal.Tier));

        // The game quits: the watch says so, for the popups held over a game in exclusive fullscreen.
        running = null;
        watch.Tick();
        Assert.Equal([Valheim], ended);
        Assert.Null(watch.Watching);
    }

    [Fact]
    public void ACH_09_a_game_whose_file_is_being_written_as_it_starts_never_pops_up_what_it_had_already()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Path.Combine(world.Root, "Steam");
        var stats = Directory.CreateDirectory(Path.Combine(steam, "appcache", "stats")).FullName;
        Write(Path.Combine(stats, $"UserGameStatsSchema_{Valheim}.bin"), Kv.Block("892970", Kv.Block("stats", Kv.Block("1", Kv.Text("type", "ACHIEVEMENTS"),
            Kv.Block("bits",
                Achievement(0, "A", Kv.Text("name", "I Have Arrived!"), "Arrive", hidden: false, "a.jpg"),
                Achievement(1, "B", Kv.Text("name", "Hunter"), "Defeat Eikthyr", hidden: false, "b.jpg"))))));
        var progress = Path.Combine(stats, $"UserGameStats_281062582_{Valheim}.bin");

        // The game starts while Steam is writing its file; A was unlocked a minute ago, in the session before.
        File.WriteAllBytes(progress, [0, (byte)'c']);
        long? running = Valheim;
        using var watch = new AchievementWatch(data, _ => (ValheimId, "Valheim"), steamRoot: () => steam, running: () => running, every: TimeSpan.FromHours(1));
        var popped = new List<AchievementUnlock>();
        watch.Unlocked += popped.Add;
        watch.Tick();

        var aMinuteAgo = (int)DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
        Write(progress, Kv.Block("cache", Kv.Block("1", Kv.Int("data", 0b01), Kv.Block("AchievementTimes", Kv.Int("0", aMinuteAgo)))));
        File.SetLastWriteTimeUtc(progress, DateTime.UtcNow.AddSeconds(-30));
        watch.Tick();
        Assert.Empty(popped);

        // What comes after does.
        Write(progress, Kv.Block("cache", Kv.Block("1", Kv.Int("data", 0b11),
            Kv.Block("AchievementTimes", Kv.Int("0", aMinuteAgo), Kv.Int("1", (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds())))));
        File.SetLastWriteTimeUtc(progress, DateTime.UtcNow.AddSeconds(-20));
        watch.Tick();
        Assert.Equal(["Hunter"], popped.Select(p => p.Achievement.Name));
    }

    [Fact]
    public void KAN_120_each_chime_is_a_short_soft_sound_GameSync_makes_itself_in_the_sound_and_volume_picked()
    {
        // The owner, 3 Oct 2026: "The popup sound is too loud, it needs to be softer. Give me options i can choose from."
        static double[] Samples(byte[] wave)
        {
            Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
            Assert.Equal(wave.Length - 8, BitConverter.ToInt32(wave, 4));
            Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(wave, 8, 8));

            // PCM, mono, 44.1 kHz, 16-bit.
            Assert.Equal((1, 1, GameSync.Windows.Chime.SampleRate, 16), (BitConverter.ToInt16(wave, 20), BitConverter.ToInt16(wave, 22), BitConverter.ToInt32(wave, 24),
                BitConverter.ToInt16(wave, 34)));
            Assert.Equal("data", Encoding.ASCII.GetString(wave, 36, 4));
            var count = BitConverter.ToInt32(wave, 40) / 2;
            Assert.Equal(wave.Length - 44, count * 2);
            return Enumerable.Range(0, count).Select(i => BitConverter.ToInt16(wave, 44 + i * 2) / (double)short.MaxValue).ToArray();
        }

        static double Rms(double[] samples, double seconds)
        {
            var n = Math.Min(samples.Length, (int)(seconds * GameSync.Windows.Chime.SampleRate));
            return Math.Sqrt(samples.Take(n).Sum(v => v * v) / n);
        }

        var sounds = GameSync.Windows.Chime.Sounds.Select(s => s.Id).ToList();
        Assert.Equal(["bell", "marimba", "glass", "harp", "pop"], sounds);
        Assert.Equal(["quiet", "soft", "medium", "loud"], GameSync.Windows.Chime.Volumes.Select(v => v.Id));
        foreach (var sound in sounds)
        {
            foreach (var tier in new[] { "bronze", "silver", "gold", "zenith" })
            {
                var samples = Samples(GameSync.Windows.Chime.Wave(tier, sound));

                // Short, never past the ceiling, and fading out so it ends without a click.
                Assert.InRange(samples.Length / (double)GameSync.Windows.Chime.SampleRate, 0.5, 3);
                Assert.True(samples.Max(Math.Abs) <= GameSync.Windows.Chime.Ceiling + 0.001, $"{sound} {tier}");
                Assert.InRange(Math.Abs(samples[^1]), 0, 0.0001);
            }

            // Louder volume by volume, every one of them softer than the first chime's 0.6 at its loudest.
            var levels = GameSync.Windows.Chime.Volumes.Select(v => Rms(Samples(GameSync.Windows.Chime.Wave("silver", sound, v.Id)), 0.5)).ToList();
            Assert.Equal(levels.Order(), levels);
            Assert.True(levels[1] < levels[3] * 0.5, sound);
        }

        // At the same volume every sound is about as loud as the others, so changing the sound doesn't startle.
        var soft = sounds.Select(sound => Rms(Samples(GameSync.Windows.Chime.Wave("silver", sound, "soft")), 0.5)).ToList();
        Assert.True(soft.Max() / soft.Min() < 1.6, string.Join(", ", soft));

        // Each sound is its own; the defaults are the soft bell at Soft, and what isn't a choice falls back to them.
        Assert.Equal(sounds.Count, sounds.Select(sound => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(GameSync.Windows.Chime.Wave("gold", sound)))).Distinct().Count());
        Assert.Equal(GameSync.Windows.Chime.Wave("bronze", "bell", "soft"), GameSync.Windows.Chime.Wave(null));
        Assert.Equal(GameSync.Windows.Chime.Wave("bronze", "bell", "soft"), GameSync.Windows.Chime.Wave("tin", "kazoo", "deafening"));
    }

    [Fact]
    public void ACH_09_the_popup_its_sound_and_its_corner_are_kept_on_this_PC()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");

        Assert.Equal(new AchievementPopupSettings(true, true, "top-right", Chime: "bell", Volume: "soft"), AchievementPopupSettings.Read(data));

        AchievementPopupSettings.Set(data, popups: false);
        Assert.Equal(new AchievementPopupSettings(false, true, "top-right"), AchievementPopupSettings.Read(data));
        AchievementPopupSettings.Set(data, sound: false, corner: "bottom-left");
        Assert.Equal(new AchievementPopupSettings(false, false, "bottom-left"), AchievementPopupSettings.Read(data));
        AchievementPopupSettings.Set(data, new AchievementPopupChange(Chime: "marimba", Volume: "quiet"));
        Assert.Equal(new AchievementPopupSettings(false, false, "bottom-left", "marimba", "quiet"), AchievementPopupSettings.Read(data));

        // What isn't a choice is the default's.
        AchievementPopupSettings.Set(data, popups: true, sound: true, corner: "middle", chime: "kazoo", volume: "deafening");
        Assert.Equal(new AchievementPopupSettings(), AchievementPopupSettings.Read(data));

        // Settings: showing what's kept saves nothing; a switch saves only itself; picking a sound or a volume plays it;
        // Hear it plays what's picked; Try the popup asks the app now or in 10 seconds.
        var saved = new List<AchievementPopupChange>();
        var heard = new List<(string, string)>();
        var tried = new List<TimeSpan>();
        var section = new AchievementSettings(new SettingsActions
        {
            SetAchievementPopups = change =>
            {
                saved.Add(change);
                return Task.CompletedTask;
            },
            HearChime = (sound, volume) => heard.Add((sound, volume)),
            TryPopup = tried.Add,
        });
        section.Show(new SettingsView
        {
            BackupFolder = data, ThisPc = "DESKTOP", AppVersion = "0.5.0", Achievements = new AchievementPopupSettings(false, true, "top-left", "glass", "medium"),
        });
        Assert.Equal((false, true, "top-left", "glass", "medium"), (section.Popups, section.Sound, section.Corner, section.Chime, section.Volume));
        Assert.Equal(["Soft bell", "Marimba", "Glass", "Harp", "Pop"], section.ChimeOptions.Select(o => o.Label));
        Assert.Empty(saved);
        Assert.Empty(heard);
        section.Popups = true;
        section.Corner = "bottom-right";
        section.Chime = "harp";
        section.Volume = "quiet";
        Assert.Equal([new AchievementPopupChange(Popups: true), new AchievementPopupChange(Corner: "bottom-right"),
            new AchievementPopupChange(Chime: "harp"), new AchievementPopupChange(Volume: "quiet")], saved);
        section.HearCommand.Execute(null);
        Assert.Equal([("harp", "medium"), ("harp", "quiet"), ("harp", "quiet")], heard);

        section.TryNowCommand.Execute(null);
        section.TryLaterCommand.Execute(null);
        Assert.Equal([TimeSpan.Zero, TimeSpan.FromSeconds(10)], tried);
        Assert.Equal("Showing in 10 seconds: switch to your game now.", section.TryLine);
    }

    [Fact]
    public async Task KAN_111_a_game_Steam_here_keeps_none_for_shows_Steams_list_at_0()
    {
        // The owner's Cuphead is a copy in its own folder, so Steam's files hold nothing of it; Steam's public list has them all.
        const string Answer = """
            {"response":{"achievements":[
              {"internal_name":"ACH_DEVIL","localized_name":"Devil's Bargain","localized_desc":"Beat the Devil","icon":"aaaaaaaaaaaaaaaaaaaa.jpg","icon_gray":"bbbbbbbbbbbbbbbbbbbb.jpg","hidden":false,"player_percent_unlocked":"12.4"},
              {"internal_name":"ACH_SECRET","localized_name":"Secret Ending","localized_desc":"Join the Devil","icon":"cccccccccccccccccccc.jpg","icon_gray":"dddddddddddddddddddd.jpg","hidden":true,"player_percent_unlocked":3.1},
              {"internal_name":"ACH_ODD","localized_name":"Odd","hidden":false,"player_percent_unlocked":"lots"},
              {"internal_name":"","localized_name":"No id","hidden":false}
            ]}}
            """;
        var list = Core.Art.AchievementCache.ParseList(Answer);
        Assert.Equal(["ACH_DEVIL", "ACH_SECRET", "ACH_ODD"], list.All.Select(a => a.Id));
        Assert.Equal((12.4, 3.1, false), (list.Percents["ACH_DEVIL"], list.Percents["ACH_SECRET"], list.Percents.ContainsKey("ACH_ODD")));
        Assert.True(list.All[1].Hidden);
        Assert.All(list.All, a => Assert.False(a.Unlocked));
        Assert.Empty(Core.Art.AchievementCache.ParseList("<html>busy</html>").All);
        Assert.Empty(Core.Art.AchievementCache.ParseList("""{"response":{}}""").All);

        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Directory.CreateDirectory(Path.Combine(world.Root, "Steam")).FullName;
        var cuphead = GameId.Parse("cuphead");
        Assert.Null(Achievements.For(data, cuphead, "Cuphead", StoreKind.Loose, Cuphead, steam));

        // Asked of Steam once, with no key, and again only after a month.
        var asked = new List<string>();
        var answer = Answer;
        using var handler = new Steam(url =>
        {
            asked.Add(url);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(answer) };
        });
        using (var cache = new Core.Art.AchievementCache(data, handler))
        {
            Assert.True(await cache.FetchListAsync(Cuphead, CancellationToken.None));
            Assert.StartsWith(Core.Art.AchievementCache.ListApi + "?appid=268910", Assert.Single(asked), StringComparison.Ordinal);
            Assert.False(await cache.FetchListAsync(Cuphead, CancellationToken.None));
            Assert.Single(asked);
        }

        using (var later = new Core.Art.AchievementCache(data, handler, () => DateTime.UtcNow.AddDays(31)))
        {
            Assert.True(later.ListStale(Cuphead));
        }

        // The copy that isn't Steam's: every one at 0, with the list's percentages for their tiers; a hidden one stays secret.
        var copy = Achievements.For(data, cuphead, "Cuphead", StoreKind.Loose, Cuphead, steam)!;
        Assert.Equal((AchievementsFrom.ListNotSteamCopy, false, 0, 3), (copy.From, copy.IsTracked, copy.Unlocked, copy.Total));
        Assert.Equal(AchievementTier.Silver, copy.All[0].Tier);
        var secret = copy.All.Single(a => a.Id == "ACH_SECRET");
        Assert.True(secret.Secret);
        Assert.Null(secret.IconFile);

        // A Steam copy Steam on this PC has never run: the same list, and its page says why nothing's unlocked.
        Assert.Equal(AchievementsFrom.ListNotPlayedHere, Achievements.For(data, cuphead, "Cuphead", StoreKind.Steam, Cuphead, steam)!.From);

        // A game with no Steam app ID has none to read.
        Assert.Null(Achievements.For(data, GameId.Parse("bloodborne"), "Bloodborne", StoreKind.Loose, null, steam));

        // One Steam lists none for says 0, and isn't asked about again for a month (Child of Light's are Ubisoft's).
        const long ChildOfLight = 256290;
        answer = """{"response":{"schema_version":3,"groups":[{"groupid":0,"total_achievements":0}]}}""";
        using (var cache = new Core.Art.AchievementCache(data, handler))
        {
            Assert.False(cache.ListedNone(ChildOfLight));
            Assert.False(await cache.FetchListAsync(ChildOfLight, CancellationToken.None));
            Assert.Equal((false, true, null), (cache.ListStale(ChildOfLight), cache.ListedNone(ChildOfLight), cache.List(ChildOfLight)));
        }

        var none = Achievements.For(data, GameId.Parse("child-of-light"), "Child of Light", StoreKind.Loose, ChildOfLight, steam)!;
        Assert.Equal((AchievementsFrom.NoneOnSteam, 0, false), (none.From, none.Total, none.IsTracked));
        asked.Clear();
        Assert.False(await Achievements.FetchForAsync(data, none.Game, none.Title, StoreKind.Loose, ChildOfLight, CancellationToken.None, handler));
        Assert.Empty(asked);

        // Learning it the first time is news for the page, which reads again and says 0.
        const long Another = 1234;
        Assert.True(await Achievements.FetchForAsync(data, GameId.Parse("another"), "Another", StoreKind.Loose, Another, CancellationToken.None, handler));
        Assert.Equal(AchievementsFrom.NoneOnSteam, Achievements.For(data, GameId.Parse("another"), "Another", StoreKind.Loose, Another, steam)!.From);
    }

    [Fact]
    public void KAN_110_a_game_left_out_has_no_place_in_the_achievements_until_it_is_counted_again()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Path.Combine(world.Root, "Steam");
        var stats = Directory.CreateDirectory(Path.Combine(steam, "appcache", "stats")).FullName;
        SteamGame(stats, Valheim, 0b01);
        SteamGame(stats, RiskOfRain, 0b11);

        var now = DateTime.UtcNow;
        var valheim = new LauncherGame { Id = ValheimId, Title = "Valheim", Store = StoreKind.Steam, SteamAppId = Valheim, Installed = true, LastPlayedUtc = now };
        var ror2 = new LauncherGame
        {
            Id = GameId.Parse("risk-of-rain-2"), Title = "Risk of Rain 2", Store = StoreKind.Steam, SteamAppId = RiskOfRain, Installed = true, LastPlayedUtc = now.AddDays(-2),
        };

        // A Steam game never run here whose list is kept: its page says 0, but it's counted nowhere.
        var cuphead = new LauncherGame { Id = GameId.Parse("cuphead"), Title = "Cuphead", Store = StoreKind.Steam, SteamAppId = Cuphead };
        var kept = Directory.CreateDirectory(Path.Combine(data, "art", "achievements", "268910")).FullName;
        File.WriteAllText(Path.Combine(kept, "list.json"), """{"response":{"achievements":[{"internal_name":"ACH_DEVIL","localized_name":"Devil's Bargain","hidden":false}]}}""");
        LauncherGame[] games = [valheim, ror2, cuphead];

        string Row(AchievementGameRow r) => $"{r.Title}: {r.Line}{(r.Zenith ? " · Zenith" : "")}{(r.LeftOut ? ", left out" : "")}";
        Assert.Equal(["Risk of Rain 2: 2 of 2 · Zenith", "Valheim: 1 of 2"], Achievements.SettingsRows(data, games, steam).Select(Row));
        Assert.Equal(["Valheim", "Risk of Rain 2"], Achievements.ForAll(data, games, steam).Select(v => v.Title));
        Assert.Equal("Valheim", Achievements.ForHome(data, valheim, games, steam).View!.Title);

        Achievements.SetLeftOut(data, ValheimId, true);
        Assert.Equal([ValheimId], Achievements.LeftOut(data));
        using (var state = new Core.State.StateStore(data))
        {
            Assert.True(Achievements.IsLeftOut(state, ValheimId));
        }

        Assert.Equal(["Risk of Rain 2: 2 of 2 · Zenith", "Valheim: 1 of 2, left out"], Achievements.SettingsRows(data, games, steam).Select(Row));
        Assert.Equal(["Risk of Rain 2"], Achievements.ForAll(data, games, steam).Select(v => v.Title));

        // Home's card goes to the last game played that's counted, and says why it isn't the hero's.
        var home = Achievements.ForHome(data, valheim, games, steam);
        Assert.Equal(("Risk of Rain 2", true, true), (home.View!.Title, home.HeroHasNone, home.HeroLeftOut));
        Assert.EndsWith("Valheim is left out of your achievements.",
            HomeAchievements.Of(home.View, valheim, home.HeroHasNone, games, DateTime.Now, home.HeroLeftOut).Subtitle, StringComparison.Ordinal);

        Achievements.SetLeftOut(data, ValheimId, false);
        Assert.Empty(Achievements.LeftOut(data));
        Assert.Equal(2, Achievements.ForAll(data, games, steam).Count);

        // Settings' switch: showing a row saves nothing; turning it off leaves the game out and says what that means.
        var set = new List<(GameId, bool)>();
        var item = new AchievementGameItem(new AchievementGameRow(ValheimId, "Valheim", "1 of 2", false, null), (game, leftOut) =>
        {
            set.Add((game, leftOut));
            return Task.CompletedTask;
        });
        Assert.Equal((true, "1 of 2"), (item.Counted, item.Line));
        Assert.Empty(set);
        item.Counted = false;
        Assert.Equal([(ValheimId, true)], set);
        Assert.Equal("Left out: not on the Achievements page or Home, no popup", item.Line);
        item.Counted = true;
        Assert.Equal("1 of 2", item.Line);
    }

    [Fact]
    public void KAN_131_each_games_popup_is_off_where_its_launcher_shows_its_own_and_Achievements_by_game_lists_them()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Path.Combine(world.Root, "Steam");
        var stats = Directory.CreateDirectory(Path.Combine(steam, "appcache", "stats")).FullName;
        SteamGame(stats, Valheim, 0b01);
        SteamGame(stats, RiskOfRain, 0b11);
        var ror2Id = GameId.Parse("risk-of-rain-2");
        LauncherGame[] games =
        [
            new() { Id = ValheimId, Title = "Valheim", Store = StoreKind.Steam, SteamAppId = Valheim, Installed = true },
            new() { Id = ror2Id, Title = "Risk of Rain 2", Store = StoreKind.Steam, SteamAppId = RiskOfRain, Installed = true },
        ];

        // The owner, 4 Oct 2026: "games on steam/epic/official external launchers have their own achievement popups, so
        // those games will have their popups turned off by default on gamesync". A copy of its own has only GameSync's.
        Assert.Equal(["Steam", "Epic", "The EA app", null, null], new StoreKind?[] { StoreKind.Steam, StoreKind.Epic, StoreKind.Ea, StoreKind.Loose, null }.Select(Achievements.LauncherOf));
        var none = new Dictionary<GameId, bool>();
        Assert.False(Achievements.PopupOn(none, ValheimId, launcherShowsOwn: true));
        Assert.True(Achievements.PopupOn(none, ValheimId, launcherShowsOwn: false));
        Assert.Equal([("Risk of Rain 2", "Steam", false, true), ("Valheim", "Steam", false, false)],
            Achievements.SettingsRows(data, games, steam).Select(r => (r.Title, r.Launcher, r.Popup, r.Zenith)));

        // Turned on for one game, it stays on for that game only; turned off for a copy of its own, it stays off.
        Achievements.SetPopup(data, ValheimId, true);
        Achievements.SetPopup(data, ror2Id, false);
        var chosen = Achievements.PopupChoices(data);
        Assert.True(Achievements.PopupOn(chosen, ValheimId, launcherShowsOwn: true));
        Assert.False(Achievements.PopupOn(chosen, ror2Id, launcherShowsOwn: false));
        Assert.Equal([("Risk of Rain 2", false), ("Valheim", true)], Achievements.SettingsRows(data, games, steam).Select(r => (r.Title, r.Popup)));

        // Settings' row says how many count and have GameSync's popup; Choose games… opens Achievements by game.
        var popups = new List<(GameId, bool)>();
        var outs = new List<(GameId, bool)>();
        object? opened = null;
        var closed = 0;
        var section = new AchievementSettings(new SettingsActions
        {
            AchievementGames = _ => Task.FromResult(Achievements.SettingsRows(data, games, steam)),
            SetAchievementPopup = (game, on) =>
            {
                popups.Add((game, on));
                return Task.CompletedTask;
            },
            SetAchievementsLeftOut = (game, leftOut) =>
            {
                outs.Add((game, leftOut));
                return Task.CompletedTask;
            },
            OpenDialog = dialog => opened = dialog,
            CloseDialog = () => closed++,
        });
        section.LoadGames();
        Assert.Equal(2, section.Games.Count);
        Assert.Equal("2 of 2 games count on the Achievements page and Home. GameSync's popup is on for 1: Steam shows its own for the rest.", section.GamesLine);
        var ror2 = section.Games[0];
        Assert.Equal(("Risk of Rain 2", "2 of 2 · Zenith · Steam shows its own popup", false), (ror2.Title, ror2.Line, ror2.PopupShown));
        Assert.Equal(("Count Risk of Rain 2 in your achievements", "GameSync's popup for Risk of Rain 2"), (ror2.SwitchName, ror2.PopupSwitchName));

        section.ChooseGamesCommand.Execute(null);
        var dialog = Assert.IsType<AchievementGamesViewModel>(opened);
        Assert.Equal("2 games · 2 counted · 1 popup", dialog.Summary);

        // The popup's switch saves only itself; a game left out has no popup and its switch can't turn one on.
        ror2.PopupShown = true;
        Assert.Equal([(ror2Id, true)], popups);
        Assert.Equal("2 games · 2 counted · 2 popups", dialog.Summary);
        ror2.Counted = false;
        Assert.Equal([(ror2Id, true)], outs);
        Assert.False(ror2.PopupShown);
        ror2.PopupShown = true;
        Assert.Single(popups);
        Assert.Equal("Left out: not on the Achievements page or Home, no popup", ror2.Line);
        Assert.Equal("2 games · 1 counted · 1 popup", dialog.Summary);
        Assert.Equal("1 of 2 games count on the Achievements page and Home. GameSync's popup is on for 1.", section.GamesLine);

        // Find a game; Esc clears the search first, then closes.
        dialog.Query = "val";
        Assert.Equal(["Valheim"], dialog.Shown.Select(g => g.Title));
        dialog.Query = "zelda";
        Assert.True(dialog.NoMatch);
        Assert.Equal("No game matches \u201czelda\u201d.", dialog.NoMatchLine);
        dialog.Escape();
        Assert.Equal(("", 2, 0), (dialog.Query, dialog.Shown.Count, closed));
        dialog.Escape();
        Assert.Equal(1, closed);
        dialog.CloseCommand.Execute(null);
        Assert.Equal(2, closed);
    }

    [Fact]
    public void KAN_111_every_games_page_has_its_achievements_saying_0_or_why_there_are_none()
    {
        var now = DateTime.Now;
        var outs = new List<(GameId, bool)>();
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
        {
            OpenAchievements = (_, _) => { },
            SetAchievementsLeftOut = (game, leftOut) => outs.Add((game, leftOut)),
        };
        GameViewModel Page(LauncherGame game, GameAchievementsView? view, bool leftOut = false)
        {
            var page = new GameViewModel(game, HomeViewModel.Tile(game, now), actions);
            page.Show(new GameDetail { Id = game.Id, Achievements = view, AchievementsLeftOut = leftOut }, now);
            return page;
        }

        // The owner's Cuphead, a copy that isn't Steam's: 0 of 28, from Steam's list.
        var cuphead = new LauncherGame { Id = GameId.Parse("cuphead"), Title = "Cuphead", Store = StoreKind.Loose, SteamAppId = Cuphead, Installed = true };
        var list = View(Enumerable.Range(1, 28).Select(i => ($"A{i}", false, false, (double?)i, 0)).ToArray()) with { Game = cuphead.Id, From = AchievementsFrom.ListNotSteamCopy };
        var page = Page(cuphead, list);
        Assert.Equal((true, true, "0 of 28 · Steam's list", false), (page.HasAchievements, page.ShowsAchievementsOverview, page.AchievementsSubtitle, page.NoneUnlocked));
        Assert.StartsWith("This copy isn't Steam's", page.AchievementsNote, StringComparison.Ordinal);
        Assert.Equal("info", page.AchievementsNoteIcon);
        Assert.True(page.CanViewAllAchievements);

        // A Steam game Steam here hasn't run, one played here, and one finished.
        var valheim = new LauncherGame { Id = ValheimId, Title = "Valheim", Store = StoreKind.Steam, SteamAppId = Valheim, Installed = true };
        Assert.Equal("0 of 28 · none unlocked on this PC", Page(valheim, list with { From = AchievementsFrom.ListNotPlayedHere }).AchievementsSubtitle);
        var played = Page(valheim, View(("A", true, false, 50, 2), ("B", false, false, 3, 0)));
        Assert.Equal(("1 of 2 unlocked on Steam", false), (played.AchievementsSubtitle, played.HasAchievementsNote));
        Assert.Equal("Every achievement unlocked on Steam", Page(valheim, View(("A", true, false, 50, 2))).AchievementsSubtitle);

        // Left out in Settings: the card says so, and Count it again counts it back in.
        var leftOut = Page(valheim, View(("A", true, false, 50, 2)), leftOut: true);
        Assert.Equal((true, false, "Left out of your achievements", false), (leftOut.AchievementsLeftOut, leftOut.ShowsAchievementsOverview, leftOut.AchievementsSubtitle,
            leftOut.HasAchievementsNote));
        leftOut.CountAchievementsAgainCommand.Execute(null);
        Assert.Equal([(ValheimId, false)], outs);

        // Steam lists none for Child of Light, a Ubisoft game: it says 0, and where its achievements may be.
        var childOfLight = new LauncherGame { Id = GameId.Parse("child-of-light"), Title = "Child of Light", Store = StoreKind.Loose, SteamAppId = 256290, Installed = true };
        var zero = Page(childOfLight, new GameAchievementsView(childOfLight.Id, "Child of Light", 256290, [], AchievementsFrom.NoneOnSteam));
        Assert.Equal(("0 achievements on Steam", false, "trophy", false), (zero.AchievementsSubtitle, zero.ShowsAchievementsOverview, zero.AchievementsNoteIcon,
            zero.CanViewAllAchievements));
        Assert.Contains("Ubisoft Connect", zero.AchievementsNote, StringComparison.Ordinal);

        // None to read: Bloodborne on an emulator has no Steam app ID; a Steam game's list comes when GameSync can ask.
        var bloodborne = new LauncherGame { Id = GameId.Parse("bloodborne"), Title = "Bloodborne", Store = StoreKind.Loose, Installed = true };
        var none = Page(bloodborne, null);
        Assert.Equal((true, false, "trophy", false), (none.HasAchievements, none.ShowsAchievementsOverview, none.AchievementsNoteIcon, none.CanViewAllAchievements));
        Assert.Equal("GameSync can't read this game's achievements yet: it reads Steam's for now.", none.AchievementsNote);
        Assert.Equal("Its list of achievements comes from Steam the next time GameSync can ask.", Page(cuphead, null).AchievementsNote);

        // A folder of your own isn't played, so it has no card.
        var world = new LauncherGame { Id = GameId.Parse("server-world"), Title = "Server world", IsFolder = true, Installed = true };
        Assert.False(Page(world, null).HasAchievements);
    }

    [Fact]
    public void KAN_105_Within_reach_is_what_most_players_have_that_you_dont_yet_never_a_hidden_one()
    {
        var ror2 = View(("A", true, false, 40, 1), ("B", false, false, 70, 0), ("C", false, true, 90, 0), ("D", false, false, null, 0), ("E", false, false, 20, 0))
            with { Game = GameId.Parse("ror2"), Title = "Risk of Rain 2" };
        var valheim = View(("A", true, false, 80, 2), ("B", false, false, 60, 0)) with { Game = ValheimId, Title = "Valheim" };
        var apex = View(("A", true, false, 2, 3)) with { Game = GameId.Parse("apex"), Title = "Apex Legends" };
        var unplayed = View(("A", false, false, 99, 0)) with { Game = GameId.Parse("deadlock"), Title = "Deadlock" };

        // A game's own: the locked ones most players have, never one hidden or with no percentage.
        Assert.Equal(["B", "E"], ror2.WithinReach(3).Select(a => a.Id));

        // The page's: across the games started and not finished, a Zenith and a game not started left out.
        var room = new TrophyRoomViewModel();
        room.Show([ror2, valheim, apex, unplayed], DateTime.Now);
        Assert.True(room.HasReach);
        Assert.Equal(["Risk of Rain 2 B", "Valheim B", "Risk of Rain 2 E"], room.Reach.Select(r => $"{r.GameTitle} {r.Achievement.Name}"));
        Assert.StartsWith("B, Risk of Rain 2, locked, 70", room.Reach[0].ReachSpoken, StringComparison.Ordinal);

        room.Show([apex], DateTime.Now);
        Assert.False(room.HasReach);
    }

    [Fact]
    public void A_small_rings_count_goes_under_it_and_a_small_Zenith_medal_has_no_glow()
    {
        // The owner, 3 Oct 2026: on Home "the number from the achievements is spilling out" of the 76px ring.
        var ring = new UI.Controls.GsProgressRing { Sub = "123 / 171", Width = 76 };
        Assert.Equal((false, true), (ring.ShowsSubInside, ring.ShowsSubUnder));
        ring.Width = 140;
        Assert.Equal((true, false), (ring.ShowsSubInside, ring.ShowsSubUnder));

        // KAN-121: a finished ring's halo reaches past it evenly, drawn round it rather than cut off by its square box.
        Assert.Equal(new Avalonia.Thickness(-20), ring.GlowMargin);
        ring.Sub = null;
        Assert.Equal((false, false), (ring.ShowsSubInside, ring.ShowsSubUnder));

        // "The platinum art has a weird square": the glow is drawn round the medal, only where it's big enough to show.
        var medal = new UI.Controls.GsZenithMedal { IsEarned = true, Width = 24 };
        Assert.False(medal.ShowsGlow);
        medal.Width = 64;
        Assert.True(medal.ShowsGlow);
        // KAN-118 (the owner: "The glow is not centered"): the same on every side; since version 49 a warm glow under the
        // Zenith's banner, a little past it.
        Assert.Equal(new Avalonia.Thickness(-12), medal.GlowMargin);
        medal.IsEarned = false;
        Assert.False(medal.ShowsGlow);

        // The popup's bar: how far the game is now; a Zenith's popup shows the game finished instead.
        var popup = new UI.Controls.GsAchievementPopup { Tier = "gold", Done = 123, Total = 171 };
        Assert.Equal((true, "123 / 171", 40.0), (popup.ShowsProgress, popup.ProgressText, popup.FillWidth));
        popup.Tier = "zenith";
        Assert.Equal((true, false), (popup.IsZenith, popup.ShowsProgress));
    }

    [Fact]
    public void ACH_02_a_covers_ring_shows_how_far_its_achievements_are_and_the_Zenith_medal_once_every_one_is()
    {
        // Design system version 39 ("library covers show progress"): a ring opposite the save mark once one is unlocked.
        var ror2 = new LauncherGame { Id = GameId.Parse("ror2"), Title = "Risk of Rain 2", Store = StoreKind.Steam, Installed = true };
        var apex = new LauncherGame { Id = GameId.Parse("apex"), Title = "Apex Legends", Store = StoreKind.Steam, Installed = true };
        var deadlock = new LauncherGame { Id = GameId.Parse("deadlock"), Title = "Deadlock", Store = StoreKind.Steam, Installed = true };
        var progress = new Dictionary<GameId, (int Done, int Total)> { [ror2.Id] = (123, 171), [apex.Id] = (12, 12), [deadlock.Id] = (0, 30) };
        var tiles = LibraryViewModel.Tiles([ror2, apex, deadlock], DateTime.Now, null, progress);
        Assert.Equal((123, 171), (tiles[ror2.Id].AchievementsDone, tiles[ror2.Id].AchievementsTotal));

        var tile = new UI.Controls.GsGameTile { Title = "Risk of Rain 2", AchievementsDone = 123, AchievementsTotal = 171 };
        Assert.Equal((true, false, 71.0), (tile.ShowsAchievementsRing, tile.ShowsAchievementsMedal, tile.AchievementsPercent));
        Assert.Equal("Achievements: 71% · 123 of 171", tile.AchievementsWords);
        Assert.Equal("Risk of Rain 2, Achievements: 71% · 123 of 171", Avalonia.Automation.AutomationProperties.GetName(tile));

        // 170 of 171 is 99%, never full; every one is the Zenith medal; none unlocked, nothing.
        Assert.Equal(99.0, new UI.Controls.GsGameTile { AchievementsDone = 170, AchievementsTotal = 171 }.AchievementsPercent);
        var zenith = new UI.Controls.GsGameTile { Title = "Apex Legends", AchievementsDone = 12, AchievementsTotal = 12 };
        Assert.Equal((false, true, "Zenith: every achievement, 12 of 12"), (zenith.ShowsAchievementsRing, zenith.ShowsAchievementsMedal, zenith.AchievementsWords));
        var none = new UI.Controls.GsGameTile { Title = "Deadlock", AchievementsDone = 0, AchievementsTotal = 30 };
        Assert.Equal((false, false, null), (none.ShowsAchievementsRing, none.ShowsAchievementsMedal, none.AchievementsWords));
        Assert.Equal("Deadlock", Avalonia.Automation.AutomationProperties.GetName(none));
    }

    /// <summary>A Steam game with two achievements, A and B, and the bits of those unlocked, each a day apart in September 2026.</summary>
    private static void SteamGame(string stats, long appId, int bits)
    {
        var app = appId.ToString(CultureInfo.InvariantCulture);
        Write(Path.Combine(stats, $"UserGameStatsSchema_{app}.bin"), Kv.Block(app, Kv.Block("stats", Kv.Block("1", Kv.Text("type", "ACHIEVEMENTS"),
            Kv.Block("bits",
                Achievement(0, "A", Kv.Text("name", "First"), "Do the first", hidden: false, "a.jpg"),
                Achievement(1, "B", Kv.Text("name", "Second"), "Do the second", hidden: false, "b.jpg"))))));
        var times = Enumerable.Range(0, 2).Where(bit => (bits & (1 << bit)) != 0)
            .Select(bit => Kv.Int(bit.ToString(CultureInfo.InvariantCulture), (int)new DateTimeOffset(2026, 9, 20 + bit, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()))
            .ToArray();
        Write(Path.Combine(stats, $"UserGameStats_281062582_{app}.bin"), Kv.Block("cache", Kv.Block("1", Kv.Int("data", bits), Kv.Block("AchievementTimes", times))));
    }
}
