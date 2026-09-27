using System.Diagnostics;
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
/// The milestones' "done when": every decision and crash scenario (<see cref="FullScenario"/>) on copies of real
/// saves, through the cloud folder and through the in-memory Drive. The source folder is only ever read.
/// </summary>
public class RealSavesTests(ITestOutputHelper output)
{
    [RealSavesFact]
    public Task Every_decision_and_crash_scenario_holds_on_copies_of_real_saves() => RunAllAsync(drive: false);

    [RealSavesFact]
    public Task Every_decision_and_crash_scenario_holds_on_copies_of_real_saves_over_Drive() => RunAllAsync(drive: true);

    private async Task RunAllAsync(bool drive)
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
                await FullScenario.RunAsync(folder, RealSaves.SaveFiles(folder), () => new TestWorld(drive));
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
}
