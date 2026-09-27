using System.Text.Json;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Sync;

namespace GameSync.Core.Tests;

/// <summary>Two simulated PCs sharing one cloud folder, with real files on disk.</summary>
public class TwoPcTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task SYNC_01_a_save_travels_both_ways()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("sekiro");
        laptop.AddGame("sekiro");

        desktop.Write("sekiro", "S0000.sl2", "boss 1 beaten");
        Assert.Equal(SyncAction.Upload, Single(await desktop.SyncAsync()).Action);

        Assert.Equal(SyncAction.Download, Single(await laptop.SyncAsync()).Action);
        Assert.Equal("boss 1 beaten", laptop.Read("sekiro", "S0000.sl2"));

        laptop.Played("sekiro");
        laptop.Write("sekiro", "S0000.sl2", "boss 2 beaten");
        Assert.Equal(SyncAction.Upload, Single(await laptop.SyncAsync()).Action);

        Assert.Equal(SyncAction.Download, Single(await desktop.SyncAsync()).Action);
        Assert.Equal("boss 2 beaten", desktop.Read("sekiro", "S0000.sl2"));
        Assert.Equal(SyncAction.None, Single(await desktop.SyncAsync()).Action);
        Assert.Equal(desktop.Tree("sekiro"), laptop.Tree("sekiro"));
    }

    [Fact]
    public async Task SYNC_02_a_locked_save_file_only_holds_up_its_own_game()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("locked");
        desktop.AddGame("fine");
        var lockedFile = desktop.Write("locked", "slot.sav", "a");
        desktop.Write("fine", "slot.sav", "b");

        using (new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var results = await desktop.SyncAsync();

            Assert.Equal(GameStatus.FilesInUse, results.Single(r => r.Game.Value == "locked").Status);
            Assert.Equal(GameStatus.Synced, results.Single(r => r.Game.Value == "fine").Status);
        }

        Assert.Single(await Cloud.VersionsAsync(world, "fine"));
        Assert.Empty(await Cloud.VersionsAsync(world, "locked"));
    }

    [Fact]
    public async Task SYNC_03_offline_play_on_both_pcs_is_one_conflict_and_nothing_else()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("hades");
        desktop.AddGame("other");
        laptop.AddGame("hades");
        desktop.Write("hades", "save.sav", "start");
        desktop.Write("other", "save.sav", "untouched");
        await desktop.SyncAsync();
        await laptop.SyncAsync();

        // The laptop goes offline with its own copy of the cloud; both PCs play and upload from the same parent.
        var offlineCloud = Path.Combine(world.Root, "laptop-offline-cloud");
        Cloud.CopyFolder(world.Cloud, offlineCloud);
        desktop.Played("hades");
        desktop.Write("hades", "save.sav", "desktop run");
        await desktop.SyncAsync();
        laptop.Played("hades");
        laptop.Write("hades", "save.sav", "laptop run");
        await laptop.SyncAsync(offlineCloud);

        Cloud.CopyFolder(offlineCloud, world.Cloud);
        var results = await desktop.SyncAsync();

        Assert.Equal(GameStatus.Conflict, results.Single(r => r.Game.Value == "hades").Status);
        Assert.Contains("same starting point", results.Single(r => r.Game.Value == "hades").Message);
        Assert.Equal(GameStatus.Synced, results.Single(r => r.Game.Value == "other").Status);
        Assert.Equal("desktop run", desktop.Read("hades", "save.sav"));
    }

    [Fact]
    public async Task SYNC_04_newest_save_wins_the_other_is_pinned_and_Swap_reverses_it()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("sekiro");
        laptop.AddGame("sekiro");
        desktop.Write("sekiro", "S0000.sl2", "start");
        await desktop.SyncAsync();
        await laptop.SyncAsync();

        desktop.Played("sekiro");
        laptop.Played("sekiro");
        desktop.Write("sekiro", "S0000.sl2", "desktop progress", DateTime.UtcNow.AddMinutes(-10));
        laptop.Write("sekiro", "S0000.sl2", "laptop progress", DateTime.UtcNow.AddMinutes(-5));
        await laptop.SyncAsync();

        var result = Single(await desktop.SyncAsync());

        Assert.Equal(SyncAction.Download, result.Action);
        Assert.Equal("laptop progress", desktop.Read("sekiro", "S0000.sl2"));
        Assert.Contains("Swap", result.Notice);
        var history = await desktop.Service().HistoryAsync(GameId.Parse("sekiro"), Ct);
        Assert.Contains(history, h => h.Pinned && h.Version.Kind == VersionKind.Kept && h.Version.Device.Name == "DESKTOP");

        await desktop.Service().SwapAsync(GameId.Parse("sekiro"), Ct);
        Assert.Equal("desktop progress", desktop.Read("sekiro", "S0000.sl2"));

        Assert.Equal(SyncAction.Download, Single(await laptop.SyncAsync()).Action);
        Assert.Equal("desktop progress", laptop.Read("sekiro", "S0000.sl2"));
    }

    [Fact]
    public async Task SYNC_05_new_pc_with_an_older_local_save_keeps_the_cloud_current()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("terraria");
        laptop.AddGame("terraria");
        desktop.Write("terraria", "Worlds/world1.wld", "100 hours");
        await desktop.SyncAsync();
        laptop.Write("terraria", "Worlds/world1.wld", "old copy", DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(SyncAction.Download, Single(await laptop.SyncAsync()).Action);

        Assert.Equal("100 hours", laptop.Read("terraria", "Worlds/world1.wld"));
        var history = await laptop.Service().HistoryAsync(GameId.Parse("terraria"), Ct);
        Assert.Contains(history, h => h.Pinned && h.Version.Origin == VersionOrigin.KeptAtFirstSync);
    }

    [Fact]
    public async Task SYNC_12_an_unplugged_drive_never_reads_as_no_saves()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        var missing = Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => (char)c).First(c => !Directory.Exists($"{c}:\\"));
        desktop.AddGame("wukong", savesFolder: $@"{missing}:\Black Myth Wukong\b1\Saved\SaveGames");

        var result = Single(await desktop.SyncAsync());

        Assert.Equal(GameStatus.NotAvailable, result.Status);
        Assert.Contains($"Drive {missing}:", result.Message);
        Assert.Empty(await Cloud.VersionsAsync(world, "wukong"));
    }

    [Fact]
    public async Task SYNC_13_rotating_autosaves_sync_but_an_emptied_folder_does_not()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        var a = desktop.Write("game", "auto1.sav", "a");
        var b = desktop.Write("game", "auto2.sav", "b");
        await desktop.SyncAsync();

        desktop.Played("game");
        File.Delete(a);
        Assert.Equal(SyncAction.Upload, Single(await desktop.SyncAsync()).Action);

        File.Delete(b);
        var result = Single(await desktop.SyncAsync());
        Assert.Equal(GameStatus.SavesMissing, result.Status);
        Assert.Equal(2, (await Cloud.VersionsAsync(world, "game")).Count);
    }

    [Fact]
    public async Task SYNC_14_sync_does_exactly_what_the_plan_said()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("one");
        desktop.AddGame("two");
        desktop.Write("one", "a.sav", "a");
        desktop.Write("two", "b.sav", "b");
        var service = desktop.Service();

        var plans = await service.PlanAsync(null, Ct);
        var results = await service.ExecuteAsync(plans, Ct);

        Assert.Equal(plans.Select(p => (p.Stream.Id, p.Decision!.Action)), results.Select(r => (r.Game, r.Action!.Value)));
        Assert.DoesNotContain(results, r => r.Message.StartsWith("Changed since the plan", StringComparison.Ordinal));

        var stale = await service.PlanAsync(null, Ct);
        desktop.Played("one");
        desktop.Write("one", "a.sav", "changed after planning");
        var rerun = await service.ExecuteAsync(stale, Ct);
        Assert.StartsWith("Changed since the plan", rerun.Single(r => r.Game.Value == "one").Message);
        Assert.Equal(SyncAction.None, rerun.Single(r => r.Game.Value == "two").Action);
    }

    [Fact]
    public async Task BAK_03_every_version_is_kept()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Played("game");
        var service = desktop.Service();
        for (var i = 0; i < 60; i++)
        {
            desktop.Write("game", "slot.sav", $"progress {i}");
            await service.SyncAsync(null, Ct);
        }

        Assert.Equal(60, (await service.HistoryAsync(GameId.Parse("game"), Ct)).Count);
    }

    [Fact]
    public async Task BAK_04_and_BAK_05_thinning_removes_exactly_the_preview_and_never_a_pinned_version()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        var game = GameId.Parse("game");
        desktop.AddGame("game");
        desktop.Played("game");
        var service = desktop.Service();
        for (var i = 0; i < 12; i++)
        {
            desktop.Write("game", "slot.sav", $"progress {i}");
            await service.SyncAsync(null, Ct);
        }

        var before = (await service.HistoryAsync(game, Ct)).Select(h => h.Version.Id).Reverse().ToList();
        await service.PinAsync(game, before[2], "the good run", Ct);

        var preview = await service.PreviewThinAsync(game, keepNewest: 5, Ct);
        await service.ThinAsync(preview, keepNewest: 5, Ct);

        var after = (await service.HistoryAsync(game, Ct)).Select(h => h.Version.Id).ToHashSet();
        Assert.Equal(6, preview.Versions.Count);
        Assert.All(preview.Versions, v => Assert.DoesNotContain(v.Id, after));
        Assert.True(after.SetEquals(before.Except(preview.Versions.Select(v => v.Id))));
        Assert.Contains(before[2], after);
        Assert.Equal(SyncAction.None, Single(await service.SyncAsync(null, Ct)).Action);
    }

    [Fact]
    public async Task BAK_08_restore_an_old_version_and_undo_it()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        var game = GameId.Parse("game");
        desktop.AddGame("game");
        desktop.Played("game");
        var service = desktop.Service();
        foreach (var text in new[] { "one", "two", "three" })
        {
            desktop.Write("game", "slot.sav", text);
            await service.SyncAsync(null, Ct);
        }

        var history = await service.HistoryAsync(game, Ct);
        var first = history.Last().Version.Id;
        var third = history.First().Version.Id;

        await service.RestoreAsync(game, first, Ct);
        Assert.Equal("one", desktop.Read("game", "slot.sav"));

        await service.RestoreAsync(game, third, Ct);
        Assert.Equal("three", desktop.Read("game", "slot.sav"));
        Assert.Equal(SyncAction.None, Single(await service.SyncAsync(null, Ct)).Action);
        Assert.Empty(Directory.EnumerateFiles(desktop.Folder("game"), "*.gs-new-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task BAK_09_restored_files_keep_their_original_modified_times()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        var modified = new DateTime(2024, 3, 9, 21, 4, 17, 123, DateTimeKind.Utc);
        desktop.Write("game", "slot1.sav", "a", modified);
        desktop.Write("game", "Sub/slot2.sav", "b", modified.AddDays(1));
        await desktop.SyncAsync();

        await laptop.SyncAsync();

        Assert.Equal(desktop.Tree("game"), laptop.Tree("game"));
    }

    [Fact]
    public async Task BAK_11_a_change_made_with_the_game_closed_is_held_until_approved()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        desktop.Write("game", "slot.sav", "honest progress");
        await desktop.SyncAsync();
        await laptop.SyncAsync();

        desktop.Write("game", "slot.sav", "edited with the game closed");
        Assert.Equal(GameStatus.HeldForReview, Single(await desktop.SyncAsync()).Status);
        Assert.Equal(SyncAction.None, Single(await laptop.SyncAsync()).Action);
        Assert.Equal("honest progress", laptop.Read("game", "slot.sav"));

        await desktop.Service().ApproveAsync(GameId.Parse("game"), Ct);
        Assert.Equal(SyncAction.Download, Single(await laptop.SyncAsync()).Action);
        Assert.Equal("edited with the game closed", laptop.Read("game", "slot.sav"));
    }

    [Fact]
    public async Task BAK_16_back_up_now_works_on_a_backup_only_game()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("cyberpunk", mode: GameMode.BackupOnly);
        desktop.Write("cyberpunk", "ManualSave-0/sav.dat", "night city");

        var result = Single(await desktop.Service().BackupNowAsync(GameId.Parse("cyberpunk"), Ct));

        Assert.Equal(GameStatus.BackupOnly, result.Status);
        Assert.NotNull(result.NewVersion);
        Assert.Single(await Cloud.VersionsAsync(world, "cyberpunk"));
    }

    [Fact]
    public async Task FIND_09_settings_stay_per_pc_while_saves_sync()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game", withConfig: true);
        laptop.AddGame("game", withConfig: true);
        desktop.Write("game", "slot.sav", "progress");
        desktop.Write("game", "graphics.ini", "4K ultra", root: "config");
        await desktop.SyncAsync();

        laptop.Write("game", "graphics.ini", "1080p low", root: "config");
        await laptop.SyncAsync();
        laptop.Write("game", "graphics.ini", "720p potato", root: "config");
        await laptop.SyncAsync();
        await desktop.SyncAsync();

        Assert.Equal("progress", laptop.Read("game", "slot.sav"));
        Assert.Equal("4K ultra", desktop.Read("game", "graphics.ini", root: "config"));
        Assert.Equal("720p potato", laptop.Read("game", "graphics.ini", root: "config"));
        Assert.Contains(laptop.Service().Streams, s => s.PerDevice);
    }

    [Fact]
    public async Task R1_and_R2_program_files_are_never_backed_up_and_the_game_warns()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "real save");
        desktop.Write("game", "a.dll", "not really a dll");
        desktop.WriteBytes("game", "sneaky.sav", SafetyTests.FakeProgram());

        var result = Single(await desktop.SyncAsync());

        var version = Assert.Single(await Cloud.VersionsAsync(world, "game"));
        Assert.Equal("saves/slot.sav", Assert.Single(version.Files).Path);
        Assert.Equal(2, result.Warnings.Count(w => w.StartsWith("Program file", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task R3_a_file_the_antivirus_flags_blocks_the_restore()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        desktop.Write("game", "slot.sav", $"looks harmless {FakeMalwareScanner.Marker}");
        await desktop.SyncAsync();

        var result = Single(await laptop.SyncAsync());

        Assert.Equal(GameStatus.Blocked, result.Status);
        Assert.Contains("antivirus", result.Message);
        Assert.Empty(laptop.Tree("game"));
    }

    [Fact]
    public async Task R6_and_R8_crafted_cloud_versions_never_write_outside_the_rules()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game", include: "*.sav");
        laptop.AddGame("game", include: "*.sav");
        desktop.Write("game", "slot.sav", "real");
        await desktop.SyncAsync();
        var real = Assert.Single(await Cloud.VersionsAsync(world, "game"));

        // A path that climbs out of the save folder: the version is ignored and the real one downloads.
        await Craft(world, real, "evil1", "saves/../../../evil.sav");
        var result = Single(await laptop.SyncAsync());
        Assert.Equal("real", laptop.Read("game", "slot.sav"));
        Assert.Contains(result.Warnings, w => w.Contains("evil1", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(world.Root, "evil.sav")));

        // A tidy path the game's rules don't cover, then a program: each becomes the current version, and each
        // restore is blocked with nothing written.
        var parent = real.Id;
        foreach (var (name, path) in new[] { ("evil2", "saves/autorun.txt"), ("evil3", "saves/payload.dll") })
        {
            await Craft(world, real, name, path, parent);
            var blocked = Single(await laptop.SyncAsync());
            Assert.Equal(GameStatus.Blocked, blocked.Status);
            Assert.Equal(["slot.sav"], laptop.Tree("game").Keys);
            parent = VersionId.Parse(name);
        }
    }

    private static async Task Craft(TestWorld world, VersionRecord real, string id, string path, VersionId? parent = null)
    {
        var crafted = real with
        {
            Id = VersionId.Parse(id),
            Parent = parent ?? real.Id,
            Kind = VersionKind.Normal,
            Device = new DeviceInfo(DeviceId.Parse("d-evil"), "EVIL"),
            CreatedUtc = DateTime.UtcNow,
            Files = [real.Files[0] with { Path = path }],
        };
        await File.WriteAllBytesAsync(Path.Combine(Cloud.VersionsFolder(world.Cloud, "game"), $"{id}.json"),
            JsonSerializer.SerializeToUtf8Bytes(crafted, Json.Options));
    }

    private static GameResult Single(IReadOnlyList<GameResult> results) => Assert.Single(results);
}
