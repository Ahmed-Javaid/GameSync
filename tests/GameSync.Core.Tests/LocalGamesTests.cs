using GameSync.Core.Art;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// The owner's library wishes of 29 Sep 2026: the Installed and Local views (LIB-22), a game found by its saves located
/// by its program (LIB-24), the game playing now on Home whether it syncs or not (PLAY-12), your own art (ART-06), the
/// space the backups take (MGR-03), and Back from a page the rail reached (LIB-21).
/// </summary>
public class LocalGamesTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 21, 30, 0, DateTimeKind.Local);
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public void PLAY_12_the_game_playing_now_is_Homes_hero_whether_its_saves_sync_or_not()
    {
        using var world = new TestWorld();
        using var state = new StateStore(Path.Combine(world.Root, "data"));
        var plays = new Dictionary<long, SteamPlay> { [632360] = new(632360, Now.AddHours(-1).ToUniversalTime(), TimeSpan.FromHours(218)) };
        var since = Now.AddMinutes(-35).ToUniversalTime();
        var ghost = GameId.Parse("ghost-of-tsushima");
        var library = new[]
        {
            Entry("risk-of-rain-2", "Risk of Rain 2", StoreKind.Steam, "632360"),
            Entry("ghost-of-tsushima", "Ghost of Tsushima", StoreKind.Loose, null),
        };

        var games = Launcher.Games(library, [], state, null, plays, null, new Dictionary<GameId, DateTime> { [ghost] = since });
        var home = Launcher.Home(games, state, Now);

        // Risk of Rain 2 was played last, but Ghost of Tsushima runs now, and it doesn't even sync.
        Assert.Equal("Ghost of Tsushima", home.Hero!.Title);
        Assert.True(home.Hero.IsRunning);
        Assert.False(home.Hero.Syncs);
        var page = HomeViewModel.From(home, games, Now);
        Assert.Equal($"Playing now · since {since.ToLocalTime():HH:mm} · In its own folder", page.HeroEyebrow);
        Assert.Equal((GameStatus.Playing, "Playing now"), (page.HeroStatus, page.HeroStatusLabel));
        Assert.False(page.HeroShowsPlay);
        Assert.StartsWith("GameSync saw it start, so its play counts.", page.HeroBlurb);

        // Its tile says Playing, its row in the library shows it, and its page has Playing in place of Play.
        var tile = HomeViewModel.Tile(games.Single(g => g.Id == ghost), Now);
        Assert.Equal(GameStatus.Playing, tile.Status);
        Assert.True(tile.ShowsListStatus);
        var game = new GameViewModel(games.Single(g => g.Id == ghost), tile, null);
        Assert.Equal(("Playing", false), (game.PrimaryLabel, game.PrimaryEnabled));
        Assert.Equal("Playing now", game.Stats[0].Value);
        Assert.Equal("Ghost of Tsushima is running", ShellViewModel.DefaultRail("Ghost of Tsushima", 0).Single(r => r.Id == "library").DotLabel);

        // Nothing running: the last played is the hero again.
        var quiet = Launcher.Games(library, [], state, null, plays, null);
        Assert.Equal("Risk of Rain 2", Launcher.Home(quiet, state, Now).Hero!.Title);
    }

    [Fact]
    public void LIB_22_the_library_shows_installed_games_or_local_ones_and_keeps_the_view_on_this_PC()
    {
        using var world = new TestWorld();
        using var state = new StateStore(Path.Combine(world.Root, "data"));
        var library = new[]
        {
            Entry("risk-of-rain-2", "Risk of Rain 2", StoreKind.Steam, "632360"),
            Entry("black-myth-wukong", "Black Myth: Wukong", StoreKind.Loose, null),
            Entry("god-of-war", "God of War", null, null) with { Installed = false },
            Entry("hades", "Hades", StoreKind.Epic, "min") with { Installed = false },
        };
        var games = Launcher.Games(library, [], state, null, new Dictionary<long, SteamPlay>(), null);
        var kept = new List<string>();
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { }) { SetView = kept.Add };

        var view = LibraryViewModel.From(games, Now, "installed", actions);
        Assert.Equal(["all", "installed", "local", "attn"], view.Tabs.Select(t => t.Id));
        Assert.Equal(["Black Myth: Wukong", "Risk of Rain 2"], view.OtherTiles.Select(t => t.Title).Order());
        view.SelectedTab = "local";
        Assert.Equal(["Black Myth: Wukong"], view.OtherTiles.Select(t => t.Title));
        Assert.True(view.IsLocalView);

        // The view the person picks is kept; Home's My games only visits All games.
        view.ShowView("all");
        Assert.Equal(4, view.OtherTiles.Count);
        view.SelectedTab = "installed";
        Assert.Equal(["installed", "local", "installed"], kept);
        Assert.Equal("local", new LibraryViewModel(actions, view: "local").SelectedTab);
        Assert.Equal("all", new LibraryViewModel(actions, view: "attn").SelectedTab);
    }

    [Fact]
    public void LIB_21_reached_from_the_rail_a_game_page_still_open_leads_Back_to_the_covers_not_Home()
    {
        var shown = new List<string>();
        var actions = new LauncherActions(_ => { }, () => { }, (page, _) => shown.Add(page), (_, _) => { });
        using var world = new TestWorld();
        using var state = new StateStore(Path.Combine(world.Root, "data"));
        var games = Launcher.Games([Entry("counter-strike-2", "Counter-Strike 2", StoreKind.Steam, "730")], [], state, null, new Dictionary<long, SteamPlay>(), null);
        var library = LibraryViewModel.From(games, Now, actions: actions);
        var home = new object();
        var shell = new ShellViewModel(id => id == "library" ? library : home, "home");
        var cs2 = GameId.Parse("counter-strike-2");

        // Home opens CS2's page: Back returns to Home.
        library.Open(cs2, "home");
        shell.Open("library");
        Assert.Equal("home", library.ReturnTo);

        // The person goes Home by the rail, then back to the library by the rail: CS2's page is still open, and Back
        // leads to the covers.
        shell.NavigateCommand.Execute("home");
        shell.NavigateCommand.Execute("library");
        Assert.Equal(cs2, library.Selected);
        Assert.Null(library.ReturnTo);
        library.BackCommand.Execute(null);
        Assert.Null(library.Selected);
        Assert.Empty(shown);
    }

    [Fact]
    public void LIB_24_a_programs_game_folder_is_above_the_folders_engines_keep_programs_in()
    {
        using var world = new TestWorld();
        var wukong = Path.Combine(world.Root, "G", "Black Myth Wukong");
        Directory.CreateDirectory(Path.Combine(wukong, "Engine"));
        var shipping = Path.Combine(wukong, "b1", "Binaries", "Win64", "b1-Win64-Shipping.exe");
        Write(shipping, "MZ");
        Write(Path.Combine(wukong, "b1.exe"), "MZ");
        var cyberpunk = Path.Combine(world.Root, "Games", "Cyberpunk 2077");
        Write(Path.Combine(cyberpunk, "bin", "x64", "Cyberpunk2077.exe"), "MZ");
        var ghost = Path.Combine(world.Root, "Games", "Ghost of Tsushima");
        Write(Path.Combine(ghost, "GhostOfTsushima.exe"), "MZ");

        Assert.Equal(wukong, LooseScanner.GameFolderOf(shipping));
        Assert.Equal(wukong, LooseScanner.GameFolderOf(Path.Combine(wukong, "b1.exe")));
        Assert.Equal(cyberpunk, LooseScanner.GameFolderOf(Path.Combine(cyberpunk, "bin", "x64", "Cyberpunk2077.exe")));
        Assert.Equal(ghost, LooseScanner.GameFolderOf(Path.Combine(ghost, "GhostOfTsushima.exe")));
    }

    [Fact]
    public void LIB_24_a_located_game_stays_installed_through_rescans_while_its_folder_is_here()
    {
        using var world = new TestWorld();
        var folder = Path.Combine(world.Root, "Games", "Ghost of Tsushima");
        Write(Path.Combine(folder, "GhostOfTsushima.exe"), "MZ");
        var found = Entry("ghost-of-tsushima", "Ghost of Tsushima", null, null) with { Installed = false };

        var located = Library.Locate(found, folder, null, DateTime.UtcNow);
        Assert.Equal((true, StoreKind.Loose, folder, folder), (located.Installed, located.Store, located.InstallDir, located.InstallDirByHand));

        // A rescan that doesn't find it (the folder is in no game folder) keeps it installed there.
        var rescanned = Library.Reconcile([located], [], DateTime.UtcNow).Single();
        Assert.True(rescanned.Installed);
        Assert.Equal(folder, rescanned.InstallDir);

        // Its folder gone (an unplugged drive): Not installed for now, and where it was stays for when it's back.
        Directory.Delete(folder, recursive: true);
        var away = Library.Reconcile([located], [], DateTime.UtcNow).Single();
        Assert.False(away.Installed);
        Assert.Equal(folder, away.InstallDirByHand);

        // A scan that finds it in a game folder takes over.
        Write(Path.Combine(folder, "GhostOfTsushima.exe"), "MZ");
        var game = new InstalledGame { Store = StoreKind.Loose, Title = "Ghost of Tsushima", InstallDir = folder };
        var scanned = Library.Reconcile([located], [new DiscoveredGame { Installed = game, Title = game.Title, Print = Fingerprinter.Read(folder) }], DateTime.UtcNow).Single();
        Assert.True(scanned.Installed);
        Assert.Null(scanned.InstallDirByHand);
    }

    [Fact]
    public async Task LIB_24_Locate_the_game_marks_it_installed_and_Play_starts_the_program_picked()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        var id = GameId.Parse("ghost-of-tsushima");
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([Entry("ghost-of-tsushima", "Ghost of Tsushima", null, null) with { Installed = false }]);
        }

        var folder = Path.Combine(world.Root, "Games", "Ghost of Tsushima");
        var program = Path.Combine(folder, "GhostOfTsushima.exe");
        Write(program, "MZ");

        // Not a program, or one in GameSync's own folders: refused, and nothing changes.
        Write(Path.Combine(folder, "readme.txt"), "hello");
        await Assert.ThrowsAsync<UsageException>(() => LocalGames.LocateAsync(data, id, Path.Combine(folder, "readme.txt"), new Quiet(), Ct));
        Write(Path.Combine(data, "tools", "helper.exe"), "MZ");
        var ours = await Assert.ThrowsAsync<UsageException>(() => LocalGames.LocateAsync(data, id, Path.Combine(data, "tools", "helper.exe"), new Quiet(), Ct));
        Assert.Contains("GameSync", ours.Message, StringComparison.Ordinal);

        var said = await LocalGames.LocateAsync(data, id, program, new Quiet(), Ct);
        Assert.Contains("Play starts GhostOfTsushima.exe", said, StringComparison.Ordinal);
        using var engine = Engine.Open(data);
        var entry = engine.Library.All().Single();
        Assert.Equal((true, StoreKind.Loose, folder), (entry.Installed, entry.Store, entry.InstallDir));
        Assert.Equal(program, GameLaunch.PickedProgram(engine.State, id));
        Assert.Equal(folder, engine.InstallDirs[id]);

        // The agent watches it now, though its saves don't sync.
        Assert.Contains(engine.Watched(), w => w.Id == id && !w.Syncs);
    }

    [Fact]
    public void PLAY_12_the_agent_watches_every_game_installed_here_but_Steams_software_and_ignored_games()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        string Folder(string name)
        {
            var folder = Path.Combine(world.Root, "Games", name);
            Write(Path.Combine(folder, name + ".exe"), "MZ");
            return folder;
        }

        using (var library = new LibraryStore(data))
        {
            library.SaveAll(
            [
                Entry("ghost-of-tsushima", "Ghost of Tsushima", StoreKind.Loose, null) with { InstallDir = Folder("Ghost") },
                Entry("wallpaper-engine", "Wallpaper Engine", StoreKind.Steam, "431960") with { InstallDir = Folder("Wallpaper") },
                Entry("old-game", "Old Game", StoreKind.Loose, null) with { InstallDir = Folder("Old"), State = LibraryState.Ignored },
                Entry("gone", "Gone", StoreKind.Loose, null) with { InstallDir = Path.Combine(world.Root, "Games", "Gone") },
            ]);
        }

        using var engine = Engine.Open(data);
        var watched = engine.Watched(app => app == 431960);
        Assert.Equal(["ghost-of-tsushima"], watched.Select(w => w.Id.Value));
    }

    [Fact]
    public async Task ART_06_your_own_cover_banner_and_logo_stay_on_this_PC_checked_and_shown_before_Steams()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        var id = GameId.Parse("risk-of-rain-2");
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([Entry("risk-of-rain-2", "Risk of Rain 2", StoreKind.Steam, "632360")]);
        }

        var png = Path.Combine(world.Root, "Pictures", "banner.png");
        Directory.CreateDirectory(Path.GetDirectoryName(png)!);
        File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3]);
        var fake = Path.Combine(world.Root, "Pictures", "banner.jpg");
        File.WriteAllBytes(fake, "MZ this is a program, not a picture"u8.ToArray());

        // The dialog checks a picture as it's picked, like Steam's art: a program named .jpg is refused at once.
        var properties = new PropertiesViewModel(id, "Risk of Rain 2", null, "art");
        properties.Show((await GameSettings.ReadAsync(data, id, 632360, Ct))!);
        var banner = properties.Art.Single(a => a.Kind == ArtKind.Hero);
        Assert.Equal(("Banner", "None: a title cover shows", "Choose an image…"), (banner.Title, banner.From, banner.ChooseLabel));
        banner.Choose(fake);
        Assert.Contains("isn't a JPEG, PNG or WebP", banner.Error);
        Assert.Equal(0, properties.Changes);
        banner.Choose(png);
        Assert.Null(banner.Error);
        Assert.Equal(("Your image", "Choose another…", 1), (banner.From, banner.ChooseLabel, properties.Changes));

        // Saved: kept under the data folder's own art, never with the saves, and shown before Steam's.
        var said = await GameSettings.ApplyAsync(data, id, properties.Change(), () => { }, Ct);
        Assert.Contains("your own banner", said, StringComparison.Ordinal);
        using var art = new ArtCache(data);
        var own = art.FindOwn(id, ArtKind.Hero);
        Assert.NotNull(own);
        Assert.StartsWith(Path.Combine(data, "art", "own", "risk-of-rain-2"), own);
        using (var state = new StateStore(data))
        using (var library = new LibraryStore(data))
        {
            var game = Launcher.Games(library.All(), [], state, null, new Dictionary<long, SteamPlay>(), art).Single();
            Assert.Equal(own, game.HeroPath);
        }

        // Use Steam's (Remove, with none from Steam) takes it away again.
        var again = new PropertiesViewModel(id, "Risk of Rain 2", null, "art");
        again.Show((await GameSettings.ReadAsync(data, id, 632360, Ct))!);
        var slot = again.Art.Single(a => a.Kind == ArtKind.Hero);
        Assert.Equal(("Your image", "Remove"), (slot.From, slot.UndoLabel));
        slot.UndoCommand.Execute(null);
        await GameSettings.ApplyAsync(data, id, again.Change(), () => { }, Ct);
        Assert.Null(art.FindOwn(id, ArtKind.Hero));
    }

    [Fact]
    public void MGR_03_the_save_manager_shows_the_space_the_backups_take_on_their_drive()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        string history;
        using (var state = new StateStore(data))
        {
            history = Cli.HistoryFolder(state, data);
        }

        Write(Path.Combine(history, "blobs", "aa", "one"), new string('x', 1000));
        Write(Path.Combine(history, "blobs", "bb", "two"), new string('y', 500));

        var space = SaveOverview.Space(data)!;
        Assert.True(space.Bytes >= 1500);
        Assert.Equal(Path.GetPathRoot(history)!.TrimEnd('\\'), space.Drive);
        Assert.NotNull(space.FreeBytes);

        var saves = new SaveManagerViewModel();
        saves.ShowSpace(space, Now);
        var stat = saves.Stats.Single(s => s.Label == "Backups on this PC");
        Assert.Equal(Cli.FormatSize(space.Bytes), stat.Value);
        Assert.StartsWith($"{space.Drive} · ", stat.Sub);
        Assert.EndsWith(" free", stat.Sub);
    }

    private static LibraryEntry Entry(string id, string title, StoreKind? store, string? storeId) => new()
    {
        Id = GameId.Parse(id),
        Title = title,
        Store = store,
        StoreId = storeId,
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

        public void NeedsYou(string title, string message) => throw new InvalidOperationException($"{title}: {message}");
    }
}
