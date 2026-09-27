using System.Diagnostics;
using System.Security.Cryptography;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;

namespace GameSync.Core.Tests;

/// <summary>The backup folder on each PC: its limits, moving it, a missing drive, crashes on the way up, and the readable cloud copy.</summary>
public class LocalHistoryTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly GameId Game = GameId.Parse("game");

    [Fact]
    public async Task BAK_17_this_pc_keeps_the_newest_10_versions_files_and_the_cloud_keeps_all()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        await MakeVersionsAsync(desktop, 12);

        var versions = await Cloud.VersionsAsync(world, "game");
        var history = new LocalHistory(desktop.HistoryDir);
        Assert.Equal(12, versions.Count);
        Assert.Equal(12, (await history.Log.ListAsync(Game, Ct)).Count);
        Assert.Equal(10, history.Blobs.List(Game).Count());

        // The oldest version's files come back from the cloud when a restore needs them.
        var oldest = versions.OrderBy(v => v.CreatedUtc).First();
        await desktop.Service().RestoreAsync(Game, oldest.Id, Ct);
        Assert.Equal("progress 1", desktop.Read("game", "slot.sav"));
    }

    [Fact]
    public async Task BAK_17_named_saves_stay_on_this_pc_past_the_newest_10()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "at the first boss");
        await desktop.SyncAsync();
        await desktop.Service().SaveAsAsync(Game, "Before the first boss", Ct);
        await MakeVersionsAsync(desktop, 12);

        var blobs = new LocalHistory(desktop.HistoryDir).Blobs.List(Game).Select(b => b.Id).ToList();

        Assert.Contains(BlobId.FromHash(SHA256.HashData("at the first boss"u8)), blobs);
        Assert.Equal(11, blobs.Count);
    }

    [Fact]
    public async Task FOLD_06_keep_everything_keeps_every_versions_files()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.Options = desktop.Options with { HistoryLimits = HistoryLimits.Everything };
        desktop.AddGame("game");
        await MakeVersionsAsync(desktop, 12);

        Assert.Equal(12, new LocalHistory(desktop.HistoryDir).Blobs.List(Game).Count());
    }

    [Fact]
    public async Task BAK_17_the_size_limit_drops_the_oldest_files_first_and_never_the_current_save()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.Options = desktop.Options with { HistoryLimits = new HistoryLimits(VersionsPerGame: 10, MaxBytes: 1) };
        desktop.AddGame("game");
        await MakeVersionsAsync(desktop, 3);

        var history = new LocalHistory(desktop.HistoryDir);
        var current = (await history.Log.ListAsync(Game, Ct)).MaxBy(v => v.CreatedUtc)!;
        Assert.Equal(current.Files.Single().Hash, Assert.Single(history.Blobs.List(Game)).Id);
    }

    [Fact]
    public async Task A_stray_folder_in_the_backup_folder_is_left_alone()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        var stray = Directory.CreateDirectory(Path.Combine(desktop.HistoryDir, "games", "not a game!")).FullName;
        await File.WriteAllTextAsync(Path.Combine(stray, "notes.txt"), "mine");

        await MakeVersionsAsync(desktop, 2);

        Assert.Equal("mine", await File.ReadAllTextAsync(Path.Combine(stray, "notes.txt")));
        Assert.Equal(2, (await Cloud.VersionsAsync(world, "game")).Count);
    }

    [Fact]
    public async Task BAK_12_a_crash_between_uploading_files_and_the_record_resumes_on_the_next_start()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "first");

        CrashPoints.Arm(CrashPoints.PushAfterBlobs);
        await Assert.ThrowsAsync<SimulatedCrashException>(() => desktop.Service().SyncAsync(null, Ct));

        Assert.Empty(await Cloud.VersionsAsync(world, "game"));
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(world.Cloud, "games", "game", "blobs"), "*.gz", SearchOption.AllDirectories));

        await desktop.Service().RecoverAsync(Ct);
        Assert.Single(await Cloud.VersionsAsync(world, "game"));
        Assert.False(new LocalHistory(desktop.HistoryDir).HasPending(Game));
    }

    [Fact]
    public async Task Versions_the_cloud_lost_upload_again_from_the_backup_folder()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        await MakeVersionsAsync(desktop, 2);

        Directory.Delete(Path.Combine(world.Cloud, "games", "game"), recursive: true);
        var result = Single(await desktop.SyncAsync());

        Assert.Contains(result.Warnings, w => w.Contains("missing 2 versions", StringComparison.Ordinal));
        Assert.Equal(GameStatus.Synced, result.Status);
        Assert.Equal(2, (await Cloud.VersionsAsync(world, "game")).Count);
        Assert.Equal(SyncAction.Download, Single(await laptop.SyncAsync()).Action);
        Assert.Equal("progress 2", laptop.Read("game", "slot.sav"));
    }

    [Fact]
    public async Task FOLD_05_a_backup_folder_on_a_missing_drive_pauses_backups_and_deletes_nothing()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        var missing = Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => (char)c).First(c => !Directory.Exists($"{c}:\\"));
        desktop.HistoryDir = $@"{missing}:\Backups\GameSync";
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "keep me");

        var service = desktop.Service();
        var result = Single(await service.SyncAsync(null, Ct));

        Assert.Equal(GameStatus.NotAvailable, result.Status);
        Assert.Contains($"{missing}: isn't connected", result.Message);
        Assert.Contains(service.Notices, n => n.Contains("paused", StringComparison.Ordinal));
        Assert.Equal("keep me", desktop.Read("game", "slot.sav"));
        Assert.Empty(await Cloud.VersionsAsync(world, "game"));
    }

    [Fact]
    public async Task FOLD_03_moving_the_backup_folder_copies_and_checks_everything_first()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        await MakeVersionsAsync(desktop, 2);
        var history = new LocalHistory(desktop.HistoryDir);
        var target = Path.Combine(world.Root, "other-drive", "GameSync");

        Assert.Equal(0, history.CopyTo(target));

        Assert.Equal(Files(desktop.HistoryDir), Files(target));
        desktop.HistoryDir = target;
        Assert.Equal(SyncAction.None, Single(await desktop.SyncAsync()).Action);
    }

    [Fact]
    public async Task FOLD_03_a_failed_move_leaves_the_old_folder_as_it_was()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        await MakeVersionsAsync(desktop, 2);
        var history = new LocalHistory(desktop.HistoryDir);
        var before = Files(desktop.HistoryDir);

        // A file where the target's parent folder should be fails the copy partway, like a drive pulled out.
        var blocker = Path.Combine(world.Root, "blocker");
        await File.WriteAllTextAsync(blocker, "not a folder");
        Assert.ThrowsAny<IOException>(() => history.CopyTo(Path.Combine(blocker, "GameSync")));

        // A folder that isn't empty is refused before anything is copied.
        var busy = Directory.CreateDirectory(Path.Combine(world.Root, "busy")).FullName;
        await File.WriteAllTextAsync(Path.Combine(busy, "something.txt"), "mine");
        Assert.Throws<InvalidOperationException>(() => history.CopyTo(busy));

        Assert.Equal(before, Files(desktop.HistoryDir));
        Assert.Equal(SyncAction.None, Single(await desktop.SyncAsync()).Action);
    }

    [Fact]
    public async Task FOLD_03_a_damaged_file_stays_behind_instead_of_blocking_the_move()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        await MakeVersionsAsync(desktop, 2);
        var damaged = Directory.EnumerateFiles(Path.Combine(desktop.HistoryDir, "games", "game", "blobs"), "*.gz", SearchOption.AllDirectories).First();
        await File.WriteAllBytesAsync(damaged, [1, 2, 3]);

        Assert.Equal(1, new LocalHistory(desktop.HistoryDir).CopyTo(Path.Combine(world.Root, "moved")));
    }

    [Fact]
    public void FOLD_04_the_backup_folder_refuses_game_folders_sync_tools_and_system_folders()
    {
        var guard = new BackupFolderGuard([(@"C:\Users\You\OneDrive", "OneDrive"), (@"G:\", "Google Drive")], [@"C:\Windows", @"C:\Program Files"]);
        var terraria = new GameDefinition
        {
            Id = GameId.Parse("terraria"),
            Title = "Terraria",
            Roots = new Dictionary<string, string> { ["saves"] = @"C:\Users\You\Documents\My Games\Terraria" },
            Rules = [new SaveRule { Root = "saves" }],
        };
        string[] installs = [@"E:\Games\Terraria"];

        Assert.Contains("OneDrive already syncs", guard.Check(@"C:\Users\You\OneDrive\Backups", [terraria], installs));
        Assert.Contains("Google Drive already syncs", guard.Check(@"G:\My Drive\GameSync", [terraria], installs));
        Assert.Contains("Windows protects", guard.Check(@"C:\Program Files\GameSync", [terraria], installs));
        Assert.Contains("install folder", guard.Check(@"E:\Games\Terraria\Backups", [terraria], installs));
        Assert.Contains("save folder", guard.Check(@"C:\Users\You\Documents\My Games\Terraria\GameSync", [terraria], installs));
        Assert.Contains("full path", guard.Check(@"Backups\GameSync", [terraria], installs));
        Assert.Null(guard.Check(@"D:\Backups\GameSync", [terraria], installs));
    }

    [Fact]
    public async Task CLOUD_03_the_cloud_keeps_a_plain_copy_of_the_newest_save_and_a_restore_kit()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        var t1 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        desktop.Write("game", "slot1.sav", "one", t1);
        desktop.Write("game", "sub/slot2.sav", "two", t1.AddMinutes(1));
        await desktop.SyncAsync();

        var latest = Path.Combine(world.Cloud, "games", "game", "latest");
        Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(latest, "saves", "slot1.sav")));
        Assert.Equal(t1, File.GetLastWriteTimeUtc(Path.Combine(latest, "saves", "slot1.sav")));
        Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(latest, "saves", "sub", "slot2.sav")));
        Assert.True(File.Exists(Path.Combine(latest, "VERSION.txt")));
        Assert.True(File.Exists(Path.Combine(world.Cloud, "HOW-TO-RESTORE.txt")));
        Assert.True(File.Exists(Path.Combine(world.Cloud, "restore.ps1")));

        // The next version replaces it, and files the save no longer has leave it.
        desktop.Played("game");
        File.Delete(Path.Combine(desktop.Folder("game"), "sub", "slot2.sav"));
        desktop.Write("game", "slot1.sav", "one, later");
        await desktop.SyncAsync();

        Assert.Equal("one, later", await File.ReadAllTextAsync(Path.Combine(latest, "saves", "slot1.sav")));
        Assert.False(File.Exists(Path.Combine(latest, "saves", "sub", "slot2.sav")));
    }

    [Fact]
    public async Task CLOUD_03_restore_ps1_restores_an_old_version_without_GameSync()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        var t1 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        desktop.Write("game", "slot1.sav", "first run", t1);
        desktop.Write("game", "deep/folder/slot2.sav", "second file", t1.AddMinutes(5));
        await desktop.SyncAsync();
        var first = Assert.Single(await Cloud.VersionsAsync(world, "game"));
        desktop.Played("game");
        desktop.Write("game", "slot1.sav", "second run");
        await desktop.SyncAsync();
        var script = Path.Combine(world.Cloud, "restore.ps1");
        var output = Path.Combine(world.Root, "restored");

        var (code, text) = RunPowerShell(script, "-Game", "game", "-Version", first.Id.Value, "-To", output);

        Assert.True(code == 0, text);
        Assert.Equal("first run", await File.ReadAllTextAsync(Path.Combine(output, "saves", "slot1.sav")));
        Assert.Equal(t1, File.GetLastWriteTimeUtc(Path.Combine(output, "saves", "slot1.sav")));
        Assert.Equal("second file", await File.ReadAllTextAsync(Path.Combine(output, "saves", "deep", "folder", "slot2.sav")));

        var (listCode, list) = RunPowerShell(script, "-Game", "game");
        Assert.True(listCode == 0, list);
        Assert.Contains(first.Id.Value, list);

        var (refusedCode, refused) = RunPowerShell(script, "-Game", "game", "-Version", "latest", "-To", output);
        Assert.NotEqual(0, refusedCode);
        Assert.Contains("isn't empty", refused);
    }

    /// <summary>Each version changes the one save file during a play session.</summary>
    private static async Task MakeVersionsAsync(TestPc pc, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            pc.Played("game");
            pc.Write("game", "slot.sav", $"progress {i}");
            await pc.SyncAsync();
        }
    }

    private static SortedDictionary<string, string> Files(string folder) =>
        new(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(folder, f), f => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f)))),
            StringComparer.OrdinalIgnoreCase);

    private static (int Code, string Output) RunPowerShell(string script, params string[] args)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }.Concat(args))
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill();
            throw new TimeoutException("restore.ps1 didn't finish within a minute.");
        }

        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    private static GameResult Single(IReadOnlyList<GameResult> results) => Assert.Single(results);
}
