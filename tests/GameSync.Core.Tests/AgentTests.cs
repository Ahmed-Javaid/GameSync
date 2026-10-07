using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Host;

namespace GameSync.Core.Tests;

/// <summary>
/// Milestone 4's done-when, on one PC: DESKTOP and LAPTOP are two data folders sharing a folder as the cloud. The fake
/// game plays on DESKTOP with the agent watching; once it closes, LAPTOP's agent brings the save down with no click.
/// </summary>
public class AgentTests
{
    private static readonly GameId Fake = GameId.Parse("fake");

    [Fact]
    public async Task Play_quit_and_the_laptop_has_it_without_a_single_click()
    {
        using var world = new TestWorld();
        var install = Path.Combine(world.Root, "installs", "Fake Game");
        var exe = FakeGames.Install(install, "FakeGame");
        var desktop = Pc(world, "DESKTOP", install);
        var laptop = Pc(world, "LAPTOP", installDir: null);
        var desktopSave = Path.Combine(desktop.Saves, "slot.sav");
        File.WriteAllText(desktopSave, "before playing");
        File.SetLastWriteTimeUtc(desktopSave, DateTime.UtcNow.AddHours(-1));
        var said = new TestOutput();

        using (var agent = new Agent(desktop.Data, said))
        {
            await agent.TickAsync(DateTime.UtcNow, CancellationToken.None);
            using var game = FakeGames.Run(exe, "--save", desktopSave, "--write-at", "1", "--text", "played on DESKTOP", "--run", "3");
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline && (await Versions(world)).Count < 2)
            {
                await agent.TickAsync(DateTime.UtcNow, CancellationToken.None);
                await Task.Delay(500);
            }
        }

        var played = (await Versions(world)).MaxBy(v => v.CreatedUtc)!;
        Assert.Equal((VersionKind.Normal, VersionOrigin.Session), (played.Kind, played.Origin));
        Assert.True(played.Session!.Covers(File.GetLastWriteTimeUtc(desktopSave), TimeSpan.Zero), "The save written while playing is in the session.");
        Assert.Null(await new FolderVersionLog(world.Cloud).GetMarkerAsync(Fake, CancellationToken.None));
        Assert.Contains(said.Lines, l => l.StartsWith("Fake Game: playing since", StringComparison.Ordinal));
        Assert.Contains(said.Lines, l => l.StartsWith("Fake Game: session over after", StringComparison.Ordinal));

        // BG-07: the app's window and tray icon heard the session start and end, and the sync that followed.
        Assert.Equal([(Fake, "Fake Game", true), (Fake, "Fake Game", false)], said.Plays);
        Assert.Contains(said.Syncs, results => results.Any(r => r.Game == Fake && r.Action == Core.Sync.SyncAction.Upload));
        Assert.Equal(said.Working.Count(w => w), said.Working.Count(w => !w));

        using (var agent = new Agent(laptop.Data, new TestOutput()))
        {
            await agent.TickAsync(DateTime.UtcNow, CancellationToken.None);
        }

        Assert.Equal("played on DESKTOP at 1 s", File.ReadAllText(Path.Combine(laptop.Saves, "slot.sav")));
    }

    [Fact]
    public async Task The_engine_is_one_at_a_time_and_a_second_agent_stays_out()
    {
        using var world = new TestWorld();
        var data = Directory.CreateDirectory(Path.Combine(world.Root, "data")).FullName;

        using (var first = await EngineLock.AcquireAsync(data, null, CancellationToken.None))
        {
            var waited = false;
            var second = EngineLock.AcquireAsync(data, () => waited = true, CancellationToken.None);
            await Task.Delay(600);
            Assert.False(second.IsCompleted);
            Assert.True(waited);
            first.Dispose();
            (await second.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        }

        using var agent = EngineLock.TryAcquireAgent(data);
        Assert.NotNull(agent);
        Assert.Null(EngineLock.TryAcquireAgent(data));
        Assert.True(EngineLock.AgentRunning(data));
    }

    [Fact]
    public async Task Sync_now_syncs_every_game_at_the_next_round_and_the_tray_sees_the_work()
    {
        using var world = new TestWorld();
        var desktop = Pc(world, "DESKTOP", installDir: null);
        File.WriteAllText(Path.Combine(desktop.Saves, "slot.sav"), "a save");
        var said = new TestOutput();
        using var agent = new Agent(desktop.Data, said);

        await agent.TickAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.Single(said.Syncs);
        Assert.Equal([true, false], said.Working);

        await agent.TickAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.Single(said.Syncs);

        agent.SyncSoon();
        await agent.TickAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.Equal(2, said.Syncs.Count);
    }

    [Fact]
    public async Task KAN_88_the_apps_agent_syncs_a_game_here_then_uploads_it_beside_its_rounds()
    {
        using var world = new TestWorld();
        var desktop = Pc(world, "DESKTOP", installDir: null);
        File.WriteAllText(Path.Combine(desktop.Saves, "slot.sav"), "a save");
        var said = new TestOutput();
        using var agent = new Agent(desktop.Data, said) { UploadsInBackground = true };

        await agent.TickAsync(DateTime.UtcNow, CancellationToken.None);
        await agent.UploadsUnderWay.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(await Versions(world));
        using (var state = new StateStore(desktop.Data))
        {
            Assert.Equal(GameStatus.Synced, state.GetState(Fake).Status);
        }

        // The tray icon saw the round's work and the upload's, each started and finished.
        Assert.Equal(said.Working.Count(w => w), said.Working.Count(w => !w));
        Assert.DoesNotContain(said.Lines, l => l.StartsWith("! ", StringComparison.Ordinal));

        // KAN-80: the window heard the upload as it went, the game's name with it, and that it was done.
        Assert.Equal(TransferState.Running, said.Transfers[0].State);
        Assert.Equal("Fake Game", said.Transfers[0].Title);
        Assert.True(said.Transfers.Last(t => t.State == TransferState.Running).Progress.Finished);
        Assert.Equal(TransferState.Done, said.Transfers[^1].State);
        Assert.Matches(@"^\d+ B in ", said.Transfers[^1].Note);
    }

    [Fact]
    public async Task BG_01_the_apps_agent_waits_while_a_terminal_agent_runs_and_takes_over_when_it_stops()
    {
        using var world = new TestWorld();
        var desktop = Pc(world, "DESKTOP", installDir: null);
        var said = new TestOutput();
        var agent = new AppAgent(desktop.Data, said);
        using var stop = new CancellationTokenSource();
        Task running;

        using (EngineLock.TryAcquireAgent(desktop.Data))
        {
            running = agent.RunAsync(stop.Token);
            Assert.True(await Eventually(() => said.Lines.Any(l => l.StartsWith("Another GameSync agent is running", StringComparison.Ordinal))));
            Assert.False(agent.IsRunning);
        }

        agent.Kick();
        Assert.True(await Eventually(() => agent.IsRunning));
        stop.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task PLAY_06_done_playing_ends_a_session_an_emulator_still_closing_keeps_open_and_the_game_syncs()
    {
        // The owner's Bloodborne on shadPS4, 2 Oct 2026: its window closed, the emulator ran on for minutes, and the game
        // read Playing all the while. The stand-in game runs on long after writing its save, as the emulator did.
        using var world = new TestWorld();
        var install = Path.Combine(world.Root, "installs", "Fake Game");
        var exe = FakeGames.Install(install, "FakeGame");
        var desktop = Pc(world, "DESKTOP", install);
        var save = Path.Combine(desktop.Saves, "slot.sav");
        File.WriteAllText(save, "before playing");
        File.SetLastWriteTimeUtc(save, DateTime.UtcNow.AddHours(-1));
        var said = new TestOutput();
        using var agent = new Agent(desktop.Data, said);
        await agent.TickAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.Single(await Versions(world));

        using var game = FakeGames.Run(exe, "--save", save, "--write-at", "1", "--text", "played", "--run", "120");
        using var stop = new Stopper(game);
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline && (said.Plays.Count == 0 || !File.ReadAllText(save).StartsWith("played", StringComparison.Ordinal)))
        {
            await agent.TickAsync(DateTime.UtcNow, CancellationToken.None);
            await Task.Delay(200);
        }

        Assert.Equal([(Fake, "Fake Game", true)], said.Plays);
        using (var state = new StateStore(desktop.Data))
        {
            // What `gamesync done fake` does with the agent running.
            state.SetSetting(Agent.DoneKey(Fake), DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }

        await agent.TickAsync(DateTime.UtcNow, CancellationToken.None);

        // The session is over and the game synced, though its program still runs; the app's own actions don't take it
        // for the game either, so nothing reads Playing until it's started again.
        Assert.Equal([(Fake, "Fake Game", true), (Fake, "Fake Game", false)], said.Plays);
        Assert.Contains(said.Lines, l => l == "Fake Game: session over after 1 min, ended by hand.");
        Assert.Equal(2, (await Versions(world)).Count);
        using (var state = new StateStore(desktop.Data))
        {
            Assert.Equal(GameStatus.Synced, state.GetState(Fake).Status);
        }

        using var engine = Engine.Open(desktop.Data);
        Assert.False(new RunningGames(engine).IsRunning(Fake));
        await agent.TickAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.Equal(2, said.Plays.Count);
    }

    private static async Task<bool> Eventually(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++)
        {
            await Task.Delay(100);
        }

        return done();
    }

    private static Task<IReadOnlyList<VersionRecord>> Versions(TestWorld world) => new FolderVersionLog(world.Cloud).ListAsync(Fake, CancellationToken.None);

    /// <summary>A PC's data folder: games.json with the fake game, synced through the test's cloud folder.</summary>
    private static (string Data, string Saves) Pc(TestWorld world, string name, string? installDir)
    {
        var data = Path.Combine(world.Root, name, "data");
        var saves = Directory.CreateDirectory(Path.Combine(world.Root, name, "saves")).FullName;
        new AppConfig
        {
            Remote = world.Cloud,
            Games = [new GameDefinition { Id = Fake, Title = "Fake Game", Roots = new Dictionary<string, string> { ["saves"] = saves }, Rules = [new SaveRule { Root = "saves" }] }],
        }.Save(data);
        using var state = new StateStore(data);
        state.GetOrCreateDevice(name);
        if (installDir is not null)
        {
            state.SetSetting($"installDir.{Fake}", installDir);
        }

        return (data, saves);
    }

    /// <summary>Ends the stand-in game when the test does, however it ends.</summary>
    private sealed class Stopper(System.Diagnostics.Process process) : IDisposable
    {
        public void Dispose()
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(5000);
            }
        }
    }

    private sealed class TestOutput : IAgentOutput
    {
        public List<string> Lines { get; } = [];

        public List<bool> Working { get; } = [];

        public List<IReadOnlyList<Core.Sync.GameResult>> Syncs { get; } = [];

        public List<(GameId Game, string Title, bool Playing)> Plays { get; } = [];

        public List<TransferUpdate> Transfers { get; } = [];

        public void Say(string line)
        {
            lock (Lines)
            {
                Lines.Add(line);
            }
        }

        public void NeedsYou(string title, string message) => Say($"! {title}: {message}");

        void IAgentOutput.Working(bool busy) => Working.Add(busy);

        void IAgentOutput.Synced(IReadOnlyList<Core.Sync.GameResult> results) => Syncs.Add(results);

        void IAgentOutput.Played(GameId game, string title, bool playing) => Plays.Add((game, title, playing));

        void IAgentOutput.Transfer(TransferUpdate update)
        {
            lock (Transfers)
            {
                Transfers.Add(update);
            }
        }
    }
}
