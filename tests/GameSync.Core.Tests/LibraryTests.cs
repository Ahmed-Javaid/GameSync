using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Sync;
using GameSync.Host;
using static GameSync.Core.Tests.DiscoveryTests;

namespace GameSync.Core.Tests;

/// <summary>The library: IDs, fixes that stick across rescans (LIB-07), games that disappear (LIB-08), confirming (FIND-06).</summary>
public class LibraryTests
{
    [Fact]
    public void KAN_44_a_copy_of_the_person_s_own_is_never_synced_by_a_store()
    {
        using var world = new TestWorld();
        var folder = Path.Combine(world.Root, "Games", "Child of Light");
        Directory.CreateDirectory(folder);

        // Child of Light's saves are found before the game is, and the save list says Ubisoft's cloud keeps them.
        var listed = new SaveListGame { Title = "Child of Light", Cloud = ["ubisoft"] };
        LeftoverGame Saves() => new(listed, [new Proposal(FoundBy.SaveList, "<programData>/Orbit/611", "**", SaveCategory.Save, 1, 38_195, Now)], StoreCloud: true);
        var found = Assert.Single(Library.Reconcile([], [], Now, leftovers: [Saves()]));
        Assert.True(found.StoreCloud);
        var confirmed = Library.Confirm(found);
        Assert.Equal(GameMode.BackupOnly, confirmed.Confirmed!.Mode);

        // Located in a folder of the person's own: no store syncs that copy, so GameSync syncs it between PCs.
        var located = Library.Locate(confirmed, folder, null, Now);
        Assert.Equal((false, GameMode.Sync), (located.StoreCloud, located.Confirmed!.Mode));

        // Rescans keep it so, though the save list still says the store's cloud keeps its saves.
        var again = Assert.Single(Library.Reconcile([located], [], Now, leftovers: [Saves()]));
        Assert.Equal((false, GameMode.Sync, true), (again.StoreCloud, again.Confirmed!.Mode, again.Installed));

        // One located before this was put right is put right by the next scan; one the person set to back up only stays so.
        var stale = located with { StoreCloud = true, Confirmed = located.Confirmed with { Mode = GameMode.BackupOnly } };
        Assert.Equal(GameMode.Sync, Assert.Single(Library.Reconcile([stale], [], Now, leftovers: [Saves()])).Confirmed!.Mode);
        var chosen = again with { Confirmed = again.Confirmed with { Mode = GameMode.BackupOnly } };
        Assert.Equal(GameMode.BackupOnly, Assert.Single(Library.Reconcile([chosen], [], Now, leftovers: [Saves()])).Confirmed!.Mode);

        // A game not located anywhere is still taken for its store's, as its saves are.
        Assert.True(Assert.Single(Library.Reconcile([found], [], Now, leftovers: [Saves()])).StoreCloud);

        // The owner's case: located by hand before this rule, with no scan that sees it again (its folder isn't a scan
        // folder, and its saves aren't leftovers once it's installed). Opening GameSync's data puts it right, and keeps it so.
        Assert.Equal(GameMode.Sync, Assert.Single(Library.Reconcile([stale], [], Now)).Confirmed!.Mode);
        Assert.Equal((false, GameMode.Sync), Assert.Single(Library.SettleOwnCopies([stale, found])) is var s ? (s.StoreCloud, s.Confirmed!.Mode) : default);
        Assert.Empty(Library.SettleOwnCopies([chosen, found]));

        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud }.Save(data);
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([stale]);
        }

        using (var engine = GameSync.Host.Engine.Open(data))
        {
            var game = Assert.Single(engine.Games, g => g.Id == stale.Id);
            Assert.Equal(GameMode.Sync, game.Mode);
        }

        using (var library = new LibraryStore(data))
        {
            Assert.Equal((false, GameMode.Sync), Assert.Single(library.All()) is var saved ? (saved.StoreCloud, saved.Confirmed!.Mode) : default);
        }
    }

    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Black Myth: Wukong", "black-myth-wukong")]
    [InlineData("Sekiro™: Shadows Die Twice", "sekiro-shadows-die-twice")]
    [InlineData("God of War Ragnarök", "god-of-war-ragnarok")]
    [InlineData("R.E.P.O.", "r-e-p-o")]
    public void Game_ids_come_from_titles_so_both_PCs_agree(string title, string id) =>
        Assert.Equal(id, Library.NewId(title, []).Value);

    [Fact]
    public void A_clashing_id_gets_a_number() =>
        Assert.Equal("terraria-2", Library.NewId("Terraria", [GameId.Parse("terraria")]).Value);

    [Fact]
    public void LIB_07_a_rename_an_ignore_and_a_confirmation_survive_rescans_and_restarts()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        DiscoveryTests.Write(pc.Folders["<documents>"], "My Games/Terraria/Worlds/home.wld", "a world");
        var terraria = Installed(world, StoreKind.Steam, "Terraria", "Terraria", "105600");
        var rounds = Installed(world, StoreKind.Loose, "ROUNDS", "ROUNDS");
        var first = Library.Reconcile([], [pc.Discoverer.Discover(terraria), pc.Discoverer.Discover(rounds)], Now);

        var edited = first.Select(e => e.Id.Value switch
        {
            "terraria" => Library.Confirm(e) with { TitleByHand = "Terraria (main)" },
            "rounds" => e with { State = LibraryState.Ignored },
            _ => e,
        }).ToList();
        using (var store = new LibraryStore(world.Root))
        {
            store.SaveAll(edited);
        }

        using var reopened = new LibraryStore(world.Root);
        var rescanned = Library.Reconcile(reopened.All(), [pc.Discoverer.Discover(terraria), pc.Discoverer.Discover(rounds)], Now.AddDays(1));

        var t = rescanned.Single(e => e.Id.Value == "terraria");
        Assert.Equal(("Terraria (main)", LibraryState.Synced), (t.DisplayTitle, t.State));
        Assert.NotNull(t.Confirmed);
        Assert.Equal(LibraryState.Ignored, rescanned.Single(e => e.Id.Value == "rounds").State);
        Assert.Equal(2, rescanned.Count);
    }

    [Fact]
    public void LIB_08_a_game_that_disappears_is_not_installed_and_keeps_its_rules()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        DiscoveryTests.Write(pc.Folders["<documents>"], "My Games/Terraria/Worlds/home.wld", "a world");
        var terraria = Installed(world, StoreKind.Steam, "Terraria", "Terraria", "105600");
        var library = Library.Reconcile([], [pc.Discoverer.Discover(terraria)], Now).Select(e => Library.Confirm(e)).ToList();

        var gone = Library.Reconcile(library, [], Now.AddDays(1));

        var entry = Assert.Single(gone);
        Assert.False(entry.Installed);
        Assert.NotNull(entry.Confirmed);

        // Installed again somewhere else, it's the same game again.
        var moved = new InstalledGame { Store = StoreKind.Loose, Title = "Terraria", InstallDir = Path.Combine(world.Root, "PC", "installs", "Terraria copy") };
        Directory.CreateDirectory(moved.InstallDir);
        var back = Assert.Single(Library.Reconcile(gone, [pc.Discoverer.Discover(moved)], Now.AddDays(2)));
        Assert.True(back.Installed);
        Assert.Equal(moved.InstallDir, back.InstallDir);
    }

    [Fact]
    public void Saves_of_a_game_not_installed_are_an_entry_that_becomes_installed_when_the_game_is()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        DiscoveryTests.Write(pc.Folders["<documents>"], "My Games/Terraria/Worlds/home.wld", "a world");
        var leftovers = pc.Discoverer.FindUninstalled([]);

        var first = Assert.Single(Library.Reconcile([], [], Now, leftovers: leftovers));
        Assert.Equal(("terraria", false, "<documents>/My Games/Terraria"), (first.Id.Value, first.Installed, first.Proposals[0].Root));

        var terraria = pc.Discoverer.Discover(Installed(world, StoreKind.Steam, "Terraria", "Terraria", "105600"));
        var installed = Assert.Single(Library.Reconcile([first], [terraria], Now.AddDays(1), leftovers: pc.Discoverer.FindUninstalled([terraria])));
        Assert.Equal(("terraria", true, StoreKind.Steam), (installed.Id.Value, installed.Installed, installed.Store));

        // Uninstalled again with its saves gone too: nothing was found for it, and the entry stays.
        var gone = Assert.Single(Library.Reconcile([installed], [], Now.AddDays(2)));
        Assert.Equal((false, 0), (gone.Installed, gone.Proposals.Count));
    }

    [Fact]
    public void LIB_07_a_merged_game_stays_merged_and_its_saves_join_the_other()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        DiscoveryTests.Write(pc.Folders["<roaming>"], "SlayTheSpire2/profile1/progress.save", "act 2");
        DiscoveryTests.Write(pc.Folders["<roaming>"], "Spacewar/screens/1.jpg", "not a save");
        var spire = Installed(world, StoreKind.Loose, "Slay the Spire 2", "Slay the Spire 2");
        var spacewar = Installed(world, StoreKind.Steam, "Spacewar", "Spacewar", "480");
        var library = Library.Reconcile([], [pc.Discoverer.Discover(spire), pc.Discoverer.Discover(spacewar)], Now);

        var (merged, into) = Library.Merge(library.Single(e => e.Id.Value == "spacewar"), library.Single(e => e.Id.Value == "slay-the-spire-2"));
        var rescanned = Library.Reconcile([merged, into], [pc.Discoverer.Discover(spacewar), pc.Discoverer.Discover(spire)], Now.AddDays(1));

        Assert.Equal(GameId.Parse("slay-the-spire-2"), rescanned.Single(e => e.Id.Value == "spacewar").MergedInto);
        var game = rescanned.Single(e => e.Id.Value == "slay-the-spire-2");
        Assert.Equal(spire.InstallDir, game.InstallDir);
        Assert.Contains(game.Proposals, p => p.Root == "<roaming>/Spacewar");
        Assert.Contains(game.Proposals, p => p.Root == "<roaming>/SlayTheSpire2");
    }

    [Fact]
    public void LIB_10_a_game_its_stores_cloud_syncs_is_confirmed_backup_only()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        DiscoveryTests.Write(pc.Folders["<documents>"], "My Games/Terraria/Worlds/home.wld", "a world");
        var entry = Library.Reconcile([], [pc.Discoverer.Discover(Installed(world, StoreKind.Steam, "Terraria", "Terraria", "105600"))], Now).Single();

        Assert.Equal(GameMode.BackupOnly, Library.Confirm(entry).Confirmed!.Mode);
        Assert.Throws<InvalidOperationException>(() => Library.Confirm(entry with { Proposals = [] }));
    }

    [Fact]
    public void R8_a_changed_proposal_after_confirming_is_a_suggestion_and_the_confirmed_rules_stay()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        DiscoveryTests.Write(pc.Folders["<roaming>"], "SlayTheSpire2/profile1/progress.save", "act 2");
        var spire = Installed(world, StoreKind.Loose, "Slay the Spire 2", "Slay the Spire 2");
        var confirmed = Library.Reconcile([], [pc.Discoverer.Discover(spire)], Now).Select(e => Library.Confirm(e)).ToList();

        DiscoveryTests.Write(pc.Folders["<documents>"], "SlayTheSpire2/extra.save", "a second place");
        var rescanned = Assert.Single(Library.Reconcile(confirmed, [pc.Discoverer.Discover(spire)], Now.AddDays(1)));

        Assert.True(rescanned.HasSuggestion);
        Assert.Equal(["<roaming>/SlayTheSpire2"], rescanned.Confirmed!.Roots.Values);
    }

    /// <summary>
    /// The point of it all: each PC finds the game in its own folders, both confirm the same rules, and the save moves
    /// between them (FIND-08 with detection).
    /// </summary>
    [Fact]
    public async Task Two_PCs_that_each_found_a_game_themselves_sync_it()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        var onDesktop = FakePc(world, "DESKTOP-scan");
        var onLaptop = FakePc(world, "LAPTOP-scan");
        DiscoveryTests.Write(onDesktop.Folders["<documents>"], "My Games/Terraria/Worlds/home.wld", "desktop's world");
        Directory.CreateDirectory(Path.Combine(onLaptop.Folders["<documents>"], "My Games", "Terraria"));
        DiscoveryTests.Write(onLaptop.Folders["<documents>"], "My Games/Terraria/placeholder.txt", "fresh install");

        var desktopGame = Library.Confirm(Library.Reconcile([], [onDesktop.Discoverer.Discover(Installed(world, StoreKind.Loose, "Terraria", "Terraria", pc: "DESKTOP-scan"))], Now).Single());
        var laptopGame = Library.Confirm(Library.Reconcile([], [onLaptop.Discoverer.Discover(Installed(world, StoreKind.Loose, "Terraria", "Terraria", pc: "LAPTOP-scan"))], Now).Single());
        Assert.Equal(desktopGame.Confirmed!.Roots, laptopGame.Confirmed!.Roots);

        desktop.AddResolvedGame(desktopGame.Confirmed!, onDesktop.Folders);
        laptop.AddResolvedGame(laptopGame.Confirmed!, onLaptop.Folders);
        await desktop.SyncAsync();
        var result = Assert.Single(await laptop.SyncAsync());

        Assert.Equal(SyncAction.Download, result.Action);
        Assert.Equal("desktop's world", File.ReadAllText(Path.Combine(onLaptop.Folders["<documents>"], "My Games", "Terraria", "Worlds", "home.wld")));
    }
}
