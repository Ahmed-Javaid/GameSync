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

    private sealed class TestOutput : IAgentOutput
    {
        public List<string> Lines { get; } = [];

        public void Say(string line)
        {
            lock (Lines)
            {
                Lines.Add(line);
            }
        }

        public void NeedsYou(string title, string message) => Say($"! {title}: {message}");
    }
}
