using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Sync;

namespace GameSync.Core.Tests;

/// <summary>While a game runs, and around it: BAK-10, BG-08, PLAY-03, PLAY-07 and BAK-06.</summary>
public class PlayTests
{
    private static readonly GameId Game = GameId.Parse("game");

    [Fact]
    public async Task BAK_10_while_the_game_runs_nothing_downloads_or_restores_and_it_says_why()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        desktop.Write("game", "slot.sav", "desktop's");
        await desktop.SyncAsync();
        laptop.Running.Add(Game);

        var playing = Assert.Single(await laptop.SyncAsync());
        var desktops = Assert.Single(await Cloud.VersionsAsync(world, "game"));
        var restore = await Assert.ThrowsAsync<InvalidOperationException>(() => laptop.Service().RestoreAsync(Game, desktops.Id, CancellationToken.None));

        Assert.Equal((SyncAction.Playing, GameStatus.Playing), (playing.Action, playing.Status));
        Assert.Contains("is running", restore.Message, StringComparison.Ordinal);
        Assert.Empty(laptop.Tree("game"));

        laptop.Running.Clear();
        Assert.Equal(SyncAction.Download, Assert.Single(await laptop.SyncAsync()).Action);
        Assert.Equal("desktop's", laptop.Read("game", "slot.sav"));
    }

    [Fact]
    public async Task BG_08_while_the_game_runs_even_its_waiting_upload_waits()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "first");
        await desktop.SyncAsync();
        desktop.Write("game", "slot.sav", "played offline");
        desktop.Played("game");
        desktop.Offline = true;
        await desktop.SyncAsync();
        desktop.Offline = false;
        desktop.Running.Add(Game);

        await desktop.SyncAsync();

        Assert.Single(await Cloud.VersionsAsync(world, "game"));
        desktop.Running.Clear();
        await desktop.SyncAsync();
        Assert.Equal(2, (await Cloud.VersionsAsync(world, "game")).Count);
    }

    [Fact]
    public async Task PLAY_07_the_other_PC_sees_who_is_playing_until_the_save_comes_back()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        desktop.Write("game", "slot.sav", "before");
        await desktop.SyncAsync();
        await laptop.SyncAsync();

        await desktop.Service().MarkPlayingAsync(Game, DateTime.UtcNow, CancellationToken.None);
        var check = await laptop.Service().PrepareLaunchAsync(Game, CancellationToken.None);
        Assert.Contains(check.Warnings, w => w.StartsWith("DESKTOP has been playing it since", StringComparison.Ordinal));

        // DESKTOP quits: its save uploads and its marker goes.
        desktop.Write("game", "slot.sav", "after");
        desktop.Played("game");
        await desktop.SyncAsync();
        await desktop.Service().ClearPlayingAsync(Game, CancellationToken.None);
        var after = await laptop.Service().PrepareLaunchAsync(Game, CancellationToken.None);
        Assert.DoesNotContain(after.Warnings, w => w.Contains("DESKTOP", StringComparison.Ordinal));
        Assert.Equal(SyncAction.Download, after.Download!.Action);
        Assert.Equal("after", laptop.Read("game", "slot.sav"));
    }

    [Fact]
    public async Task PLAY_07_a_marker_that_couldnt_come_down_offline_says_so_and_comes_down_later()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        desktop.Write("game", "slot.sav", "a save");
        await desktop.SyncAsync();
        await desktop.Service().MarkPlayingAsync(Game, DateTime.UtcNow, CancellationToken.None);

        desktop.Offline = true;
        Assert.False(await desktop.Service().ClearPlayingAsync(Game, CancellationToken.None));
        Assert.NotNull(await laptop.Service().PlayingElsewhereAsync(Game, CancellationToken.None));

        desktop.Offline = false;
        Assert.True(await desktop.Service().ClearPlayingAsync(Game, CancellationToken.None));
        Assert.Null(await laptop.Service().PlayingElsewhereAsync(Game, CancellationToken.None));
    }

    [Fact]
    public async Task PLAY_07_a_marker_12_hours_old_with_no_upload_reads_never_synced_back()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        desktop.Write("game", "slot.sav", "a save");
        await desktop.SyncAsync();

        // DESKTOP starts playing and its network drops; LAPTOP's clock moves 13 hours on.
        await desktop.Service().MarkPlayingAsync(Game, DateTime.UtcNow, CancellationToken.None);
        laptop.Options = laptop.Options with { UtcNow = () => DateTime.UtcNow.AddHours(13) };
        var result = Assert.Single(await laptop.SyncAsync());

        Assert.Contains(result.Warnings, w => w.StartsWith("DESKTOP never synced back", StringComparison.Ordinal));
        await laptop.Service().ClearPlayingAsync(Game, CancellationToken.None);
        Assert.NotNull(await laptop.Service().PlayingElsewhereAsync(Game, CancellationToken.None));
    }

    [Fact]
    public async Task PLAY_03_the_pre_launch_check_leaves_a_conflict_alone_and_says_so()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game", policy: Games.ConflictPolicy.AlwaysAsk);
        laptop.AddGame("game", policy: Games.ConflictPolicy.AlwaysAsk);
        desktop.Write("game", "slot.sav", "start");
        await desktop.SyncAsync();
        await laptop.SyncAsync();
        desktop.Write("game", "slot.sav", "desktop's");
        desktop.Played("game");
        await desktop.SyncAsync();
        laptop.Write("game", "slot.sav", "laptop's");
        laptop.Played("game");

        var check = await laptop.Service().PrepareLaunchAsync(Game, CancellationToken.None);

        Assert.Null(check.Download);
        Assert.Contains(check.Warnings, w => w.StartsWith("A conflict waits for you", StringComparison.Ordinal));
        Assert.Equal("laptop's", laptop.Read("game", "slot.sav"));
    }

    [Fact]
    public async Task BAK_06_the_save_is_kept_before_an_update_and_the_current_save_stays_as_it_is()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "made by build 100");
        await desktop.SyncAsync();
        desktop.Write("game", "slot.sav", "played more on build 100");

        var kept = await desktop.Service().KeepBeforeUpdateAsync(Game, "101", CancellationToken.None);
        var again = await desktop.Service().KeepBeforeUpdateAsync(Game, "101", CancellationToken.None);

        var versions = await Cloud.VersionsAsync(world, "game");
        var before = Assert.Single(versions, v => v.Origin == VersionOrigin.BeforeUpdate);
        Assert.Equal((VersionKind.Kept, true), (before.Kind, before.Pinned));
        Assert.StartsWith("before update to build 101 (", before.Label, StringComparison.Ordinal);
        Assert.Equal(before.Id, kept.NewVersion);
        Assert.Equal(before.Id, again.NewVersion);
        Assert.Equal(2, versions.Count);
        Assert.Equal("played more on build 100", desktop.Read("game", "slot.sav"));
    }
}
