using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;
using GameSync.Host;
using GameSync.UI.Branding;
using GameSync.UI.Controls;
using GameSync.UI.Theming;
using GameSync.UI.ViewModels;
using GameSync.Windows;
using Microsoft.Win32;

namespace GameSync.Core.Tests;

/// <summary>
/// Milestone 5's app: the tray icon's state and pictures (BG-07), one app per person that a second start finds (BG-01),
/// starting at sign-in from the Run key, how the exe was started, the look kept per PC (LOOK-01, LOOK-07), and the Console.
/// </summary>
public class AppTests
{
    private static readonly SyncCounts AllFine = new(Syncing: 44, Synced: 44, NeedYou: 0, Waiting: 0);

    [Fact]
    public void BG_07_the_tray_shows_what_matters_most_and_hovering_gives_the_counts()
    {
        var synced = TrayStatus.From(setUp: true, AllFine, working: false, cloud: null, playing: null);
        Assert.Equal(TrayMood.Synced, synced.Mood);
        Assert.Equal("GameSync: all synced\n44 of 44 synced", synced.Tooltip);

        Assert.Equal(TrayMood.Working, TrayStatus.From(true, AllFine, working: true, null, null).Mood);
        var playing = TrayStatus.From(true, AllFine, working: true, null, "Elden Ring");
        Assert.Equal(TrayMood.Playing, playing.Mood);
        Assert.Equal("GameSync: playing Elden Ring\n44 of 44 synced", playing.Tooltip);

        var twoNeedYou = TrayStatus.From(true, new SyncCounts(44, 41, 2, 1), working: true, CloudErrorKind.Offline, "Elden Ring");
        Assert.Equal(TrayMood.NeedsYou, twoNeedYou.Mood);
        Assert.Equal("GameSync: 2 games need you\n41 of 44 synced · 2 need you · 1 waiting", twoNeedYou.Tooltip);

        var offline = TrayStatus.From(true, new SyncCounts(44, 43, 0, 1), working: true, CloudErrorKind.Offline, "Elden Ring");
        Assert.Equal(TrayMood.Offline, offline.Mood);
        Assert.StartsWith("GameSync: offline, saves wait on this PC", offline.Tooltip, StringComparison.Ordinal);

        Assert.Equal(TrayMood.NeedsYou, TrayStatus.From(true, AllFine, false, CloudErrorKind.SignInExpired, null).Mood);
        Assert.Equal(TrayMood.NeedsYou, TrayStatus.From(true, AllFine, false, CloudErrorKind.StorageFull, null).Mood);
        Assert.Equal(TrayMood.Working, TrayStatus.From(true, new SyncCounts(44, 43, 0, 1), false, null, null).Mood);
        Assert.Equal("GameSync: no games sync yet", TrayStatus.From(true, SyncCounts.None, false, null, null).Tooltip);
        Assert.Equal("GameSync isn't set up on this PC yet", TrayStatus.From(false, SyncCounts.None, false, null, null).Tooltip);

        var longTitle = TrayStatus.From(true, new SyncCounts(1000, 998, 1, 1), false, null, null);
        Assert.True(longTitle.Tooltip.Length < 128, "Windows shows at most 127 characters.");
    }

    [Fact]
    public void BG_07_counts_come_from_each_games_one_status()
    {
        var counts = SyncCounts.Of([GameStatus.Synced, GameStatus.BackupOnly, GameStatus.Conflict, GameStatus.Blocked, GameStatus.UploadPending,
            GameStatus.NewerInCloud, GameStatus.Playing, GameStatus.NotAvailable, null]);

        Assert.Equal(new SyncCounts(Syncing: 9, Synced: 2, NeedYou: 2, Waiting: 2), counts);
    }

    [Fact]
    public async Task BG_07_an_offline_sync_says_so_on_every_game_so_the_tray_can_tell_offline_from_needs_you()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("hades");
        desktop.AddGame("celeste");
        desktop.Write("hades", "slot.sav", "one");
        desktop.Write("celeste", "slot.sav", "one");
        desktop.Offline = true;

        var offline = await desktop.SyncAsync();
        Assert.All(offline, r => Assert.Equal(CloudErrorKind.Offline, r.CloudProblem));
        Assert.Equal(CloudErrorKind.Offline, TrayStatus.CloudTrouble(offline));

        desktop.Offline = false;
        var online = await desktop.SyncAsync();
        Assert.All(online, r => Assert.Null(r.CloudProblem));
        Assert.Null(TrayStatus.CloudTrouble(online));

        GameResult Result(CloudErrorKind kind) => new(GameId.Parse("hades"), "Hades", null, GameStatus.UploadPending, "") { CloudProblem = kind };
        Assert.Equal(CloudErrorKind.SignInExpired, TrayStatus.CloudTrouble([Result(CloudErrorKind.Offline), Result(CloudErrorKind.SignInExpired)]));
    }

    [Theory]
    [InlineData(TrayBadge.None)]
    [InlineData(TrayBadge.Working)]
    [InlineData(TrayBadge.NeedsYou)]
    [InlineData(TrayBadge.Offline)]
    [InlineData(TrayBadge.Playing)]
    public void BG_07_the_tray_icon_is_the_mark_with_a_badge_cut_out_of_it(TrayBadge badge)
    {
        foreach (var light in new[] { false, true })
        {
            var status = ThemeEngine.Build(light ? new ThemeChoice(Mode: ThemeMode.Light) : new ThemeChoice());
            var icon = MarkArt.Tray(24, badge, light);
            Assert.Equal(24 * 24 * 4, icon.Length);
            Assert.Equal(0, Pixel(icon, 24, 0, 0).A);

            // The mark's left edge is in the taskbar's ink, or grey when offline.
            var ink = badge == TrayBadge.Offline ? status["neutral"] : light ? "#1a1a1a" : "#ffffff";
            Assert.Equal(ink, Pixel(icon, 24, 3, 6).Hex);

            // Where the front square's edge passes the badge, a ring is cut out so the badge stands apart.
            Assert.Equal(badge == TrayBadge.None ? 255 : 0, Pixel(icon, 24, 20, 12).A);
            if (badge != TrayBadge.None)
            {
                var color = status[badge switch { TrayBadge.Working => "ok", TrayBadge.NeedsYou => "warn", TrayBadge.Playing => "play", _ => "neutral" }];
                Assert.Equal((color, (byte)255), Pixel(icon, 24, 15, 20));
            }
        }
    }

    [Fact]
    public void The_app_icon_holds_every_size_windows_asks_for_and_the_one_in_the_repo_is_current()
    {
        var ico = MarkArt.Ico(MarkArt.IconSizes, MarkArt.AppIcon);
        Assert.Equal([0, 0, 1, 0, (byte)MarkArt.IconSizes.Length, 0], ico[..6]);
        var last = 6 + 16 * (MarkArt.IconSizes.Length - 1);
        var pngAt = BitConverter.ToInt32(ico, last + 12);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], ico[pngAt..(pngAt + 4)]);

        var big = MarkArt.AppIcon(256);
        Assert.Equal(0, Pixel(big, 256, 0, 0).A);
        Assert.Equal(ThemeEngine.Build(new ThemeChoice())["bg-100"], Pixel(big, 256, 128, 128).Hex);

        // Made with: dotnet run --project tools/GameSync.Snapshots -- <folder> icons
        var committed = Path.Combine(RepoRoot(), "src", "GameSync.Tray", "Assets", "gamesync.ico");
        Assert.True(File.ReadAllBytes(committed).SequenceEqual(ico), "The app's icon file is older than MarkArt; make it again with the snapshot tool's 'icons'.");
    }

    [Fact]
    public void BG_01_one_app_per_windows_user_and_data_folder()
    {
        Assert.Equal(AppPipe.NameFor(@"C:\Data\GameSync"), AppPipe.NameFor(@"c:\data\gamesync\"));
        Assert.NotEqual(AppPipe.NameFor(@"C:\Data\GameSync"), AppPipe.NameFor(@"C:\Data\Other"));
        Assert.DoesNotContain("Data", AppPipe.NameFor(@"C:\Data\GameSync"), StringComparison.OrdinalIgnoreCase);

        using var world = new TestWorld();
        using var first = AppPipe.TryClaim(world.Root);
        Assert.NotNull(first);
        Assert.Null(AppPipe.TryClaim(world.Root));
        using var other = AppPipe.TryClaim(Path.Combine(world.Root, "other"));
        Assert.NotNull(other);
    }

    [Fact]
    public async Task BG_01_a_second_start_asks_the_running_app_to_show_its_window()
    {
        using var world = new TestWorld();
        using var stop = new CancellationTokenSource();
        var heard = new List<string>();
        var serving = AppPipe.ServeAsync(world.Root, message =>
        {
            heard.Add(message);
            return Task.FromResult(message == "show" ? "ok" : "unknown");
        }, e => throw e, stop.Token);

        Assert.Equal("ok", await AppPipe.SendAsync(world.Root, "show", TimeSpan.FromSeconds(10)));
        Assert.Equal("ok", await AppPipe.SendAsync(world.Root, "show", TimeSpan.FromSeconds(10)));
        Assert.Equal("unknown", await AppPipe.SendAsync(world.Root, "format c:", TimeSpan.FromSeconds(10)));
        Assert.Equal(["show", "show", "format c:"], heard);

        stop.Cancel();
        await serving.WaitAsync(TimeSpan.FromSeconds(10));

        // With no app running, a start doesn't wait long.
        Assert.Null(await AppPipe.SendAsync(Path.Combine(world.Root, "nobody"), "show", TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public void BG_01_start_at_sign_in_is_the_persons_run_key_and_task_managers_switch_counts()
    {
        var root = $@"Software\GameSync.Tests.{Guid.NewGuid():N}";
        try
        {
            var start = new SignInStart(Registry.CurrentUser, root + @"\Run", root + @"\Approved");
            const string command = "\"C:\\Apps\\GameSync.Tray.exe\" --background";
            Assert.False(start.IsOn);

            start.TurnOn(command);
            Assert.Equal(command, start.Command);

            using (var approved = Registry.CurrentUser.CreateSubKey(root + @"\Approved"))
            {
                approved.SetValue(SignInStart.Name, new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
            }

            Assert.False(start.IsOn);
            start.TurnOn(command);
            Assert.True(start.IsOn);

            start.TurnOff();
            Assert.False(start.IsOn);
            using var run = Registry.CurrentUser.OpenSubKey(root + @"\Run");
            Assert.Null(run?.GetValue(SignInStart.Name));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void BG_01_sign_in_starts_the_app_in_the_tray_and_every_other_start_is_read_right()
    {
        Assert.Equal("\"C:\\Apps\\GameSync.Tray.exe\" --background", Schedule.SignInCommand(@"C:\Apps\GameSync.Tray.exe", Engine.DefaultDataDir));
        Assert.Equal("\"C:\\Apps\\GameSync.Tray.exe\" --data \"D:\\Sync\" --background", Schedule.SignInCommand(@"C:\Apps\GameSync.Tray.exe", @"D:\Sync"));

        Assert.Equal(new AppStart(Engine.DefaultDataDir, IsApp: true, ShowWindow: true), AppStart.Parse([]));
        Assert.Equal(new AppStart(Engine.DefaultDataDir, IsApp: true, ShowWindow: false), AppStart.Parse(["--background"]));
        Assert.Equal(new AppStart(@"D:\Sync", IsApp: true, ShowWindow: false), AppStart.Parse(["--data", @"D:\Sync", "--background"]));
        Assert.Equal(new AppStart(Engine.DefaultDataDir, IsApp: true, ShowWindow: false), AppStart.Parse(["agent"]));
        Assert.False(AppStart.Parse(["daily", "--if-missed"]).IsApp);
        Assert.False(AppStart.Parse(["launch", "hades", "--", @"C:\Games\Hades\Hades.exe"]).IsApp);
    }

    [Fact]
    public void LOOK_07_the_look_is_kept_per_pc_and_match_windows_follows_windows()
    {
        using var world = new TestWorld();
        using var pc = world.Pc("DESKTOP");
        Assert.Equal(new Look(), Look.Load(pc.State));

        var sakura = new Look(Look.MatchWindows, PureBlack: true, Preset: "sakura", Primary: "cyan", Secondary: null);
        sakura.Save(pc.State);
        Assert.Equal(sakura, Look.Load(pc.State));

        Assert.Equal(ThemeMode.Light, sakura.Resolve(windowsLight: true, "#0078d4").Mode);
        Assert.Equal(ThemeMode.Dark, sakura.Resolve(windowsLight: false, "#0078d4").Mode);
        Assert.Equal(ThemeMode.Light, (sakura with { Mode = Look.Light }).Resolve(windowsLight: false, null).Mode);
        Assert.Equal("#0078d4", new Look(Preset: "windows").Resolve(false, "#0078d4").Accent);

        // LOOK-10: reset to default is Arcade, dark, pure black off.
        new Look().Save(pc.State);
        Assert.Equal(new ThemeChoice(), Look.Load(pc.State).Resolve(windowsLight: true, null));

        pc.State.SetSetting("look.preset", "no-such-theme");
        Assert.Equal("arcade", Look.Load(pc.State).Preset);
    }

    [Fact]
    public void The_console_starts_with_todays_log_and_keeps_the_last_two_thousand_lines()
    {
        var console = new ConsoleViewModel();
        console.AddFile(["09:15:02  Hades: playing since 09:15:01.", "10:02:40  ! Hades: A conflict waits for you.", "not a log line"], new DateTime(2026, 9, 28));

        Assert.Equal(
            [new LogLine("09:15:02", LogLevel.Info, "agent", "Hades: playing since 09:15:01."), new LogLine("10:02:40", LogLevel.Warn, "note", "Hades: A conflict waits for you.")],
            console.Lines);

        for (var i = 0; i < ConsoleViewModel.Keep + 10; i++)
        {
            console.Add(DateTime.Now, $"line {i}");
        }

        Assert.Equal(ConsoleViewModel.Keep, console.Lines.Count);
        Assert.Equal($"line {ConsoleViewModel.Keep + 9}", console.Lines[^1].Message);
    }

    [Fact]
    public void The_apps_output_logs_like_the_background_app_and_tells_the_window()
    {
        using var world = new TestWorld();
        var output = new AppOutput(world.Root, (_, _) => { });
        var lines = new List<string>();
        var working = new List<bool>();
        output.Line += lines.Add;
        output.WorkingChanged += working.Add;

        output.Say("Hades: synced.");
        output.NeedsYou("Hades", "A conflict waits for you.");
        ((IAgentOutput)output).Working(true);
        ((IAgentOutput)output).Working(false);

        Assert.Equal(["Hades: synced.", "! Hades: A conflict waits for you."], lines);
        Assert.Equal([true, false], working);
        var log = File.ReadAllText(Path.Combine(world.Root, "logs", $"agent-{DateTime.Now:yyyy-MM-dd}.log"));
        Assert.Contains("Hades: synced.", log);
        Assert.Contains("! Hades: A conflict waits for you.", log);
    }

    [Fact]
    public void PLAY_01_the_librarys_tabs_switch_its_view_in_place_and_homes_tabs_open_those_views()
    {
        LauncherGame Game(string id, GameStatus? status = GameStatus.Synced, bool software = false, bool hidden = false) =>
            new() { Id = GameId.Parse(id), Title = id, Status = status, Syncs = true, IsSoftware = software, IsHidden = hidden, LastPlayedUtc = DateTime.UtcNow };
        IReadOnlyList<LauncherGame> games = [Game("hades"), Game("celeste", GameStatus.Conflict), Game("wallpaper-engine", software: true), Game("rounds", hidden: true)];
        var shown = new List<(string Page, string? Tab)>();
        var played = new List<GameId>();
        var synced = 0;
        var actions = new LauncherActions(played.Add, () => synced++, (page, tab) => shown.Add((page, tab)), (_, _) => { });

        var library = LibraryViewModel.From(games, DateTime.Now, "all", actions);
        var changed = new List<string?>();
        library.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal(["all", "attn", "software", "hidden"], library.Tabs.Select(t => t.Id));
        Assert.Equal(["hades", "celeste"], library.Tiles.Select(t => t.Title));
        library.SelectedTab = "attn";
        Assert.Equal(["celeste"], library.Tiles.Select(t => t.Title));
        library.SelectedTab = "hidden";
        Assert.Equal(["rounds"], library.Tiles.Select(t => t.Title));
        Assert.Contains(nameof(LibraryViewModel.Tiles), changed);
        Assert.Equal("all", LibraryViewModel.From([Game("hades")], DateTime.Now, "hidden").SelectedTab);

        using var world = new TestWorld();
        using var pc = world.Pc("DESKTOP");
        var home = HomeViewModel.From(Launcher.Home(games, pc.State, DateTime.Now), games, DateTime.Now, actions);
        home.SelectedTab = "attn";
        Assert.Equal(HomeViewModel.ThisPage, home.SelectedTab);
        home.PlayCommand.Execute(null);
        home.SyncNowCommand.Execute(null);
        home.OpenLibraryCommand.Execute(null);
        home.OpenSavesCommand.Execute(null);
        Assert.Equal([("library", "attn"), ("library", "all"), ("saves", null)], shown);
        Assert.Equal([GameId.Parse("hades")], played);
        Assert.Equal(1, synced);
    }

    [Fact]
    public async Task PLAY_02_a_game_found_but_not_synced_starts_from_the_launcher_with_nothing_to_check()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud }.Save(data);
        var folder = Path.Combine(world.Root, "Games", "Fake Game");
        FakeGames.Install(folder, "FakeGame");
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([new LibraryEntry { Id = GameId.Parse("fake-game"), Title = "Fake Game", InstallDir = folder }]);
        }

        var said = new List<string>();
        var warned = new List<string>();
        await AppActions.PlayAsync(data, GameId.Parse("fake-game"), new Output(said.Add, warned.Add));

        Assert.Equal(["Launched Fake Game."], said);
        Assert.Empty(warned);
        Assert.Empty(Directory.EnumerateFileSystemEntries(world.Cloud));

        await AppActions.PlayAsync(data, GameId.Parse("no-such-game"), new Output(said.Add, warned.Add));
        Assert.StartsWith("There's no game 'no-such-game' in the library", Assert.Single(warned), StringComparison.Ordinal);

        // The fake game runs for a second; let it finish before its folder goes.
        await Task.Delay(TimeSpan.FromSeconds(1.5));
    }

    private sealed class Output(Action<string> say, Action<string> needsYou) : IAgentOutput
    {
        public void Say(string line) => say(line);

        public void NeedsYou(string title, string message) => needsYou(message);
    }

    private static (string Hex, byte A) Pixel(byte[] bgra, int size, int x, int y)
    {
        var i = (y * size + x) * 4;
        return ($"#{bgra[i + 2]:x2}{bgra[i + 1]:x2}{bgra[i]:x2}", bgra[i + 3]);
    }

    private static string RepoRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "GameSync.sln")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The tests run outside the repository.");
    }
}
