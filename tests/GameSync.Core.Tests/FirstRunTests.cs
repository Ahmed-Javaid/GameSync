using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Host;
using GameSync.UI.ViewModels;
using GameSync.Windows;

namespace GameSync.Core.Tests;

/// <summary>
/// First run (ONB-01 to ONB-05): the scan before GameSync is set up, what it found in groups, finishing with the games
/// ticked, Skip for now keeping every version on the PC until a cloud is connected, and the screen's four steps.
/// </summary>
public class FirstRunTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private const string Manifest = """
        Lantern Keep:
          files:
            "<base>/Saves":
              tags: [save]
          installDir:
            Lantern Keep: {}
        """;

    [Fact]
    public async Task ONB_01_first_run_scans_a_PC_not_set_up_yet_and_finishes_with_the_games_ticked()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var games = Path.Combine(world.Root, "Games");
        Write(Path.Combine(games, "Lantern Keep", "Lantern.exe"), "MZ");
        Write(Path.Combine(games, "Lantern Keep", "Saves", "slot1.sav"), "the lighthouse");
        Write(Path.Combine(games, "Quiet Game", "Quiet.exe"), "MZ");
        var manifest = Path.Combine(world.Root, "manifest.yaml");
        File.WriteAllText(manifest, Manifest);
        new SaveListStore(data).UpdateFromFile(manifest);

        // Add a game folder, then the scan: it only reads, and GameSync still isn't set up.
        Assert.Null(FirstRun.AddGameFolder(data, games));
        var seen = new List<ScanProgress>();
        var scan = await FirstRun.ScanAsync(data, new Collect(seen), Ct, folders => new StoreSources { GameFolders = folders });
        Assert.False(File.Exists(AppConfig.PathIn(data)));
        Assert.Equal((2, 1), (scan.Games, scan.WithSaves));
        Assert.Equal(["Steam", "Epic Games", "EA app"], scan.Stores.Select(s => s.Name));
        Assert.All(scan.Stores, s => Assert.Equal((0, "Nothing installed right now"), (s.Games, s.Where)));
        Assert.Equal(new GameFolderSummary(games, 2, false), Assert.Single(scan.Folders));
        Assert.Contains(seen, p => p.Total == 2 && p.Done == 2);

        // Choose games: Lantern Keep syncs, found by the save list in its game folder; Quiet Game is watched.
        var groups = FirstRun.Groups(data);
        var sync = Assert.Single(groups, g => g.Kind == SetupGroupKind.Sync);
        var lantern = Assert.Single(sync.Games);
        Assert.Equal<(string, string?, string?)>(("Lantern Keep", @"Game folder\Saves", "save list"), (lantern.Title, lantern.SavePath, lantern.FoundBy));
        Assert.Equal("Quiet Game", Assert.Single(Assert.Single(groups, g => g.Kind == SetupGroupKind.NoSaves).Games).Title);

        // Start using GameSync: the cloud, the game ticked, start at sign-in and the daily backup.
        var schedule = new FakeSchedule();
        var cloud = FirstRun.CloudFolder(data, Path.Combine(world.Root, "NAS"));
        Assert.Equal(Path.Combine(world.Root, "NAS", "GameSync"), cloud);
        var result = await FirstRun.FinishAsync(data, new SetupChoice { Remote = cloud, Games = [lantern.Id], DailyAt = new TimeOnly(21, 30) }, schedule, Ct);
        Assert.Equal((1, 0), (result.Syncing, result.BackedUp));
        Assert.Empty(result.Notes);
        Assert.Equal(cloud, AppConfig.Load(data)!.Remote);
        Assert.True(Directory.Exists(cloud));
        Assert.Equal<(bool?, TimeOnly?)>((true, new TimeOnly(21, 30)), (schedule.AtSignIn, schedule.DailyAt));
        using var engine = Engine.Open(data);
        Assert.Contains(engine.Games, g => g.Id == lantern.Id);
        Assert.Equal(LibraryState.Found, engine.Library.All().Single(e => e.Title == "Quiet Game").State);
    }

    [Fact]
    public async Task ONB_01_skip_for_now_keeps_every_version_on_this_PC_until_a_cloud_is_connected()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var saves = Path.Combine(world.Root, "Saves", "Lantern Keep");
        Write(Path.Combine(saves, "slot1.sav"), "the lighthouse");
        var id = GameId.Parse("lantern-keep");
        new AppConfig
        {
            Remote = AppConfig.NoCloud,
            Games = [new GameDefinition { Id = id, Title = "Lantern Keep", Roots = new Dictionary<string, string> { ["saves"] = saves }, Rules = [new SaveRule { Root = "saves" }] }],
        }.Save(data);

        // With no cloud, the game's first sync keeps its files on this PC, as offline, and says why in its own words;
        // nothing about it is trouble.
        var output = new Recorded();
        await AppActions.BackUpNowAsync(data, id, output, Ct);
        LocalHistory history;
        using (var engine = Engine.Open(data))
        {
            history = new LocalHistory(Cli.HistoryFolder(engine.State, data));
            Assert.Equal(VersionOrigin.KeptAtFirstSync, Assert.Single(await history.Log.ListAsync(id, Ct)).Origin);
            Assert.True(history.HasPending(id));
            Assert.StartsWith("No cloud is connected yet, and this is the game's first sync on this PC", engine.State.GetState(id).Detail, StringComparison.Ordinal);
        }

        Assert.Empty(output.NeedsYouLines);
        Assert.DoesNotContain(output.Said, line => line.Contains("Offline", StringComparison.Ordinal));
        var tray = TrayStatus.From(true, new SyncCounts(1, 0, 0, 1), false, CloudErrorKind.NotConnected, null);
        Assert.Equal(TrayMood.Synced, tray.Mood);
        Assert.Contains("kept on this PC", tray.Tooltip, StringComparison.Ordinal);
        Assert.Null(LauncherData.Devices(data).Cloud);

        // Connect the cloud later: at the next sync the first-sync rule runs as it would have, and what waited goes up.
        await FirstRun.ConnectAsync(data, world.Cloud, Ct);
        Assert.Equal(world.Cloud, AppConfig.Load(data)!.Remote);
        await AppActions.RetryAsync(data, id, output, Ct);
        var up = await new FolderCloud(world.Cloud).Log.ListAsync(id, Ct);
        Assert.Equal([VersionOrigin.KeptAtFirstSync, VersionOrigin.FirstBackup], up.OrderBy(v => v.CreatedUtc).Select(v => v.Origin));
        Assert.False(history.HasPending(id));
        Assert.Empty(output.NeedsYouLines);
    }

    [Fact]
    public void ONB_01_the_cloud_in_a_folder_is_GameSyncs_own_folder_inside_it_and_never_the_data_folder()
    {
        using var world = new TestWorld();

        // As on a PC, where the data folder is %LOCALAPPDATA%\GameSync.
        var data = Path.Combine(world.Root, "GameSync");
        Directory.CreateDirectory(data);
        var nas = Path.Combine(world.Root, "NAS");
        Directory.CreateDirectory(nas);

        Assert.Equal(Path.Combine(nas, "GameSync"), FirstRun.CloudFolder(data, nas));
        Assert.Equal(Path.Combine(nas, "GameSync"), FirstRun.CloudFolder(data, Path.Combine(nas, "GameSync") + Path.DirectorySeparatorChar));
        Assert.Equal(Path.Combine(world.Root, "USB", "GameSync"), FirstRun.CloudFolder(data, Path.Combine(world.Root, "USB")));
        Assert.Throws<UsageException>(() => FirstRun.CloudFolder(data, data));
        Assert.Throws<UsageException>(() => FirstRun.CloudFolder(data, world.Root));
        Assert.Throws<UsageException>(() => FirstRun.CloudFolder(data, Path.Combine(data, "cloud")));
        var noDrive = Enumerable.Range('D', 23).Select(c => $"{(char)c}:\\").First(d => !Directory.Exists(d));
        Assert.Throws<UsageException>(() => FirstRun.CloudFolder(data, Path.Combine(noDrive, "Backups")));
        Assert.Equal(@"Documents\My Games\Terraria", FirstRun.Friendly("<documents>/My Games/Terraria"));
        Assert.Equal(@"AppData\LocalLow\Team Cherry\Hollow Knight", FirstRun.Friendly("<localLow>/Team Cherry/Hollow Knight"));
    }

    [Fact]
    public void BG_01_a_copy_on_another_data_folder_never_changes_what_starts_at_sign_in()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var before = SignInStart.ForThisUser().Command;

        var schedule = new FirstRun.WindowsSchedule(data);
        Assert.Contains("isn't its usual data folder", schedule.StartAtSignIn(true), StringComparison.Ordinal);
        Assert.Contains("isn't its usual data folder", schedule.Daily(new TimeOnly(20, 0)), StringComparison.Ordinal);
        Assert.Null(schedule.StartAtSignIn(false));
        Assert.Equal(before, SignInStart.ForThisUser().Command);
    }

    [Fact]
    public async Task ONB_02_the_screen_scans_groups_what_it_found_and_starts_with_what_was_chosen()
    {
        SetupChoice? finished = null;
        var groups = new List<SetupGroup>
        {
            new(SetupGroupKind.Sync, Enumerable.Range(1, 6).Select(n => Game($"game-{n}", $"Game {n}", 1_000_000)).ToList()),
            new(SetupGroupKind.StoreCloud, [Game("terraria", "Terraria", 300_000_000)]),
            new(SetupGroupKind.OnlineOnly, [Game("apex", "Apex Legends", 1000, "EasyAntiCheat"), Game("rocket", "Rocket League", 1000)]),
            new(SetupGroupKind.NoSaves, [Game("rounds", "ROUNDS", 0)]),
        };
        var folder = @"D:\NAS\GameSync";
        var actions = new SetupActions(
            (_, _) => Task.FromResult(new SetupScan([new StoreSummary("Steam", 10, @"E:\Steam")], [new GameFolderSummary(@"G:\", 3, false)], 10, 9, TimeSpan.FromSeconds(12))),
            _ => Task.FromResult<IReadOnlyList<SetupGroup>>(groups),
            (_, _) => Task.FromResult<string?>(null),
            new CloudActions(false, _ => Task.FromResult<string?>(null), _ => folder),
            (choice, _) =>
            {
                finished = choice;
                return Task.FromResult(new SetupResult(7, 1, []));
            });
        var page = new FirstRunViewModel(actions);

        // The steps: only the ones reached can be picked; the scan comes first.
        Assert.Equal(["scan", "choose", "cloud", "daily"], page.Steps.Select(s => s.Id));
        await page.ScanAsync();
        Assert.StartsWith("Scan finished in 12 seconds: 10 games, saves found for 9", page.ScanLabel, StringComparison.Ordinal);
        Assert.Equal((@"G:\", "3 games"), (page.Folders[0].Path, page.Folders[0].Meta));
        Assert.True(page.CanChoose);
        page.NextCommand.Execute(null);
        Assert.True(page.IsChoose);
        Assert.Equal("check", page.Steps[0].Icon);

        // Groups: Sync open with four rows and Show 2 more; online-only unticked; No saves found yet watched, with no box.
        var sync = page.Groups[0];
        Assert.Equal((true, 4, true, "Show 2 more"), (sync.IsOpen, sync.VisibleRows.Count, sync.HasMore, sync.MoreLabel));
        Assert.Equal(true, sync.IsChecked);
        Assert.Equal(false, page.Groups[2].IsChecked);
        Assert.Equal("only settings files found · Apex Legends and Rocket League", page.Groups[2].Why);
        Assert.True(page.Groups[3].IsWatch);
        Assert.StartsWith("Apex Legends has an anti-cheat", page.AntiCheatNote, StringComparison.Ordinal);
        sync.Rows[0].IsChecked = false;
        Assert.Null(sync.IsChecked);
        sync.ToggleCommand.Execute(null);
        Assert.Equal(true, sync.IsChecked);
        sync.Rows[5].IsChecked = false;

        // The cloud: without Google's client, a folder; Skip for now keeps everything on the PC.
        page.NextCommand.Execute(null);
        Assert.Equal(("Skip for now", false), (page.CloudNextLabel, page.CloudNextIsMain));
        Assert.Equal(AppConfig.NoCloud, page.Cloud.Remote);
        Assert.True(page.Cloud.CannotSignIn);
        page.Cloud.ChooseFolder(@"D:\NAS");
        Assert.Equal((folder, "Continue"), (page.Cloud.Remote, page.CloudNextLabel));
        Assert.Contains("runs in the background", page.FirstBackupText, StringComparison.Ordinal);

        // Backups and startup: the time is checked; Start using GameSync takes it all.
        page.NextCommand.Execute(null);
        Assert.True(page.IsDaily);
        page.EditTimeCommand.Execute(null);
        page.TimeText = "25:00";
        page.SaveTimeCommand.Execute(null);
        Assert.True(page.HasTimeError);
        page.TimeText = "7:30";
        page.SaveTimeCommand.Execute(null);
        Assert.Equal("Back up every day at 07:30", page.DailyTitle);
        Assert.StartsWith("5 games sync and 1 is backed up.", page.ReadyText, StringComparison.Ordinal);
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)page.FinishCommand).ExecuteAsync(null);
        Assert.NotNull(finished);
        Assert.Equal(folder, finished.Remote);
        Assert.Equal(new TimeOnly(7, 30), finished.DailyAt);
        Assert.True(finished.StartAtSignIn);
        Assert.Equal(["game-1", "game-2", "game-3", "game-4", "game-5", "terraria"], finished.Games.Select(g => g.Value));
    }

    [Fact]
    public async Task ONB_01_Home_offers_Connect_the_cloud_until_one_is_and_first_run_has_the_whole_window()
    {
        using var world = new TestWorld();
        using var state = new StateStore(Path.Combine(world.Root, "data"));
        var now = new DateTime(2026, 9, 30, 20, 0, 0, DateTimeKind.Local);
        var games = Launcher.Games([], [], state, null, new Dictionary<long, SteamPlay>(), null);
        var asked = 0;
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { }) { ConnectCloud = () => asked++ };

        // Skipped in first run: Home's top bar offers Connect the cloud in place of where the saves go.
        var home = HomeViewModel.From(Launcher.Home(games, state, now), games, now, actions, new HomeStatus("No cloud yet", NoCloud.Message, "DESKTOP", []) { NoCloud = true });
        Assert.Equal((true, false), (home.NoCloud, home.ShowsCloud));
        home.ConnectCloudCommand.Execute(null);
        Assert.Equal(1, asked);
        var connectedHome = HomeViewModel.From(Launcher.Home(games, state, now), games, now, actions, new HomeStatus("Google Drive", "Reachable", "DESKTOP", []));
        Assert.Equal((false, true), (connectedHome.NoCloud, connectedHome.ShowsCloud));

        // Its dialog connects what's chosen at once: a folder, then Google Drive; Done closes it.
        var connected = new List<string>();
        var closed = false;
        var cloud = new CloudActions(true, _ => Task.FromResult<string?>("player@example.com"), picked => Path.Combine(picked, "GameSync")) { SignedIn = true };
        var dialog = new ConnectCloudViewModel(cloud, (remote, _) =>
        {
            connected.Add(remote);
            return Task.CompletedTask;
        }, () => closed = true);
        Assert.Equal(("Not now", false, false), (dialog.CloseLabel, dialog.CloseIsMain, dialog.Cloud.IsSignedIn));
        dialog.Cloud.ChooseFolder(@"D:\NAS");
        Assert.Equal(("Done", true), (dialog.CloseLabel, dialog.CloseIsMain));
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)dialog.Cloud.SignInCommand).ExecuteAsync(null);
        Assert.Equal([@"D:\NAS\GameSync", "drive"], connected);
        Assert.Equal("Signed in as player@example.com", dialog.Cloud.SignedInTitle);
        dialog.CloseCommand.Execute(null);
        Assert.True(closed);

        // A cloud that can't be connected says so in the dialog.
        var refused = new ConnectCloudViewModel(cloud, (_, _) => throw new UsageException("That folder is read-only."), () => { });
        refused.Cloud.ChooseFolder(@"D:\NAS");
        Assert.Equal("GameSync couldn't connect it: That folder is read-only.", refused.Error);

        // First run has the whole window: no rail until it's done.
        var setup = new FirstRunViewModel(new SetupActions(
            (_, _) => Task.FromResult(new SetupScan([], [], 0, 0, TimeSpan.Zero)),
            _ => Task.FromResult<IReadOnlyList<SetupGroup>>([]),
            (_, _) => Task.FromResult<string?>(null),
            cloud,
            (_, _) => Task.FromResult(new SetupResult(0, 0, []))));
        var shell = new ShellViewModel(id => id == "setup" ? setup : home, "setup");
        Assert.False(shell.ShowsRail);
        shell.Open("home");
        Assert.True(shell.ShowsRail);
    }

    private static SetupGame Game(string id, string title, long bytes, string? antiCheat = null) =>
        new(GameId.Parse(id), title, null, @"Documents\My Games\" + title, "save list", bytes, antiCheat);

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private sealed class Collect(List<ScanProgress> seen) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value)
        {
            lock (seen)
            {
                seen.Add(value);
            }
        }
    }

    private sealed class FakeSchedule : ISetupSchedule
    {
        public bool? AtSignIn { get; private set; }

        public TimeOnly? DailyAt { get; private set; }

        public string? StartAtSignIn(bool on)
        {
            AtSignIn = on;
            return null;
        }

        public string? Daily(TimeOnly? at)
        {
            DailyAt = at;
            return null;
        }
    }

    private sealed class Recorded : IAgentOutput
    {
        public List<string> NeedsYouLines { get; } = [];

        public List<string> Said { get; } = [];

        public void Say(string line) => Said.Add(line);

        public void NeedsYou(string title, string message) => NeedsYouLines.Add($"{title}: {message}");
    }
}
