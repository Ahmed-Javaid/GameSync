using System.IO.Compression;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Sync;

namespace GameSync.Core.Tests;

/// <summary>ONB-04: moving from Ludusavi: its ignore list, the games added by hand, and its backups as named saves.</summary>
public class LudusaviTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Its_config_gives_the_backup_folder_the_ignore_list_and_the_games_added_by_hand()
    {
        using var world = new TestWorld();
        var path = Path.Combine(world.Root, "config.yaml");
        File.WriteAllText(path, """
            ---
            roots:
              - store: steam
                path: "D:/Steam"
            backup:
              path: "D:\\Backups\\Ludusavi"
              ignoredGames:
                - Apex Legends
                - "Borderlands: The Pre-Sequel"
              toggledPaths: {}
            customGames:
              - name: Kept By Hand
                integration: override
                files:
                  - "<winDocuments>/Kept By Hand/slots"
                registry: []
                installDir:
              - name: Extended Game
                integration: extend
                files:
                  - "D:/Elsewhere/Extended Game/saves"
                registry:
                  - "HKEY_CURRENT_USER/Software/Extended Game"
              - name: Switched Off
                ignore: true
                files:
                  - "<winDocuments>/Off"
            """);

        var config = LudusaviReader.ReadConfig(path);

        Assert.Equal(@"D:\Backups\Ludusavi", config.BackupFolder);
        Assert.Equal(["Apex Legends", "Borderlands: The Pre-Sequel"], config.IgnoredGames);
        Assert.Equal(["Kept By Hand", "Extended Game"], config.CustomGames.Select(g => g.Name));
        Assert.Equal((false, true), (config.CustomGames[0].Extends, config.CustomGames[1].Extends));
        Assert.Equal(["HKEY_CURRENT_USER/Software/Extended Game"], config.CustomGames[1].Registry);
    }

    [Fact]
    public void Each_games_backups_come_from_its_mapping()
    {
        using var world = new TestWorld();
        var folder = Path.Combine(world.Root, "Ludusavi");
        Mapping(Path.Combine(folder, "Game One"), "Game One", "\".\"", "2026-06-21T16:02:12.362240900Z");
        Mapping(Path.Combine(folder, "Game Two"), "Game Two", "backup-20260620T100000Z.zip", "2026-06-20T10:00:00Z");
        Directory.CreateDirectory(Path.Combine(folder, "Not A Game"));
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(folder, "Broken")).FullName, "mapping.yaml"), "name: [unclosed");

        var problems = new List<string>();
        var sets = LudusaviReader.ReadBackups(folder, problems);

        Assert.Equal(["Game One", "Game Two"], sets.Select(s => s.Title));
        Assert.Equal(new DateTime(2026, 6, 21, 16, 2, 12, 362, DateTimeKind.Utc).AddTicks(2409), sets[0].Backups[0].WhenUtc);
        Assert.Equal("C:", sets[0].Drives["drive-C"]);
        Assert.True(sets[1].Backups[0].IsZip);
        Assert.Contains(problems, p => p.StartsWith("Broken", StringComparison.Ordinal));
    }

    [Fact]
    public void ONB_04_the_ignore_list_ignores_found_games_keeps_synced_ones_and_remembers_the_rest()
    {
        var library = new List<LibraryEntry>
        {
            new() { Id = GameId.Parse("apex-legends"), Title = "Apex Legends" },
            new() { Id = GameId.Parse("terraria"), Title = "Terraria", State = LibraryState.Synced },
        };

        var after = Library.IgnoreTitles(library, ["Apex Legends", "Terraria", "Deadlock"], Now);

        Assert.Equal(LibraryState.Ignored, after.Single(e => e.Id.Value == "apex-legends").State);
        Assert.Equal(LibraryState.Synced, after.Single(e => e.Id.Value == "terraria").State);
        var deadlock = after.Single(e => e.Id.Value == "deadlock");
        Assert.Equal((LibraryState.Ignored, false), (deadlock.State, deadlock.Installed));

        // Installed later, it's found ignored.
        using var world = new TestWorld();
        var pc = DiscoveryTests.FakePc(world);
        var found = pc.Discoverer.Discover(DiscoveryTests.Installed(world, StoreKind.Loose, "Deadlock", "Deadlock"));
        Assert.Equal(LibraryState.Ignored, Library.Reconcile(after, [found], Now.AddDays(1)).Single(e => e.Id.Value == "deadlock").State);
    }

    [Fact]
    public void ONB_04_a_game_added_by_hand_is_checked_joins_its_entry_and_replaces_or_extends_what_was_found()
    {
        using var world = new TestWorld();
        var pc = DiscoveryTests.FakePc(world);
        DiscoveryTests.Write(pc.Folders["<documents>"], "GameX/slots/slot1.sav", "a slot");
        var full = Path.Combine(pc.Folders["<documents>"], "GameX", "slots").Replace('\\', '/');
        var scanned = new LibraryEntry
        {
            Id = GameId.Parse("gamex"),
            Title = "GameX",
            Proposals = [new Proposal(FoundBy.NameSearch, "<documents>/GameX", "**", SaveCategory.Save, 2, 20, null)],
        };

        var byHand = new LudusaviCustomGame("Game X Remastered", Extends: false, [full, "<winDocuments>/Nothing Here"], []);
        var places = pc.Discoverer.FromLudusavi(byHand);
        var joined = Library.WithLudusaviGame([scanned], byHand, places, Now);

        // A full path under Documents became portable, and the path with nothing in it was left out.
        var place = Assert.Single(places);
        Assert.Equal((FoundBy.Ludusavi, "<documents>/GameX/slots", "**"), (place.FoundBy, place.Root, place.Include));
        Assert.Equal("gamex", joined.Id.Value);
        Assert.Equal(["<documents>/GameX/slots"], joined.Proposals.Select(p => p.Root));
        Assert.Contains(SaveList.Normalize("Game X Remastered"), Library.Names(joined));

        var extended = Library.WithLudusaviGame([scanned], byHand with { Extends = true }, places, Now);
        Assert.Equal(["<documents>/GameX", "<documents>/GameX/slots"], extended.Proposals.Select(p => p.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ONB_04_the_latest_backup_becomes_a_named_save_kept_aside_and_the_live_save_is_untouched(bool zipped)
    {
        using var world = new TestWorld();
        using var pc = world.Pc("DESKTOP");
        var game = pc.AddPortableGame("game", "<documents>/Game");
        pc.Write("game", "slot1.sav", "live");
        var folder = Path.Combine(world.Root, "Ludusavi", "Game");
        var live = game.Roots["saves"];
        var inBackup = Path.Combine("drive-" + live[0], live[3..], "slot1.sav");
        if (zipped)
        {
            Directory.CreateDirectory(folder);
            using var zip = ZipFile.Open(Path.Combine(folder, "backup-20260620T100000Z.zip"), ZipArchiveMode.Create);
            using var writer = new StreamWriter(zip.CreateEntry(inBackup.Replace('\\', '/')).Open());
            writer.Write("from ludusavi");
        }
        else
        {
            DiscoveryTests.Write(folder, inBackup, "from ludusavi");
            DiscoveryTests.Write(folder, Path.Combine("drive-" + live[0], live[3..], "debug.log"), "noise");
        }

        Mapping(folder, "Game", zipped ? "backup-20260620T100000Z.zip" : "\".\"", "2026-06-20T10:00:00Z", live[..2]);
        var set = Assert.Single(LudusaviReader.ReadBackups(Path.Combine(world.Root, "Ludusavi"), []));

        var preview = await pc.Service().ImportLudusaviAsync(GameId.Parse("game"), set, apply: false, CancellationToken.None);
        Assert.Equal((0, 1), (preview.Added, Assert.Single(preview.Items).Files));
        var applied = await pc.Service().ImportLudusaviAsync(GameId.Parse("game"), set, apply: true, CancellationToken.None);

        Assert.Equal(1, applied.Added);
        var saved = Assert.Single(await pc.Service().NamedSavesAsync(GameId.Parse("game"), CancellationToken.None));
        Assert.StartsWith("Ludusavi backup (2026-06-20", saved.Name, StringComparison.Ordinal);
        Assert.Equal((VersionKind.Kept, VersionOrigin.Imported), (saved.Version.Kind, saved.Version.Origin));
        Assert.Equal("live", pc.Read("game", "slot1.sav"));

        // Twice is once, and the backup restores by its name.
        Assert.Equal(0, (await pc.Service().ImportLudusaviAsync(GameId.Parse("game"), set, apply: true, CancellationToken.None)).Added);
        await pc.Service().RestoreNamedAsync(GameId.Parse("game"), "Ludusavi backup", CancellationToken.None);
        Assert.Equal("from ludusavi", pc.Read("game", "slot1.sav"));
    }

    [Fact]
    public async Task ONB_04_a_backup_of_a_game_not_synced_here_becomes_history_the_other_PC_is_offered()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = world.Pc("LAPTOP");
        var folder = Path.Combine(world.Root, "Ludusavi", "Old Game");
        var gone = Path.Combine(desktop.KnownFolders["<documents>"], "Old Game");
        DiscoveryTests.Write(folder, Path.Combine("drive-" + gone[0], gone[3..], "slot.sav"), "from long ago");
        Mapping(folder, "Old Game", "\".\"", "2025-01-02T03:04:05Z", gone[..2]);
        var set = Assert.Single(LudusaviReader.ReadBackups(Path.Combine(world.Root, "Ludusavi"), []));
        var rules = new GameDefinition
        {
            Id = GameId.Parse("old-game"),
            Title = "Old Game",
            Roots = new Dictionary<string, string> { ["documents-old-game"] = "<documents>/Old Game" },
            Rules = [new SaveRule { Root = "documents-old-game" }],
        };
        var resolved = new RootResolver(desktop.KnownFolders, desktop.Accounts, desktop.InstallDirs).Resolve(rules);

        var report = await desktop.Service().ImportLudusaviAsync(resolved, set, apply: true, CancellationToken.None);

        Assert.Equal(1, report.Added);
        Assert.False(Directory.Exists(gone));
        var offered = Assert.Single(await laptop.Service().OtherGamesAsync(CancellationToken.None));
        Assert.Equal(("old-game", "Old Game", VersionKind.Kept), (offered.Id.Value, offered.Title, offered.Newest.Kind));
        Assert.Equal("<documents>/Old Game", offered.Rules!.Roots["documents-old-game"]);
    }

    private static void Mapping(string folder, string title, string backupName, string when, string drive = "C:")
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mapping.yaml"), $"""
            ---
            name: {title}
            drives:
              drive-{drive[0]}: "{drive}"
            backups:
              - name: {backupName}
                when: "{when}"
                os: windows
                files:
                  "{drive}/Users/someone/Documents/Game/slot1.sav":
                    hash: 0000
                    size: 13
            """);
    }
}
