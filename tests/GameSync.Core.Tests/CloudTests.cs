using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;

namespace GameSync.Core.Tests;

/// <summary>Two PCs and a cloud that goes offline, fills up, errs, or disagrees about the time.</summary>
public class CloudTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task PC_05_a_dropped_connection_leaves_a_newer_cloud_save_waiting_then_it_downloads()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        await StartBothAsync(desktop, laptop);
        desktop.Played("game");
        desktop.Write("game", "slot.sav", "desktop run");
        await desktop.SyncAsync();

        laptop.Fault = call => call == "blobs.get" ? new CloudException(CloudErrorKind.Offline, "You're offline.") : null;
        var waiting = Single(await laptop.SyncAsync());

        Assert.Equal(GameStatus.NewerInCloud, waiting.Status);
        Assert.Equal("start", laptop.Read("game", "slot.sav"));

        laptop.Fault = null;
        Assert.Equal(SyncAction.Download, Single(await laptop.SyncAsync()).Action);
        Assert.Equal("desktop run", laptop.Read("game", "slot.sav"));
    }

    [Fact]
    public async Task PC_05_offline_changes_on_both_sides_keep_this_pcs_save_and_wait_for_the_cloud()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        await StartBothAsync(desktop, laptop);
        desktop.Played("game");
        desktop.Write("game", "slot.sav", "desktop run", DateTime.UtcNow.AddMinutes(-10));
        await desktop.SyncAsync();

        // The laptop saw the desktop's save but lost the connection before downloading it, then played offline.
        laptop.Fault = call => call == "blobs.get" ? new CloudException(CloudErrorKind.Offline, "You're offline.") : null;
        await laptop.SyncAsync();
        laptop.Offline = true;
        laptop.Played("game");
        laptop.Write("game", "slot.sav", "laptop run", DateTime.UtcNow.AddMinutes(-5));
        var offline = Single(await laptop.SyncAsync());

        Assert.Equal(SyncAction.WaitForCloud, offline.Action);
        Assert.Equal(GameStatus.UploadPending, offline.Status);
        Assert.Equal("laptop run", laptop.Read("game", "slot.sav"));

        // Back online, the normal rules apply: the laptop's save is newer, so it wins and the desktop's is pinned.
        laptop.Offline = false;
        var online = Single(await laptop.SyncAsync());
        Assert.Equal(SyncAction.Upload, online.Action);
        Assert.Contains("Swap", online.Notice);
        Assert.Equal(SyncAction.Download, Single(await desktop.SyncAsync()).Action);
        Assert.Equal("laptop run", desktop.Read("game", "slot.sav"));
    }

    [Fact]
    public async Task PC_05_a_first_sync_made_offline_still_lets_the_cloud_win_once_back_online()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "100 hours");
        await desktop.SyncAsync();

        // A fresh install on the laptop, synced for the first time while offline.
        laptop.AddGame("game");
        laptop.Write("game", "slot.sav", "fresh install");
        laptop.Offline = true;
        var offline = Single(await laptop.SyncAsync());
        Assert.Equal(SyncAction.WaitForCloud, offline.Action);
        Assert.Equal(GameStatus.UploadPending, offline.Status);

        // SYNC-05 still applies once the cloud can be seen: its save stays current, and the laptop's is kept.
        laptop.Offline = false;
        Assert.Equal(SyncAction.Download, Single(await laptop.SyncAsync()).Action);
        Assert.Equal("100 hours", laptop.Read("game", "slot.sav"));
        var history = await laptop.Service().HistoryAsync(GameId.Parse("game"), Ct);
        Assert.Contains(history, h => h.Version.Origin == VersionOrigin.KeptAtFirstSync && h.Pinned && h.Uploaded);
        Assert.Equal("100 hours", desktop.Read("game", "slot.sav"));
    }

    [Fact]
    public async Task PC_05_a_pin_made_offline_reaches_the_cloud_later_and_an_unpin_needs_the_cloud()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "start");
        await desktop.SyncAsync();
        var version = Assert.Single(await Cloud.VersionsAsync(world, "game"));
        var cloudLog = new FolderVersionLog(world.Cloud);

        desktop.Offline = true;
        await desktop.Service().PinAsync(version.Game, version.Id, "before the final boss", Ct);
        Assert.Empty(await cloudLog.ListPinsAsync(version.Game, Ct));
        await Assert.ThrowsAsync<CloudException>(() => desktop.Service().UnpinAsync(version.Game, version.Id, Ct));

        desktop.Offline = false;
        await desktop.SyncAsync();
        Assert.Equal("before the final boss", Assert.Single(await cloudLog.ListPinsAsync(version.Game, Ct)).Label);
    }

    [Theory]
    [InlineData(CloudErrorKind.StorageFull, "Google Drive is full.")]
    [InlineData(CloudErrorKind.RateLimited, "Google Drive asked GameSync to slow down.")]
    [InlineData(CloudErrorKind.SignInExpired, "The sign-in to Google Drive expired.")]
    [InlineData(CloudErrorKind.Offline, "You're offline.")]
    public async Task CLOUD_04_a_failed_upload_waits_on_this_pc_and_says_why(CloudErrorKind kind, string message)
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "progress");
        desktop.Fault = call => call is "blobs.put" or "log.append" ? new CloudException(kind, message) : null;

        var waiting = Single(await desktop.SyncAsync());

        Assert.Equal(GameStatus.UploadPending, waiting.Status);
        Assert.Contains(message, waiting.Message);
        if (kind == CloudErrorKind.SignInExpired)
        {
            Assert.Contains("gamesync signin", waiting.Message);
        }

        Assert.Empty(await Cloud.VersionsAsync(world, "game"));

        desktop.Fault = null;
        Assert.Equal(GameStatus.Synced, Single(await desktop.SyncAsync()).Status);
        Assert.Single(await Cloud.VersionsAsync(world, "game"));
    }

    [Fact]
    public async Task CLOUD_06_a_file_the_cloud_flags_as_malware_is_never_downloaded()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        desktop.Write("game", "slot.sav", "progress");
        await desktop.SyncAsync();
        laptop.Fault = call => call == "blobs.get" ? new CloudException(CloudErrorKind.Flagged, "Google flagged slot.sav as malware.") : null;

        var result = Single(await laptop.SyncAsync());

        Assert.Equal(GameStatus.Blocked, result.Status);
        Assert.Contains("flagged", result.Message);
        Assert.Empty(laptop.Tree("game"));
    }

    [Fact]
    public async Task SYNC_08_a_wrong_clock_turns_newest_wins_off_and_says_so_once()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        await StartBothAsync(desktop, laptop);
        desktop.Played("game");
        laptop.Played("game");
        desktop.Write("game", "slot.sav", "desktop progress", DateTime.UtcNow.AddMinutes(-5));
        laptop.Write("game", "slot.sav", "laptop progress", DateTime.UtcNow.AddMinutes(-10));
        await laptop.SyncAsync();
        desktop.ClockSkew = TimeSpan.FromMinutes(5);

        var service = desktop.Service();
        var result = Single(await service.SyncAsync(null, Ct));

        Assert.Equal(GameStatus.Conflict, result.Status);
        Assert.Contains("5 minutes ahead of", result.Message);
        Assert.Contains(service.Notices, n => n.Contains("clock", StringComparison.Ordinal));
        Assert.Equal("desktop progress", desktop.Read("game", "slot.sav"));
    }

    [Fact]
    public async Task CLOUD_05_the_cloud_passing_80_percent_full_warns_once()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "progress");

        desktop.Storage = (81, 100);
        var first = desktop.Service();
        await first.SyncAsync(null, Ct);
        var second = desktop.Service();
        await second.SyncAsync(null, Ct);
        desktop.Storage = (50, 100);
        await desktop.SyncAsync();
        desktop.Storage = (90, 100);
        var third = desktop.Service();
        await third.SyncAsync(null, Ct);

        Assert.Contains(first.Notices, n => n.Contains("81% full", StringComparison.Ordinal));
        Assert.DoesNotContain(second.Notices, n => n.Contains("full", StringComparison.Ordinal));
        Assert.Contains(third.Notices, n => n.Contains("90% full", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PC_01_and_PC_02_each_pc_is_listed_and_a_rename_reaches_the_other_pc()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        await StartBothAsync(desktop, laptop);
        laptop.Played("game");
        laptop.Write("game", "slot.sav", "laptop run");
        await laptop.SyncAsync();

        var devices = await desktop.Service().DevicesAsync(Ct);
        Assert.Equal(["DESKTOP", "LAPTOP"], devices.Select(d => d.Name).Order());
        Assert.All(devices, d =>
        {
            Assert.Equal("test", d.AppVersion);
            Assert.True(DateTime.UtcNow - d.LastSeenUtc < TimeSpan.FromMinutes(5));
        });

        laptop.State.RenameDevice("TRAVEL");
        await laptop.SyncAsync();
        await desktop.SyncAsync();

        var history = await desktop.Service().HistoryAsync(GameId.Parse("game"), Ct);
        Assert.Contains(history, h => h.DeviceName == "TRAVEL");
        Assert.DoesNotContain(history, h => h.DeviceName == "LAPTOP");
    }

    [Fact]
    public async Task FIND_08_one_version_lands_in_each_pcs_own_install_folder()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        var wukong = GameId.Parse("wukong");
        desktop.InstallDirs[wukong] = Path.Combine(world.Root, "DESKTOP", "G", "Black Myth Wukong");
        laptop.InstallDirs[wukong] = Path.Combine(world.Root, "LAPTOP", "D", "Games", "Wukong");
        desktop.AddPortableGame("wukong", "<installDir>/b1/Saved/SaveGames");
        laptop.AddPortableGame("wukong", "<installDir>/b1/Saved/SaveGames");

        desktop.Write("wukong", "ArchiveSaveFile.1.sav", "chapter 3");
        await desktop.SyncAsync();

        Assert.Equal(SyncAction.Download, Single(await laptop.SyncAsync()).Action);
        Assert.Equal("chapter 3", await File.ReadAllTextAsync(
            Path.Combine(world.Root, "LAPTOP", "D", "Games", "Wukong", "b1", "Saved", "SaveGames", "ArchiveSaveFile.1.sav")));
        var version = Assert.Single(await Cloud.VersionsAsync(world, "wukong"));
        Assert.Equal("saves/ArchiveSaveFile.1.sav", Assert.Single(version.Files).Path);
    }

    [Fact]
    public async Task FIND_08_documents_and_other_folders_resolve_on_each_pc()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddPortableGame("terraria", "<documents>/My Games/Terraria");
        laptop.AddPortableGame("terraria", "<documents>/My Games/Terraria");

        desktop.Write("terraria", "Players/hero.plr", "hero");
        await desktop.SyncAsync();
        await laptop.SyncAsync();

        Assert.Equal("hero", await File.ReadAllTextAsync(Path.Combine(laptop.KnownFolders["<documents>"], "My Games", "Terraria", "Players", "hero.plr")));
    }

    [Fact]
    public async Task FIND_08_a_game_not_installed_on_this_pc_is_not_available_never_empty()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var tablet = world.Pc("TABLET");
        desktop.InstallDirs[GameId.Parse("wukong")] = Path.Combine(world.Root, "DESKTOP", "Games", "Wukong");
        desktop.AddPortableGame("wukong", "<installDir>/b1/Saved/SaveGames");
        tablet.AddPortableGame("wukong", "<installDir>/b1/Saved/SaveGames");
        desktop.Write("wukong", "ArchiveSaveFile.1.sav", "chapter 3");
        await desktop.SyncAsync();

        var result = Single(await tablet.SyncAsync());

        Assert.Equal(GameStatus.NotAvailable, result.Status);
        Assert.Contains("isn't installed on this PC", result.Message);
        Assert.Single(await Cloud.VersionsAsync(world, "wukong"));
    }

    [Fact]
    public async Task PC_03_a_save_from_another_steam_account_lands_with_a_warning()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.Accounts["steamUser"] = "76561198000000001";
        laptop.Accounts["steamUser"] = "76561198000000002";
        desktop.AddPortableGame("sekiro", "<roaming>/Sekiro/<steamUser>");
        laptop.AddPortableGame("sekiro", "<roaming>/Sekiro/<steamUser>");
        desktop.Write("sekiro", "S0000.sl2", "boss 1");
        await desktop.SyncAsync();

        var version = Assert.Single(await Cloud.VersionsAsync(world, "sekiro"));
        Assert.Equal("76561198000000001", version.Accounts!["steamUser"]);

        var result = Single(await laptop.SyncAsync());
        Assert.Equal(SyncAction.Download, result.Action);
        Assert.Contains(result.Warnings, w => w.Contains("another Steam account", StringComparison.Ordinal));
        Assert.Equal("boss 1", await File.ReadAllTextAsync(Path.Combine(laptop.KnownFolders["<roaming>"], "Sekiro", "76561198000000002", "S0000.sl2")));
    }

    [Fact]
    public async Task PC_03_without_a_steam_account_on_this_pc_the_game_is_not_available()
    {
        using var world = new TestWorld();
        using var laptop = world.Pc("LAPTOP");
        laptop.AddPortableGame("sekiro", "<roaming>/Sekiro/<steamUser>");

        var result = Single(await laptop.SyncAsync());

        Assert.Equal(GameStatus.NotAvailable, result.Status);
        Assert.Contains("No Steam account is set on this PC", result.Message);
    }

    /// <summary>Both PCs have the game, and the desktop's first save is on both.</summary>
    private static async Task StartBothAsync(TestPc desktop, TestPc laptop)
    {
        desktop.AddGame("game");
        laptop.AddGame("game");
        desktop.Write("game", "slot.sav", "start");
        await desktop.SyncAsync();
        await laptop.SyncAsync();
    }

    private static GameResult Single(IReadOnlyList<GameResult> results) => Assert.Single(results);
}
