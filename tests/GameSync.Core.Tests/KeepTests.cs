using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// KAN-63: named saves, backups and imports before syncing. A game not syncing yet is kept first (backed up only, not
/// synced between PCs), and Sync these saves makes it sync between PCs later.
/// </summary>
public class KeepTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly GameId Bloodborne = GameId.Parse("bloodborne");

    [Fact]
    public async Task KAN_23_add_a_place_on_a_game_not_syncing_yet_starts_it_syncing_with_that_place_alone()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        var found = Path.Combine(world.Root, "Emulator", "savedata", "CUSA00207");
        Write(Path.Combine(found, "SPRJ0005", "userdata0000"), "found by the scan");
        var mine = Path.Combine(world.Root, "Elsewhere", "Bloodborne saves");
        Write(Path.Combine(mine, "userdata0000"), "the real save");
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([new LibraryEntry
            {
                Id = Bloodborne,
                Title = "Bloodborne",
                FirstSeenUtc = DateTime.UtcNow,
                Installed = true,
                Store = StoreKind.Loose,
                InstallDir = Path.Combine(world.Root, "Games", "Bloodborne"),
                Proposals = [new Proposal(FoundBy.NameSearch, RootResolver.ToPortable(found, Cli.FoldersForThisPc()), "**", SaveCategory.Save, 1, 17, null)],
            }]);
        }

        var said = await GameSettings.ApplyAsync(data, Bloodborne, new GamePropertiesChange { AddPlace = mine }, () => { }, Ct);

        Assert.StartsWith("Bloodborne: syncing from now on, a new place, ", said, StringComparison.Ordinal);
        using var engine = Engine.Open(data);
        var game = engine.Games.Single(g => g.Id == Bloodborne);
        Assert.Equal(GameMode.Sync, game.Mode);
        Assert.Equal([Path.GetFullPath(mine)], game.Roots.Values.Select(r => Path.GetFullPath(r)));
        Assert.True(LauncherData.Read(data, DateTime.Now).Games.Single(g => g.Id == Bloodborne).Syncs);
    }

    [Fact]
    public async Task KAN_63_a_named_save_before_syncing_keeps_the_game_backed_up_but_not_synced_between_PCs()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        var live = Path.Combine(world.Root, "Emulator", "savedata", "CUSA00207");
        Write(Path.Combine(live, "SPRJ0005", "userdata0000"), "at the Orphan of Kos lamp");
        var place = RootResolver.ToPortable(live, Cli.FoldersForThisPc());
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([new LibraryEntry
            {
                Id = Bloodborne,
                Title = "Bloodborne",
                FirstSeenUtc = DateTime.UtcNow,
                Installed = true,
                Store = StoreKind.Loose,
                InstallDir = Path.Combine(world.Root, "Games", "Bloodborne"),
                Proposals = [new Proposal(FoundBy.NameSearch, place, "**", SaveCategory.Save, 1, 25, null)],
            }]);
        }

        var output = new Quiet();
        Assert.False(LauncherData.Read(data, DateTime.Now).Games.Single(g => g.Id == Bloodborne).Syncs);

        // Each button says first what keeping it starts with, as the last scan found it.
        var found = (await GameDetails.ReadAsync(data, Bloodborne, Ct))!;
        Assert.Equal((1, 25L), (found.FoundFiles, found.FoundBytes));
        Assert.StartsWith("GameSync starts keeping its saves, 1 file and 25 B as they are now: every version", GameSavesViewModel.KeepNoteFor(found.FoundFiles, found.FoundBytes));
        Assert.StartsWith("GameSync starts keeping its saves, 1,051 files and ", GameSavesViewModel.KeepNoteFor(1051, 1_071_854_387));

        // Save as… on a game not syncing yet: it's kept, backed up only, and the named save is there.
        await AppActions.SaveAsAsync(data, Bloodborne, "Before Orphan of Kos", output, Ct);
        await Uploads.RunAsync(data, null, Ct);
        using (var engine = Engine.Open(data))
        {
            Assert.Equal(GameMode.BackupOnly, engine.Games.Single(g => g.Id == Bloodborne).Mode);
        }

        Assert.Equal("Before Orphan of Kos", Assert.Single((await GameDetails.ReadAsync(data, Bloodborne, Ct))!.NamedSaves).Name);
        var kept = LauncherData.Read(data, DateTime.Now).Games.Single(g => g.Id == Bloodborne);
        Assert.True(kept.Syncs);
        Assert.False(kept.StoreSyncs);
        Assert.Equal(GameStatus.BackupOnly, kept.Status);
        Assert.Equal("Backed up", HomeViewModel.StatusLabel(kept));
        Assert.Equal("Backed up; not synced between your PCs", HomeViewModel.MarkLabel(kept.Status, kept.Store, kept.StoreSyncs));
        Assert.StartsWith("GameSync keeps every version of its saves", GameSavesViewModel.SentenceOf(kept));

        // Keeping again changes nothing; Sync these saves makes it sync between PCs.
        Assert.True(await AppActions.KeepAsync(data, Bloodborne, output, Ct));
        Assert.True(await AppActions.SyncGameAsync(data, Bloodborne, output, Ct));
        using (var engine = Engine.Open(data))
        {
            Assert.Equal(GameMode.Sync, engine.Games.Single(g => g.Id == Bloodborne).Mode);
        }
    }

    [Fact]
    public void KAN_63_a_store_synced_game_keeps_its_store_s_words_while_one_kept_by_choice_says_backed_up()
    {
        LauncherGame Game(bool storeSyncs) => new()
        {
            Id = Bloodborne, Title = "Game", Store = StoreKind.Steam, Status = GameStatus.BackupOnly, Syncs = true, StoreSyncs = storeSyncs,
        };

        Assert.Equal("Synced by Steam", HomeViewModel.StatusLabel(Game(true)));
        Assert.Equal("Backed up", HomeViewModel.StatusLabel(Game(false)));
        Assert.Equal("Backed up; Steam syncs it", HomeViewModel.MarkLabel(GameStatus.BackupOnly, StoreKind.Steam, storeSyncs: true));
        Assert.Contains("Steam syncs its saves", GameSavesViewModel.SentenceOf(Game(true)), StringComparison.Ordinal);
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

        public void NeedsYou(string title, string message)
        {
        }
    }
}
