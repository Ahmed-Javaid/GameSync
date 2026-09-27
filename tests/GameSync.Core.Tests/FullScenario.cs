using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Scanning;
using GameSync.Core.Sync;

namespace GameSync.Core.Tests;

/// <summary>
/// Every decision and crash scenario on one set of save files, between two PCs: a first backup, a download on a new PC,
/// play on one PC, a conflict and Swap, a crash between file contents and record, and a crash midway through swapping
/// files in. The source folder is only read; everything happens on copies under %TEMP%.
/// </summary>
internal static class FullScenario
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    public static async Task RunAsync(string source, IEnumerable<string> files, Func<TestWorld> newWorld)
    {
        var title = Path.GetFileName(source);
        using var world = newWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        var game = desktop.AddGame("game");
        laptop.AddGame("game");
        foreach (var file in files)
        {
            var target = Path.Combine(desktop.Folder("game"), Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        var synced = SyncedPaths(desktop, game);
        Assert.True(synced.Count > 0, $"{title}: nothing to sync after the default excludes.");

        // SYNC-01, BAK-02, BAK-09: a first backup, then a new PC gets the same bytes and modified times.
        Expect(SyncAction.Upload, await desktop.SyncAsync(), title);
        Expect(SyncAction.Download, await laptop.SyncAsync(), title);
        AssertSame(desktop, laptop, synced, title);

        // SYNC-01: the laptop plays and changes its biggest save; the desktop downloads it.
        var biggest = synced.OrderByDescending(p => new FileInfo(Path.Combine(laptop.Folder("game"), p)).Length).First();
        laptop.Played("game");
        Append(laptop, biggest, "laptop session", DateTime.UtcNow);
        Expect(SyncAction.Upload, await laptop.SyncAsync(), title);
        Expect(SyncAction.Download, await desktop.SyncAsync(), title);
        AssertSame(desktop, laptop, synced, title);

        // SYNC-04: both play; the laptop's save is newer and wins on the desktop; Swap brings the desktop's back.
        desktop.Played("game");
        Append(desktop, biggest, "desktop run", DateTime.UtcNow.AddMinutes(-10));
        Append(laptop, biggest, "laptop run", DateTime.UtcNow.AddMinutes(-5));
        Expect(SyncAction.Upload, await laptop.SyncAsync(), title);
        var conflict = Assert.Single(await desktop.SyncAsync());
        Assert.True(conflict.Action == SyncAction.Download, $"{title}: {conflict.Message}");
        AssertSame(desktop, laptop, synced, title);
        await desktop.Service().SwapAsync(GameId.Parse("game"), Ct);
        Expect(SyncAction.Download, await laptop.SyncAsync(), title);
        AssertSame(desktop, laptop, synced, title);

        // BAK-12, BAK-13: a crash between the contents and the record leaves no half version; the job resumes.
        Append(desktop, biggest, "before the crash", DateTime.UtcNow);
        var versionsBefore = (await Cloud.VersionsAsync(world, "game")).Count;
        CrashPoints.Arm(CrashPoints.AfterBlobsBeforeRecord);
        await Assert.ThrowsAsync<SimulatedCrashException>(() => desktop.Service().SyncAsync(null, Ct));
        Assert.Equal(versionsBefore, (await Cloud.VersionsAsync(world, "game")).Count);
        Expect(SyncAction.Upload, await desktop.Service().RecoverAsync(Ct), title);

        // BAK-08: the laptop dies in the middle of swapping the files in; the next start finishes the job.
        CrashPoints.Arm(CrashPoints.MidSwap);
        await Assert.ThrowsAsync<SimulatedCrashException>(() => laptop.Service().SyncAsync(null, Ct));
        await laptop.Service().RecoverAsync(Ct);
        AssertSame(desktop, laptop, synced, title);
        Assert.Empty(Directory.EnumerateFiles(laptop.Folder("game"), "*.gs-new-*", SearchOption.AllDirectories));
        Expect(SyncAction.None, await laptop.SyncAsync(), title);
    }

    /// <summary>The files a version holds: everything the scanner keeps after the default excludes and safety checks.</summary>
    private static IReadOnlyList<string> SyncedPaths(TestPc pc, GameDefinition game)
    {
        var snapshot = new SnapshotScanner(SensitivePathGuard.ForThisPc(pc.DataDir)).Scan(game, c => c == SaveCategory.Save);
        return snapshot.Files.Select(f => f.Path["saves/".Length..]).ToList();
    }

    private static void Append(TestPc pc, string relative, string text, DateTime modifiedUtc)
    {
        var path = Path.Combine(pc.Folder("game"), relative);
        File.AppendAllText(path, $"\n{text} {Guid.NewGuid()}");
        File.SetLastWriteTimeUtc(path, modifiedUtc);
    }

    private static void AssertSame(TestPc a, TestPc b, IReadOnlyList<string> synced, string title)
    {
        var treeA = a.Tree("game");
        var treeB = b.Tree("game");
        foreach (var path in synced)
        {
            Assert.True(treeA.ContainsKey(path) && treeB.ContainsKey(path), $"{title}: {path} is missing on one PC.");
            Assert.True(treeA[path] == treeB[path], $"{title}: {path} differs between the PCs.");
        }

        Assert.True(treeB.Keys.All(k => treeA.ContainsKey(k)), $"{title}: {b.Name} has files {a.Name} doesn't.");
    }

    private static void Expect(SyncAction expected, IReadOnlyList<GameResult> results, string title)
    {
        var result = Assert.Single(results);
        Assert.True(result.Action == expected, $"{title}: expected {expected}, got {result.Action} ({result.Status}: {result.Message})");
    }
}
