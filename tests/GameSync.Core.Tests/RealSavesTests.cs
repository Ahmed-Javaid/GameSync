using System.Diagnostics;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Scanning;
using GameSync.Core.Sync;
using Xunit.Abstractions;

namespace GameSync.Core.Tests;

/// <summary>Skips unless GAMESYNC_TESTDATA points at a folder of real game saves, such as a Ludusavi backup folder.</summary>
public sealed class RealSavesFactAttribute : FactAttribute
{
    public RealSavesFactAttribute()
    {
        if (RealSaves.Folder is null)
        {
            Skip = "Set GAMESYNC_TESTDATA to a folder of real saves (one subfolder per game) to run this.";
        }
    }
}

internal static class RealSaves
{
    private const long Budget = 200L * 1024 * 1024;

    public static string? Folder =>
        Environment.GetEnvironmentVariable("GAMESYNC_TESTDATA") is { Length: > 0 } folder && Directory.Exists(folder) ? folder : null;

    /// <summary>
    /// A spread of games within 200 MB: the one with the most files, then the biggest, then the smallest, up to 8.
    /// GAMESYNC_TESTDATA_ALL=1 takes every game.
    /// </summary>
    public static IReadOnlyList<(string Folder, int Files, long Bytes)> PickGames()
    {
        var all = Directory.EnumerateDirectories(Folder!)
            .Select(d => (Folder: d, Files: SaveFiles(d).Select(f => new FileInfo(f)).ToList()))
            .Where(g => g.Files.Count > 0)
            .Select(g => (g.Folder, g.Files.Count, g.Files.Sum(f => f.Length)))
            .ToList();
        if (Environment.GetEnvironmentVariable("GAMESYNC_TESTDATA_ALL") == "1")
        {
            return all;
        }

        var picked = new List<(string, int, long)>();
        long used = 0;
        var order = all.OrderByDescending(g => g.Item2).Take(1)
            .Concat(all.OrderByDescending(g => g.Item3))
            .Concat(all.OrderBy(g => g.Item3));
        foreach (var game in order)
        {
            if (picked.Count < 8 && !picked.Contains(game) && used + game.Item3 <= Budget)
            {
                picked.Add(game);
                used += game.Item3;
            }
        }

        return picked;
    }

    /// <summary>Game folders with no file saves: empty, or only a registry export (registry saves come later).</summary>
    public static IEnumerable<string> GamesWithoutFiles() =>
        Directory.EnumerateDirectories(Folder!).Where(d => !SaveFiles(d).Any());

    /// <summary>
    /// Ludusavi keeps its own files next to the saves: its mapping and its export of registry saves. Those aren't file
    /// saves, and a .reg file is blocked anyway (R1).
    /// </summary>
    public static IEnumerable<string> SaveFiles(string gameFolder) =>
        Directory.EnumerateFiles(gameFolder, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) is not ("mapping.yaml" or "registry.yaml" or "registry.reg"));
}

/// <summary>
/// Milestone 1's "done when": every decision and crash scenario, run on copies of real saves. The source folder is
/// only ever read; everything happens on copies under %TEMP%.
/// </summary>
public class RealSavesTests(ITestOutputHelper output)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [RealSavesFact]
    public async Task Every_decision_and_crash_scenario_holds_on_copies_of_real_saves()
    {
        var games = RealSaves.PickGames();
        Assert.NotEmpty(games);
        foreach (var folder in RealSaves.GamesWithoutFiles())
        {
            output.WriteLine($"{Path.GetFileName(folder),-48} skipped: no file saves (empty, or registry only)");
        }

        // Every game runs even after one fails, so a single run shows them all.
        var failures = new List<string>();
        foreach (var (folder, fileCount, bytes) in games)
        {
            var clock = Stopwatch.StartNew();
            var name = Path.GetFileName(folder);
            var line = $"{name,-48} {fileCount,5} files {bytes / 1024.0 / 1024,8:0.0} MB";
            try
            {
                await RunScenarioAsync(folder);
                output.WriteLine($"{line}  passed in {clock.Elapsed.TotalSeconds:0.0} s");
            }
            catch (Exception e)
            {
                var message = e.Message.StartsWith(name, StringComparison.Ordinal) ? e.Message : $"{name}: {e.GetType().Name}: {e.Message}";
                failures.Add(message);
                output.WriteLine($"{line}  FAILED: {message}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of {games.Count} games failed:\n{string.Join('\n', failures)}");
    }

    private async Task RunScenarioAsync(string source)
    {
        var title = Path.GetFileName(source);
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        var game = desktop.AddGame("game");
        laptop.AddGame("game");
        foreach (var file in RealSaves.SaveFiles(source))
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
