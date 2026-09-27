using System.Globalization;
using System.Xml.Linq;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;
using GameSync.Host;

namespace GameSync.Core.Tests;

/// <summary>
/// Milestone 4's background work: the tasks Windows runs (BG-01, BG-02, SET-02), notifications (BG-05, BG-06), and
/// launching from Steam's launch options with no window (PLAY-08). No task is registered and no notification shown.
/// </summary>
public class BackgroundTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private static readonly GameId Fake = GameId.Parse("fake");
    private static readonly string Me = $"{Environment.UserDomainName}\\{Environment.UserName}";

    [Fact]
    public void BG_02_the_daily_task_runs_as_you_without_admin_at_low_priority_and_never_wakes_the_PC()
    {
        var program = @"C:\Games & Tools\GameSync\GameSync.Tray.exe";
        var task = XDocument.Parse(Schedule.TaskXml("GameSync: backs up <every> game.",
            Schedule.DailyTrigger(new TimeOnly(20, 0), new DateTime(2026, 9, 28, 9, 15, 0)), program, Schedule.Arguments("daily", @"D:\GameSync data"),
            unlimited: false)).Root!;

        Assert.Equal("2026-09-28T20:00:00", task.Descendants(Ns + "StartBoundary").Single().Value);
        Assert.Equal("1", task.Descendants(Ns + "DaysInterval").Single().Value);
        var principal = task.Descendants(Ns + "Principal").Single();
        Assert.Equal((Me, "InteractiveToken", "LeastPrivilege"),
            (principal.Element(Ns + "UserId")!.Value, principal.Element(Ns + "LogonType")!.Value, principal.Element(Ns + "RunLevel")!.Value));
        var settings = task.Element(Ns + "Settings")!;
        Assert.Equal(("false", "7", "PT2H", "false"), (settings.Element(Ns + "WakeToRun")!.Value, settings.Element(Ns + "Priority")!.Value,
            settings.Element(Ns + "ExecutionTimeLimit")!.Value, settings.Element(Ns + "DisallowStartIfOnBatteries")!.Value));
        Assert.Equal("GameSync: backs up <every> game.", task.Descendants(Ns + "Description").Single().Value);
        var exec = task.Descendants(Ns + "Exec").Single();
        Assert.Equal(program, exec.Element(Ns + "Command")!.Value);
        Assert.Equal("--data \"D:\\GameSync data\" daily", exec.Element(Ns + "Arguments")!.Value);
    }

    [Fact]
    public void SET_02_the_catch_up_waits_10_minutes_after_sign_in_and_the_background_app_starts_at_once_and_runs_on()
    {
        var catchUp = XDocument.Parse(Schedule.TaskXml("catch-up", Schedule.SignInTrigger(Schedule.CatchUpDelay), "GameSync.Tray.exe",
            Schedule.Arguments("daily --if-missed", Engine.DefaultDataDir), unlimited: false)).Root!;
        var signIn = catchUp.Descendants(Ns + "LogonTrigger").Single();
        Assert.Equal(("PT10M", Me), (signIn.Element(Ns + "Delay")!.Value, signIn.Element(Ns + "UserId")!.Value));
        Assert.Equal("daily --if-missed", catchUp.Descendants(Ns + "Arguments").Single().Value);

        var background = XDocument.Parse(Schedule.TaskXml("background", Schedule.SignInTrigger(null), "GameSync.Tray.exe", "agent", unlimited: true)).Root!;
        Assert.Empty(background.Descendants(Ns + "Delay"));
        Assert.Equal("PT0S", background.Descendants(Ns + "ExecutionTimeLimit").Single().Value);
        Assert.Equal("IgnoreNew", background.Descendants(Ns + "MultipleInstancesPolicy").Single().Value);
    }

    [Theory]
    [InlineData("2026-09-28 21:00", "2026-09-28 20:01", false)]
    [InlineData("2026-09-28 21:00", "2026-09-27 20:05", true)]
    [InlineData("2026-09-28 10:00", "2026-09-27 20:05", false)]
    [InlineData("2026-09-28 10:00", "2026-09-26 20:05", true)]
    [InlineData("2026-09-28 10:00", null, true)]
    public void SET_02_the_daily_run_was_missed_when_its_last_time_passed_without_one(string now, string? lastRun, bool missed)
    {
        static DateTime Local(string text) => DateTime.SpecifyKind(DateTime.ParseExact(text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), DateTimeKind.Local);

        Assert.Equal(missed, Daily.Missed(lastRun is null ? null : Local(lastRun).ToUniversalTime(), new TimeOnly(20, 0), Local(now)));
    }

    [Theory]
    [InlineData("20:00", true)]
    [InlineData("9:30", true)]
    [InlineData("07:05", true)]
    [InlineData("25:00", false)]
    [InlineData("8", false)]
    [InlineData("8pm", false)]
    [InlineData("", false)]
    public void SET_02_the_daily_time_is_hours_and_minutes(string text, bool valid) => Assert.Equal(valid, Daily.TryParseTime(text, out _));

    [Fact]
    public void BG_06_notifications_wait_while_a_fullscreen_game_runs_and_show_once_it_closes()
    {
        var busy = true;
        var shown = new List<(string, string)>();
        var queue = new NotificationQueue(() => busy, (title, message) => shown.Add((title, message)));

        queue.Add("Hades", "A conflict waits for you.");
        queue.Add("Hades", "A conflict waits for you.");
        queue.Flush();
        Assert.Empty(shown);

        busy = false;
        queue.Flush();
        queue.Add("Hades", "A conflict waits for you.");
        Assert.Equal([("Hades", "A conflict waits for you.")], shown);
    }

    [Fact]
    public void BG_05_a_problem_that_comes_back_after_the_game_was_fine_shows_again()
    {
        var shown = new List<string>();
        var queue = new NotificationQueue(() => false, (title, _) => shown.Add(title));

        queue.Add("Hades", "A conflict waits for you.");
        queue.Add("Hades", "A conflict waits for you.");
        queue.Forget("Hades");
        queue.Add("Hades", "A conflict waits for you.");

        Assert.Equal(["Hades", "Hades"], shown);
    }

    [Fact]
    public void BG_05_many_at_once_become_one_notification()
    {
        var busy = true;
        var shown = new List<(string Title, string Message)>();
        var queue = new NotificationQueue(() => busy, (title, message) => shown.Add((title, message)));
        foreach (var game in new[] { "Hades", "Celeste", "Sekiro", "Terraria" })
        {
            queue.Add(game, "A conflict waits for you.");
        }

        busy = false;
        queue.Flush();

        var one = Assert.Single(shown);
        Assert.Equal("GameSync needs you", one.Title);
        Assert.StartsWith("Hades, Celeste, Sekiro, Terraria.", one.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BG_05_a_healthy_sync_needs_nobody_and_an_expired_sign_in_is_one_notification_for_every_game()
    {
        var output = new Recorder();
        Agent.Report(output, [Result("a", GameStatus.Synced, "Up to date."), Result("b", GameStatus.Synced, "Up to date.")]);
        Assert.Empty(output.NeedYou);
        Assert.Equal(["a", "b", "GameSync"], output.Fine);

        const string expired = "The sign-in to Google Drive expired. Run 'gamesync signin' to sign in again.";
        Agent.Report(output, [Result("a", GameStatus.UploadPending, expired), Result("b", GameStatus.UploadPending, expired),
            Result("c", GameStatus.Conflict, "Both PCs changed it.")]);
        Assert.Equal(["c", "GameSync"], output.NeedYou.Select(n => n.Title));
    }

    [Fact]
    public void A_session_nobody_saw_end_ends_at_the_last_save_written()
    {
        using var world = new TestWorld();
        var saves = Directory.CreateDirectory(Path.Combine(world.Root, "saves")).FullName;
        var start = DateTime.UtcNow.AddHours(-3);
        Touch(Path.Combine(saves, "old.sav"), start.AddHours(-1));
        Touch(Path.Combine(saves, "slot 1", "slot.sav"), start.AddMinutes(95));
        Touch(Path.Combine(saves, "clock-wrong.sav"), DateTime.UtcNow.AddHours(1));

        Assert.Equal(start.AddMinutes(95), SaveActivity.LastWrite([saves], start, DateTime.UtcNow));
        Assert.Equal(start, SaveActivity.LastWrite([Path.Combine(world.Root, "unplugged")], start, DateTime.UtcNow));
    }

    [Fact]
    public void BAK_10_a_session_left_open_by_a_stopped_agent_doesnt_count_as_playing()
    {
        using var world = new TestWorld();
        var (data, _) = DataFolder(world, installDir: null);
        using (var state = new StateStore(data))
        {
            state.SetSetting(RunningGames.OpenSessionKey(Fake), DateTime.UtcNow.AddHours(-5).ToString("O", CultureInfo.InvariantCulture));
        }

        using var engine = Engine.Open(data);
        Assert.False(new RunningGames(engine).IsRunning(Fake));

        using var agent = EngineLock.TryAcquireAgent(data);
        Assert.True(new RunningGames(engine).IsRunning(Fake));
    }

    [Fact]
    public async Task PLAY_08_Steams_launch_option_starts_the_game_even_when_GameSync_cant_check_first()
    {
        using var world = new TestWorld();
        var exe = FakeGames.Install(Path.Combine(world.Root, "installs", "Fake Game"), "FakeGame");
        var save = Path.Combine(world.Root, "saves", "slot.sav");
        var output = new Recorder();

        var code = await Cli.LaunchAsync(Path.Combine(world.Root, "never set up"), ["fake", "--", exe, "--save", save, "--write-at", "0", "--run", "0"], output);

        Assert.Equal(0, code);
        Assert.Contains(output.NeedYou, n => n.Message.StartsWith("GameSync couldn't check the save before the game started", StringComparison.Ordinal));
        Assert.True(await Eventually(() => File.Exists(save)), "The game ran.");
    }

    [Fact]
    public async Task PLAY_08_with_the_background_app_off_a_launch_waits_for_the_game_to_close_and_syncs_it()
    {
        using var world = new TestWorld();
        var install = Path.Combine(world.Root, "installs", "Fake Game");
        var exe = FakeGames.Install(install, "FakeGame");
        var (data, saves) = DataFolder(world, install);
        var save = Path.Combine(saves, "slot.sav");
        var output = new Recorder();

        var code = await Cli.LaunchAsync(data, ["fake", "--", exe, "--save", save, "--write-at", "1", "--text", "played", "--run", "2"], output);

        Assert.Equal(0, code);
        Assert.Contains(output.Lines, l => l.StartsWith("Played Fake Game for", StringComparison.Ordinal));
        var log = new FolderVersionLog(world.Cloud);
        var played = (await log.ListAsync(Fake, CancellationToken.None)).MaxBy(v => v.CreatedUtc)!;
        Assert.True(played.Session!.Covers(File.GetLastWriteTimeUtc(save), TimeSpan.Zero), "The save written while playing is in the session.");
        Assert.Null(await log.GetMarkerAsync(Fake, CancellationToken.None));
        using var state = new StateStore(data);
        Assert.Empty(state.GetSettings("session.marker."));
    }

    private static GameResult Result(string game, GameStatus status, string message) => new(GameId.Parse(game), game, SyncAction.None, status, message);

    private static void Touch(string path, DateTime modifiedUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "a save");
        File.SetLastWriteTimeUtc(path, modifiedUtc);
    }

    private static async Task<bool> Eventually(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++)
        {
            await Task.Delay(100);
        }

        return done();
    }

    /// <summary>A data folder with games.json holding the fake game, synced through the test's cloud folder.</summary>
    private static (string Data, string Saves) DataFolder(TestWorld world, string? installDir)
    {
        var data = Path.Combine(world.Root, "data");
        var saves = Directory.CreateDirectory(Path.Combine(world.Root, "saves")).FullName;
        new AppConfig
        {
            Remote = world.Cloud,
            Games = [new GameDefinition { Id = Fake, Title = "Fake Game", Roots = new Dictionary<string, string> { ["saves"] = saves }, Rules = [new SaveRule { Root = "saves" }] }],
        }.Save(data);
        using var state = new StateStore(data);
        state.GetOrCreateDevice("DESKTOP");
        if (installDir is not null)
        {
            state.SetSetting($"installDir.{Fake}", installDir);
        }

        return (data, saves);
    }

    private sealed class Recorder : IAgentOutput
    {
        public List<string> Lines { get; } = [];

        public List<(string Title, string Message)> NeedYou { get; } = [];

        public List<string> Fine { get; } = [];

        public void Say(string line)
        {
            lock (Lines)
            {
                Lines.Add(line);
            }
        }

        public void NeedsYou(string title, string message) => NeedYou.Add((title, message));

        void IAgentOutput.Fine(string title) => Fine.Add(title);
    }
}
