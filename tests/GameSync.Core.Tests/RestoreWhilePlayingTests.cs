using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;

namespace GameSync.Core.Tests;

/// <summary>
/// KAN-91 (the owner, 2 Oct 2026): a restore goes ahead while the game runs, keeping the save there first; a file the game
/// holds stops it with nothing changed and nothing left behind; Back up now works while playing, as New named save does.
/// </summary>
public class RestoreWhilePlayingTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly GameId Game = GameId.Parse("game");

    [Fact]
    public async Task KAN_91_a_restore_goes_ahead_while_the_game_runs_and_the_save_there_is_kept_first()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "before the boss");
        await desktop.SyncAsync();
        await desktop.Service().SaveAsAsync(Game, "Before the boss", Ct);
        desktop.Write("game", "slot.sav", "after the boss");
        desktop.Played("game");
        await desktop.SyncAsync();

        // Playing again, the save has moved on; the person brings the named save back from the game's menu.
        desktop.Running.Add(Game);
        desktop.Write("game", "slot.sav", "lost to the next boss");
        await desktop.Service().RestoreNamedAsync(Game, "Before the boss", Ct);

        Assert.Equal("before the boss", desktop.Read("game", "slot.sav"));
        var history = await desktop.Service().HistoryAsync(Game, Ct);
        Assert.Contains(history, v => v.Version.Origin == VersionOrigin.KeptBeforeRestore && v.Version.Files.Single().Size == "lost to the next boss".Length);
        Assert.Contains(history, v => v.Version.Origin == VersionOrigin.Restore);
        Assert.Empty(Directory.EnumerateFiles(desktop.Folder("game"), "*.gs-new-*"));
    }

    [Fact]
    public async Task KAN_91_a_file_the_game_holds_stops_the_restore_with_nothing_changed_and_nothing_left_behind()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "before the boss");
        await desktop.SyncAsync();
        await desktop.Service().SaveAsAsync(Game, "Before the boss", Ct);
        var slot = desktop.Write("game", "slot.sav", "after the boss");
        desktop.Played("game");
        await desktop.SyncAsync();
        desktop.Running.Add(Game);
        var versions = (await desktop.Service().HistoryAsync(Game, Ct)).Count;

        // The game holds its save open: it can be read, but not replaced.
        InvalidOperationException refused;
        using (new FileStream(slot, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            refused = await Assert.ThrowsAsync<InvalidOperationException>(() => desktop.Service().RestoreNamedAsync(Game, "Before the boss", Ct));
        }

        Assert.Equal("game has saves\\slot.sav open, so nothing was restored and its save is as it was. Go back to the game's main menu, or quit it, then try again.",
            refused.Message);
        Assert.Equal("after the boss", desktop.Read("game", "slot.sav"));
        Assert.Empty(Directory.EnumerateFiles(desktop.Folder("game"), "*.gs-new-*"));
        Assert.False(Directory.Exists(RestoreJournal.Folder(desktop.DataDir)) && Directory.EnumerateFiles(RestoreJournal.Folder(desktop.DataDir)).Any());

        // No "Restored from" version was left for a later sync to bring in by itself, or to fork the history with.
        Assert.Equal(versions, (await desktop.Service().HistoryAsync(Game, Ct)).Count);
        desktop.Running.Clear();
        var after = Assert.Single(await desktop.SyncAsync());
        Assert.NotEqual(GameStatus.Conflict, after.Status);
        Assert.Equal("after the boss", desktop.Read("game", "slot.sav"));
        Assert.DoesNotContain(await desktop.Service().HistoryAsync(Game, Ct), v => v.Version.Origin == VersionOrigin.Restore);
    }

    [Fact]
    public void KAN_91_a_swap_stopped_part_way_puts_back_everything_it_had_moved()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "gamesync-tests", Guid.NewGuid().ToString("N")[..12])).FullName;
        try
        {
            string Put(string name, string text)
            {
                var path = Path.Combine(folder, name);
                File.WriteAllText(path, text);
                return path;
            }

            var a = Put("a.sav", "old a");
            Put("a.sav.new", "new a");
            var added = Path.Combine(folder, "added.sav");
            Put("added.sav.new", "new added");
            var b = Put("b.sav", "old b");
            Put("b.sav.new", "new b");
            var aside = Path.Combine(folder, "aside");
            var journal = new RestoreJournal
            {
                Id = "test",
                Game = Game,
                Target = new VersionRecord
                {
                    Id = VersionId.Parse("v1"),
                    Game = Game,
                    Kind = VersionKind.Normal,
                    Origin = VersionOrigin.Restore,
                    Device = new DeviceInfo(DeviceId.Parse("d-desktop"), "DESKTOP"),
                    CreatedUtc = DateTime.UtcNow,
                    Files = [],
                },
                Ops =
                [
                    new JournalOp("replace", a, a + ".new", Path.Combine(aside, "0-a.sav")),
                    new JournalOp("replace", added, added + ".new", Path.Combine(aside, "1-added.sav")),
                    new JournalOp("replace", b, b + ".new", Path.Combine(aside, "2-b.sav")),
                ],
                Ready = true,
            };

            // b.sav was opened a moment ago, after the check: the swap stops there and puts a.sav and added.sav back.
            using (new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var stopped = Assert.Throws<SwapStoppedException>(() => journal.Commit(undoOnFailure: true));
                Assert.True(stopped.Undone);
                Assert.Equal(b, stopped.Target);
            }

            Assert.Equal(("old a", "new a"), (File.ReadAllText(a), File.ReadAllText(a + ".new")));
            Assert.False(File.Exists(added));
            Assert.Equal("new added", File.ReadAllText(added + ".new"));
            Assert.Equal(("old b", "new b"), (File.ReadAllText(b), File.ReadAllText(b + ".new")));

            // Once nothing holds it, the same journal finishes the swap, as a restart's recovery would.
            journal.Commit();
            Assert.Equal(("new a", "new added", "new b"), (File.ReadAllText(a), File.ReadAllText(added), File.ReadAllText(b)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task KAN_91_back_up_now_works_while_playing_and_its_upload_waits_until_the_game_closes()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "chapter 1");
        await desktop.SyncAsync();
        desktop.Running.Add(Game);
        desktop.Write("game", "slot.sav", "chapter 2");

        var backedUp = Assert.Single(await desktop.Service().BackupNowAsync(Game, Ct));

        Assert.Equal(SyncAction.Upload, backedUp.Action);
        Assert.Contains(await desktop.Service().HistoryAsync(Game, Ct), v => v.Version.Files.Single().Size == "chapter 2".Length);
        Assert.Single(await Cloud.VersionsAsync(world, "game"));

        // Quitting, the session's sync sends it.
        desktop.Running.Clear();
        await desktop.SyncAsync();
        Assert.Equal(2, (await Cloud.VersionsAsync(world, "game")).Count);
    }
}
