using System.Diagnostics;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Host;

namespace GameSync.Core.Tests;

/// <summary>
/// KAN-88: what the person does in the app (a named save, a restore) is done on this PC within 2 seconds, never waiting
/// on an upload to Google Drive, and the upload after it sends what they did.
/// </summary>
public class UploadTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly GameId Lantern = GameId.Parse("lantern-keep");

    [Fact]
    public async Task KAN_88_a_named_save_and_a_restore_finish_within_2_seconds_while_an_upload_holds_its_lock()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var saves = Path.Combine(world.Root, "Saves");
        var slot = Path.Combine(saves, "Lantern Keep", "slot.sav");
        Write(slot, "at the lighthouse door");
        new AppConfig { Remote = world.Cloud, Games = [Game("lantern-keep", "Lantern Keep", saves)] }.Save(data);
        var output = new Quiet();
        await SyncPlans.RunAsync(data, (await SyncPlans.CheckAsync(data, output, Ct)).Changes, output, Ct);
        await Uploads.RunAsync(data, null, Ct);
        var cloud = new FolderCloud(world.Cloud).Log;
        Assert.Single(await cloud.ListAsync(Lantern, Ct));

        // A long upload to Google Drive is under way: it holds the upload lock throughout.
        using (await EngineLock.AcquireUploadAsync(data, null, Ct))
        {
            var watch = Stopwatch.StartNew();
            Assert.Null(await AppActions.SaveAsAsync(data, Lantern, "Before the lighthouse", output, Ct));
            Write(slot, "past the lighthouse");
            var named = Assert.Single((await GameDetails.ReadAsync(data, Lantern, Ct))!.NamedSaves);
            Assert.Null(await AppActions.RestoreAsync(data, Lantern, named.Version, named.Name, output, Ct));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"The named save and the restore took {watch.Elapsed.TotalSeconds:0.0} s.");

            // Both done here, and nothing went up meanwhile: the upload under way has the lock.
            Assert.Equal("at the lighthouse door", File.ReadAllText(slot));
            Assert.Single(await cloud.ListAsync(Lantern, Ct));
            Assert.Empty(await cloud.ListPinsAsync(Lantern, Ct));
        }

        // The upload after sends all of it: the save kept before the restore, the restored one, and the name.
        await Uploads.RunAsync(data, null, Ct);
        Assert.Equal(3, (await cloud.ListAsync(Lantern, Ct)).Count);
        Assert.Equal("Before the lighthouse", Assert.Single(await cloud.ListPinsAsync(Lantern, Ct)).Label);
        using var state = new StateStore(data);
        Assert.Equal(GameStatus.Synced, state.GetState(Lantern).Status);
        Assert.Empty(output.NeedsYouLines);
    }

    [Fact]
    public async Task KAN_88_a_named_save_goes_on_with_what_this_pc_knows_when_the_cloud_is_slow_to_answer()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        var game = GameId.Parse("game");
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "start");
        await desktop.SyncAsync();

        // Google Drive takes half a minute to say what's new; the person's named save waits half a second for it.
        desktop.Slow = call => call is "log.ids" or "log.pins" ? TimeSpan.FromSeconds(30) : TimeSpan.Zero;
        desktop.Options = desktop.Options with { DeferUploads = true, PullBudget = TimeSpan.FromMilliseconds(500) };
        var watch = Stopwatch.StartNew();
        await desktop.Service().SaveAsAsync(game, "Before the boss", Ct);
        Assert.Equal("Before the boss", Assert.Single(await desktop.Service().NamedSavesAsync(game, Ct)).Name);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"The named save took {watch.Elapsed.TotalSeconds:0.0} s.");

        // Once the cloud answers, the next sync sends it.
        desktop.Slow = null;
        desktop.Options = desktop.Options with { DeferUploads = false, PullBudget = null };
        await desktop.SyncAsync();
        Assert.Equal("Before the boss", Assert.Single(await new FolderVersionLog(world.Cloud).ListPinsAsync(game, Ct)).Label);
    }

    [Fact]
    public async Task KAN_88_a_look_at_google_drive_reads_only_the_pins_that_changed_since_they_were_last_read()
    {
        using var world = new TestWorld(drive: true);
        using var desktop = world.Pc("DESKTOP");
        var game = GameId.Parse("game");
        desktop.AddGame("game");
        for (var i = 0; i < 5; i++)
        {
            desktop.Write("game", "slot.sav", $"save {i}");
            await desktop.Service().SaveAsAsync(game, $"Save {i}", Ct);
        }

        // The first look reads the pins; every look after reads none of them, each a new connection to Drive as the app's are.
        await desktop.Service().NamedSavesAsync(game, Ct);
        var downloads = 0;
        world.Drive!.Fault = call =>
        {
            if (call == "download")
            {
                Interlocked.Increment(ref downloads);
            }

            return null;
        };
        Assert.Equal(5, (await desktop.Service().NamedSavesAsync(game, Ct)).Count);
        Assert.Equal(0, downloads);

        // A rename writes one pin again, and the next look reads that one alone.
        await desktop.Service().RenameSaveAsync(game, "Save 2", "Before the boss", Ct);
        downloads = 0;
        Assert.Contains("Before the boss", (await desktop.Service().NamedSavesAsync(game, Ct)).Select(s => s.Name));
        Assert.Equal(1, downloads);
    }

    private static GameDefinition Game(string id, string title, string saves) => new()
    {
        Id = GameId.Parse(id),
        Title = title,
        Roots = new Dictionary<string, string> { ["saves"] = Path.Combine(saves, title) },
        Rules = [new SaveRule { Root = "saves" }],
    };

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private sealed class Quiet : IAgentOutput
    {
        public List<string> NeedsYouLines { get; } = [];

        public void Say(string line)
        {
        }

        public void NeedsYou(string title, string message) => NeedsYouLines.Add($"{title}: {message}");
    }
}
