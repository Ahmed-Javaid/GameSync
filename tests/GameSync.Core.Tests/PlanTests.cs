using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;

namespace GameSync.Core.Tests;

/// <summary>
/// SYNC-14: the plan says what the next sync would do for each game and why, with nothing done until it's run, and running
/// it does exactly that for the games chosen; a game that changed since the plan doesn't run. The app's Plan tab was taken
/// out on 9 Oct 2026 (the owner); <c>gamesync plan</c> shows the plan.
/// </summary>
public class PlanTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task SYNC_14_the_plan_shows_what_each_game_would_do_and_runs_only_what_stays_ticked()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var saves = Path.Combine(world.Root, "Saves");
        string Slot(string game) => Path.Combine(saves, game, "slot.sav");
        foreach (var game in new[] { "Lantern Keep", "Quiet Game", "Held Game", "Gone Game" })
        {
            Write(Slot(game), $"{game}, at the start");
        }

        new AppConfig
        {
            Remote = world.Cloud,
            Games = [Game("lantern-keep", "Lantern Keep", saves), Game("quiet-game", "Quiet Game", saves), Game("held-game", "Held Game", saves), Game("gone-game", "Gone Game", saves)],
        }.Save(data);
        var output = new Recorded();

        // The first plan: every game's first backup; running it does all four.
        var first = await SyncPlans.CheckAsync(data, output, Ct);
        Assert.Equal([PlanStep.Upload, PlanStep.Upload, PlanStep.Upload, PlanStep.Upload], first.Changes.Select(c => c.Step));
        Assert.Equal("Ran 4 changes.", (await SyncPlans.RunAsync(data, first.Changes, output, Ct)).Sentence);

        // Then Lantern Keep is played, Held Game changes while it isn't running, and Gone Game's folder goes missing.
        var at = DateTime.UtcNow;
        Write(Slot("Lantern Keep"), "Lantern Keep, floor 2");
        File.SetLastWriteTimeUtc(Slot("Lantern Keep"), at);
        using (var state = new StateStore(data))
        {
            state.AddSession(GameId.Parse("lantern-keep"), new SessionInfo(at.AddMinutes(-20), at.AddMinutes(1)));
        }

        Write(Slot("Held Game"), "Held Game, edited by something else");
        Directory.Delete(Path.GetDirectoryName(Slot("Gone Game"))!, recursive: true);

        var plan = await SyncPlans.CheckAsync(data, output, Ct);
        Assert.Equal([("Held Game", PlanStep.Hold), ("Lantern Keep", PlanStep.Upload)], plan.Changes.Select(c => (c.Title, c.Step)));
        Assert.Equal("Lantern Keep, floor 2".Length, plan.Changes.Single(c => c.Title == "Lantern Keep").Bytes);
        var gone = Assert.Single(plan.Waits);
        Assert.Equal(("Gone Game", GameStatus.NotAvailable, "See where"), (gone.Title, gone.Status, gone.Action));
        Assert.Equal(["Quiet Game"], plan.InSync);

        // Only Lantern Keep stays ticked: only it runs, and Held Game still waits to be held.
        var run = await SyncPlans.RunAsync(data, plan.Changes.Where(c => c.Title == "Lantern Keep").ToList(), output, Ct);
        Assert.Equal("Ran 1 change.", run.Sentence);
        var after = await SyncPlans.CheckAsync(data, output, Ct);
        Assert.Equal([("Held Game", PlanStep.Hold)], after.Changes.Select(c => (c.Title, c.Step)));
        Assert.Contains("Lantern Keep", after.InSync);

        // A game that changed since the plan doesn't run: the person checks again first.
        Write(Slot("Held Game"), "Held Game, edited again");
        var stale = await SyncPlans.RunAsync(data, after.Changes, output, Ct);
        Assert.Empty(stale.Results);
        Assert.Equal(["Held Game"], stale.Changed);
        Assert.StartsWith("Held Game changed since the plan, so it didn't run", stale.Sentence, StringComparison.Ordinal);
    }

    private static GameDefinition Game(string id, string title, string saves) => new()
    {
        Id = GameId.Parse(id),
        Title = title,
        Roots = new Dictionary<string, string> { ["saves"] = Path.Combine(saves, title) },
        Rules = [new SaveRule { Root = "saves" }],
    };

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private sealed class Recorded : IAgentOutput
    {
        public void Say(string line)
        {
        }

        public void NeedsYou(string title, string message)
        {
        }
    }
}
