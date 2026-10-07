using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Host;

namespace GameSync.Core.Tests;

/// <summary>LIB-12: the library rescans by itself when a game appears, so it's watched before it's played.</summary>
public class LibraryWatchTests
{
    [Fact]
    public async Task LIB_12_a_game_copied_into_a_game_folder_is_found_without_a_restart_and_waits_while_a_game_that_syncs_plays()
    {
        // The owner's Sons of the Forest, 3 Oct 2026: copied into G:\ at 01:03 and played for hours, unseen, as nothing
        // had scanned since 30 Sep.
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        var games = Directory.CreateDirectory(Path.Combine(world.Root, "G")).FullName;
        var rescans = 0;
        var busy = true;
        var found = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var said = new List<string>();
        var output = new Said(said);
        using var watch = new LibraryWatch(data, output, () => busy, added => found.TrySetResult(added))
        {
            AfterStart = TimeSpan.FromHours(1),
            AfterChange = TimeSpan.FromMilliseconds(200),
            WhileBusy = TimeSpan.FromMilliseconds(300),
            Folders = () => [games],
            Rescan = _ =>
            {
                Interlocked.Increment(ref rescans);
                IReadOnlyList<LibraryEntry> entries = [new LibraryEntry
                {
                    Id = GameId.Parse("sons-of-the-forest"),
                    Title = "Sons of the Forest",
                    FirstSeenUtc = DateTime.UtcNow,
                    Installed = true,
                    InstallDir = Path.Combine(games, "Sons Of The Forest"),
                }];
                return Task.FromResult(entries);
            },
        };
        watch.Start();

        Directory.CreateDirectory(Path.Combine(games, "Sons Of The Forest"));

        // A game that syncs is playing: nothing reads the disk for GameSync meanwhile (BG-08).
        await Task.Delay(800);
        Assert.Equal(0, rescans);
        busy = false;

        var added = await found.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(["Sons of the Forest"], added);
        Assert.Contains("Sons of the Forest: found installed on this PC; GameSync watches it now.", said);
        Assert.Equal(1, rescans);
    }

    private sealed class Said(List<string> lines) : IAgentOutput
    {
        public void Say(string line)
        {
            lock (lines)
            {
                lines.Add(line);
            }
        }

        public void NeedsYou(string title, string message) => Say($"! {title}: {message}");
    }
}
