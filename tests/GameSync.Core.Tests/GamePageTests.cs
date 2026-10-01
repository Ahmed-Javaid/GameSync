using CommunityToolkit.Mvvm.Input;
using System.Security.Cryptography;
using System.Text;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>A game's page (LIB-18, BAK-14, BAK-15, BAK-18, FIND-06): what it shows, read from this PC alone, and the library around it.</summary>
public class GamePageTests
{
    private static readonly GameId Game = GameId.Parse("hollow-knight");
    private static readonly DateTime T0 = new(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DeviceInfo Desktop = new(DeviceId.Parse("d-desktop"), "DESKTOP");
    private static readonly DeviceInfo Laptop = new(DeviceId.Parse("d-laptop"), "LAPTOP");
    private static readonly RootResolver Resolver = new(
        new Dictionary<string, string> { ["<localLow>"] = @"C:\Users\You\AppData\LocalLow", ["<documents>"] = @"C:\Users\You\Documents" },
        new Dictionary<string, string>(), new Dictionary<GameId, string>());

    [Fact]
    public void BAK_14_BAK_15_every_version_from_every_PC_newest_first_with_its_PC_its_note_and_the_space_they_take()
    {
        var v1 = V("v1", null, Desktop, 0, F("user1.dat", "one"));
        var v2 = V("v2", v1, Laptop, 60, F("user1.dat", "two"), F("user2.dat", "bee"));
        var kept = V("k1", v1, Laptop, 90, F("user1.dat", "lost")) with { Kind = VersionKind.Kept, Origin = VersionOrigin.KeptInConflict };
        var v3 = V("v3", v2, Desktop, 120, F("user1.dat", "two"), F("user2.dat", "bee"));
        var pins = new Dictionary<VersionId, PinRecord>
        {
            [v2.Id] = new(v2.Id, "Before the Radiance", T0, Laptop, Named: true),
        };
        var definition = new GameDefinition
        {
            Id = Game,
            Title = "Hollow Knight",
            Roots = new Dictionary<string, string> { ["saves"] = @"C:\Users\You\AppData\LocalLow\Team Cherry\Hollow Knight" },
            PortableRoots = new Dictionary<string, string> { ["saves"] = "<localLow>/Team Cherry/Hollow Knight" },
            Rules = [new SaveRule { Root = "saves" }],
        };

        var page = GameDetails.Describe(Game, entry: null, definition, [v1, v2, kept, v3], pins, new HashSet<VersionId> { v3.Id },
            new Dictionary<DeviceId, string> { [Laptop.Id] = "LAPTOP-2" }, [], Resolver);

        Assert.True(page.Syncs);
        Assert.Equal(["v3", "k1", "v2", "v1"], page.Versions.Select(v => v.Id.Value));
        Assert.Equal(["DESKTOP", "LAPTOP-2", "LAPTOP-2", "DESKTOP"], page.Versions.Select(v => v.Pc));
        Assert.True(page.Versions[0].IsCurrent);
        Assert.False(page.Versions[0].Uploaded);
        Assert.Equal("Kept from a conflict", page.Versions[1].Note);
        Assert.Equal("Before the Radiance", page.Versions[2].Note);
        Assert.True(page.Versions[2].IsPinned);

        // The same file in two versions takes its space once: one, two, bee and lost.
        Assert.Equal(3 + 3 + 3 + 4, page.HistoryBytes);

        var named = Assert.Single(page.NamedSaves);
        Assert.Equal(("Before the Radiance", "LAPTOP-2"), (named.Name, named.Pc));

        // BAK-14: where the saves are, from the current version's files.
        var place = Assert.Single(page.Places);
        Assert.Equal("<localLow>/Team Cherry/Hollow Knight", place.Portable);
        Assert.Equal(@"C:\Users\You\AppData\LocalLow\Team Cherry\Hollow Knight", place.Folder);
        Assert.StartsWith("2 files · 6 B · newest ", place.Evidence);
        Assert.StartsWith("Added by hand. Backed up since ", page.FoundBy);
    }

    [Fact]
    public void FIND_06_a_game_not_syncing_yet_shows_what_the_scan_found_and_where_on_this_PC()
    {
        var entry = new LibraryEntry
        {
            Id = GameId.Parse("core-keeper"),
            Title = "Core Keeper",
            LastSeenUtc = T0,
            Proposals =
            [
                new Proposal(FoundBy.SaveList, "<localLow>/Pugstorm/Core Keeper/Steam", "*/**", SaveCategory.Save, 80, 43_900_000, T0),
                new Proposal(FoundBy.SaveList, "<localLow>/Pugstorm/Core Keeper/Steam", "*/prefs.json", SaveCategory.Config, 1, 3_000, T0),
            ],
            RegistryProposals = [new RegistryProposal(FoundBy.SaveList, "HKEY_CURRENT_USER/Software/Pugstorm/Core Keeper", SaveCategory.Config, 34, T0)],
        };

        var page = GameDetails.Describe(entry.Id, entry, definition: null, [], new Dictionary<VersionId, PinRecord>(), new HashSet<VersionId>(),
            new Dictionary<DeviceId, string>(), [], Resolver);

        Assert.False(page.Syncs);
        Assert.Empty(page.Versions);
        Assert.Equal($"Found by the save list at the last scan, {GameDetails.Day(T0)}.", page.FoundBy);
        Assert.Equal(3, page.Places.Count);
        Assert.Equal("<localLow>/Pugstorm/Core Keeper/Steam  (*/**)", page.Places[0].Portable);
        Assert.Equal(@"C:\Users\You\AppData\LocalLow\Pugstorm\Core Keeper\Steam", page.Places[0].Folder);
        Assert.StartsWith("80 files · 41.9 MB", page.Places[0].Evidence);
        Assert.Equal("Settings", page.Places[1].Tag);
        Assert.Equal((@"HKEY_CURRENT_USER\Software\Pugstorm\Core Keeper", "Registry", "34 values"), (page.Places[2].Portable, page.Places[2].Tag, page.Places[2].Evidence));
    }

    [Fact]
    public void LIB_14_to_LIB_18_the_library_keeps_favourites_first_searches_both_sides_and_opens_a_games_page_beside_the_list()
    {
        var now = new DateTime(2026, 9, 28, 21, 30, 0, DateTimeKind.Local);
        LauncherGame Game(string title, int daysAgo, bool favourite = false, GameStatus? status = null) => new()
        {
            Id = Core.Discovery.Library.NewId(title, []),
            Title = title,
            LastPlayedUtc = now.AddDays(-daysAgo).ToUniversalTime(),
            IsFavourite = favourite,
            Status = status,
            Syncs = status is not null,
        };
        var games = new[]
        {
            Game("Risk of Rain 2", 3),
            Game("Terraria", 1, favourite: true),
            Game("Sekiro: Shadows Die Twice", 0, status: GameStatus.Conflict),
            Game("Slay the Spire 2", 5, favourite: true),
        };
        var opened = new List<GameId>();
        var library = new LibraryViewModel(new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
        {
            LoadGame = (id, _) =>
            {
                opened.Add(id);
                return Task.FromResult<GameDetail?>(null);
            },
        });
        library.Update(games, LibraryViewModel.Tiles(games, now, actions: null));

        // Favourites first on both sides, each part in the order chosen: recently played, then by name.
        Assert.Equal(["Terraria", "Slay the Spire 2"], library.FavouriteTiles.Select(t => t.Title));
        Assert.Equal(["Sekiro: Shadows Die Twice", "Risk of Rain 2"], library.OtherTiles.Select(t => t.Title));
        Assert.Equal(["Favourites", "Terraria", "Slay the Spire 2", "Games", "Sekiro: Shadows Die Twice", "Risk of Rain 2"], Names(library));
        library.Sort = LibrarySort.Name;
        Assert.Equal(["Favourites", "Slay the Spire 2", "Terraria", "Games", "Risk of Rain 2", "Sekiro: Shadows Die Twice"], Names(library));

        // The search narrows both sides; the count says how many of the view match.
        library.Search = "sts";
        Assert.Equal(["Favourites", "Slay the Spire 2"], Names(library));
        Assert.Equal("1 of 4", library.CountLabel);
        library.Search = "zelda";
        Assert.Equal("No game here matches “zelda”.", library.Nothing);
        library.Search = "";

        // A group closed from its heading.
        library.ToggleGroupCommand.Execute("favourites");
        Assert.Equal(["Favourites", "Games", "Risk of Rain 2", "Sekiro: Shadows Die Twice"], Names(library));

        // A game's page opens beside the list at full glass, reads its places and history, and Back returns to the covers.
        library.OpenCommand.Execute(games[0].Id);
        Assert.False(library.ShowsCovers);
        Assert.Equal("Risk of Rain 2", library.Page!.Title);
        Assert.Equal(UI.Theming.GlassStrength.Glass, library.Strength);
        Assert.Equal([games[0].Id], opened);
        library.BackCommand.Execute(null);
        Assert.True(library.ShowsCovers);
        Assert.Equal(UI.Theming.GlassStrength.Glass, library.Strength);
    }

    [Fact]
    public void BAK_18_a_named_saves_menu_renames_it_and_takes_its_name_away_keeping_the_save()
    {
        var renamed = new List<(GameId Game, string Name, string NewName)>();
        var forgotten = new List<(GameId Game, string Name)>();
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
        {
            RenameSave = (game, name, newName) => renamed.Add((game, name, newName)),
            ForgetSave = (game, name) => forgotten.Add((game, name)),
        };
        var game = new LauncherGame { Id = Game, Title = "Hollow Knight", Syncs = true, Status = GameStatus.Synced };
        var saves = new GameSavesViewModel(game, actions, new RelayCommand(() => { }));
        var v1 = V("v1", null, Desktop, 0, F("user1.dat", "one"));
        saves.Show(new GameDetail
        {
            Id = Game,
            Syncs = true,
            NamedSaves = [new GameNamedSave("Before the Radiance", v1.Id, T0, "DESKTOP", Uploaded: true)],
        }, T0.ToLocalTime());
        var named = Assert.Single(saves.NamedSaves);
        Assert.Equal("Before the Radiance", named.NewName);

        // An unchanged or empty name does nothing; a new one is sent with the old, trimmed.
        saves.RenameSaveCommand.Execute(named);
        named.NewName = "   ";
        saves.RenameSaveCommand.Execute(named);
        named.NewName = "  Before the Radiance, again ";
        saves.RenameSaveCommand.Execute(named);
        Assert.Equal([(Game, "Before the Radiance", "Before the Radiance, again")], renamed);

        saves.ForgetSaveCommand.Execute(named);
        Assert.Equal([(Game, "Before the Radiance")], forgotten);
    }

    [Fact]
    public void ONB_06_a_game_says_where_its_saves_go_Google_Drive_a_cloud_folder_or_this_PC_until_a_cloud_is_connected()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        LauncherGame Read(string remote)
        {
            new AppConfig
            {
                Remote = remote,
                Games = [new GameDefinition { Id = Game, Title = "Hollow Knight", Roots = new Dictionary<string, string> { ["saves"] = Path.Combine(world.Root, "Saves") }, Rules = [new SaveRule { Root = "saves" }] }],
            }.Save(data);
            return LauncherData.Read(data, DateTime.Now).Games.Single() with { Status = GameStatus.Synced, FirstBackupPending = false };
        }

        // KAN-60: until its first backup, a game that syncs says so, never Synced.
        var drive = Read("drive");
        var first = LauncherData.Read(data, DateTime.Now).Games.Single();
        Assert.Equal((GameStatus.UploadPending, true), (first.Status, first.FirstBackupPending));
        Assert.Equal("Not backed up yet", HomeViewModel.StatusLabel(first));
        Assert.StartsWith("It isn't backed up yet", GameSavesViewModel.SentenceOf(first));
        Assert.StartsWith("Not backed up yet", GameViewModel.SentenceOf(first));
        Assert.Null(HomeViewModel.MarkLabel(first.Status, first.Store));

        Assert.Equal("Backed up on this PC and in your Google Drive, every version kept.", GameViewModel.SentenceOf(drive));
        Assert.Equal("Its saves are backed up on this PC and in your Google Drive, and every version is kept.", GameSavesViewModel.SentenceOf(drive));
        Assert.Equal("Backed up on this PC and in your cloud folder, every version kept.", GameViewModel.SentenceOf(Read(world.Cloud)));

        // Skip for now: every version waits on this PC until a cloud is connected.
        var none = Read(AppConfig.NoCloud);
        Assert.Null(none.Cloud);
        Assert.Equal("Backed up on this PC, every version kept; it goes up once you connect a cloud.", GameViewModel.SentenceOf(none));
        Assert.Equal("Its saves are backed up on this PC, and every version is kept; they go up once you connect a cloud.", GameSavesViewModel.SentenceOf(none));
    }

    private static IEnumerable<string> Names(LibraryViewModel library) =>
        library.ListItems.Select(i => i switch
        {
            GameListHeading heading => heading.Label,
            GameListRow row => row.Game.Title,
            _ => "?",
        });

    private static FileEntry F(string name, string content, int minute = 0) =>
        new($"saves/{name}", content.Length, T0.AddMinutes(minute), BlobId.FromHash(SHA256.HashData(Encoding.UTF8.GetBytes(content))));

    private static VersionRecord V(string id, VersionRecord? parent, DeviceInfo device, int minute, params FileEntry[] files) => new()
    {
        Id = VersionId.Parse(id),
        Game = Game,
        Parent = parent?.Id,
        Kind = VersionKind.Normal,
        Origin = VersionOrigin.Session,
        Device = device,
        CreatedUtc = T0.AddMinutes(minute),
        Files = files,
    };
}
