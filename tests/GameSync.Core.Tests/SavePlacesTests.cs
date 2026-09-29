using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Storage;
using GameSync.Core.Sync;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// Add a place (FOLD-01): a folder or file picked by hand, looked at before anything is added (how every PC reads it,
/// what's there, what can't be one game's place), then a root of the game's own that travels with its saves; and
/// Import kept saves (BAK-19), the dialog around the engine's import.
/// </summary>
public class SavePlacesTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public void FOLD_01_a_place_picked_by_hand_reads_as_every_PC_does_and_what_cant_be_one_games_place_is_refused()
    {
        using var world = new TestWorld();
        var home = Path.Combine(world.Root, "home");
        var folders = new Dictionary<string, string>
        {
            ["<documents>"] = Path.Combine(home, "Documents"),
            ["<localLow>"] = Path.Combine(home, "AppData", "LocalLow"),
        };
        var data = Path.Combine(world.Root, "data");
        var backups = Path.Combine(world.Root, "Store", "Backups");
        var guard = new SensitivePathGuard([(data, "it's GameSync's own data, including its sign-in tokens")]);
        var hollow = Path.Combine(home, "AppData", "LocalLow", "Team Cherry", "Hollow Knight");
        Write(Path.Combine(hollow, "user1.dat"), "one");
        Write(Path.Combine(hollow, "user2.dat"), "two, longer");
        Write(Path.Combine(hollow, "tools", "helper.dll"), "MZ not a save");
        Write(Path.Combine(home, "Documents", "My Games", "Lantern", "slot1.sav"), "slot");
        Directory.CreateDirectory(backups);
        Directory.CreateDirectory(data);
        var terraria = new GameDefinition
        {
            Id = GameId.Parse("terraria"),
            Title = "Terraria",
            Roots = new Dictionary<string, string> { ["saves"] = hollow },
            Rules = [new SaveRule { Root = "saves" }],
        };
        var game = new GameDefinition
        {
            Id = GameId.Parse("hollow-knight"),
            Title = "Hollow Knight",
            Roots = new Dictionary<string, string> { ["saves"] = Path.Combine(home, "Documents", "My Games", "Lantern") },
            Rules = [new SaveRule { Root = "saves" }],
        };
        NewPlaceLook Look(string path, IReadOnlyList<GameDefinition>? others = null) => SavePlaces.Look(path, game, others ?? [], folders, null, guard, backups);

        // A folder under one of Windows' own folders reads the same on every PC; program files are counted apart, never taken.
        var place = Look(hollow);
        Assert.Null(place.Refused);
        Assert.Equal("<localLow>/Team Cherry/Hollow Knight", place.Portable);
        Assert.True(place.Travels);
        Assert.Equal((2, 3L + 11L, 1), (place.Files, place.Bytes, place.Programs));
        Assert.False(place.IsFile);

        // Another game keeps saves there too: it can be added, with a warning.
        Assert.StartsWith("Terraria keeps saves here too.", Look(hollow, [terraria]).Warning);

        // One file: its folder is the place, and it's taken by itself; a program file never is.
        var file = Look(Path.Combine(home, "Documents", "My Games", "Lantern", "slot1.sav"));
        Assert.True(file.IsFile);
        Assert.Equal("<documents>/My Games/Lantern", file.Portable);
        Assert.Equal(1, file.Files);
        Assert.Contains("is a program file", Look(Path.Combine(hollow, "tools", "helper.dll")).Refused);

        // Under none of Windows' own folders it's a full path, which needs the same drive and folder elsewhere.
        var loose = Path.Combine(world.Root, "Games", "Old Game", "SAVE");
        Directory.CreateDirectory(loose);
        Assert.False(Look(loose).Travels);

        // Refused: a whole Windows folder, a drive, GameSync's own data, its backups, a folder holding them, a place it already has.
        Assert.StartsWith("That's your whole Documents folder", Look(Path.Combine(home, "Documents")).Refused);
        Assert.StartsWith("A whole drive is too broad", Look(Path.GetPathRoot(world.Root)!).Refused);
        Assert.Contains("GameSync's own data", Look(data).Refused);
        Assert.StartsWith("That's where GameSync keeps its backups", Look(backups).Refused);
        Assert.StartsWith("It holds GameSync's backup folder", Look(Path.Combine(world.Root, "Store")).Refused);
        Assert.Contains("is too broad: it contains", Look(world.Root).Refused);
        Assert.StartsWith("It's already one of Hollow Knight's places", Look(Path.Combine(home, "Documents", "My Games", "Lantern")).Refused);
        Assert.Contains("isn't there on this PC", Look(Path.Combine(world.Root, "Nowhere")).Refused);
    }

    [Fact]
    public void FOLD_01_an_added_place_becomes_a_root_of_the_games_own_keyed_as_every_PC_keys_it()
    {
        var game = new GameDefinition
        {
            Id = GameId.Parse("hollow-knight"),
            Title = "Hollow Knight",
            Roots = new Dictionary<string, string> { ["saves"] = "<localLow>/Team Cherry/Hollow Knight" },
            Rules = [new SaveRule { Root = "saves" }],
        };

        var added = GameSettings.Changed(game, new GamePropertiesChange(), new NewPlace("<documents>/My Games/Hollow Knight", "**", SaveCategory.Screenshots));
        var key = Core.Discovery.Discoverer.RootKey("<documents>/My Games/Hollow Knight");
        Assert.Equal("<documents>/My Games/Hollow Knight", added.Roots[key]);
        var rule = Assert.Single(added.Rules, r => r.Root == key);
        Assert.Equal(("**", SaveCategory.Screenshots, true), (rule.Include, rule.Category, rule.UseDefaultExcludes));

        // The same place again adds nothing; one file there is taken by itself, whatever it's called.
        var again = GameSettings.Changed(added, new GamePropertiesChange(), new NewPlace("<documents>/My Games/Hollow Knight/", "**", SaveCategory.Screenshots));
        Assert.Equal(added.Rules.Count, again.Rules.Count);
        var file = GameSettings.Changed(added, new GamePropertiesChange(), new NewPlace("<documents>/My Games/Hollow Knight", "user3.log", SaveCategory.Save));
        Assert.Contains(file.Rules, r => r.Root == key && r.Include == "user3.log" && !r.UseDefaultExcludes);

        // The rules travel with the saves (R8): the other PC is offered the new place, keyed the same.
        Assert.Contains(PortableRules.From(added).Describe(), d => d.Contains("My Games/Hollow Knight", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FOLD_01_Add_a_place_saves_it_in_the_games_own_rules_and_the_next_backup_takes_it()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var saves = Path.Combine(world.Root, "Saves", "Lantern Keep");
        var extra = Path.Combine(world.Root, "Saves", "Lantern Keep Extra");
        Write(Path.Combine(saves, "slot1.sav"), "slot one");
        Write(Path.Combine(extra, "profile.dat"), "the profile");
        var id = GameId.Parse("lantern-keep");
        new AppConfig
        {
            Remote = world.Cloud,
            Games = [new GameDefinition { Id = id, Title = "Lantern Keep", Roots = new Dictionary<string, string> { ["saves"] = saves }, Rules = [new SaveRule { Root = "saves" }] }],
        }.Save(data);

        var said = await GameSettings.ApplyAsync(data, id, new GamePropertiesChange { AddPlace = extra }, () => { }, Ct);
        Assert.Contains("a new place", said, StringComparison.Ordinal);
        var saved = AppConfig.Load(data)!.Games.Single();
        var key = saved.Roots.Keys.Single(k => k != "saves");
        Assert.Contains(saved.Rules, r => r.Root == key && r.Include == "**" && r.Category == SaveCategory.Save);

        // A place that can't be one is refused, and nothing is saved.
        await Assert.ThrowsAsync<UsageException>(() => GameSettings.ApplyAsync(data, id, new GamePropertiesChange { AddPlace = data }, () => { }, Ct));
        Assert.Equal(2, AppConfig.Load(data)!.Games.Single().Roots.Count);

        // The engine still opens, and the next backup takes the new place's file.
        await AppActions.BackUpNowAsync(data, id, new Quiet(), Ct);
        using var engine = Engine.Open(data);
        var history = new LocalHistory(Cli.HistoryFolder(engine.State, data));
        var newest = (await history.Log.ListAsync(id, Ct)).MaxBy(v => v.CreatedUtc)!;
        Assert.Contains(newest.Files, f => f.Path == $"{key}/profile.dat");
        Assert.Contains(newest.Files, f => f.Path == "saves/slot1.sav");
    }

    [Fact]
    public void FOLD_01_the_dialog_looks_at_the_pick_and_adds_it_with_what_it_holds()
    {
        var id = GameId.Parse("hollow-knight");
        var looked = new NewPlaceLook
        {
            Path = @"C:\Users\You\AppData\LocalLow\Team Cherry\Hollow Knight",
            Folder = @"C:\Users\You\AppData\LocalLow\Team Cherry\Hollow Knight",
            Portable = "<localLow>/Team Cherry/Hollow Knight",
            Files = 6,
            Bytes = 1153434,
            NewestUtc = new DateTime(2026, 9, 29, 19, 4, 0, DateTimeKind.Utc),
            Programs = 2,
        };
        var added = new List<(GameId, string, SaveCategory)>();
        var closed = 0;
        var dialog = new AddPlaceViewModel(id, "Hollow Knight", new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
        {
            LookPlace = (_, _, _) => Task.FromResult(looked),
            AddPlace = (game, path, category) => added.Add((game, path, category)),
            CloseDialog = () => closed++,
        });
        Assert.True(dialog.NoPlace);
        Assert.False(dialog.CanAdd);

        dialog.Look(looked.Path);
        Assert.True(dialog.CanAdd);
        Assert.Equal("<localLow>/Team Cherry/Hollow Knight", dialog.Portable);
        Assert.StartsWith("6 files · 1.1 MB · newest ", AddPlaceViewModel.MetaOf(looked, looked.NewestUtc!.Value.ToLocalTime()));
        Assert.Equal("2 program files are there too, and never taken: only save data moves.", dialog.ProgramsNote);
        Assert.Null(dialog.PortableNote);

        dialog.SelectCommand.Execute("config");
        Assert.Equal("Settings: each PC keeps its own", dialog.CategoryLabel);
        dialog.AddCommand.Execute(null);
        Assert.Equal([(id, looked.Path, SaveCategory.Config)], added);
        Assert.Equal(1, closed);

        // Refused: the reason shows, and Add stays off.
        var refusing = new AddPlaceViewModel(id, "Hollow Knight", new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
        {
            LookPlace = (_, _, _) => Task.FromResult(looked with { Refused = "That's your whole Documents folder." }),
        });
        refusing.Look(@"C:\Users\You\Documents");
        Assert.True(refusing.IsRefused);
        Assert.False(refusing.CanAdd);
        Assert.Null(refusing.Meta);
    }

    [Fact]
    public void BAK_19_Import_kept_saves_reads_the_folder_says_what_each_copy_becomes_then_imports_them()
    {
        var id = GameId.Parse("bloodborne");
        var saved = new DateTime(2026, 9, 12, 19, 5, 0, DateTimeKind.Utc);
        var report = new ImportReport(
        [
            new ImportItem("Before Orphan of Kos", "Before Orphan of Kos", 3, 1189888, saved.AddDays(8), null),
            new ImportItem("Before Rom", "Before Rom", 3, 1171456, saved, null),
            new ImportItem("After Rom", "After Rom", 3, 1171456, saved, "'Before Rom'"),
            new ImportItem("Before Amelia", "Before Amelia", 3, 1160192, saved.AddDays(-3), "version 2026-09-09T20-40-00Z_DESKTOP_a1b2c3"),
        ], ["SPRJ0005 is the live save, so it's left as it is."], 0);
        var asked = new List<(string Folder, string? Root, bool Apply)>();
        var dialog = new ImportKeptViewModel(id, "Bloodborne", new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
        {
            SaveRoots = (_, _) => Task.FromResult<IReadOnlyList<SaveRoot>>([new SaveRoot("saves", "<localAppData>/shad/saves"), new SaveRoot("extra", "<documents>/Bloodborne")]),
            KeptSaves = (_, folder, root, apply, _) =>
            {
                asked.Add((folder, root, apply));
                return Task.FromResult(apply ? report with { Added = 3 } : report);
            },
        });
        dialog.Load();
        Assert.True(dialog.HasRoots);
        Assert.Equal("saves", dialog.Root);
        Assert.True(dialog.Picking);

        dialog.Read(@"D:\Saves\Bloodborne\CUSA00207");
        Assert.True(dialog.Reviewing);
        Assert.Equal(["Before Amelia", "Before Rom", "After Rom", "Before Orphan of Kos"], dialog.Items.Select(i => i.Name));

        // A copy the same as another here isn't named again; one already in the history only gets its name.
        Assert.Equal("Already kept", dialog.Items[0].SameText);
        Assert.Equal("Same as Before Rom", dialog.Items[2].SameText);
        Assert.Equal("3 named saves · 1 the same as another, not kept twice", dialog.Summary);
        Assert.Equal("Import 3 named saves", dialog.ImportLabel);
        Assert.Single(dialog.Skipped);

        dialog.ImportCommand.Execute(null);
        Assert.True(dialog.Done);
        Assert.Equal("Imported 3 named saves.", dialog.DoneText);
        Assert.Equal([(@"D:\Saves\Bloodborne\CUSA00207", "saves", false), (@"D:\Saves\Bloodborne\CUSA00207", "saves", true)], asked);
    }

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
