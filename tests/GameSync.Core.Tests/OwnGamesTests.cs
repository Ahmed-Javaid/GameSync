using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.Sessions;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// LIB-13, your own games and folders: Add a game or folder takes a name, a folder and optionally the program that uses
/// it, and syncs it like a game: when that program closes, or with none once the folder has been quiet for 5 minutes.
/// </summary>
public class OwnGamesTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly GameId World = GameId.Parse("minecraft-server-world");

    [Fact]
    public async Task LIB_13_a_folder_of_your_own_syncs_like_a_game_and_rescans_keep_it()
    {
        using var world = new TestWorld();
        var data = SetUp(world);
        var folder = ServerWorld(world);
        var output = new Recorded();

        var added = await OwnGames.AddAsync(data, new OwnGame("Minecraft server world", folder), output, Ct);
        Assert.Equal(World, added.Id);
        Assert.StartsWith("Minecraft server world syncs from now on, once its folder has been quiet for 5 minutes.", added.Sentence, StringComparison.Ordinal);

        using (var engine = Engine.Open(data))
        {
            var entry = engine.Library.All().Single(e => e.Id == World);
            Assert.Equal((true, true, LibraryState.Synced, "Minecraft server world"), (entry.IsOwn, entry.IsOwnFolder, entry.State, entry.DisplayTitle));
            Assert.Equal(folder, Assert.Single(engine.Games.Single(g => g.Id == World).Roots.Values));

            // A rescan finds nothing of it: it stays, installed while its folder is here, with the place it was added with.
            var rescanned = Library.Reconcile(engine.Library.All(), [], DateTime.UtcNow).Single(e => e.Id == World);
            Assert.Equal((true, FoundBy.ByHand), (rescanned.Installed, Assert.Single(rescanned.Proposals).FoundBy));
        }

        // Backed up like any game, and the server's program files in it are never copied (R1).
        await AppActions.BackUpNowAsync(data, World, output, Ct);
        await Uploads.RunAsync(data, null, Ct);
        var version = Assert.Single(await new FolderCloud(world.Cloud).Log.ListAsync(World, Ct));
        Assert.Contains(version.Files, f => f.Path.EndsWith("/level.dat", StringComparison.Ordinal));
        Assert.DoesNotContain(version.Files, f => f.Path.EndsWith(".jar", StringComparison.Ordinal));
        Assert.Empty(output.NeedsYouLines);
    }

    [Fact]
    public async Task LIB_13_its_name_folder_and_program_are_checked_before_anything_is_added()
    {
        using var world = new TestWorld();
        var data = SetUp(world);
        var folder = ServerWorld(world);
        var output = new Recorded();
        await OwnGames.AddAsync(data, new OwnGame("Minecraft server world", folder), output, Ct);

        async Task<string> Refusal(OwnGame game) => (await Assert.ThrowsAsync<UsageException>(() => OwnGames.AddAsync(data, game, output, Ct))).Message;
        Assert.StartsWith("Give it a name", await Refusal(new OwnGame("  ", folder)), StringComparison.Ordinal);
        Assert.StartsWith("There's already a game called Minecraft server world", await Refusal(new OwnGame("minecraft SERVER world!", folder)), StringComparison.Ordinal);
        Assert.Contains("isn't there on this PC", await Refusal(new OwnGame("Gone", Path.Combine(world.Root, "Nowhere"))), StringComparison.Ordinal);
        Assert.Contains("is a file", await Refusal(new OwnGame("A file", Path.Combine(folder, "level.dat"))), StringComparison.Ordinal);
        Assert.Contains("GameSync's own", await Refusal(new OwnGame("Its data", data)), StringComparison.Ordinal);
        Assert.Contains("isn't a program", await Refusal(new OwnGame("With a jar", folder, Path.Combine(folder, "..", "server.jar"))), StringComparison.Ordinal);

        // The name a folder suggests: its own, or with its parent's when it says little by itself.
        Assert.Equal("Minecraft world", OwnGames.SuggestName(@"D:\Servers\Minecraft\world"));
        Assert.Equal("Diablo II", OwnGames.SuggestName(@"C:\Users\You\Saved Games\Diablo II\"));
        using var engine = Engine.Open(data);
        Assert.Single(engine.Library.All());
    }

    [Fact]
    public async Task LIB_13_with_its_program_it_is_a_game_in_its_own_folder_that_syncs_when_the_program_closes()
    {
        using var world = new TestWorld();
        var data = SetUp(world);
        var saves = Path.Combine(world.Root, "Saved Games", "Diablo II");
        Write(Path.Combine(saves, "Hero.d2s"), "level 12");
        var install = Path.Combine(world.Root, "Games", "Diablo II");
        var program = Path.Combine(install, "Game.exe");
        Write(program, "MZ");

        Assert.Equal(install, OwnGames.ProgramFolder(data, program));
        var added = await OwnGames.AddAsync(data, new OwnGame("Diablo II", saves, program), new Recorded(), Ct);
        Assert.Contains("when Game.exe closes", added.Sentence, StringComparison.Ordinal);

        using var engine = Engine.Open(data);
        var entry = engine.Library.All().Single(e => e.Id == added.Id);
        Assert.Equal((true, false, StoreKind.Loose, install), (entry.IsOwn, entry.IsOwnFolder, entry.Store, entry.InstallDir));
        Assert.Equal(program, engine.State.GetSetting(GameLaunch.ProgramKey(added.Id)));
        Assert.Contains(engine.Watched(), w => w.Id == added.Id && w.InstallDir == install);
    }

    [Fact]
    public async Task LIB_13_in_first_run_it_joins_choose_games_ticked_and_syncs_once_setup_is_done()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var folder = ServerWorld(world);

        var added = await OwnGames.AddAsync(data, new OwnGame("Minecraft server world", folder), new Recorded(), Ct);
        Assert.StartsWith("Minecraft server world is in the list, ticked.", added.Sentence, StringComparison.Ordinal);
        Assert.False(File.Exists(AppConfig.PathIn(data)));
        var game = Assert.Single(Assert.Single(FirstRun.Groups(data), g => g.Kind == SetupGroupKind.Sync).Games);
        Assert.Equal<(string, string?)>(("Minecraft server world", "added by you"), (game.Title, game.FoundBy));
        Assert.EndsWith(@"Servers\Minecraft\world", game.SavePath, StringComparison.Ordinal);

        // A rescan in first run (a game folder added) keeps it.
        using (var library = new LibraryStore(data))
        {
            library.SaveAll(Library.Reconcile(library.All(), [], DateTime.UtcNow));
        }

        Assert.Contains(FirstRun.Groups(data).SelectMany(g => g.Games), g => g.Id == World);
        var result = await FirstRun.FinishAsync(data, new SetupChoice { Remote = world.Cloud, Games = [World] }, new NoSchedule(), Ct);
        Assert.Equal(1, result.Syncing);
        using var engine = Engine.Open(data);
        Assert.Contains(engine.Games, g => g.Id == World);
    }

    [Fact]
    public void LIB_13_a_folders_session_starts_at_its_first_change_and_ends_once_it_has_been_quiet_for_5_minutes()
    {
        var quiet = new QuietFolderTracker();
        var t0 = new DateTime(2026, 9, 30, 20, 0, 0, DateTimeKind.Utc);
        var started = Assert.IsType<SessionStarted>(Assert.Single(quiet.Tick(t0.AddSeconds(1), Writes(t0))));
        Assert.Equal(t0 - SessionTracker.StartSlack, started.StartUtc);
        Assert.True(quiet.Active.ContainsKey(World));

        // Autosaves keep it open; five quiet minutes after the last one, it ends there.
        Assert.Empty(quiet.Tick(t0.AddMinutes(3), Writes(t0.AddMinutes(2))));
        Assert.Empty(quiet.Tick(t0.AddMinutes(6), Writes(t0.AddMinutes(2))));
        var ended = Assert.IsType<SessionEnded>(Assert.Single(quiet.Tick(t0.AddMinutes(7), Writes(t0.AddMinutes(2)))));
        Assert.Equal(new SessionInfo(t0 - SessionTracker.StartSlack, t0.AddMinutes(2)), ended.Session);
        Assert.Empty(quiet.Active);

        // The same last change starts nothing again; a new one does, and stopping ends it at its last change.
        Assert.Empty(quiet.Tick(t0.AddMinutes(8), Writes(t0.AddMinutes(2))));
        Assert.IsType<SessionStarted>(Assert.Single(quiet.Tick(t0.AddMinutes(30), Writes(t0.AddMinutes(29)))));
        Assert.Equal(new SessionInfo(t0.AddMinutes(29) - SessionTracker.StartSlack, t0.AddMinutes(29)), Assert.Single(quiet.EndAll()).Session);
        Assert.Empty(quiet.Active);
    }

    [Fact]
    public async Task LIB_13_the_agent_syncs_a_folder_once_it_has_been_quiet_and_its_changes_count_as_made_in_a_session()
    {
        using var world = new TestWorld();
        var data = SetUp(world);
        var folder = ServerWorld(world);
        var said = new Recorded();
        await OwnGames.AddAsync(data, new OwnGame("Minecraft server world", folder), said, Ct);
        var log = new FolderCloud(world.Cloud).Log;

        using var agent = new Agent(data, said);
        await agent.TickAsync(DateTime.UtcNow, Ct);
        Assert.Equal(VersionOrigin.FirstBackup, Assert.Single(await log.ListAsync(World, Ct)).Origin);

        // The server autosaves: the folder is in use, and nothing syncs while it changes.
        var level = Path.Combine(folder, "level.dat");
        File.WriteAllText(level, "autosave at 20:55");
        await Until(() => agent.TickAsync(DateTime.UtcNow, Ct), () => said.Lines.Any(l => l.StartsWith("Minecraft server world: changing since", StringComparison.Ordinal)));
        using (var state = new StateStore(data))
        {
            Assert.NotEmpty(state.GetSetting(RunningGames.QuietOpenKey(World)) ?? "");
        }

        Assert.Single(await log.ListAsync(World, Ct));

        // Five quiet minutes on, it syncs, as made in a session: not held for review.
        await agent.TickAsync(DateTime.UtcNow.AddMinutes(6), Ct);
        var synced = (await log.ListAsync(World, Ct)).MaxBy(v => v.CreatedUtc)!;
        Assert.Equal(VersionOrigin.Session, synced.Origin);
        Assert.True(synced.Session!.Covers(File.GetLastWriteTimeUtc(level), TimeSpan.Zero));
        using (var state = new StateStore(data))
        {
            Assert.Equal(GameStatus.Synced, state.GetState(World).Status);
            Assert.Equal("", state.GetSetting(RunningGames.QuietOpenKey(World)) ?? "");
        }

        Assert.Empty(said.NeedsYouLines);
    }

    [Fact]
    public void LIB_13_a_folder_isnt_played_so_home_and_its_page_say_what_it_is()
    {
        using var world = new TestWorld();
        using var state = new StateStore(Path.Combine(world.Root, "data"));
        var now = new DateTime(2026, 9, 30, 21, 0, 0, DateTimeKind.Local);
        state.AddSession(World, new SessionInfo(now.AddHours(-3).ToUniversalTime(), now.AddHours(-1).ToUniversalTime()));
        var library = new[]
        {
            new LibraryEntry { Id = World, Title = "Minecraft server world", TitleByHand = "Minecraft server world", State = LibraryState.Synced, OwnFolder = @"D:\Servers\Minecraft\world" },
            new LibraryEntry { Id = GameId.Parse("hades"), Title = "Hades", Store = StoreKind.Steam, StoreId = "1145360" },
        };

        var games = Launcher.Games(library, [], state, null, new Dictionary<long, SteamPlay>(), null);
        var folder = games.Single(g => g.Id == World);
        Assert.Equal((true, true, TimeSpan.Zero), (folder.IsOwn, folder.IsFolder, folder.Playtime));

        // Home is about play: the folder isn't the hero or in Jump back in, and its changes aren't play time.
        var home = Launcher.Home(games, state, now);
        Assert.Equal("Hades", home.Hero!.Title);
        Assert.DoesNotContain(home.JumpBackIn, g => g.Id == World);
        Assert.All(home.MonthDays, level => Assert.Equal(0, level));

        // Its page offers Back up now, and says when it last changed, not played.
        var page = new GameViewModel(folder with { Status = GameStatus.Synced }, HomeViewModel.Tile(folder, now), null);
        Assert.Equal(("Back up now", "Added by you"), (page.PrimaryLabel, page.Eyebrow));
        Assert.Equal(["Last changed", "Saves"], page.Stats.Select(s => s.Label));
    }

    [Fact]
    public async Task LIB_13_the_dialog_names_the_game_from_its_folder_and_adds_it_once_it_can()
    {
        using var world = new TestWorld();
        var folder = ServerWorld(world);
        OwnGame? asked = null;
        var closed = 0;
        var look = new NewPlaceLook { Path = folder, Folder = folder, Portable = folder, Files = 142, Bytes = 40_265_318, NewestUtc = DateTime.UtcNow, Programs = 2 };
        var actions = new OwnActions(
            (_, _) => Task.FromResult(look),
            (path, _) => path.EndsWith(".exe", StringComparison.Ordinal) ? Task.FromResult(Path.GetDirectoryName(path)!) : throw new UsageException("That isn't a program."),
            (game, _) =>
            {
                asked = game;
                return Task.FromResult(new OwnGameAdded(World, game.Name, "added"));
            },
            () => closed++);

        var dialog = new AddOwnViewModel(actions);
        Assert.Equal(("Add and sync", false, "Choose its folder first"), (dialog.AddLabel, dialog.CanAdd, dialog.AddTip));
        dialog.LookAt(folder);
        Assert.Equal(("Minecraft world", true), (dialog.Name, dialog.CanAdd));
        Assert.StartsWith("142 files · 38.4 MB · last changed today", dialog.Meta, StringComparison.Ordinal);
        Assert.StartsWith("It holds 2 program files", dialog.ProgramsNote, StringComparison.Ordinal);

        dialog.ChooseProgram(@"D:\Servers\Minecraft\server.jar");
        Assert.Equal(("That isn't a program.", false), (dialog.Error, dialog.HasProgram));
        dialog.Name = "Minecraft server world";
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)dialog.AddCommand).ExecuteAsync(null);
        Assert.Equal(new OwnGame("Minecraft server world", folder, null), asked);
        Assert.Equal(1, closed);

        // Over first run it only adds: nothing syncs until first run ends.
        var setup = new AddOwnViewModel(actions, setup: true);
        Assert.Equal(("Add", "It joins Choose games, ticked. Nothing syncs until you start using GameSync."), (setup.AddLabel, setup.FootNote));
    }

    /// <summary>A data folder set up with a folder as the cloud and no games yet.</summary>
    private static string SetUp(TestWorld world)
    {
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        return data;
    }

    /// <summary>A server's world, with the server's program beside it and one inside it by mistake.</summary>
    private static string ServerWorld(TestWorld world)
    {
        var folder = Path.Combine(world.Root, "Servers", "Minecraft", "world");
        Write(Path.Combine(folder, "level.dat"), "spawn at 0,64,0");
        Write(Path.Combine(folder, "region", "r.0.0.mca"), "chunks");
        Write(Path.Combine(folder, "datapacks", "helper.jar"), "PK");
        Write(Path.Combine(folder, "..", "server.jar"), "PK");
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-1));
        }

        return folder;
    }

    private static IReadOnlyDictionary<GameId, DateTime> Writes(DateTime at) => new Dictionary<GameId, DateTime> { [World] = at };

    private static async Task Until(Func<Task> tick, Func<bool> done)
    {
        for (var i = 0; i < 40 && !done(); i++)
        {
            await Task.Delay(100);
            await tick();
        }

        Assert.True(done());
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text);
    }

    private sealed class NoSchedule : ISetupSchedule
    {
        public string? StartAtSignIn(bool on) => null;

        public string? Daily(TimeOnly? at) => null;
    }

    private sealed class Recorded : IAgentOutput
    {
        public List<string> Lines { get; } = [];

        public List<string> NeedsYouLines { get; } = [];

        public void Say(string line)
        {
            lock (Lines)
            {
                Lines.Add(line);
            }
        }

        public void NeedsYou(string title, string message) => NeedsYouLines.Add($"{title}: {message}");
    }
}
