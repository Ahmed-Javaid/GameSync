using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// The conflict screen (SYNC-10): both sides read from this PC alone, the side suggested and why, why GameSync asked,
/// the files that differ, Keep; and after newest wins, which save is current, with Swap (SYNC-04).
/// </summary>
public class ConflictTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task SYNC_10_a_waiting_conflict_shows_both_sides_the_suggestion_and_why_GameSync_asked()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        var game = desktop.AddGame("sekiro", policy: ConflictPolicy.AlwaysAsk);
        laptop.AddGame("sekiro", policy: ConflictPolicy.AlwaysAsk);
        var now = DateTime.UtcNow;
        desktop.Write("sekiro", "S0000.sl2", "start", now.AddHours(-3));
        desktop.Write("sekiro", "S0000.sl2.bak", "start bak", now.AddHours(-3));
        await desktop.SyncAsync();
        await laptop.SyncAsync();

        // Since the last sync, LAPTOP played and uploaded; DESKTOP played longer and saved later, before it heard of it.
        laptop.State.AddSession(game.Id, new SessionInfo(now.AddMinutes(-50), now.AddMinutes(1)));
        laptop.Write("sekiro", "S0000.sl2", "laptop: Genichiro", now.AddMinutes(-20));
        await laptop.SyncAsync();
        desktop.State.AddSession(game.Id, new SessionInfo(now.AddMinutes(-90), now.AddMinutes(1)));
        desktop.Write("sekiro", "S0000.sl2", "desktop: Genichiro and the Guardian Ape", now.AddMinutes(-2));
        desktop.Write("sekiro", "S0000.sl2.bak", "desktop bak", now.AddMinutes(-3));
        Assert.Equal(SyncAction.NeedsYou, Assert.Single(await desktop.SyncAsync()).Action);

        var detail = Assert.IsType<ConflictDetail>(await ReadAsync(desktop, game));
        Assert.True(detail.Waiting);
        Assert.Equal(["DESKTOP", "LAPTOP"], detail.Sides.Select(s => s.Pc));
        var (mine, theirs) = (detail.Sides[0], detail.Sides[1]);
        Assert.True(mine.IsThisPc && mine.Newest && mine.Suggested);
        Assert.False(theirs.IsThisPc || theirs.Newest || theirs.Suggested);
        Assert.Equal(["S0000.sl2", "S0000.sl2.bak"], mine.Changed);
        Assert.Equal(["S0000.sl2"], theirs.Changed);
        Assert.Equal((1, TimeSpan.FromMinutes(91)), (mine.Sessions, mine.Played));
        Assert.Equal((1, TimeSpan.FromMinutes(51)), (theirs.Sessions, theirs.Played));
        Assert.Equal(2, theirs.Files);

        // Compare files: what each PC did to each file since they last agreed.
        Assert.Equal([("S0000.sl2", "Changed on both PCs"), ("S0000.sl2.bak", "Changed on DESKTOP")], detail.Files.Select(f => (f.Name, f.What)));

        // In words: the headline, what each PC did, the suggestion with why, and why GameSync asked.
        var nowLocal = now.ToLocalTime();
        Assert.Equal("sekiro changed on two PCs", ConflictViewModel.HeadlineOf(detail));
        Assert.StartsWith($"DESKTOP saved at {mine.SavedUtc.ToLocalTime():HH:mm} and LAPTOP ", ConflictViewModel.SentenceOf(detail, nowLocal));
        Assert.Contains(", both after the last sync. Both copies are safe.", ConflictViewModel.SentenceOf(detail, nowLocal));
        Assert.Equal("It's the newest save and the longer play.", ConflictViewModel.WhyOf(detail));
        Assert.Equal("You're asked because sekiro is set to Always ask.", ConflictViewModel.AskedBecause(detail));
        Assert.Equal(["Saved", "Session", "Since the last sync", "Size", "Changed files"], ConflictViewModel.FactsOf(mine, nowLocal).Select(f => f.Label));
        Assert.Equal("1 session, 1 h 31 min", ConflictViewModel.FactsOf(mine, nowLocal)[2].Value);

        // The reason it asked stays while the conflict waits, rather than a later sync's "a conflict is waiting".
        Assert.Equal(SyncAction.NeedsYou, Assert.Single(await desktop.SyncAsync()).Action);
        Assert.Equal(detail.Reason, DecisionEngine.WaitingReason(desktop.State.GetState(game.Id).Detail));
        Assert.Contains("this game is set to always ask", detail.Reason);
    }

    [Fact]
    public async Task SYNC_10_keeping_this_PCs_save_is_one_click_keeping_the_other_asks_first_and_the_screen_then_says_what_was_kept()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        var game = await ConflictAsync(desktop, laptop);
        var detail = Assert.IsType<ConflictDetail>(await ReadAsync(desktop, game));

        var asked = new List<(GameId Game, bool ThisPc, VersionId? Version)>();
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
        {
            Resolve = (g, thisPc, version) => asked.Add((g, thisPc, version)),
        };
        var page = new ConflictViewModel(Launcher(game), actions, new Noop(), new Noop(), new Noop());
        page.Show(detail, DateTime.Now);
        Assert.True(page.ShowsWaiting);
        Assert.Equal("Suggested: keep DESKTOP's.", page.Suggestion);
        var (mine, theirs) = (page.Sides[0], page.Sides[1]);
        Assert.True(mine.KeepsAtOnce && !mine.AsksFirst);
        Assert.True(theirs.AsksFirst && !theirs.KeepsAtOnce);
        Assert.Equal("Carry on with LAPTOP's save?", theirs.ConfirmTitle);

        theirs.Keep!.Execute(null);
        mine.Keep!.Execute(null);
        Assert.Equal([(game.Id, false, (VersionId?)detail.Sides[1].Version), (game.Id, true, null)], asked);
        Assert.Equal("Keeping DESKTOP's save…", page.Busy);

        // Settled by hand: the same screen says which save was kept and offers Swap to the other.
        await desktop.Service().ResolveAsync(game.Id, keepThisPc: true, null, Ct);
        var settled = Assert.IsType<ConflictDetail>(await ReadAsync(desktop, game));
        Assert.False(settled.Waiting);
        Assert.True(settled.ByHand);
        Assert.Equal(("DESKTOP", "LAPTOP"), (settled.KeptPc, settled.PinnedPc));
        page.Show(settled, DateTime.Now);
        Assert.True(page.ShowsSettled);
        Assert.Equal("You kept DESKTOP's save", page.Headline);
        Assert.Equal("Swap to LAPTOP's", page.SwapLabel);
        Assert.Null(page.Busy);
    }

    [Fact]
    public async Task SYNC_04_after_newest_wins_the_screen_says_which_save_is_current_and_Swap_switches_back()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        var game = await ConflictAsync(desktop, laptop, ConflictPolicy.NewestWins);

        // DESKTOP's is newer and was played, so it won by itself; LAPTOP's is pinned.
        var detail = Assert.IsType<ConflictDetail>(await ReadAsync(desktop, game));
        Assert.False(detail.Waiting);
        Assert.False(detail.ByHand);
        Assert.Equal(("DESKTOP", "LAPTOP"), (detail.KeptPc, detail.PinnedPc));
        Assert.Equal("Newest kept: DESKTOP's save", ConflictViewModel.HeadlineOf(detail));
        Assert.Equal("Changed on both PCs", Assert.Single(detail.Files).What);

        // Swap brings LAPTOP's back; the settled view steps aside, and the history has both.
        await desktop.Service().SwapAsync(game.Id, Ct);
        Assert.Equal("laptop: Genichiro", desktop.Read("sekiro", "S0000.sl2"));
        Assert.Null(await ReadAsync(desktop, game));

        // On LAPTOP, which lost, the same conflict reads the same way once it has DESKTOP's records.
        await laptop.SyncAsync();
        Assert.Null(await ReadAsync(laptop, game));
    }

    [Fact]
    public async Task SYNC_06_a_newer_save_that_lost_half_its_files_is_not_the_one_suggested()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        var game = desktop.AddGame("hades");
        laptop.AddGame("hades");
        var now = DateTime.UtcNow;
        foreach (var slot in new[] { "1", "2", "3", "4" })
        {
            desktop.Write("hades", $"Profile{slot}.sav", $"profile {slot}", now.AddHours(-5));
        }

        await desktop.SyncAsync();
        await laptop.SyncAsync();

        // LAPTOP's newer save lost three of its four files; DESKTOP changed one, earlier, while it played.
        laptop.Played("hades");
        foreach (var slot in new[] { "2", "3", "4" })
        {
            File.Delete(Path.Combine(laptop.Folder("hades"), $"Profile{slot}.sav"));
        }

        laptop.Write("hades", "Profile1.sav", "profile 1, fresh start", now.AddMinutes(-1));
        await laptop.SyncAsync();
        desktop.Played("hades");
        desktop.Write("hades", "Profile2.sav", "profile 2, further", now.AddMinutes(-30));
        Assert.Equal(SyncAction.NeedsYou, Assert.Single(await desktop.SyncAsync()).Action);

        var detail = Assert.IsType<ConflictDetail>(await ReadAsync(desktop, game));
        var (mine, theirs) = (detail.Sides[0], detail.Sides[1]);
        Assert.True(theirs.Newest && theirs.LostHalf && !theirs.Suggested);
        Assert.True(mine.Suggested);
        Assert.Equal("LAPTOP's is newer, but it lost more than half of its files or size, which looks like a reset.", ConflictViewModel.WhyOf(detail));
        Assert.Null(ConflictViewModel.AskedBecause(detail));
        Assert.Contains(("Profile3.sav", "Removed on LAPTOP"), detail.Files.Select(f => (f.Name, f.What)));
    }

    [Fact]
    public void LIB_21_Back_from_a_conflict_returns_to_where_it_was_opened()
    {
        var shown = new List<string>();
        var actions = new LauncherActions(_ => { }, () => { }, (page, _) => shown.Add(page), (_, _) => { });
        var game = new LauncherGame { Id = GameId.Parse("sekiro"), Title = "Sekiro", Syncs = true, Status = GameStatus.Conflict };
        var saves = new SaveManagerViewModel(actions);
        saves.Update([game]);

        // From Home's Needs you: Back and Decide later go back Home.
        saves.OpenConflict(game.Id, "home");
        Assert.Same(saves.Conflict, saves.Page);
        Assert.False(saves.ShowsTable);
        saves.Conflict!.BackCommand.Execute(null);
        Assert.Equal(["home"], shown);
        Assert.Null(saves.Conflict);

        // Over the game's saves: Back returns to them; the breadcrumb's first step to every game's saves.
        saves.Open(game.Id);
        saves.OpenConflict(game.Id);
        saves.Conflict!.BackCommand.Execute(null);
        Assert.Same(saves.Game, saves.Page);
        Assert.Equal(["home"], shown);
        saves.OpenConflict(game.Id);
        saves.Conflict!.OpenTableCommand.Execute(null);
        Assert.True(saves.ShowsTable);

        // The breadcrumb's game: its saves.
        saves.OpenConflict(game.Id);
        saves.Conflict!.OpenSavesCommand.Execute(null);
        Assert.Equal(game.Id, saves.Game?.Id);
        Assert.Null(saves.Conflict);
    }

    [Fact]
    public void SYNC_10_the_reason_comes_without_the_words_in_front_or_a_note_a_sync_added()
    {
        Assert.Equal("Changed on DESKTOP and on LAPTOP; this game is set to always ask.",
            DecisionEngine.WaitingReason("Conflict, needs you: Changed on DESKTOP and on LAPTOP; this game is set to always ask. Kept on this PC; the upload waits: you're offline."));
        Assert.Null(DecisionEngine.WaitingReason("Changed on this PC."));
        Assert.Null(DecisionEngine.WaitingReason(null));
        Assert.Equal("1 h 31 min", ConflictViewModel.Duration(TimeSpan.FromMinutes(91)));
        Assert.Equal("47 min", ConflictViewModel.Duration(TimeSpan.FromMinutes(47)));
        Assert.Equal("2 h", ConflictViewModel.Duration(TimeSpan.FromHours(2)));
    }

    /// <summary>Sekiro changed on both PCs since they last agreed: LAPTOP first, then DESKTOP, longer and later, both while playing.</summary>
    private static async Task<GameDefinition> ConflictAsync(TestPc desktop, TestPc laptop, ConflictPolicy policy = ConflictPolicy.AlwaysAsk)
    {
        var game = desktop.AddGame("sekiro", policy: policy);
        laptop.AddGame("sekiro", policy: policy);
        var now = DateTime.UtcNow;
        desktop.Write("sekiro", "S0000.sl2", "start", now.AddHours(-3));
        await desktop.SyncAsync();
        await laptop.SyncAsync();
        laptop.State.AddSession(game.Id, new SessionInfo(now.AddMinutes(-50), now.AddMinutes(1)));
        laptop.Write("sekiro", "S0000.sl2", "laptop: Genichiro", now.AddMinutes(-20));
        await laptop.SyncAsync();
        desktop.State.AddSession(game.Id, new SessionInfo(now.AddMinutes(-90), now.AddMinutes(1)));
        desktop.Write("sekiro", "S0000.sl2", "desktop: Genichiro and the Guardian Ape", now.AddMinutes(-2));
        await desktop.SyncAsync();
        return game;
    }

    /// <summary>The conflict as the app reads it on <paramref name="pc"/>: from its backup folder's records and its own state.</summary>
    private static async Task<ConflictDetail?> ReadAsync(TestPc pc, GameDefinition game)
    {
        var history = new LocalHistory(pc.HistoryDir);
        var versions = await history.Log.ListAsync(game.Id, Ct);
        var pins = await history.Log.ListPinsAsync(game.Id, Ct);
        var pcs = history.LoadDevices().ToDictionary(d => d.Id, d => d.Name);
        return ConflictDetails.Describe(game, versions, pins, pc.State.GetState(game.Id), pc.State.GetSessions(game.Id), pc.Device, pcs);
    }

    private static LauncherGame Launcher(GameDefinition game) => new()
    {
        Id = game.Id,
        Title = game.Title,
        Syncs = true,
        Status = GameStatus.Conflict,
    };

    private sealed class Noop : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }
}
