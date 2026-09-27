using System.Text;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;
using GameSync.Storage.Drive;

namespace GameSync.Core.Tests;

/// <summary>The Google Drive backend against an in-memory Drive: layout, races between PCs, errors, and the readable copy.</summary>
public class DriveTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Every_decision_and_crash_scenario_holds_over_Drive()
    {
        using var saves = new TestWorld();
        var source = Directory.CreateDirectory(Path.Combine(saves.Root, "Some Game")).FullName;
        await File.WriteAllTextAsync(Path.Combine(source, "slot1.sav"), new string('a', 5000));
        Directory.CreateDirectory(Path.Combine(source, "profiles"));
        await File.WriteAllTextAsync(Path.Combine(source, "profiles", "hero.dat"), "hero");
        await File.WriteAllTextAsync(Path.Combine(source, "settings.ini"), "vsync=1");

        await FullScenario.RunAsync(source, Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories), () => new TestWorld(drive: true));
    }

    [Fact]
    public async Task CLOUD_01_everything_lives_in_one_GameSync_folder_laid_out_like_the_design()
    {
        using var world = new TestWorld(drive: true);
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("terraria");
        desktop.Write("terraria", "Worlds/home.wld", "a world");
        await desktop.SyncAsync();
        var drive = world.Drive!;

        var root = Assert.Single(drive.Items, i => i.IsFolder && !i.Trashed && i.AppProperties.GetValueOrDefault("gamesync") == "root");
        Assert.Equal("GameSync", root.Name);
        Assert.Equal("root", root.Parent);
        Assert.NotNull(drive.Child(root.Id, "HOW-TO-RESTORE.txt"));
        Assert.NotNull(drive.Child(root.Id, "restore.ps1"));
        Assert.NotNull(drive.At("GameSync/devices"));

        var game = drive.At("GameSync/games/terraria")!;
        Assert.Equal("terraria", game.AppProperties["gamesyncGame"]);
        var version = Assert.Single(drive.Items, i => i.Parent == drive.At("GameSync/games/terraria/versions")!.Id);
        Assert.EndsWith(".json", version.Name);
        var blob = Assert.Single(drive.Items, i => i.Parent == drive.At("GameSync/games/terraria/blobs")!.Id);
        Assert.EndsWith(".gz", blob.Name);
        Assert.Equal("a world", Encoding.UTF8.GetString(drive.At("GameSync/games/terraria/latest/saves/Worlds/home.wld")!.Content));
        Assert.NotNull(drive.At("GameSync/games/terraria/latest/VERSION.txt"));
    }

    [Fact]
    public async Task The_GameSync_folder_is_found_after_the_user_renames_or_moves_it()
    {
        using var world = new TestWorld(drive: true);
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        desktop.Write("game", "slot.sav", "start");
        await desktop.SyncAsync();
        var drive = world.Drive!;

        var root = drive.At("GameSync")!;
        var backups = drive.AddFolder("root", "Backups");
        root.Name = "My game saves";
        root.Parent = backups.Id;

        Assert.Equal(SyncAction.Download, Assert.Single(await laptop.SyncAsync()).Action);
        Assert.Single(drive.Items, i => i.IsFolder && !i.Trashed && i.AppProperties.GetValueOrDefault("gamesync") == "root");
    }

    [Fact]
    public async Task Two_pcs_that_created_the_GameSync_folder_at_once_end_up_sharing_the_older_one()
    {
        using var world = new TestWorld(drive: true);
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        var drive = world.Drive!;

        // Each PC made its own GameSync folder before seeing the other's, and backed up a game into it.
        drive.AddFolder("root", "GameSync", new Dictionary<string, string> { ["gamesync"] = "root" });
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "desktop's first save");
        await desktop.SyncAsync();
        var older = drive.At("GameSync")!;
        var newer = drive.AddFolder("root", "GameSync", new Dictionary<string, string> { ["gamesync"] = "root" });
        drive.AddFolder(newer.Id, "devices");

        laptop.AddGame("game");
        Assert.Equal(SyncAction.Download, Assert.Single(await laptop.SyncAsync()).Action);

        Assert.True(newer.Trashed);
        Assert.False(older.Trashed);
        Assert.Equal("desktop's first save", laptop.Read("game", "slot.sav"));
        Assert.Single(drive.Items, i => i.IsFolder && !i.Trashed && i.Parent == older.Id && i.Name == "devices");
    }

    [Fact]
    public async Task A_duplicate_folder_another_app_left_a_file_in_is_retired_not_trashed_and_syncing_carries_on()
    {
        using var world = new TestWorld(drive: true);
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        var drive = world.Drive!;
        drive.AddFolder("root", "GameSync", new Dictionary<string, string> { ["gamesync"] = "root" });
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "desktop's save");
        await desktop.SyncAsync();

        // A second copy, holding something a sync client put there that GameSync can't see, so Drive won't trash it.
        var newer = drive.AddFolder("root", "GameSync", new Dictionary<string, string> { ["gamesync"] = "root" });
        drive.AddHiddenFile(newer.Id, "desktop.ini");

        laptop.AddGame("game");
        Assert.Equal(SyncAction.Download, Assert.Single(await laptop.SyncAsync()).Action);
        Assert.False(newer.Trashed);
        Assert.Equal("GameSync (merged)", newer.Name);
        Assert.Equal("merged", newer.AppProperties["gamesync"]);

        // It's never picked again.
        Assert.Equal(SyncAction.None, Assert.Single(await laptop.SyncAsync()).Action);
        Assert.Equal(SyncAction.None, Assert.Single(await desktop.SyncAsync()).Action);
        Assert.Equal("GameSync (merged)", newer.Name);
    }

    [Fact]
    public async Task Two_copies_of_a_games_versions_folder_are_merged_and_nothing_is_lost()
    {
        using var world = new TestWorld(drive: true);
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        desktop.Write("game", "slot.sav", "start");
        await desktop.SyncAsync();
        var drive = world.Drive!;

        // A second "versions" folder, as if another PC created one at the same moment, holding a record moved from the first.
        var game = drive.At("GameSync/games/game")!;
        var first = drive.At("GameSync/games/game/versions")!;
        var second = drive.AddFolder(game.Id, "versions");
        var record = Assert.Single(drive.Items, i => i.Parent == first.Id);
        record.Parent = second.Id;

        Assert.Equal(SyncAction.Download, Assert.Single(await laptop.SyncAsync()).Action);
        Assert.True(second.Trashed);
        Assert.Equal(first.Id, record.Parent);
    }

    [Fact]
    public async Task An_upload_Drive_stored_wrongly_is_thrown_away_and_tried_again()
    {
        using var world = new TestWorld(drive: true);
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "progress");
        world.Drive!.CorruptUploads = true;

        var waiting = Assert.Single(await desktop.SyncAsync());

        Assert.Equal(GameStatus.UploadPending, waiting.Status);
        Assert.Empty(await Cloud.VersionsAsync(world, "game"));

        world.Drive.CorruptUploads = false;
        Assert.Equal(GameStatus.Synced, Assert.Single(await desktop.SyncAsync()).Status);
        Assert.Single(await Cloud.VersionsAsync(world, "game"));
    }

    [Fact]
    public async Task CLOUD_06_a_file_Drive_flags_as_malware_is_never_downloaded()
    {
        using var world = new TestWorld(drive: true);
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        desktop.Write("game", "slot.sav", "progress");
        await desktop.SyncAsync();
        foreach (var blob in world.Drive!.Items.Where(i => i.Name.EndsWith(".gz", StringComparison.Ordinal)))
        {
            blob.Flagged = true;
        }

        var result = Assert.Single(await laptop.SyncAsync());

        Assert.Equal(GameStatus.Blocked, result.Status);
        Assert.Contains("malware", result.Message);
        Assert.Empty(laptop.Tree("game"));
    }

    [Fact]
    public async Task CLOUD_03_the_plain_latest_copy_on_Drive_follows_the_current_save()
    {
        using var world = new TestWorld(drive: true);
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Write("game", "keep.sav", "same");
        desktop.Write("game", "old/gone.sav", "will go");
        await desktop.SyncAsync();
        var drive = world.Drive!;
        var keep = drive.At("GameSync/games/game/latest/saves/keep.sav")!;

        desktop.Played("game");
        File.Delete(Path.Combine(desktop.Folder("game"), "old", "gone.sav"));
        desktop.Write("game", "new.sav", "arrived");
        await desktop.SyncAsync();

        Assert.Null(drive.At("GameSync/games/game/latest/saves/old/gone.sav"));
        Assert.Equal("arrived", Encoding.UTF8.GetString(drive.At("GameSync/games/game/latest/saves/new.sav")!.Content));
        Assert.Same(keep, drive.At("GameSync/games/game/latest/saves/keep.sav"));
        Assert.Contains("DESKTOP", Encoding.UTF8.GetString(drive.At("GameSync/games/game/latest/VERSION.txt")!.Content));
    }

    [Fact]
    public async Task A_second_sync_with_nothing_new_costs_only_a_few_Drive_calls()
    {
        using var world = new TestWorld(drive: true);
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        for (var i = 0; i < 20; i++)
        {
            desktop.Write("game", $"slot{i}.sav", $"save {i}");
        }

        await desktop.SyncAsync();
        var before = world.Drive!.Calls;
        Assert.Equal(SyncAction.None, Assert.Single(await desktop.SyncAsync()).Action);

        // Finding the folders, listing records, pins and thinning marks, and saying this PC was here.
        Assert.InRange(world.Drive.Calls - before, 1, 25);
    }

    [Theory]
    [InlineData(401, null, CloudErrorKind.SignInExpired)]
    [InlineData(403, "storageQuotaExceeded", CloudErrorKind.StorageFull)]
    [InlineData(403, "userRateLimitExceeded", CloudErrorKind.RateLimited)]
    [InlineData(429, null, CloudErrorKind.RateLimited)]
    [InlineData(403, "cannotDownloadAbusiveFile", CloudErrorKind.Flagged)]
    [InlineData(503, null, CloudErrorKind.Other)]
    [InlineData(404, "notFound", CloudErrorKind.Other)]
    public void CLOUD_04_drive_errors_are_told_apart(int status, string? reason, CloudErrorKind expected) =>
        Assert.Equal(expected, DriveErrors.FromApi(status, reason, "message").Kind);

    [Fact]
    public async Task CLOUD_04_rate_limits_are_retried_with_backoff_then_given_up()
    {
        var attempts = 0;
        var result = await DriveErrors.RetryAsync(() =>
        {
            attempts++;
            return attempts < 3
                ? throw new CloudException(CloudErrorKind.RateLimited, "Slow down.")
                : Task.FromResult("done");
        }, Ct, _ => TimeSpan.Zero);
        Assert.Equal("done", result);
        Assert.Equal(3, attempts);

        attempts = 0;
        await Assert.ThrowsAsync<CloudException>(() => DriveErrors.RetryAsync<string>(() =>
        {
            attempts++;
            throw new CloudException(CloudErrorKind.StorageFull, "Full.");
        }, Ct, _ => TimeSpan.Zero));
        Assert.Equal(1, attempts);
    }
}
