using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Sync;

namespace GameSync.Core.Tests;

/// <summary>
/// Save rules between PCs: every version records the rules it was taken with, a PC without the game is offered them
/// (PC-04), and rules that differ are never taken up by themselves (R8).
/// </summary>
public class SharedRulesTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    private static readonly GameDefinition Narrow = new()
    {
        Id = GameId.Parse("game"),
        Title = "Game",
        Roots = new Dictionary<string, string> { ["docs"] = "<documents>/Game" },
        Rules = [new SaveRule { Root = "docs" }],
    };

    private static readonly GameDefinition Wide = Narrow with
    {
        Roots = new Dictionary<string, string> { ["docs"] = "<documents>/Game", ["local"] = "<localAppData>/Game" },
        Rules = [new SaveRule { Root = "docs" }, new SaveRule { Root = "local" }],
    };

    [Fact]
    public async Task Each_version_records_the_portable_rules_it_was_taken_with()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddResolvedGame(Wide, desktop.KnownFolders);
        Write(desktop, "<documents>", "Game/slot1.sav", "a");

        await desktop.SyncAsync();

        var version = Assert.Single(await Cloud.VersionsAsync(world, "game"));
        Assert.Equal("<localAppData>/Game", version.Rules!.Roots["local"]);
        Assert.True(version.Rules.SameFilesAs(PortableRules.From(Wide)));
    }

    [Fact]
    public async Task R8_a_PC_with_other_rules_is_told_and_uses_nothing_of_theirs()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddResolvedGame(Wide, desktop.KnownFolders);
        laptop.AddResolvedGame(Narrow, laptop.KnownFolders);
        Write(desktop, "<documents>", "Game/slot1.sav", "desktop");
        Write(desktop, "<localAppData>", "Game/profile.cfg", "desktop");
        await desktop.SyncAsync();

        var result = Assert.Single(await laptop.SyncAsync());

        Assert.Contains(result.Warnings, w => w.Contains("DESKTOP saves this game with other save rules", StringComparison.Ordinal) &&
            w.Contains("<localAppData>/Game: **", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(laptop.KnownFolders["<documents>"], "Game", "slot1.sav")));
    }

    [Fact]
    public async Task A_version_taken_with_fewer_rules_leaves_the_other_folders_alone_when_it_restores()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddResolvedGame(Narrow, desktop.KnownFolders);
        laptop.AddResolvedGame(Wide, laptop.KnownFolders);
        Write(desktop, "<documents>", "Game/slot1.sav", "desktop");
        await desktop.SyncAsync();
        Write(laptop, "<documents>", "Game/slot1.sav", "laptop");
        Write(laptop, "<localAppData>", "Game/profile.cfg", "laptop's own");

        var result = Assert.Single(await laptop.SyncAsync());

        Assert.Equal(SyncAction.Download, result.Action);
        Assert.Equal("desktop", Read(laptop, "<documents>", "Game/slot1.sav"));
        Assert.Equal("laptop's own", Read(laptop, "<localAppData>", "Game/profile.cfg"));
        Assert.Contains(result.Warnings, w => w.Contains("DESKTOP saves this game with other save rules", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PC_04_a_game_only_the_other_PC_syncs_is_offered_with_its_rules_even_offline(bool drive)
    {
        using var world = new TestWorld(drive);
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddResolvedGame(Wide with { Title = "Terraria" }, desktop.KnownFolders);
        Write(desktop, "<documents>", "Game/slot1.sav", "desktop");
        await desktop.SyncAsync();

        var offered = Assert.Single(await laptop.Service().OtherGamesAsync(CancellationToken.None));

        Assert.Equal(("game", "Terraria", "DESKTOP"), (offered.Id.Value, offered.Title, offered.Newest.Device.Name));
        Assert.True(offered.Rules!.SameFilesAs(PortableRules.From(Wide)));
        Assert.Empty(await desktop.Service().OtherGamesAsync(CancellationToken.None));

        laptop.Offline = true;
        Assert.Equal("game", Assert.Single(await laptop.Service().OtherGamesAsync(CancellationToken.None)).Id.Value);
    }

    [Fact]
    public async Task PC_04_taking_up_the_other_PCs_rules_brings_its_save_down()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        desktop.AddResolvedGame(Wide, desktop.KnownFolders);
        Write(desktop, "<documents>", "Game/slot1.sav", "desktop's slot");
        Write(desktop, "<localAppData>", "Game/profile.cfg", "desktop's profile");
        await desktop.SyncAsync();
        var offered = Assert.Single(await laptop.Service().OtherGamesAsync(CancellationToken.None));

        var adopted = Library.Adopt(new LibraryEntry { Id = offered.Id, Title = offered.Title }, offered.Rules!);
        laptop.AddResolvedGame(adopted.Confirmed!, laptop.KnownFolders);
        var result = Assert.Single(await laptop.SyncAsync());

        Assert.Equal(SyncAction.Download, result.Action);
        Assert.Equal("desktop's slot", Read(laptop, "<documents>", "Game/slot1.sav"));
        Assert.Equal("desktop's profile", Read(laptop, "<localAppData>", "Game/profile.cfg"));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("other save rules", StringComparison.Ordinal));
    }

    [Fact]
    public void A_game_found_here_takes_the_ID_another_PC_gave_it()
    {
        using var world = new TestWorld();
        var pc = DiscoveryTests.FakePc(world);
        var spire = DiscoveryTests.Installed(world, StoreKind.Loose, "SlayTheSpire2", "SlayTheSpire2");
        var elsewhere = new Dictionary<GameId, string> { [GameId.Parse("sts2")] = "Slay the Spire 2" };

        var entry = Assert.Single(Library.Reconcile([], [pc.Discoverer.Discover(spire)], Now, elsewhere));

        Assert.Equal("sts2", entry.Id.Value);
    }

    [Fact]
    public void R8_confirming_a_suggestion_adds_the_new_place_and_keeps_the_rules_already_there()
    {
        var entry = new LibraryEntry
        {
            Id = GameId.Parse("game"),
            Title = "Game",
            State = LibraryState.Synced,
            Confirmed = Narrow,
            Proposals =
            [
                new Proposal(FoundBy.NameSearch, "<documents>/Game", "**", SaveCategory.Save, 1, 10, null),
                new Proposal(FoundBy.NameSearch, "<localAppData>/Game", "**", SaveCategory.Save, 1, 10, null),
            ],
        };
        Assert.Equal(["<localAppData>/Game"], entry.Suggestions.Select(s => s.Root));

        var confirmed = Library.Confirm(entry);

        Assert.Equal("<documents>/Game", confirmed.Confirmed!.Roots["docs"]);
        Assert.Equal("<localAppData>/Game", confirmed.Confirmed.Roots["localappdata-game"]);
        Assert.Empty(confirmed.Suggestions);
    }

    [Fact]
    public async Task FOLD_11_two_games_taking_the_same_file_both_show_it_and_neither_syncs()
    {
        using var world = new TestWorld();
        using var pc = world.Pc("DESKTOP");
        var folder = Directory.CreateDirectory(Path.Combine(world.Root, "Shared Saves")).FullName;
        pc.AddGame("first", savesFolder: folder);
        pc.AddGame("second", include: "slot*.sav", savesFolder: folder);
        File.WriteAllText(Path.Combine(folder, "slot1.sav"), "both take this");
        File.WriteAllText(Path.Combine(folder, "first.dat"), "only the first's");

        var results = await pc.SyncAsync();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(GameStatus.Error, r.Status));
        Assert.Contains(results, r => r.Message.Contains("second takes", StringComparison.Ordinal) && r.Message.Contains("slot1.sav", StringComparison.Ordinal));
        Assert.Empty(await Cloud.VersionsAsync(world, "first"));
    }

    [Fact]
    public async Task FOLD_11_games_sharing_a_folder_but_not_a_file_sync_as_usual()
    {
        using var world = new TestWorld();
        using var pc = world.Pc("DESKTOP");
        var folder = Directory.CreateDirectory(Path.Combine(world.Root, "Shared Saves")).FullName;
        pc.AddGame("first", include: "*.dat", savesFolder: folder);
        pc.AddGame("second", include: "*.sav", savesFolder: folder);
        File.WriteAllText(Path.Combine(folder, "slot1.sav"), "the second's");
        File.WriteAllText(Path.Combine(folder, "first.dat"), "the first's");

        Assert.All(await pc.SyncAsync(), r => Assert.Equal(SyncAction.Upload, r.Action));
    }

    [Fact]
    public void FOLD_11_a_registry_key_two_games_take_is_shared_too()
    {
        var a = Narrow with { Id = GameId.Parse("a"), Title = "A", Registry = [new RegistryRule { Key = "HKEY_CURRENT_USER/Software/Studio/Game" }] };
        var b = Narrow with { Id = GameId.Parse("b"), Title = "B", Roots = new Dictionary<string, string>(), Rules = [], Registry = [new RegistryRule { Key = @"HKCU\Software\Studio" }] };

        var shared = Scanning.RuleOverlap.Find([a, b]);

        Assert.Equal(2, shared.Count);
        Assert.Contains("registry key", shared[GameId.Parse("a")], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Roaming\Some Studio\Saves", "<roaming>/Some Studio/Saves")]
    [InlineData(@"C:\Users\me\Documents", "<documents>")]
    [InlineData(@"C:\Users\me\Stuff\", "<home>/Stuff")]
    [InlineData(@"G:\Emu\Saves", @"G:\Emu\Saves")]
    public void Full_paths_become_portable_under_the_most_specific_folder(string path, string portable)
    {
        var folders = new Dictionary<string, string>
        {
            ["<home>"] = @"C:\Users\me",
            ["<documents>"] = @"C:\Users\me\Documents",
            ["<roaming>"] = @"C:\Users\me\AppData\Roaming",
        };

        Assert.Equal(portable, RootResolver.ToPortable(path, folders));
    }

    private static void Write(TestPc pc, string folder, string relative, string content)
    {
        var path = Path.Combine(pc.KnownFolders[folder], relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Read(TestPc pc, string folder, string relative) =>
        File.ReadAllText(Path.Combine(pc.KnownFolders[folder], relative.Replace('/', Path.DirectorySeparatorChar)));
}
