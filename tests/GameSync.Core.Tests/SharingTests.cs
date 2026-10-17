using System.IO.Compression;
using System.Text.Json;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Sync;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>Sharing saves (SHARE-01 to SHARE-13, R16): a zip of picked versions, and a shared zip's saves imported as pinned versions.</summary>
public class SharingTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly GameId Lantern = GameId.Parse("lantern-keep");

    [Fact]
    public async Task SHARE_01_SHARE_08_a_zip_holds_the_versions_picked_a_manifest_and_a_README()
    {
        using var world = new TestWorld();
        var (data, saves) = await SyncedGame(world, "data");
        File.WriteAllText(Path.Combine(saves, "slot1.sav"), "past the lighthouse");
        await AppActions.BackUpNowAsync(data, Lantern, new Quiet(), Ct);

        var game = Assert.Single(await Sharing.ListAsync(data, Ct));
        Assert.Null(game.Blocked);
        Assert.Equal(2, game.Versions.Count);
        Assert.True(game.Latest!.Current);
        Assert.Equal("Current", game.Latest.Label);

        var zipPath = Sharing.ZipPathIn(world.Root, new DateTime(2026, 10, 1));
        Assert.EndsWith("GameSync-saves-2026-10-01.zip", zipPath);
        var progress = new Collect<(long Done, long Total)>();
        var pack = await Sharing.PackAsync(data, [new SharePick(Lantern, game.Versions.Select(v => v.Id).ToList())], zipPath, progress, new Quiet(), Ct);
        Assert.Equal((1, 2), (pack.Games, pack.Versions));
        Assert.Empty(pack.Notes);
        Assert.Equal(progress.Seen[^1].Total, progress.Seen[^1].Done);
        Assert.EndsWith("GameSync-saves-2026-10-01 (2).zip", Sharing.ZipPathIn(world.Root, new DateTime(2026, 10, 1)));

        using var zip = ZipFile.OpenRead(zipPath);
        var manifest = JsonSerializer.Deserialize<ShareManifest>(zip.GetEntry(ShareManifest.FileName)!.Open(), Json.Options)!;
        var shared = Assert.Single(manifest.Games);
        Assert.Equal(("Lantern Keep", 2), (shared.Title, shared.Versions.Count));
        var newest = shared.Versions[^1];
        var file = Assert.Single(newest.Files, f => f.Path.EndsWith("slot1.sav", StringComparison.Ordinal));
        using (var reader = new StreamReader(zip.GetEntry($"{shared.Folder}/{newest.Folder}/{file.Path}")!.Open()))
        {
            Assert.Equal("past the lighthouse", reader.ReadToEnd());
        }

        using var readme = new StreamReader(zip.GetEntry(ShareManifest.ReadmeName)!.Open());
        var text = readme.ReadToEnd();
        Assert.Contains("Import saves", text);
        Assert.Contains("By hand, without GameSync", text);
        Assert.Contains("Lantern Keep", text);
    }

    [Fact]
    public async Task SHARE_09_a_cancelled_pack_leaves_nothing_behind()
    {
        using var world = new TestWorld();
        var (data, _) = await SyncedGame(world, "data");
        var game = Assert.Single(await Sharing.ListAsync(data, Ct));
        var zipPath = Path.Combine(world.Root, "shared.zip");
        using var cancel = new CancellationTokenSource();
        var progress = new Cancelling(cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Sharing.PackAsync(data, [new SharePick(Lantern, [game.Latest!.Id])], zipPath, progress, new Quiet(), cancel.Token));
        Assert.False(File.Exists(zipPath));
        Assert.False(File.Exists(zipPath + ".part"));
    }

    [Fact]
    public async Task SHARE_07_R16_a_game_with_an_anti_cheat_is_locked_and_never_packed()
    {
        using var world = new TestWorld();
        var (data, _) = await SyncedGame(world, "data");
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([library.All().Single(e => e.Id == Lantern) with { AntiCheat = "EasyAntiCheat" }]);
        }

        var game = Assert.Single(await Sharing.ListAsync(data, Ct));
        Assert.Equal("Ships an anti-cheat, so its saves can't be shared.", game.Blocked);
        var zipPath = Path.Combine(world.Root, "shared.zip");
        await Assert.ThrowsAsync<UsageException>(() => Sharing.PackAsync(data, [new SharePick(Lantern, [game.Latest!.Id])], zipPath, null, new Quiet(), Ct));
        Assert.False(File.Exists(zipPath));
    }

    [Fact]
    public async Task R16_a_shared_zip_holding_a_game_with_an_anti_cheat_or_that_plays_only_online_cant_be_imported()
    {
        using var world = new TestWorld();
        var (data, _) = await SyncedGame(world, "data");
        var game = Assert.Single(await Sharing.ListAsync(data, Ct));
        var zipPath = Path.Combine(world.Root, "shared.zip");
        await Sharing.PackAsync(data, [new SharePick(Lantern, [game.Latest!.Id])], zipPath, null, new Quiet(), Ct);

        // On a friend's PC the game ships an anti-cheat: such a zip only comes from outside GameSync, which never packs it.
        var (friend, friendSaves) = FriendWith(world, e => e with { AntiCheat = "EasyAntiCheat" });
        var item = Assert.Single(Sharing.Preview(friend, zipPath).Games);
        Assert.Equal((ImportMatch.Blocked, "Has an anti-cheat, so its saves stay with the account that made them."), (item.Match, item.Warn));
        await Assert.ThrowsAsync<UsageException>(() => Sharing.ImportAsync(friend, zipPath, [Lantern], new Quiet(), Ct));
        Assert.Equal("the friend's own run", File.ReadAllText(Path.Combine(friendSaves, "slot1.sav")));

        // One that plays only online, the same.
        using (var library = new LibraryStore(friend))
        {
            library.SaveAll([library.All().Single() with { AntiCheat = null, ProbablyOnlineOnly = true }]);
        }

        item = Assert.Single(Sharing.Preview(friend, zipPath).Games);
        Assert.Equal((ImportMatch.Blocked, "Plays only online, so its saves stay with the account that made them."), (item.Match, item.Warn));

        // And a game with neither, a world or a single-player save, imports (SHARE-13 has the rest).
        using (var library = new LibraryStore(friend))
        {
            library.SaveAll([library.All().Single() with { ProbablyOnlineOnly = false }]);
        }

        Assert.Equal(ImportMatch.NotSyncing, Assert.Single(Sharing.Preview(friend, zipPath).Games).Match);
    }

    [Fact]
    public async Task R16_a_shared_save_is_never_restored_once_its_game_turns_out_to_have_an_anti_cheat()
    {
        using var world = new TestWorld();
        var (data, saves) = await SyncedGame(world, "data");
        var game = Assert.Single(await Sharing.ListAsync(data, Ct));
        var zipPath = Path.Combine(world.Root, "shared.zip");
        await Sharing.PackAsync(data, [new SharePick(Lantern, [game.Latest!.Id])], zipPath, null, new Quiet(), Ct);

        // Brought in while GameSync didn't know the game has one (not installed then, or found later). It's kept with the
        // zip's rules (R8), which on this one test PC name the first PC's folder: the folder its restores write to.
        var (friend, _) = FriendWith(world, e => e);
        Assert.StartsWith("Imported 1 save into 1 game as named saves.", await Sharing.ImportAsync(friend, zipPath, [Lantern], new Quiet(), Ct));
        var shared = Assert.Single((await GameDetails.ReadAsync(friend, Lantern, Ct))!.Versions, v => v.Note?.StartsWith("Imported ", StringComparison.Ordinal) == true);
        var slot = Path.Combine(saves, "slot1.sav");
        File.WriteAllText(slot, "the friend's run");
        using (var library = new LibraryStore(friend))
        {
            library.SaveAll([library.All().Single() with { AntiCheat = "BattlEye" }]);
        }

        // The app's Restore says why, on the game, and nothing changes.
        var output = new Recorded();
        var problem = await AppActions.RestoreAsync(friend, Lantern, shared.Id, null, output, Ct);
        Assert.Equal("Lantern Keep: that save came from a shared zip. Has an anti-cheat, so its saves stay with the account that made them. Nothing was restored.", problem);
        Assert.Contains(output.NeedsYouLines, line => line.Contains("came from a shared zip", StringComparison.Ordinal));
        Assert.Equal("the friend's run", File.ReadAllText(slot));
        Assert.DoesNotContain((await GameDetails.ReadAsync(friend, Lantern, Ct))!.Versions, v => v.Note?.StartsWith("Restored", StringComparison.Ordinal) == true);

        // Its own saves still restore as ever.
        await AppActions.BackUpNowAsync(friend, Lantern, new Quiet(), Ct);
        var own = Assert.Single((await GameDetails.ReadAsync(friend, Lantern, Ct))!.Versions, v => v.Note?.StartsWith("Imported ", StringComparison.Ordinal) != true);
        File.WriteAllText(slot, "a later run");
        Assert.Null(await AppActions.RestoreAsync(friend, Lantern, own.Id, null, new Quiet(), Ct));
        Assert.Equal("the friend's run", File.ReadAllText(slot));
    }

    [Fact]
    public async Task SHARE_12_a_shared_save_goes_into_this_PCs_account_folder_and_never_takes_another_accounts_away()
    {
        // The owner's friend's Dark Souls, 8 Oct 2026: a save shared from another PC sat in that PC's Steam ID folder, so
        // restoring it put it where this PC's game never looks, and took this PC's own save, in its own folder, away.
        using var world = new TestWorld();
        var saves = Path.Combine(world.Root, "Lantern Keep", "Saves");
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        Write(Path.Combine(saves, "0110000100000666", "slot1.sav"), "the sharer's run");
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([Entry(RootResolver.ToPortable(saves, Cli.FoldersForThisPc()))]);
        }

        Assert.True(await AppActions.SyncGameAsync(data, Lantern, new Quiet(), Ct));
        await AppActions.BackUpNowAsync(data, Lantern, new Quiet(), Ct);
        var game = Assert.Single(await Sharing.ListAsync(data, Ct));
        var zipPath = Path.Combine(world.Root, "shared.zip");
        await Sharing.PackAsync(data, [new SharePick(Lantern, [game.Latest!.Id])], zipPath, null, new Quiet(), Ct);

        // The friend's PC: the same folder holds only their own account's save.
        Directory.Delete(Path.Combine(saves, "0110000100000666"), recursive: true);
        Write(Path.Combine(saves, "76561198000000001", "slot1.sav"), "the friend's run");
        var friend = Path.Combine(world.Root, "friend");
        new AppConfig { Remote = Directory.CreateDirectory(Path.Combine(world.Root, "friend-cloud")).FullName, Games = [] }.Save(friend);
        using (var library = new LibraryStore(friend))
        {
            library.SaveAll([Entry(RootResolver.ToPortable(saves, Cli.FoldersForThisPc()))]);
        }

        Assert.True(await AppActions.SyncGameAsync(friend, Lantern, new Quiet(), Ct));
        await AppActions.BackUpNowAsync(friend, Lantern, new Quiet(), Ct);
        Assert.StartsWith("Imported 1 save into 1 game", await Sharing.ImportAsync(friend, zipPath, [Lantern], new Quiet(), Ct));
        var shared = Assert.Single((await GameDetails.ReadAsync(friend, Lantern, Ct))!.Versions, v => v.Note?.StartsWith("Imported ", StringComparison.Ordinal) == true);

        // Restored: in the friend's own account folder, which their game reads, with no folder of the sharer's.
        Assert.Null(await AppActions.RestoreAsync(friend, Lantern, shared.Id, null, new Quiet(), Ct));
        Assert.Equal("the sharer's run", File.ReadAllText(Path.Combine(saves, "76561198000000001", "slot1.sav")));
        Assert.False(Directory.Exists(Path.Combine(saves, "0110000100000666")));

        // With two accounts here, which one is the friend's can't be told: it goes into its own folder, and neither account's
        // save is taken away.
        Write(Path.Combine(saves, "76561198000000002", "slot1.sav"), "a sibling's run");
        File.WriteAllText(Path.Combine(saves, "76561198000000001", "slot1.sav"), "the friend's next run");
        Assert.Null(await AppActions.RestoreAsync(friend, Lantern, shared.Id, null, new Quiet(), Ct));
        Assert.Equal("the sharer's run", File.ReadAllText(Path.Combine(saves, "0110000100000666", "slot1.sav")));
        Assert.Equal("the friend's next run", File.ReadAllText(Path.Combine(saves, "76561198000000001", "slot1.sav")));
        Assert.Equal("a sibling's run", File.ReadAllText(Path.Combine(saves, "76561198000000002", "slot1.sav")));
    }

    /// <summary>A friend's PC that has Lantern Keep with a save of its own, not kept by GameSync yet, its entry changed by <paramref name="change"/>.</summary>
    private static (string Data, string Saves) FriendWith(TestWorld world, Func<LibraryEntry, LibraryEntry> change)
    {
        var friend = Path.Combine(world.Root, "friend");
        var cloud = Directory.CreateDirectory(Path.Combine(world.Root, "friend-cloud")).FullName;
        new AppConfig { Remote = cloud, Games = [] }.Save(friend);
        var friendSaves = Path.Combine(world.Root, "Friend", "Lantern Keep", "Saves");
        Write(Path.Combine(friendSaves, "slot1.sav"), "the friend's own run");
        using (var library = new LibraryStore(friend))
        {
            library.SaveAll([change(Entry(RootResolver.ToPortable(friendSaves, Cli.FoldersForThisPc())))]);
        }

        return (friend, friendSaves);
    }

    [Fact]
    public async Task SHARE_10_imports_are_named_saves_with_a_tag_and_one_used_now_goes_in_after_the_save_there_is_kept()
    {
        // Design system version 57 (a friend, 8 Oct 2026: "would you like to backup your current save? and then you can
        // replace the save or import as named save. It should be additive", and a tag such as "jawad" after the PC's name).
        using var world = new TestWorld();
        var (data, saves) = await SyncedGame(world, "data");
        Assert.Null(await AppActions.SaveAsAsync(data, Lantern, "Before the Warden", new Quiet(), Ct));
        var game = Assert.Single(await Sharing.ListAsync(data, Ct));
        var zipPath = Path.Combine(world.Root, "shared.zip");
        await Sharing.PackAsync(data, [new SharePick(Lantern, [game.Latest!.Id])], zipPath, null, new Quiet(), Ct);

        // The friend's PC keeps the game already, in the same folder as the sharer's (as Documents\My Games is on every PC),
        // with their own save in it, which a save used now goes into.
        var friend = Path.Combine(world.Root, "laptop");
        new AppConfig { Remote = Directory.CreateDirectory(Path.Combine(world.Root, "laptop-cloud")).FullName, Games = [] }.Save(friend);
        var friendSaves = saves;
        Write(Path.Combine(friendSaves, "slot1.sav"), "the friend's own run");
        using (var library = new LibraryStore(friend))
        {
            library.SaveAll([Entry(RootResolver.ToPortable(friendSaves, Cli.FoldersForThisPc()))]);
        }

        var told = new Recorded();
        Assert.True(await AppActions.SyncGameAsync(friend, Lantern, told, Ct), string.Join(" | ", told.NeedsYouLines));
        await AppActions.BackUpNowAsync(friend, Lantern, new Quiet(), Ct);
        var preview = Sharing.Preview(friend, zipPath);
        Assert.True(Assert.Single(preview.Games).CanUseNow);
        var said = await Sharing.ImportAsync(friend, zipPath, [Lantern], new Quiet(), Ct, useNow: [Lantern], tag: "  Sam\n ");
        Assert.Equal("Imported 1 save into 1 game as named saves, tagged Sam. Lantern Keep is using the imported save now; your own is kept as a named save, Before the import.", said);

        // The shared save is in place, named as the sharer named it, tagged, from the sharer's PC; the friend's own save is
        // the named save Before the import, which brings it back.
        var slot = Path.Combine(friendSaves, "slot1.sav");
        Assert.Equal("at the lighthouse", File.ReadAllText(slot));
        var detail = (await GameDetails.ReadAsync(friend, Lantern, Ct))!;
        var shared = Assert.Single(detail.NamedSaves, n => n.Name == "Before the Warden");
        Assert.Equal(("Sam", preview.PackedOn), (shared.Tag, shared.Pc));
        Assert.Equal("Sam", Assert.Single(detail.Versions, v => v.Id == shared.Version).Tag);
        var before = Assert.Single(detail.NamedSaves, n => n.Name == Sharing.BeforeImport);
        Assert.Null(before.Tag);
        Assert.Null(await AppActions.RestoreAsync(friend, Lantern, before.Version, before.Name, new Quiet(), Ct));
        Assert.Equal("the friend's own run", File.ReadAllText(slot));

        // Imported again: nothing new, but used now it still goes in place, the friend's save kept first once more.
        said = await Sharing.ImportAsync(friend, zipPath, [Lantern], new Quiet(), Ct, useNow: [Lantern]);
        Assert.StartsWith("Nothing new was added: the saves were in their histories already. Lantern Keep is using the imported save now;", said);
        Assert.Equal("at the lighthouse", File.ReadAllText(slot));

        // Kept as a named save, the default: nothing replaces the save there.
        using var otherWorld = new TestWorld();
        var (other, otherSaves) = FriendWith(otherWorld, e => e);
        Assert.StartsWith("Imported 1 save into 1 game as named saves. None of them replaces a save you have", await Sharing.ImportAsync(other, zipPath, [Lantern], new Quiet(), Ct));
        Assert.Equal("the friend's own run", File.ReadAllText(Path.Combine(otherSaves, "slot1.sav")));
        Assert.Single((await GameDetails.ReadAsync(other, Lantern, Ct))!.NamedSaves, n => n.Name == "Before the Warden");
        Assert.Equal("Sam", SyncService.CleanTag(" Sam\t"));
        Assert.Equal(new string('x', 24), SyncService.CleanTag(new string('x', 30)));
        Assert.Null(SyncService.CleanTag(" \n "));
    }

    [Fact]
    public void SHARE_10_the_import_dialog_offers_use_it_now_for_an_installed_game_here_and_a_tag()
    {
        var here = new ImportGamePreview(Lantern, Lantern, "Lantern Keep", ImportMatch.Matched, 1, 2048, null, 0);
        var two = new ImportGamePreview(GameId.Parse("terraria"), GameId.Parse("terraria"), "Terraria", ImportMatch.Matched, 2, 4096, null, 0);
        var away = new ImportGamePreview(GameId.Parse("hades"), GameId.Parse("hades"), "Hades", ImportMatch.NotSyncing, 1, 1024, null, 0, Installed: false);
        var missing = new ImportGamePreview(GameId.Parse("celeste"), null, "Celeste", ImportMatch.NotInstalled, 1, 1024, null, 0);
        (IReadOnlyList<GameId> Games, IReadOnlyCollection<GameId> UseNow, string? Tag)? asked = null;
        var actions = new ShareActions
        {
            Preview = (zip, _) => Task.FromResult(new ImportPreview(zip, DateTime.UtcNow, "SAM-PC", [here, two, away, missing])),
            Import = (_, games, useNow, tag) =>
            {
                asked = (games, useNow, tag);
                return Task.FromResult(new Outcome("Imported."));
            },
        };
        var dialog = new ImportSavesViewModel(actions, "shared.zip");
        dialog.Load();

        Assert.Equal([true, true, false, false], dialog.Rows.Select(r => r.ShowsUse));
        Assert.Equal("Kept as a named save · from SAM-PC", dialog.Rows[0].Meta);
        Assert.Equal("2 saves, kept as named saves", dialog.Rows[1].Meta);
        Assert.Equal("Not installed here · kept as a named save · backed up from now on", dialog.Rows[2].Meta);
        Assert.Equal("Not installed here · kept as a named save until the game is found", dialog.Rows[3].Meta);
        Assert.Equal(("4 games · 5 saves, kept as named saves", "Import 5 saves"), (dialog.Summary, dialog.AddLabel));
        Assert.EndsWith("Nothing replaces your current saves.", dialog.Note);

        dialog.Rows[1].Use = ImportRow.Now;
        Assert.Equal("2 saves, the newest used now · your save in place is kept first, as a named save", dialog.Rows[1].Meta);
        Assert.Equal("4 games · 5 saves, 1 used now", dialog.Summary);
        Assert.EndsWith("A save used now is put in place only after your own is kept as a named save.", dialog.Note);

        // + Add a tag opens the field; Done keeps it as a chip, Change opens it again.
        Assert.Equal(("Add a tag", "plus", false), (dialog.TagLabel, dialog.TagIcon, dialog.ShowsTagChip));
        dialog.ToggleTagCommand.Execute(null);
        dialog.Tag = "  Jawad ";
        Assert.Equal(("Done", "check"), (dialog.TagLabel, dialog.TagIcon));
        dialog.ToggleTagCommand.Execute(null);
        Assert.Equal(("Jawad", true, "Change", "pencil"), (dialog.TagShown, dialog.ShowsTagChip, dialog.TagLabel, dialog.TagIcon));

        dialog.Rows[3].Ticked = false;
        dialog.AddCommand.Execute(null);
        Assert.Equal([Lantern, GameId.Parse("terraria"), GameId.Parse("hades")], asked!.Value.Games);
        Assert.Equal([GameId.Parse("terraria")], asked.Value.UseNow);
        Assert.Equal("Jawad", asked.Value.Tag);
    }

    [Fact]
    public async Task SHARE_10_SHARE_13_imported_saves_join_the_history_pinned_and_never_current()
    {
        using var world = new TestWorld();
        var (data, _) = await SyncedGame(world, "data");
        var game = Assert.Single(await Sharing.ListAsync(data, Ct));
        var zipPath = Path.Combine(world.Root, "shared.zip");
        await Sharing.PackAsync(data, [new SharePick(Lantern, [game.Latest!.Id])], zipPath, null, new Quiet(), Ct);

        // The same PC: its own save is in its history already, so nothing is added twice.
        var same = Sharing.Preview(data, zipPath);
        Assert.Equal(ImportMatch.Matched, Assert.Single(same.Games).Match);
        Assert.StartsWith("Nothing was added.", await Sharing.ImportAsync(data, zipPath, [Lantern], new Quiet(), Ct));

        // A friend's PC that has the game but doesn't keep its saves yet: it's kept, and the save joins its history, pinned.
        var friend = Path.Combine(world.Root, "friend");
        new AppConfig { Remote = Path.Combine(world.Root, "friend-cloud"), Games = [] }.Save(friend);
        var friendSaves = Path.Combine(world.Root, "Friend", "Lantern Keep", "Saves");
        Write(Path.Combine(friendSaves, "slot1.sav"), "the friend's own run");
        using (var library = new LibraryStore(friend))
        {
            library.SaveAll([Entry(RootResolver.ToPortable(friendSaves, Cli.FoldersForThisPc()))]);
        }

        var preview = Sharing.Preview(friend, zipPath);
        var item = Assert.Single(preview.Games);
        Assert.Equal((ImportMatch.NotSyncing, 1), (item.Match, item.Versions));
        Assert.StartsWith("Imported 1 save into 1 game as named saves.", await Sharing.ImportAsync(friend, zipPath, [Lantern], new Quiet(), Ct));
        var detail = (await GameDetails.ReadAsync(friend, Lantern, Ct))!;
        var imported = Assert.Single(detail.Versions);
        Assert.StartsWith("Imported ", imported.Note);
        Assert.Equal("the friend's own run", File.ReadAllText(Path.Combine(friendSaves, "slot1.sav")));
    }

    [Fact]
    public async Task SHARE_13_a_game_this_PC_doesnt_have_joins_its_library_and_its_saves_wait_until_it_is_found()
    {
        using var world = new TestWorld();
        var (data, saves) = await SyncedGame(world, "data");
        var game = Assert.Single(await Sharing.ListAsync(data, Ct));
        var zipPath = Path.Combine(world.Root, "shared.zip");
        await Sharing.PackAsync(data, [new SharePick(Lantern, [game.Latest!.Id])], zipPath, null, new Quiet(), Ct);

        // A friend's PC with no Lantern Keep at all: it can be imported, and the save waits there.
        var friend = Path.Combine(world.Root, "friend");
        new AppConfig { Remote = Path.Combine(world.Root, "friend-cloud"), Games = [] }.Save(friend);
        var item = Assert.Single(Sharing.Preview(friend, zipPath).Games);
        Assert.Equal((ImportMatch.NotInstalled, (GameId?)null, (string?)null), (item.Match, item.LocalId, item.Warn));
        var said = await Sharing.ImportAsync(friend, zipPath, [Lantern], new Quiet(), Ct);
        Assert.StartsWith("Imported 1 save into 1 game as named saves.", said);
        Assert.Contains("Lantern Keep isn't installed here, so its saves wait in GameSync", said);
        Assert.StartsWith("Imported ", Assert.Single((await GameDetails.ReadAsync(friend, Lantern, Ct))!.Versions).Note);
        Assert.Equal("at the lighthouse", File.ReadAllText(Path.Combine(saves, "slot1.sav")));
        Assert.Equal(ImportMatch.Matched, Assert.Single(Sharing.Preview(friend, zipPath).Games).Match);

        // In the library, Not installed, kept with the shared save's rules and backed up only.
        LibraryEntry entry;
        using (var library = new LibraryStore(friend))
        {
            entry = Assert.Single(library.All());
        }

        Assert.Equal((Lantern, false, LibraryState.Synced, GameMode.BackupOnly), (entry.Id, entry.Installed, entry.State, entry.Confirmed!.Mode));
        Assert.Equal(RootResolver.ToPortable(saves, Cli.FoldersForThisPc()), entry.Confirmed.Roots.Values.Single());

        // Installed later: a scan finds it by its title, and the rules stay (LIB-07, R8).
        var folder = Path.Combine(world.Root, "Games", "Lantern Keep");
        Directory.CreateDirectory(folder);
        var installed = new InstalledGame { Store = StoreKind.Loose, Title = "Lantern Keep", InstallDir = folder };
        var scanned = Assert.Single(Library.Reconcile([entry], [new DiscoveredGame { Installed = installed, Title = "Lantern Keep", Print = Fingerprinter.Read(folder) }], DateTime.UtcNow));
        Assert.Equal((Lantern, true, folder), (scanned.Id, scanned.Installed, scanned.InstallDir));
        Assert.Equal(entry.Confirmed, scanned.Confirmed);
    }

    [Fact]
    public async Task SHARE_13_R5_a_game_whose_shared_rules_reach_a_protected_folder_cant_be_imported_and_says_why()
    {
        using var world = new TestWorld();
        var (data, _) = await SyncedGame(world, "data");
        var game = Assert.Single(await Sharing.ListAsync(data, Ct));
        var zipPath = Path.Combine(world.Root, "shared.zip");
        await Sharing.PackAsync(data, [new SharePick(Lantern, [game.Latest!.Id])], zipPath, null, new Quiet(), Ct);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry(ShareManifest.FileName)!;
            ShareManifest manifest;
            using (var json = entry.Open())
            {
                manifest = JsonSerializer.Deserialize<ShareManifest>(json, Json.Options)!;
            }

            entry.Delete();
            var shared = manifest.Games[0];
            var rules = shared.Rules!;
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            manifest = manifest with { Games = [shared with { Rules = rules with { Roots = rules.Roots.ToDictionary(r => r.Key, _ => Path.Combine(windows, "System32")) } }] };
            using var write = zip.CreateEntry(ShareManifest.FileName).Open();
            JsonSerializer.Serialize(write, manifest, Json.Options);
        }

        var friend = Path.Combine(world.Root, "friend");
        new AppConfig { Remote = Path.Combine(world.Root, "friend-cloud"), Games = [] }.Save(friend);
        var item = Assert.Single(Sharing.Preview(friend, zipPath).Games);
        Assert.Equal(ImportMatch.Unknown, item.Match);
        Assert.NotNull(item.Warn);
        await Assert.ThrowsAsync<UsageException>(() => Sharing.ImportAsync(friend, zipPath, [Lantern], new Quiet(), Ct));
        using var library = new LibraryStore(friend);
        Assert.Empty(library.All());
    }

    [Fact]
    public void SHARE_10_a_zip_dropped_on_the_window_is_taken_when_it_comes_alone()
    {
        const string zip = @"C:\Users\You\Downloads\GameSync-saves-2026-10-01.zip";
        Assert.Equal(zip, GameSync.UI.Views.MainWindow.ZipOf([zip]));
        Assert.Equal(@"D:\Shared\SAVES.ZIP", GameSync.UI.Views.MainWindow.ZipOf([@"D:\Shared\SAVES.ZIP"]));
        Assert.Null(GameSync.UI.Views.MainWindow.ZipOf([zip, @"C:\Users\You\Downloads\other.zip"]));
        Assert.Null(GameSync.UI.Views.MainWindow.ZipOf([@"C:\Users\You\Downloads\slot1.sav"]));
        Assert.Null(GameSync.UI.Views.MainWindow.ZipOf([null]));
        Assert.Null(GameSync.UI.Views.MainWindow.ZipOf([]));
    }

    [Fact]
    public void MGR_02_Select_all_ticks_every_game_that_syncs_or_none_once_they_all_are()
    {
        LauncherGame Game(string id, bool syncs = true) => new() { Id = GameId.Parse(id), Title = id, Status = syncs ? GameStatus.Synced : null, Syncs = syncs };
        var saves = new GameSync.UI.ViewModels.SaveManagerViewModel(new GameSync.UI.ViewModels.LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { }));
        saves.Update([Game("hades"), Game("celeste"), Game("balatro"), Game("terraria", syncs: false)]);
        Assert.True(saves.SyncingSort.Selectable);
        Assert.False(saves.AllSort.Selectable);
        Assert.False(saves.AllSelected);

        saves.SelectAllCommand.Execute(null);
        Assert.True(saves.AllSelected);
        Assert.Equal("Share 3 selected", saves.ShareSelectedLabel);
        Assert.All(saves.SyncingRows, r => Assert.True(r.Selected));

        // One unticked: a dash; Select all ticks them all again, and once more ticks none.
        saves.SelectCommand.Execute(GameId.Parse("celeste"));
        Assert.Null(saves.AllSelected);
        saves.SelectAllCommand.Execute(null);
        Assert.True(saves.AllSelected);
        saves.SelectAllCommand.Execute(null);
        Assert.False(saves.AllSelected);
        Assert.False(saves.HasSelection);
    }

    [Fact]
    public async Task SHARE_11_a_zip_whose_paths_leave_their_folder_or_hide_a_program_is_turned_away()
    {
        using var world = new TestWorld();
        var (data, _) = await SyncedGame(world, "data");
        var game = Assert.Single(await Sharing.ListAsync(data, Ct));
        var zipPath = Path.Combine(world.Root, "shared.zip");
        await Sharing.PackAsync(data, [new SharePick(Lantern, [game.Latest!.Id])], zipPath, null, new Quiet(), Ct);

        // Another PC's save, as if made elsewhere: a path out of its folder, and a program named like a save.
        var evil = Path.Combine(world.Root, "evil.zip");
        File.Copy(zipPath, evil);
        using (var zip = ZipFile.Open(evil, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry(ShareManifest.FileName)!;
            ShareManifest manifest;
            using (var json = entry.Open())
            {
                manifest = JsonSerializer.Deserialize<ShareManifest>(json, Json.Options)!;
            }

            entry.Delete();
            var shared = manifest.Games[0];
            var version = shared.Versions[0];
            var root = version.Files[0].Path.Split('/')[0];
            var program = new byte[512];
            program[0] = (byte)'M';
            program[1] = (byte)'Z';
            BitConverter.GetBytes(128).CopyTo(program, 0x3C);
            "PE\0\0"u8.CopyTo(program.AsSpan(128));
            var exe = new FileEntry($"{root}/other.sav", program.Length, DateTime.UtcNow, BlobId.FromHash(System.Security.Cryptography.SHA256.HashData(program)));
            using (var stream = zip.CreateEntry($"{shared.Folder}/{version.Folder}/{exe.Path}").Open())
            {
                stream.Write(program);
            }

            var escape = version with { Id = VersionId.New(DateTime.UtcNow, "EVIL"), Folder = "escape", Files = [version.Files[0] with { Path = $"{root}/../../outside.sav" }] };
            var withProgram = version with { Files = [.. version.Files, exe] };
            manifest = manifest with { Games = [shared with { Versions = [withProgram, escape] }] };
            using (var json = zip.CreateEntry(ShareManifest.FileName).Open())
            {
                JsonSerializer.Serialize(json, manifest, Json.Options);
            }
        }

        var friend = Path.Combine(world.Root, "friend");
        new AppConfig { Remote = Path.Combine(world.Root, "friend-cloud"), Games = [] }.Save(friend);
        using (var library = new LibraryStore(friend))
        {
            library.SaveAll([Entry(RootResolver.ToPortable(Path.Combine(world.Root, "Friend", "Saves"), Cli.FoldersForThisPc()))]);
        }

        var said = await Sharing.ImportAsync(friend, evil, [Lantern], new Quiet(), Ct);
        Assert.Contains("other.sav was left out: it's a program, not a save.", said);
        Assert.Contains("wasn't added", said);
        var record = Assert.Single(await new GameSync.Core.Storage.LocalHistory(Path.Combine(friend, "history")).Log.ListAsync(Lantern, Ct));
        Assert.DoesNotContain(record.Files, f => f.Path.EndsWith("other.sav", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(world.Root, "outside.sav")));
    }

    [Fact]
    public void SHARE_10_a_zip_GameSync_didnt_make_is_refused_with_the_reason()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        var plain = Path.Combine(world.Root, "plain.zip");
        using (var zip = ZipFile.Open(plain, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("Before Orphan/SPRJ0005/userdata0000").Open());
            writer.Write("a kept copy");
        }

        var refused = Assert.Throws<UsageException>(() => Sharing.Preview(data, plain));
        Assert.Contains("isn't a zip GameSync shared", refused.Message);
        File.WriteAllText(Path.Combine(world.Root, "not.zip"), "not a zip at all");
        Assert.Throws<UsageException>(() => Sharing.Preview(data, Path.Combine(world.Root, "not.zip")));
    }

    [Fact]
    public void SHARE_08_folder_names_in_the_zip_are_ones_Windows_takes()
    {
        Assert.Equal("Sekiro Shadows Die Twice", SyncService.FolderName("Sekiro: Shadows Die Twice", "sekiro"));
        Assert.Equal("2026-09-16 23.58 Before Orphan", SyncService.FolderName("2026-09-16 23.58 Before Orphan", "v1"));
        Assert.Equal("v1", SyncService.FolderName("...", "v1"));
        Assert.Equal("What now", SyncService.FolderName("What now?..", "v1"));
    }

    [Fact]
    public void SHARE_01_SHARE_02_SHARE_04_ticking_a_game_takes_its_latest_save_and_share_all_leaves_locked_games_out()
    {
        var now = DateTime.UtcNow;
        ShareVersionInfo Version(string label, int hoursAgo, bool current = false) =>
            new(VersionId.New(now.AddHours(-hoursAgo), "DESKTOP"), label, now.AddHours(-hoursAgo), "DESKTOP", 1000 * (hoursAgo + 1), label != "After play", current);
        var sekiro = new ShareGameInfo(GameId.Parse("sekiro"), "Sekiro", [Version("Current", 1, current: true), Version("After play", 30), Version("Before update 1.06", 300)], null);
        var gta = new ShareGameInfo(GameId.Parse("gta-v"), "GTA V", [Version("Current", 2, current: true)], "Ships an anti-cheat, so its saves can't be shared.");
        var actions = new GameSync.UI.ViewModels.ShareActions { List = _ => Task.FromResult<IReadOnlyList<ShareGameInfo>>([sekiro, gta]) };

        var share = new GameSync.UI.ViewModels.ShareViewModel(actions, new GameSync.UI.ViewModels.ShareStart(GameSync.UI.ViewModels.ShareStart.Pick, [sekiro.Id]));
        share.Load();
        var row = share.Games.Single(g => g.Id == sekiro.Id);
        Assert.True(row.Ticked);
        Assert.Equal(sekiro.Versions[0].Id, Assert.Single(row.Versions, v => v.Ticked).Info.Id);
        Assert.Equal(1, share.TickedVersions);

        // An older, pinned version as well (SHARE-02); unticking the game leaves none.
        row.Versions[2].Ticked = true;
        Assert.Equal((2, "2 of 3 versions"), (share.TickedVersions, row.Meta));
        row.Ticked = false;
        Assert.Equal(0, share.TickedVersions);
        Assert.False(share.CreateCommand.CanExecute(null) && share.CanCreate);

        // A locked game can't be ticked; Share all names it as left out (SHARE-07).
        var locked = share.Games.Single(g => g.Id == gta.Id);
        locked.Ticked = true;
        Assert.False(locked.Ticked);
        share.Mode = GameSync.UI.ViewModels.ShareStart.All;
        Assert.True(row.Ticked);
        Assert.False(locked.Ticked);
        Assert.Contains("GTA V", share.LeftOut);

        // Export (KAN-23): exactly the version picked, its game open.
        var export = new GameSync.UI.ViewModels.ShareViewModel(actions,
            new GameSync.UI.ViewModels.ShareStart(GameSync.UI.ViewModels.ShareStart.Pick, [sekiro.Id], sekiro.Id, sekiro.Versions[2].Id));
        export.Load();
        var opened = export.Games.Single(g => g.Id == sekiro.Id);
        Assert.True(opened.Expanded);
        Assert.Equal(sekiro.Versions[2].Id, Assert.Single(opened.Versions, v => v.Ticked).Info.Id);
    }

    [Fact]
    public async Task SHARE_12_a_save_made_on_another_Steam_account_is_flagged_before_it_is_added()
    {
        using var world = new TestWorld();
        var (data, _) = await SyncedGame(world, "data");
        var game = Assert.Single(await Sharing.ListAsync(data, Ct));
        var zipPath = Path.Combine(world.Root, "shared.zip");
        await Sharing.PackAsync(data, [new SharePick(Lantern, [game.Latest!.Id])], zipPath, null, new Quiet(), Ct);
        Assert.Null(Assert.Single(Sharing.Preview(data, zipPath).Games).Warn);

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry(ShareManifest.FileName)!;
            ShareManifest manifest;
            using (var json = entry.Open())
            {
                manifest = JsonSerializer.Deserialize<ShareManifest>(json, Json.Options)!;
            }

            entry.Delete();
            var shared = manifest.Games[0];
            manifest = manifest with
            {
                Games = [shared with { Versions = [shared.Versions[0] with { Accounts = new Dictionary<string, string> { ["steamUser"] = "76561198000000001" } }] }],
            };
            using var write = zip.CreateEntry(ShareManifest.FileName).Open();
            JsonSerializer.Serialize(write, manifest, Json.Options);
        }

        Assert.Equal("Made on another Steam account. Some games check the account, so it may not load.", Assert.Single(Sharing.Preview(data, zipPath).Games).Warn);
    }

    /// <summary>A data folder where Lantern Keep syncs, with its first backup made.</summary>
    private static async Task<(string Data, string Saves)> SyncedGame(TestWorld world, string name)
    {
        var data = Path.Combine(world.Root, name);
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        var saves = Path.Combine(world.Root, "Lantern Keep", "Saves");
        Write(Path.Combine(saves, "slot1.sav"), "at the lighthouse");
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([Entry(RootResolver.ToPortable(saves, Cli.FoldersForThisPc()))]);
        }

        Assert.True(await AppActions.SyncGameAsync(data, Lantern, new Quiet(), Ct));
        await AppActions.BackUpNowAsync(data, Lantern, new Quiet(), Ct);
        return (data, saves);
    }

    private static LibraryEntry Entry(string place) => new()
    {
        Id = Lantern,
        Title = "Lantern Keep",
        FirstSeenUtc = DateTime.UtcNow,
        Installed = true,
        Store = StoreKind.Loose,
        Proposals = [new Proposal(FoundBy.SaveList, place, "**", SaveCategory.Save, 1, 20, null)],
    };

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private sealed class Collect<T> : IProgress<T>
    {
        public List<T> Seen { get; } = [];

        public void Report(T value)
        {
            lock (Seen)
            {
                Seen.Add(value);
            }
        }
    }

    /// <summary>Cancels the pack as soon as it says how far it is.</summary>
    private sealed class Cancelling(CancellationTokenSource cancel) : IProgress<(long Done, long Total)>
    {
        public void Report((long Done, long Total) value) => cancel.Cancel();
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

    private sealed class Recorded : IAgentOutput
    {
        public List<string> NeedsYouLines { get; } = [];

        public void Say(string line)
        {
        }

        public void NeedsYou(string title, string message) => NeedsYouLines.Add($"{title}: {message}");
    }
}
