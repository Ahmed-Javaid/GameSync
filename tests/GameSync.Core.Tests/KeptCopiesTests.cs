using System.IO.Compression;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Sync;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// KAN-61: a save folder holding the live save beside copies kept by hand (the owner's Bloodborne): only the live save
/// syncs, and the copies come in as named saves in the same step. The folders are never changed.
/// </summary>
public class KeptCopiesTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly GameId Bloodborne = GameId.Parse("bloodborne");

    [Fact]
    public void KAN_61_a_live_save_beside_copies_kept_by_hand_is_found_in_a_folder_proposed_whole()
    {
        using var world = new TestWorld();
        var savedata = Savedata(world);
        var kept = KeptCopies.Find(savedata);
        Assert.NotNull(kept);
        Assert.Equal(Path.Combine(savedata, "1", "CUSA00207", "SPRJ0005"), kept.Live);
        Assert.Equal(Path.Combine(savedata, "1", "CUSA00207"), kept.Folder);
        Assert.Equal((6, 2), (kept.Copies, kept.LiveFiles));
        Assert.Equal(2, kept.Names.Count);

        // A save folder with no copies beside it is just a save folder.
        var plain = Path.Combine(world.Root, "Plain", "savedata");
        Write(Path.Combine(plain, "1", "CUSA00207", "SPRJ0005", "userdata0000"), "only the live save");
        Write(Path.Combine(plain, "1", "sce_sdmemory", "memory.dat"), "the emulator's own");
        Assert.Null(KeptCopies.Find(plain));
    }

    [Fact]
    public async Task KAN_61_Sync_these_saves_keeps_only_the_live_save_and_brings_the_copies_in_as_named_saves()
    {
        using var world = new TestWorld();
        var savedata = Savedata(world);
        var data = Setup(world, savedata);

        // The page shows the live save alone, with the copies beside it, and keeping it starts with the live save.
        var page = (await GameDetails.ReadAsync(data, Bloodborne, Ct))!;
        Assert.NotNull(page.Kept);
        Assert.Equal(Path.Combine(savedata, "1", "CUSA00207", "SPRJ0005"), page.Places[0].Folder);
        Assert.Equal((2, 6), (page.FoundFiles, page.Kept!.Copies));

        // The dialog's look: each copy, the zip unpacked, the one the same as another said so.
        var look = await KeptSaves.LookAsync(data, Bloodborne, Ct);
        Assert.Equal(6, look.Items.Count);
        Assert.Equal("'After maria'", look.Items.Single(i => i.Name == "Same as maria").SameAs);
        Assert.Contains(look.Items, i => i.Name == "Before sus / ded beast");
        Assert.Contains(look.Items, i => i.Name == "Before Crow");

        // Sync, with the copies: the live save is the game's only folder, and five named saves (not the twin).
        var done = await KeptSaves.ApplyAsync(data, Bloodborne, bring: true, backupOnly: false, new Quiet(), Ct);
        Assert.Equal(5, done.Named);
        using (var engine = Engine.Open(data))
        {
            var game = engine.Games.Single(g => g.Id == Bloodborne);
            Assert.Equal(GameMode.Sync, game.Mode);
            Assert.Equal(Path.Combine(savedata, "1", "CUSA00207", "SPRJ0005"), game.Roots.Values.Single());
            var entry = engine.Library.All().Single(e => e.Id == Bloodborne);
            Assert.Single(entry.LiveOnly);

            // A later scan that finds the whole folder again doesn't offer it as more saves.
            var whole = new Proposal(FoundBy.NameSearch, entry.LiveOnly[0], "**", SaveCategory.Save, 20, 400, null);
            Assert.Empty((entry with { Proposals = [whole] }).Suggestions);
        }

        var after = (await GameDetails.ReadAsync(data, Bloodborne, Ct))!;
        Assert.True(after.Syncs);
        Assert.Null(after.Kept);
        Assert.Equal(5, after.NamedSaves.Count);
        Assert.Equal("orphan", File.ReadAllText(Path.Combine(savedata, "1", "CUSA00207", "Before Orphan", "SPRJ0005", "userdata0000")));
    }

    [Fact]
    public async Task KAN_81_the_live_saves_place_shows_its_own_files_not_the_whole_folders_before_its_first_backup()
    {
        using var world = new TestWorld();
        var savedata = Savedata(world);
        var data = Setup(world, savedata);

        await KeptSaves.ApplyAsync(data, Bloodborne, bring: false, backupOnly: false, new Quiet(), Ct);

        // Nothing backed up yet: the place counts the live save's two files, not the scan's ten of the whole folder.
        var place = Assert.Single((await GameDetails.ReadAsync(data, Bloodborne, Ct))!.Places);
        Assert.StartsWith("2 files", place.Evidence, StringComparison.Ordinal);
        Assert.StartsWith("The folder SPRJ0005 and everything in it · 2 files",
            new PlaceItem(place.Portable, place.Folder, place.Tag, place.Evidence, null).Caption, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KAN_61_backed_up_only_without_the_copies_leaves_them_in_their_folders()
    {
        using var world = new TestWorld();
        var savedata = Savedata(world);
        var data = Setup(world, savedata);
        var done = await KeptSaves.ApplyAsync(data, Bloodborne, bring: false, backupOnly: true, new Quiet(), Ct);
        Assert.Equal(0, done.Named);
        using var engine = Engine.Open(data);
        Assert.Equal(GameMode.BackupOnly, engine.Games.Single(g => g.Id == Bloodborne).Mode);
        Assert.Throws<InvalidOperationException>(() => Library.ConfirmLive(engine.Library.All().Single(e => e.Id == Bloodborne), "<home>/x", "y"));
    }

    [Fact]
    public void KAN_61_the_dialog_counts_what_s_kept_and_names_its_buttons_after_it()
    {
        var kept = new GameKept("<installDir>/savedata", "1/CUSA00207/SPRJ0005", @"G:\Bloodborne\savedata\1\CUSA00207\SPRJ0005",
            @"G:\Bloodborne\savedata\1\CUSA00207", 3, 90_000_000, 34, 35_000_000, new DateTime(2026, 9, 17, 0, 14, 0, DateTimeKind.Utc), ["Before Orphan", "After maria"]);
        var at = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var look = new KeptLook(kept,
        [
            new ImportItem("After maria", "After maria\\SPRJ0005", 34, 30_000_000, at, null),
            new ImportItem("Before Orphan", "Before Orphan\\SPRJ0005", 34, 30_000_000, at.AddDays(1), null),
            new ImportItem("Same as maria", "Same as maria\\SPRJ0005", 34, 30_000_000, at.AddDays(2), "'After maria'"),
        ], [], 125_000_000);
        var dialog = new KeptCopiesViewModel(new KeptCopiesStart(Bloodborne, "Bloodborne", Backup: false), null);
        dialog.Show(look, new DateTime(2026, 10, 1, 12, 0, 0));
        // KAN-79: the buttons say what they keep, naming the live save's folder; the dialog says what syncing is.
        Assert.Equal(("Sync Bloodborne's saves", "Sync SPRJ0005 + 2 named saves"), (dialog.Heading, dialog.KeepLabel));
        Assert.Equal("Syncs the live save, SPRJ0005, between your PCs, and keeps each copy as a named save you can restore", dialog.KeepTip);
        Assert.StartsWith("Sync keeps every version, on this PC and in the cloud", dialog.Explain, StringComparison.Ordinal);
        Assert.Equal("The live save and 2 named saves · 1 the same as another, not kept twice", dialog.Summary);
        Assert.Equal("Bring the 3 copies beside it in as named saves", dialog.BringLabel);
        Assert.Equal("Sync the whole folder as one save", dialog.WholeLabel);

        dialog.Bring = false;
        Assert.Equal(("Sync SPRJ0005", "The live save alone"), (dialog.KeepLabel, dialog.Summary));
        Assert.NotNull(dialog.LeftNote);

        var backup = new KeptCopiesViewModel(new KeptCopiesStart(Bloodborne, "Bloodborne", Backup: true), null);
        backup.Show(look, DateTime.Now);
        Assert.Equal(("Back up SPRJ0005 + 2 named saves", "Backed up, not synced between your PCs", "upload"), (backup.KeepLabel, backup.LiveHead, backup.KeepIcon));
    }

    [Fact]
    public async Task KAN_61_first_run_shows_and_syncs_the_live_save_alone_and_hands_the_copies_on()
    {
        using var world = new TestWorld();
        var savedata = Savedata(world);
        var data = Setup(world, savedata);
        var live = Path.Combine(savedata, "1", "CUSA00207", "SPRJ0005");

        // Choose games: the live save's place and size, and the copies said in its tooltip.
        var row = FirstRun.Groups(data).SelectMany(g => g.Games).Single(g => g.Id == Bloodborne);
        Assert.EndsWith(@"CUSA00207\SPRJ0005", row.SavePath, StringComparison.Ordinal);
        Assert.Equal(new DirectoryInfo(live).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length), row.Bytes);
        Assert.StartsWith("6 copies (", row.Kept, StringComparison.Ordinal);

        // Finishing syncs the live save alone, and hands the copies on to be brought in as named saves.
        var result = await FirstRun.FinishAsync(data, new SetupChoice { Remote = world.Cloud, Games = [Bloodborne] }, new NoSchedule(), Ct);
        Assert.Equal(1, result.Syncing);
        var kept = Assert.Single(result.Kept);
        Assert.Equal((Bloodborne, 6, Path.Combine(savedata, "1", "CUSA00207")), (kept.Game, kept.Copies, kept.Folder));
        Assert.Contains(result.Notes, n => n.StartsWith("Bloodborne syncs its live save alone", StringComparison.Ordinal));
        using (var engine = Engine.Open(data))
        {
            Assert.Equal(live, engine.Games.Single(g => g.Id == Bloodborne).Roots.Values.Single());
        }

        // As the app brings them in after first run: five named saves, the twin kept once.
        var report = await AppActions.KeptSavesAsync(data, kept.Game, kept.Folder, kept.Root, apply: true, new Quiet(), Ct);
        Assert.Equal(5, report.Added);
    }

    [Fact]
    public async Task KAN_61_confirm_on_the_command_line_syncs_the_live_save_alone_unless_whole()
    {
        using var world = new TestWorld();
        var savedata = Savedata(world);
        var data = Setup(world, savedata);

        // Show says what confirming will do; confirming keeps the live save and says how to bring the copies in.
        var shown = new StringWriter();
        Assert.Equal(0, await CliRelay.RunAsync(["show", "bloodborne", "--data", data], shown));
        Assert.Contains("beside 6 copies (", shown.ToString(), StringComparison.Ordinal);
        Assert.Contains("gamesync confirm bloodborne --whole", shown.ToString(), StringComparison.Ordinal);

        var said = new StringWriter();
        Assert.Equal(0, await CliRelay.RunAsync(["confirm", "bloodborne", "--data", data], said));
        Assert.Contains("with its live save alone", said.ToString(), StringComparison.Ordinal);
        Assert.Contains($"gamesync import-saves bloodborne \"{Path.Combine(savedata, "1", "CUSA00207")}\" --root ", said.ToString(), StringComparison.Ordinal);
        using (var engine = Engine.Open(data))
        {
            Assert.Equal(Path.Combine(savedata, "1", "CUSA00207", "SPRJ0005"), engine.Games.Single(g => g.Id == Bloodborne).Roots.Values.Single());
        }

        // --whole keeps the folder as one save, for one that isn't what it looks like.
        using var other = new TestWorld();
        var whole = Savedata(other);
        var wholeData = Setup(other, whole);
        Assert.Equal(0, await CliRelay.RunAsync(["confirm", "bloodborne", "--whole", "--data", wholeData], TextWriter.Null));
        using (var engine = Engine.Open(wholeData))
        {
            Assert.Equal(whole, engine.Games.Single(g => g.Id == Bloodborne).Roots.Values.Single());
        }
    }

    /// <summary>A shadPS4-style savedata folder: the live save, five copies kept by hand (one nested, one the save's files directly, one the same as another) and a zip.</summary>
    private static string Savedata(TestWorld world)
    {
        var savedata = Path.Combine(world.Root, "Games", "Bloodborne", "Shadlix", "user", "savedata");
        var folder = Path.Combine(savedata, "1", "CUSA00207");
        Write(Path.Combine(folder, "SPRJ0005", "userdata0000"), "at the Orphan of Kos lamp");
        Write(Path.Combine(folder, "SPRJ0005", "userdata0001"), "settings");
        Write(Path.Combine(folder, "Before Orphan", "SPRJ0005", "userdata0000"), "orphan");
        Write(Path.Combine(folder, "After maria", "SPRJ0005", "userdata0000"), "maria");
        Write(Path.Combine(folder, "Same as maria", "SPRJ0005", "userdata0000"), "maria");
        Write(Path.Combine(folder, "Before sus", "ded beast", "SPRJ0005", "userdata0000"), "beast");
        Write(Path.Combine(folder, "SPRJ0005 - Copy", "userdata0000"), "an older run");
        using (var zip = ZipFile.Open(Path.Combine(folder, "Before Crow.zip"), ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("SPRJ0005/userdata0000").Open());
            writer.Write("crow");
        }

        Write(Path.Combine(savedata, "1", "sce_sdmemory", "memory.dat"), "the emulator's own");
        return savedata;
    }

    /// <summary>A data folder whose library has Bloodborne found by name search as its whole savedata folder, not syncing yet.</summary>
    private static string Setup(TestWorld world, string savedata)
    {
        var data = Path.Combine(world.Root, "data");
        new AppConfig { Remote = world.Cloud, Games = [] }.Save(data);
        using var library = new LibraryStore(data);
        library.SaveAll([new LibraryEntry
        {
            Id = Bloodborne,
            Title = "Bloodborne",
            FirstSeenUtc = DateTime.UtcNow,
            Installed = true,
            Store = StoreKind.Loose,
            InstallDir = Path.Combine(world.Root, "Games", "Bloodborne"),
            Proposals = [new Proposal(FoundBy.NameSearch, RootResolver.ToPortable(savedata, Cli.FoldersForThisPc()), "**", SaveCategory.Save, 10, 200, null)],
        }]);
        return data;
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private sealed class NoSchedule : ISetupSchedule
    {
        public string? StartAtSignIn(bool on) => null;

        public string? Daily(TimeOnly? at) => null;
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
