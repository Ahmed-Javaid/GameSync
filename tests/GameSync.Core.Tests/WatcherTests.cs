using System.Diagnostics;
using GameSync.Core.Model;
using GameSync.Core.Sessions;
using GameSync.Windows;

namespace GameSync.Core.Tests;

/// <summary>PLAY-05: a game is noticed from its processes, with the least access (design.md → Sessions and launching).</summary>
public class WatcherTests
{
    private static readonly GameId Terraria = GameId.Parse("terraria");

    [Fact]
    public async Task PLAY_05_a_game_running_from_its_install_folder_is_found_and_the_same_name_elsewhere_is_not()
    {
        using var world = new TestWorld();
        var installed = FakeGames.Install(Path.Combine(world.Root, "installs", "Terraria"), "Terraria");
        var elsewhere = FakeGames.Install(Path.Combine(world.Root, "downloads", "Terraria"), "Terraria");
        var programs = GamePrograms.For(Terraria, Path.GetDirectoryName(installed)!);
        var before = DateTime.UtcNow.AddSeconds(-1);
        using var game = FakeGames.Run(installed, "--run", "20");
        using var impostor = FakeGames.Run(elsewhere, "--run", "20");
        try
        {
            var watcher = new ProcessWatcher();
            var found = await Eventually(() => watcher.Find([programs]));

            var process = Assert.Single(found);
            Assert.Equal((Terraria, game.Id), (process.Game, process.ProcessId));
            Assert.InRange(process.StartedUtc, before, DateTime.UtcNow);
            Assert.Single(watcher.Find([programs]));
        }
        finally
        {
            game.Kill();
            impostor.Kill();
        }
    }

    [Fact]
    public void Launchers_crash_reporters_updaters_and_anti_cheat_dont_count_as_playing()
    {
        using var world = new TestWorld();
        var folder = Path.Combine(world.Root, "Some Game");
        foreach (var name in new[] { "Game.exe", "bin/Game-Win64-Shipping.exe", "GameLauncher.exe", "UnityCrashHandler64.exe", "CrashReportClient.exe",
            "EasyAntiCheat/EasyAntiCheat_EOS_Setup.exe", "BEService_x64.exe", "unins000.exe", "Updater.exe", "_CommonRedist/vc_redist.x64.exe" })
        {
            var path = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [0x4D, 0x5A]);
        }

        var programs = GamePrograms.For(GameId.Parse("some-game"), folder);

        Assert.Equal(["Game-Win64-Shipping.exe", "Game.exe"], programs.Names.Order(StringComparer.Ordinal));
        Assert.True(programs.Owns(Path.Combine(folder, "bin", "Game-Win64-Shipping.exe")));
        Assert.False(programs.Owns(Path.Combine(world.Root, "Game.exe")));
    }

    private static async Task<IReadOnlyList<GameProcess>> Eventually(Func<IReadOnlyList<GameProcess>> find)
    {
        for (var i = 0; i < 50; i++)
        {
            if (find() is { Count: > 0 } found)
            {
                return found;
            }

            await Task.Delay(100);
        }

        return [];
    }
}

/// <summary>The fake game (tests/GameSync.FakeGame), copied into a folder under a game's name so it runs as that game.</summary>
public static class FakeGames
{
    private static readonly string[] Files = ["FakeGame.dll", "FakeGame.runtimeconfig.json", "FakeGame.deps.json"];

    /// <summary>The fake game's program as <paramref name="folder"/>\<paramref name="name"/>.exe.</summary>
    public static string Install(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, $"{name}.exe");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "FakeGame.exe"), exe, overwrite: true);
        foreach (var file in Files)
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(folder, file), overwrite: true);
        }

        return exe;
    }

    public static Process Run(string exe, params string[] arguments)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return Process.Start(start) ?? throw new InvalidOperationException($"{exe} didn't start.");
    }
}
