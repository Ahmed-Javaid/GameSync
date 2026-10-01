using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Host;
using GameSync.UI.Controls;
using GameSync.UI.ViewModels;
using Microsoft.Data.Sqlite;

namespace GameSync.Core.Tests;

/// <summary>
/// The save manager's Versions and Log tabs (MGR-08, MGR-09): every version of every game from every PC with why it was
/// kept, a row opening its game's saves on that version; and everything GameSync did, each line tagged with what it was
/// about, searched, filtered and copied without sign-in tokens.
/// </summary>
public class HistoryTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DeviceInfo Desktop = new(DeviceId.New(), "DESKTOP");

    [Fact]
    public async Task MGR_08_and_MGR_09_the_versions_and_the_log_say_what_happened_to_each_game()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var saves = Path.Combine(world.Root, "Saves");
        string Slot(string game) => Path.Combine(saves, game, "slot.sav");
        Write(Slot("Lantern Keep"), "Lantern Keep, at the start");
        Write(Slot("Quiet Game"), "Quiet Game, at the start");
        new AppConfig { Remote = world.Cloud, Games = [Game("lantern-keep", "Lantern Keep", saves), Game("quiet-game", "Quiet Game", saves)] }.Save(data);

        // A save list already here, so the daily backup doesn't go looking for one online.
        var manifest = Path.Combine(world.Root, "manifest.yaml");
        File.WriteAllText(manifest, "Lantern Keep:\n  files:\n    \"<base>/Saves\":\n      tags: [save]\n");
        new SaveListStore(data).UpdateFromFile(manifest);
        var output = new Quiet();

        // Both games' first backups.
        var first = await SyncPlans.CheckAsync(data, output, Ct);
        await SyncPlans.RunAsync(data, first.Changes, output, Ct);

        // Lantern Keep is played, and syncs after play.
        var at = DateTime.UtcNow;
        Played(data, Slot("Lantern Keep"), "Lantern Keep, floor 2", GameId.Parse("lantern-keep"), at, minutes: 47);
        await SyncPlans.RunAsync(data, (await SyncPlans.CheckAsync(data, output, Ct)).Changes, output, Ct);

        // Quiet Game is played with GameSync closed: the daily backup brings its save in, and Lantern Keep was in sync.
        Played(data, Slot("Quiet Game"), "Quiet Game, chapter 2", GameId.Parse("quiet-game"), at.AddSeconds(5), minutes: 20);
        await Daily.RunAsync(data, output, _ => false, Ct);

        var view = await SaveHistory.ReadVersionsAsync(data, Ct);
        Assert.Equal(
            [("Quiet Game", "Daily backup", KeptFor.Daily), ("Lantern Keep", "After play · 48 min", KeptFor.Play)],
            view.Versions.Take(2).Select(v => (v.Title, v.Why, v.For)));
        Assert.All(view.Versions.Skip(2), v => Assert.Equal(("First backup", KeptFor.First), (v.Why, v.For)));
        Assert.Equal(4, view.Versions.Count);
        Assert.Equal(["Lantern Keep", "Quiet Game"], view.Games.Select(g => g.Title));
        Assert.Equal([view.ThisPc], view.Pcs);
        Assert.Equal((10, true), (view.KeptOnPc, view.HasCloud));
        Assert.All(view.Versions, v => Assert.False(v.Kept));

        // The log: each line tagged, the newest first, and a daily line only for the game that was in sync.
        var log = SaveHistory.ReadLog(data);
        Assert.All(log, e => Assert.NotNull(e.Tag));
        Assert.Equal(("Lantern Keep", EventTags.Daily, "Daily backup: In sync."), (log[0].Title, log[0].Tag, log[0].Message));
        Assert.DoesNotContain(log, e => e.Title == "Quiet Game" && e.Tag == EventTags.Daily);
        Assert.Equal(EventTags.Upload, log.First(e => e.Title == "Quiet Game").Tag);
        Assert.Equal(2, log.Count(e => e.Title == "Lantern Keep" && e.Tag == EventTags.Upload));
    }

    [Fact]
    public void MGR_08_why_a_version_was_kept_says_it_in_the_tab_s_words()
    {
        (string, KeptFor) Why(VersionKind kind, VersionOrigin origin, string? label = null, PinRecord? pin = null, SessionInfo? session = null, bool folder = false)
        {
            var version = Version(kind, origin, label) with { Session = session };
            return SaveHistory.Why(version, pin, folder);
        }

        PinRecord Pin(string label, bool conflict = false, bool named = false) => new(Id(), label, DateTime.UtcNow, Desktop, conflict, named);
        var played = new SessionInfo(DateTime.UtcNow.AddMinutes(-190), DateTime.UtcNow);

        Assert.Equal(("Named: Before Lady Maria", KeptFor.Kept), Why(VersionKind.Normal, VersionOrigin.Session, pin: Pin("Before Lady Maria", named: true)));
        Assert.Equal(("Changed outside play, held for review", KeptFor.Held), Why(VersionKind.Held, VersionOrigin.OutOfSession));
        Assert.Equal(("LAPTOP's save, replaced by DESKTOP's in a conflict", KeptFor.Kept),
            Why(VersionKind.Normal, VersionOrigin.Session, pin: Pin("LAPTOP's save, replaced by DESKTOP's in a conflict", conflict: true)));
        Assert.Equal(("Kept before update to build 42 (29 Sep)", KeptFor.Kept), Why(VersionKind.Kept, VersionOrigin.BeforeUpdate, "before update to build 42 (29 Sep)"));
        Assert.Equal(("Kept before a restore", KeptFor.Kept), Why(VersionKind.Kept, VersionOrigin.KeptBeforeRestore));
        Assert.Equal(("DESKTOP's files before its first sync", KeptFor.Kept), Why(VersionKind.Kept, VersionOrigin.KeptAtFirstSync, "DESKTOP's files before its first sync"));
        Assert.Equal(("After play · 3 h 10 min", KeptFor.Play), Why(VersionKind.Normal, VersionOrigin.Session, session: played));
        Assert.Equal(("After play", KeptFor.Play), Why(VersionKind.Normal, VersionOrigin.Session));
        Assert.Equal(("After 5 quiet minutes", KeptFor.Quiet), Why(VersionKind.Normal, VersionOrigin.Session, session: played, folder: true));
        Assert.Equal(("Daily backup", KeptFor.Daily), Why(VersionKind.Normal, VersionOrigin.Session, Daily.VersionNote, session: played));
        Assert.Equal(("First backup", KeptFor.First), Why(VersionKind.Normal, VersionOrigin.FirstBackup));
        Assert.Equal(("Restored from history", KeptFor.Restored), Why(VersionKind.Normal, VersionOrigin.Restore));
        Assert.Equal(("Held, then kept by you", KeptFor.Other), Why(VersionKind.Normal, VersionOrigin.Approved));
        Assert.Equal(("Chosen over the cloud's save on DESKTOP", KeptFor.Other), Why(VersionKind.Normal, VersionOrigin.Resolve, "chosen over the cloud's save on DESKTOP"));
    }

    [Fact]
    public void MGR_08_the_versions_tab_filters_by_game_and_PC_and_a_row_opens_its_game_s_saves_on_that_version()
    {
        var now = new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Local);
        var lantern = GameId.Parse("lantern-keep");
        var quiet = GameId.Parse("quiet-game");
        var versions = new List<SavedVersion>
        {
            Saved(lantern, "Lantern Keep", now.AddMinutes(-5), "DESKTOP", "After play · 47 min", KeptFor.Play),
            Saved(quiet, "Quiet Game", now.AddMinutes(-10), "LAPTOP", "Named: Before the lighthouse", KeptFor.Kept, named: true, kept: true),
            Saved(lantern, "Lantern Keep", now.AddMinutes(-20), "LAPTOP", "LAPTOP's save, replaced by DESKTOP's in a conflict", KeptFor.Kept, kept: true),
        };
        versions.AddRange(Enumerable.Range(1, 102).Select(i => Saved(lantern, "Lantern Keep", now.AddDays(-2).AddMinutes(-i), "DESKTOP", "After play", KeptFor.Play)));
        var view = new VersionsView(versions, [new HistoryGame(GameId.Parse("hades"), "Hades"), new(lantern, "Lantern Keep"), new(quiet, "Quiet Game")],
            "DESKTOP", ["DESKTOP", "LAPTOP"], 10, HasCloud: true);
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { }) { LoadVersions = _ => Task.FromResult(view) };
        var saves = new SaveManagerViewModel(actions);
        saves.Update([Launcher(lantern, "Lantern Keep"), Launcher(quiet, "Quiet Game")]);

        // The tab reads the versions when it's shown: the newest 100, and the rest behind Show N older.
        saves.Tab = "versions";
        var tab = saves.Versions;
        tab.Show(view, now);
        Assert.True(saves.ShowsVersions);
        Assert.Equal("DESKTOP · 105 versions of 2 games, from DESKTOP and LAPTOP", saves.TopSubtitle);
        Assert.Equal(["105", "1", "2", "105", "28 Sep"], tab.Stats.Select(s => s.Value));
        Assert.Equal((100, "Show 5 older"), (tab.Rows.Count, tab.OlderLabel));
        Assert.Equal(("Lantern Keep", "DESKTOP", "play"), (tab.Rows[0].Game, tab.Rows[0].Pc, tab.Rows[0].Icon));
        Assert.Equal("A row opens its game's saves, where Restore is. Every version is kept: all of them in the cloud, and the last 10 of each game on this PC.", tab.Foot);
        tab.ShowOlderCommand.Execute(null);
        Assert.Equal((105, false), (tab.Rows.Count, tab.HasOlder));

        // A PC, a game, then Named and kept only; the numbers follow the game and PC, not the tick.
        tab.Pc = "LAPTOP";
        Assert.Equal(["Quiet Game", "Lantern Keep"], tab.Rows.Select(r => r.Game));
        tab.Game = lantern.Value;
        Assert.Equal(["LAPTOP's save, replaced by DESKTOP's in a conflict"], tab.Rows.Select(r => r.Why));
        tab.Pc = VersionsViewModel.All;
        tab.KeptOnly = true;
        Assert.Single(tab.Rows);
        Assert.Equal("104", tab.Stats[0].Value);

        // A filter that matches nothing says why.
        tab.Game = "hades";
        Assert.True(tab.IsEmpty);
        Assert.Equal("No version of Hades yet: it hasn't synced since it was added.", tab.EmptyText);
        tab.KeptOnly = false;
        tab.Game = quiet.Value;
        tab.Pc = "DESKTOP";
        Assert.Equal("No version of Quiet Game came from DESKTOP.", tab.EmptyText);

        // A row opens its game's saves with that version picked; Back leads back to the tab.
        tab.Pc = VersionsViewModel.All;
        var named = tab.Rows.Single();
        tab.OpenCommand.Execute(named);
        Assert.Equal((quiet, named.Version.Id), (saves.Game!.Id, saves.Game.Picked));
        Assert.False(saves.ShowsVersions);
        saves.BackCommand.Execute(null);
        Assert.True(saves.ShowsVersions);
    }

    [Fact]
    public void MGR_09_the_log_tab_searches_filters_and_copies_the_lines_shown_without_sign_in_tokens()
    {
        var now = new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Local);
        LogEntry Line(int minutesAgo, string game, string title, string level, string? tag, string message) =>
            new(now.AddMinutes(-minutesAgo).ToUniversalTime(), GameId.Parse(game), title, level, tag, message);
        var entries = new List<LogEntry>
        {
            Line(1, "sts2", "Slay the Spire 2", "info", EventTags.Download, "Newer save from LAPTOP."),
            Line(2, "sekiro", "Sekiro", "warn", EventTags.Conflict, "Changed on DESKTOP and on LAPTOP. Kept DESKTOP's save."),
            Line(3, "sekiro", "Sekiro", "error", EventTags.Cloud, "Drive said no: Bearer ya29.a0AfH6SMBxQx-secret refresh_token=1//0gSecretRefreshTokenValue123"),
            Line(4, "hades", "Hades", "info", EventTags.Session, "Played 47 min."),
            Line(60 * 25, "hades", "Hades", "info", null, "Hades: kept from before tags."),
        };
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { }) { LoadLog = _ => Task.FromResult<IReadOnlyList<LogEntry>>(entries) };
        var saves = new SaveManagerViewModel(actions);
        saves.Tab = "log";
        var log = saves.LogTab;
        log.Show(entries, now);

        // Every game shares the log, so each line says which game first; a line that already does isn't said twice.
        Assert.Equal("Everything GameSync did, per game, newest first", saves.TopSubtitle);
        Assert.Equal(("5 of 5 lines", 5), (log.CountText, log.Lines.Count));
        Assert.Equal(("Today 21:59:00", "download", LogLevel.Ok, "Slay the Spire 2: Newer save from LAPTOP."),
            (log.Lines[0].Time, log.Lines[0].Tag, log.Lines[0].Level, log.Lines[0].Message));
        Assert.Equal(("Yesterday 21:00:00", "info", LogLevel.Info, "Hades: kept from before tags."),
            (log.Lines[^1].Time, log.Lines[^1].Tag, log.Lines[^1].Level, log.Lines[^1].Message));
        Assert.Equal(["All games", "Hades", "Sekiro", "Slay the Spire 2"], log.Games.Select(g => g.Label));

        // What needs you, and what moved; a game; the search matches as the library's does ("sts2").
        log.Showing = "needs";
        Assert.Equal([LogLevel.Warn, LogLevel.Error], log.Lines.Select(l => l.Level));
        log.Showing = "moves";
        Assert.Equal(["download"], log.Lines.Select(l => l.Tag));
        log.Showing = LogViewModel.All;
        log.Game = "hades";
        Assert.Equal(2, log.Lines.Count);
        log.Game = LogViewModel.All;
        log.Search = "sts2";
        Assert.Equal(("1 of 5 lines", "Slay the Spire 2: Newer save from LAPTOP."), (log.CountText, log.Lines.Single().Message));
        log.Search = "nothing like this";
        Assert.True(log.NoLines);
        Assert.Equal("Nothing in the log matches. Try fewer words, or Everything.", log.EmptyText);

        // Copy the log: the lines shown, dated in full, and never a sign-in token.
        log.Search = "sekiro";
        var copied = log.CopyText();
        Assert.Equal(2, copied.Split(Environment.NewLine).Length);
        Assert.StartsWith($"{now.AddMinutes(-2).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}  CONFLICT  Sekiro: Changed on DESKTOP", copied, StringComparison.Ordinal);
        Assert.DoesNotContain("ya29.", copied, StringComparison.Ordinal);
        Assert.DoesNotContain("1//0g", copied, StringComparison.Ordinal);
        Assert.Contains("[sign-in token removed]", copied, StringComparison.Ordinal);
    }

    [Fact]
    public void MGR_09_a_state_db_from_before_tags_opens_and_keeps_its_lines()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        Directory.CreateDirectory(data);
        using (var old = new SqliteConnection($"Data Source={Path.Combine(data, "state.db")};Pooling=False"))
        {
            old.Open();
            using var create = old.CreateCommand();
            create.CommandText = """
                CREATE TABLE events (id INTEGER PRIMARY KEY, at_utc TEXT NOT NULL, game TEXT NOT NULL, level TEXT NOT NULL, message TEXT NOT NULL);
                INSERT INTO events (at_utc, game, level, message) VALUES ('2026-09-28T10:00:00.0000000Z', 'lantern-keep', 'info', 'Changed on this PC.');
                """;
            create.ExecuteNonQuery();
        }

        // Opened by this GameSync, and again, as the app and the command line both do.
        using (var state = new StateStore(data))
        {
            state.Log(GameId.Parse("lantern-keep"), "info", "Played 47 min.", EventTags.Session);
        }

        using var again = new StateStore(data);
        Assert.Equal([("Played 47 min.", EventTags.Session), ("Changed on this PC.", null)],
            again.GetEvents(null, 10).Select(e => (e.Message, e.Tag)));
    }

    [Fact]
    public void MGR_08_the_select_shows_its_choice_and_its_menu_checks_it()
    {
        var select = new GsSelect { Label = "PC", Options = [new("all", "Every PC"), new("DESKTOP", "DESKTOP"), new("LAPTOP", "LAPTOP")], Value = "all" };
        var menu = Assert.IsType<MenuFlyout>(select.Flyout);
        Assert.Equal("Every PC", select.Content);
        Assert.Equal("PC: Every PC", Avalonia.Automation.AutomationProperties.GetName(select));
        Assert.Equal([true, false, false], menu.Items.OfType<MenuItem>().Select(i => i.IsChecked));

        menu.Items.OfType<MenuItem>().Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(("LAPTOP", "LAPTOP"), (select.Value, select.Content));
        Assert.Equal([false, false, true], menu.Items.OfType<MenuItem>().Select(i => i.IsChecked));
    }

    [Fact]
    public async Task KAN_51_restoring_a_named_save_is_one_question_then_the_page_says_it_is_in_place()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var saves = Path.Combine(world.Root, "Saves");
        var slot = Path.Combine(saves, "Lantern Keep", "slot.sav");
        var game = GameId.Parse("lantern-keep");
        Write(slot, "at the lighthouse door");
        new AppConfig { Remote = world.Cloud, Games = [Game("lantern-keep", "Lantern Keep", saves)] }.Save(data);
        var output = new Quiet();
        await SyncPlans.RunAsync(data, (await SyncPlans.CheckAsync(data, output, Ct)).Changes, output, Ct);
        await AppActions.SaveAsAsync(data, game, "Before the lighthouse", output, Ct);
        Played(data, slot, "past the lighthouse", game, DateTime.UtcNow, minutes: 30);
        await SyncPlans.RunAsync(data, (await SyncPlans.CheckAsync(data, output, Ct)).Changes, output, Ct);

        var named = Assert.Single((await GameDetails.ReadAsync(data, game, Ct))!.NamedSaves);
        Assert.False(named.InPlace);

        // The app's Restore, as the page's flyout sends it: done, the files are back, and the page reads it back.
        Assert.Null(await AppActions.RestoreAsync(data, game, named.Version, named.Name, output, Ct));
        Assert.Equal("at the lighthouse door", File.ReadAllText(slot));
        var detail = (await GameDetails.ReadAsync(data, game, Ct))!;
        Assert.True(Assert.Single(detail.NamedSaves).InPlace);
        Assert.Equal("Restored from 'Before the lighthouse'", detail.Versions.Single(v => v.IsCurrent).Note);

        // The page: "Bringing back …" while it runs, then that it's in place; the named save says In place, not Restore.
        var done = new TaskCompletionSource<string?>();
        var asked = new List<(GameId, VersionId, string?)>();
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
        {
            Restore = (g, v, n) =>
            {
                asked.Add((g, v, n));
                return done.Task;
            },
        };
        var page = new GameSavesViewModel(Launcher(game, "Lantern Keep"), actions, new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }));
        page.Show(detail with { NamedSaves = [named] }, DateTime.Now);
        var item = Assert.Single(page.NamedSaves);
        Assert.True(item.CanRestore);
        page.RestoreCommand.Execute(item);
        Assert.Equal([(game, named.Version, "Before the lighthouse")], asked);
        Assert.True(page.Restoring);
        Assert.Equal("Bringing back “Before the lighthouse”…", page.RestoreNote);
        page.RestoreCommand.Execute(item);
        Assert.Single(asked);
        done.SetResult(null);
        Assert.True(page.RestoreDone);
        Assert.StartsWith("“Before the lighthouse” is back in place.", page.RestoreNote);
        page.Show(detail, DateTime.Now);
        Assert.False(Assert.Single(page.NamedSaves).CanRestore);
        Assert.True(page.Versions.Single(v => v.IsCurrent).ShowsNote);

        // When it can't, it says why, in the danger colour.
        var refused = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
        {
            Restore = (_, _, _) => Task.FromResult<string?>("Lantern Keep is running, so restoring 'Before the lighthouse' waits until it closes. Nothing was changed."),
        };
        var busy = new GameSavesViewModel(Launcher(game, "Lantern Keep"), refused, new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }));
        busy.Show(detail, DateTime.Now);
        busy.RestoreCommand.Execute(busy.Versions.First(v => !v.IsCurrent));
        Assert.True(busy.RestoreFailed);
        Assert.False(busy.RestoreDone);
        Assert.StartsWith("Lantern Keep is running", busy.RestoreNote);
    }

    private static void Played(string data, string slot, string text, GameId game, DateTime at, int minutes)
    {
        Write(slot, text);
        File.SetLastWriteTimeUtc(slot, at);
        using var state = new StateStore(data);
        state.AddSession(game, new SessionInfo(at.AddMinutes(-minutes), at.AddMinutes(1)));
    }

    private static SavedVersion Saved(GameId game, string title, DateTime savedLocal, string pc, string why, KeptFor kind, bool named = false, bool kept = false)
    {
        var at = savedLocal.ToUniversalTime();
        return new SavedVersion(game, title, VersionId.New(at, pc), at, at, pc, why, kind, named, kept, 2, 2_202_009);
    }

    private static LauncherGame Launcher(GameId id, string title) => new() { Id = id, Title = title, Syncs = true, Status = GameStatus.Synced };

    private static VersionRecord Version(VersionKind kind, VersionOrigin origin, string? label) => new()
    {
        Id = Id(),
        Game = GameId.Parse("lantern-keep"),
        Kind = kind,
        Origin = origin,
        Device = Desktop,
        CreatedUtc = DateTime.UtcNow,
        Label = label,
        Files = [],
    };

    private static VersionId Id() => VersionId.New(DateTime.UtcNow, "DESKTOP");

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
        public void Say(string line)
        {
        }

        public void NeedsYou(string title, string message)
        {
        }
    }
}
