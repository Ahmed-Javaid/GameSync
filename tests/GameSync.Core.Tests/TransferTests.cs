using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// KAN-80: every wait shows how far it is. An upload or a download says how much of how much from its start, only ever
/// goes forward, and ends saying how it went; the window works out the speed and the time left from it.
/// </summary>
public class TransferTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly GameId Game = GameId.Parse("game");
    private const long MB = 1024 * 1024;

    [Fact]
    public async Task KAN_80_an_upload_counts_all_of_a_games_outbox_from_its_start_and_only_goes_forward()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Options = desktop.Options with { DeferUploads = true };
        desktop.Write("game", "slot1.sav", new string('a', 5000));
        desktop.Write("game", "slot2.sav", new string('b', 7000));
        await desktop.SyncAsync();
        await desktop.Service().SaveAsAsync(Game, "Before the boss", Ct);
        desktop.Write("game", "slot2.sav", new string('c', 9000));
        await desktop.Service().SaveAsAsync(Game, "After the boss", Ct);

        // Two versions sharing slot1.sav, and two names: each file counts once, with every record and every pin.
        var reports = new List<TransferProgress>();
        desktop.Options = desktop.Options with { DeferUploads = false, Progress = new DirectProgress<TransferProgress>(p => { lock (reports) { reports.Add(p); } }) };
        await desktop.Service().UploadAsync(null, Ct);

        var first = reports[0];
        Assert.Equal((0, 0L, TransferDirection.Up), (first.FilesDone, first.BytesDone, first.Direction));
        Assert.Equal(5000 + 7000 + 9000, first.BytesTotal);
        Assert.Equal(3 + 2 + 2, first.FilesTotal);
        Assert.All(reports.Zip(reports.Skip(1)), pair =>
        {
            Assert.True(pair.Second.FilesDone >= pair.First.FilesDone, "The files done never go back.");
            Assert.True(pair.Second.BytesDone >= pair.First.BytesDone, "The bytes done never go back.");
            Assert.Equal((pair.First.FilesTotal, pair.First.BytesTotal), (pair.Second.FilesTotal, pair.Second.BytesTotal));
        });
        Assert.True(reports[^1].Finished);
        Assert.Equal(2, (await new FolderVersionLog(world.Cloud).ListAsync(Game, Ct)).Count);
        Assert.Equal(2, (await new FolderVersionLog(world.Cloud).ListPinsAsync(Game, Ct)).Count);

        // Nothing waits any more, so the next upload says nothing at all.
        reports.Clear();
        await desktop.Service().UploadAsync(null, Ct);
        Assert.Empty(reports);
    }

    [Fact]
    public async Task KAN_80_another_pcs_save_comes_down_whole_before_anything_is_written_saying_how_far_it_is()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        desktop.Write("game", "slot1.sav", new string('a', 4000));
        desktop.Write("game", "slot2.sav", new string('b', 6000));
        desktop.Write("game", "copy.sav", new string('a', 4000));
        await desktop.SyncAsync();

        var reports = new List<TransferProgress>();
        laptop.Options = laptop.Options with { Progress = new DirectProgress<TransferProgress>(p => { lock (reports) { reports.Add(p); } }) };
        await laptop.SyncAsync();

        Assert.Equal(new string('b', 6000), laptop.Read("game", "slot2.sav"));
        var down = reports.Where(r => r.Direction == TransferDirection.Down).ToList();
        Assert.NotEmpty(down);
        Assert.Equal((2, 10000L), (down[0].FilesTotal, down[0].BytesTotal));
        Assert.Equal((0, 0L), (down[0].FilesDone, down[0].BytesDone));
        Assert.True(down[^1].Finished);
    }

    [Fact]
    public void KAN_80_the_window_hears_how_each_transfer_ended_and_not_every_file_on_the_way()
    {
        var said = new Said();
        var relay = new TransferRelay(said) { TitleOf = _ => "Lantern Keep" };
        relay.Report(new TransferProgress(Game, 0, 10, 0, 2 * MB));
        relay.Report(new TransferProgress(Game, 5, 10, MB, 2 * MB));
        relay.Report(new TransferProgress(Game, 10, 10, 2 * MB, 2 * MB));
        relay.End([new GameResult(Game, "Lantern Keep", SyncAction.Upload, GameStatus.Synced, "Uploaded to the cloud.")], null);

        // The first report and the last went at once; the one between them came within a tenth of a second, so it waited.
        Assert.Equal([TransferState.Running, TransferState.Running, TransferState.Done], said.Transfers.Select(t => t.State));
        Assert.True(said.Transfers[1].Progress.Finished);
        Assert.Equal("2 MB in under a second", said.Transfers[2].Note);
        Assert.All(said.Transfers, t => Assert.Equal("Lantern Keep", t.Title));

        // The cloud couldn't be reached: it says so, and when it's tried again.
        said.Transfers.Clear();
        relay.Report(new TransferProgress(Game, 0, 4, 0, MB));
        relay.End([new GameResult(Game, "Lantern Keep", SyncAction.None, GameStatus.UploadPending, "Kept on this PC; the upload waits: Google Drive can't be reached")
            { CloudProblem = CloudErrorKind.Offline }], new DateTime(2026, 10, 2, 21, 14, 0));
        Assert.Equal(TransferState.Failed, said.Transfers[^1].State);
        Assert.Equal("Google Drive can't be reached. It tries again at 21:14; your saves are safe on this PC.", said.Transfers[^1].Note);

        // A game that syncs started: what was under way pauses, and says why.
        said.Transfers.Clear();
        relay.Report(new TransferProgress(Game, 1, 4, MB / 4, MB));
        relay.Stop(TransferState.Paused, "Paused while you play Lantern Keep; it goes on once you quit.");
        Assert.Equal((TransferState.Paused, "Paused while you play Lantern Keep; it goes on once you quit."), (said.Transfers[^1].State, said.Transfers[^1].Note));

        // Nothing open: nothing more to say.
        said.Transfers.Clear();
        relay.End([], null);
        Assert.Empty(said.Transfers);
    }

    [Fact]
    public void KAN_80_a_transfer_says_how_far_how_fast_and_how_long_then_how_it_ended_for_a_few_seconds()
    {
        var view = new TransferView();
        var t0 = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        view.Apply(new TransferUpdate(new TransferProgress(Game, 0, 100, 0, 100 * MB), TransferState.Running), "your Google Drive", t0);
        Assert.True(view.IsShown);
        Assert.Equal(("Uploading to your Google Drive", "running"), (view.Title, view.State));
        Assert.Equal("0 B of 100 MB · 0 of 100 files", view.Detail);
        Assert.Null(view.Speed);

        // Half of it in 2 seconds: 25 MB a second, and as long again to go.
        view.Apply(new TransferUpdate(new TransferProgress(Game, 50, 100, 50 * MB, 100 * MB), TransferState.Running), "your Google Drive", t0.AddSeconds(2));
        Assert.Equal(50, view.Value!.Value, 3);
        Assert.Equal("50 of 100 MB · 50 of 100 files", view.Detail);
        Assert.Equal("25 MB/s", view.Speed);
        Assert.Equal("about 2 s left", view.Left);
        Assert.True(view.IsRunning);
        Assert.Equal("uploading 50%", view.Short);

        view.Apply(new TransferUpdate(new TransferProgress(Game, 100, 100, 100 * MB, 100 * MB), TransferState.Done, "100 MB in 4 s"), "your Google Drive", t0.AddSeconds(4));
        Assert.Equal(("Uploaded to your Google Drive", "done", "100 MB in 4 s"), (view.Title, view.State, view.Note));
        Assert.False(view.IsRunning);
        Assert.False(view.Expire(t0.AddSeconds(5)));
        Assert.True(view.IsShown);
        Assert.True(view.Expire(t0.AddSeconds(11)));
        Assert.False(view.IsShown);

        // A download, from another PC's save, says so; a failed one keeps its note until the next try.
        view.Apply(new TransferUpdate(new TransferProgress(Game, 0, 2, 0, MB, TransferDirection.Down), TransferState.Running), "your Google Drive", t0.AddSeconds(20));
        Assert.Equal("Downloading from your Google Drive", view.Title);
        view.Apply(new TransferUpdate(new TransferProgress(Game, 1, 2, MB / 2, MB, TransferDirection.Down), TransferState.Failed, "Google Drive can't be reached."),
            "your Google Drive", t0.AddSeconds(21));
        Assert.Equal(("The download didn't finish", "failed"), (view.Title, view.State));
        Assert.False(view.Expire(t0.AddMinutes(5)));
    }

    [Fact]
    public void KAN_80_sizes_read_as_one_amount_of_another()
    {
        Assert.Equal("42.1 of 113 MB", TransferView.OfSize((long)(42.1 * MB), 113 * MB));
        Assert.Equal("812 KB of 1.2 GB", TransferView.OfSize(812 * 1024, (long)(1.2 * 1024 * MB)));
        Assert.Equal("about 12 s left", TransferView.LeftOf(TimeSpan.FromSeconds(11.2)));
        Assert.Equal("about 3 min left", TransferView.LeftOf(TimeSpan.FromMinutes(3.2)));
        Assert.Equal("a moment left", TransferView.LeftOf(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task KAN_80_bringing_kept_copies_in_says_each_one_read_then_each_one_kept()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        var cusa = Path.Combine(world.Root, "DESKTOP", "CUSA00207");
        var live = desktop.AddGame("bloodborne", savesFolder: Path.Combine(cusa, "SPRJ0005")).Roots["saves"];
        Write(live, "userdata0000", "live");
        await desktop.SyncAsync();
        Write(Path.Combine(cusa, "Before Orphan", "SPRJ0005"), "userdata0000", new string('o', 3000));
        Write(Path.Combine(cusa, "After maria", "SPRJ0005"), "userdata0000", new string('m', 2000));
        Write(Path.Combine(cusa, "Same as maria", "SPRJ0005"), "userdata0000", new string('m', 2000));

        var reports = new List<WorkProgress>();
        await desktop.Service().ImportSavesAsync(GameId.Parse("bloodborne"), cusa, apply: true, null, Ct, new DirectProgress<WorkProgress>(reports.Add));

        var reading = reports.Where(r => r.Step == "reading").ToList();
        Assert.Equal(Enumerable.Range(0, 4), reading.Select(r => r.Done));
        Assert.All(reading, r => Assert.Equal(3, r.Total));
        Assert.Equal(7000, reading[^1].BytesDone);
        var keeping = reports.Where(r => r.Step == "keeping").ToList();
        Assert.Equal([0, 1, 2], keeping.Select(r => r.Done));
        Assert.All(keeping, r => Assert.Equal((2, 5000L), (r.Total, r.BytesTotal)));
        Assert.Equal(5000, keeping[^1].BytesDone);
        Assert.True(reports.IndexOf(reading[^1]) < reports.IndexOf(keeping[0]), "Every copy is read before the first is kept.");
    }

    private static void Write(string folder, string relative, string content)
    {
        var path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class Said : IAgentOutput
    {
        public List<TransferUpdate> Transfers { get; } = [];

        public void Say(string line)
        {
        }

        public void NeedsYou(string title, string message)
        {
        }

        void IAgentOutput.Transfer(TransferUpdate update) => Transfers.Add(update);
    }
}
