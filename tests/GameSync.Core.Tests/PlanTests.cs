using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// SYNC-14 in the app: the save manager's Plan tab shows what the next sync would do for each game and why, with nothing
/// done until Run, and Run does exactly that for the games left ticked; a game that changed since the plan doesn't run.
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

    [Fact]
    public async Task SYNC_14_the_plan_tab_counts_what_will_run_and_runs_the_ticked_games()
    {
        var checks = 0;
        IReadOnlyList<PlannedChange>? ran = null;
        (GameId Game, bool Conflict)? opened = null;
        var sekiro = GameId.Parse("sekiro");
        var view = new SyncPlanView(
            [
                new PlannedChange(GameId.Parse("ready-or-not"), "Ready or Not", PlanStep.Upload, "Changed here on 22 Sep.", 14_155_776, "a"),
                new PlannedChange(GameId.Parse("sts2"), "Slay the Spire 2", PlanStep.Download, "LAPTOP uploaded a newer save.", 913_408, "b"),
            ],
            [new PlannedWait(sekiro, sekiro, "Sekiro", GameStatus.Conflict, "Changed on both PCs.", "Resolve"),
             new PlannedWait(GameId.Parse("ghost"), GameId.Parse("ghost"), "Ghost of Tsushima", GameStatus.Playing, "Being played now.", null)],
            ["Terraria", "Hades"],
            DateTime.UtcNow);
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
        {
            CheckPlan = _ =>
            {
                checks++;
                return Task.FromResult(view);
            },
            RunPlan = (chosen, _) =>
            {
                ran = chosen;
                return Task.FromResult(new PlanRun([], []));
            },
        };
        var saves = new SaveManagerViewModel(actions);

        // The tab makes the plan the first time it's shown.
        Assert.Equal(["saves", "plan"], saves.Tabs.Select(t => t.Id));
        saves.Tab = "plan";
        Assert.Equal((true, false, 1), (saves.ShowsPlan, saves.ShowsTable, checks));
        var plan = saves.Plan;
        Assert.Equal(["2", "1", "1", "2", "14.4 MB"], plan.Stats.Select(s => s.Value));
        Assert.Equal(("Run 2 changes", "2 of 2 will run · untick a game to skip it this time"), (plan.RunLabel, plan.ChangesToolbar));

        // Unticking one skips it this time; Run sends the rest, then the plan is made again.
        plan.Changes[1].IsChecked = false;
        Assert.Equal(("Run 1 change", "13.5 MB"), (plan.RunLabel, plan.Stats[^1].Value));
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)plan.RunCommand).ExecuteAsync(null);
        Assert.Equal(["Ready or Not"], ran!.Select(c => c.Title));
        Assert.Equal(2, checks);
        Assert.False(plan.Changes.Single(c => c.Title == "Slay the Spire 2").IsChecked);

        // A waiting game's button: Resolve opens its conflict.
        plan.ShowInSyncCommand.Execute(null);
        Assert.Equal(("Hide the games already in sync", "Terraria · Hades"), (plan.InSyncLabel, plan.InSyncText));
        var runs = new SyncPlanViewModel(actions, (game, conflict) => opened = (game, conflict));
        runs.Show(view);
        runs.WaitCommand.Execute(runs.Waits[0]);
        Assert.Equal((sekiro, true), opened);
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
