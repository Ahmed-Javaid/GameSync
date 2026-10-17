using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// LIB-30 (a friend's first try, 8 Oct 2026: "FL STUDIO, GitHub repo folder, cursor and other relevant items were added to
/// library"; design system version 56, OnboardingScreen #scan-folder): what a game folder holds is judged a game or
/// probably not, with why, for the person to pick before anything joins the library; what they leave out stays out.
/// </summary>
public class FolderPickTests
{
    [Fact]
    public void LIB_30_each_program_in_a_game_folder_is_judged_a_game_or_probably_not_with_why()
    {
        using var world = new TestWorld();
        var games = Directory.CreateDirectory(Path.Combine(world.Root, "Games")).FullName;
        Exe(Path.Combine(games, "Lantern Keep", "Lantern Keep.exe"));
        File.WriteAllBytes(Path.Combine(games, "Lantern Keep", "UnityPlayer.dll"), [1]);
        Exe(Path.Combine(games, "Steamy", "Steamy.exe"));
        File.WriteAllBytes(Path.Combine(games, "Steamy", "steam_api64.dll"), [1]);
        Exe(Path.Combine(games, "Tinker", "bin", "tinker.exe"));
        Directory.CreateDirectory(Path.Combine(games, "Tinker", ".git"));
        Exe(Path.Combine(games, "Quill", "Quill.exe"));
        Directory.CreateDirectory(Path.Combine(games, "Quill", "resources"));
        File.WriteAllBytes(Path.Combine(games, "Quill", "resources", "app.asar"), [1]);
        Exe(Path.Combine(games, "Mystery", "Mystery.exe"));

        var found = FolderLook.Look(games, LooseScanner.Scan([games], []), list: null);

        Assert.Equal(
            [
                ("Lantern Keep", true, "made with Unity"),
                ("Mystery", true, "a program of its own; nothing says it isn't a game"),
                ("Steamy", true, "has Steam's game files"),
                ("Quill", false, "an app built on Chromium, like a browser or a code editor"),
                ("Tinker", false, "a code project: it has a .git folder"),
            ],
            found.Select(c => (c.Title, c.LooksLikeGame, c.Why)));
        Assert.Equal(@"Lantern Keep\Lantern Keep.exe", found[0].Program);

        // An app that says what it is: Windows' own Registry Editor, kept in a game folder.
        var regedit = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "regedit.exe");
        if (File.Exists(regedit))
        {
            var apps = Directory.CreateDirectory(Path.Combine(world.Root, "Apps", "Registry")).FullName;
            File.Copy(regedit, Path.Combine(apps, "regedit.exe"));
            var app = Assert.Single(FolderLook.Look(Path.GetDirectoryName(apps)!, LooseScanner.Scan([Path.GetDirectoryName(apps)!], []), list: null));
            Assert.False(app.LooksLikeGame);
            Assert.StartsWith("an app: its program calls itself Registry Editor", app.Why, StringComparison.Ordinal);
        }

        // What the person left out before is offered unticked, saying so.
        var again = FolderLook.Look(games, LooseScanner.Scan([games], []), null, new HashSet<string> { FolderLook.Key(Path.Combine(games, "Mystery")) });
        var mystery = again.Single(c => c.Title == "Mystery");
        Assert.Equal((true, true, false), (mystery.LooksLikeGame, mystery.LeftOutBefore, mystery.Ticked));
    }

    [Fact]
    public void LIB_30_what_is_left_out_stays_out_of_the_library_and_what_is_ticked_again_comes_back()
    {
        LibraryEntry Loose(string id, string dir, LibraryState state = LibraryState.Found) =>
            new() { Id = GameId.Parse(id), Title = id, Store = StoreKind.Loose, InstallDir = dir, State = state, Installed = true };
        var library = new List<LibraryEntry>
        {
            Loose("fl-studio", @"E:\Games\FL Studio 2024"),
            Loose("cuphead", @"E:\Games\Cuphead"),
            Loose("cursor", @"E:\Games\cursor", LibraryState.Ignored),
            Loose("terraria", @"E:\Games\Terraria", LibraryState.Synced) with { Confirmed = new GameSync.Core.Games.GameDefinition { Id = GameId.Parse("terraria"), Title = "Terraria", Roots = new Dictionary<string, string>(), Rules = [] } },
        };

        var picked = Library.Pick(library,
            new HashSet<string> { FolderLook.Key(@"E:\Games\FL Studio 2024"), FolderLook.Key(@"E:\Games\Terraria") },
            new HashSet<string> { FolderLook.Key(@"E:\Games\cursor") });

        Assert.Equal(
            [("fl-studio", LibraryState.Ignored), ("cuphead", LibraryState.Found), ("cursor", LibraryState.Found), ("terraria", LibraryState.Synced)],
            picked.Select(e => (e.Id.Value, e.State)));
        Assert.Equal([FolderLook.Key(@"E:\Games\FL Studio 2024")], Library.LeftOut(picked));
    }

    [Fact]
    public void LIB_30_the_dialog_ticks_the_games_counts_what_is_added_and_says_what_was_left_out()
    {
        var folder = @"E:\Games";
        FolderCandidate Candidate(string title, bool game, bool before = false) =>
            new(title, Path.Combine(folder, title), title + ".exe", game, game ? "in the save list" : "a code project: it has a .git folder") { LeftOutBefore = before };
        FolderPicks? done = null;
        var finished = 0;
        var dialog = new FolderPickViewModel(new FolderLookResult(folder,
            [Candidate("Cuphead", true), Candidate("Hollow Knight", true), Candidate("GameSync", false), Candidate("Mystery", true, before: true)]), picks =>
            {
                done = picks;
                finished++;
            });

        Assert.Equal(("What's in E:\\Games", "Add 2 games", true), (dialog.Title, dialog.AddLabel, dialog.CanAdd));
        Assert.Equal(["Cuphead", "Hollow Knight"], dialog.Games.Select(r => r.Title));
        Assert.Equal([("GameSync", "a code project: it has a .git folder"), ("Mystery", "you left it out before")], dialog.Others.Select(r => (r.Title, r.Why)));

        dialog.Games[1].IsChecked = false;
        dialog.Others[1].IsChecked = true;
        Assert.Equal("Add 2 games", dialog.AddLabel);
        dialog.AddCommand.Execute(null);
        dialog.CancelCommand.Execute(null);
        Assert.Equal(1, finished);
        Assert.NotNull(done);
        Assert.Equal(new[] { FolderLook.Key(@"E:\Games\Hollow Knight"), FolderLook.Key(@"E:\Games\GameSync") }.Order(), done.LeaveOut.Order());
        Assert.Equal([FolderLook.Key(@"E:\Games\Mystery")], done.TakeBack);
        Assert.Equal(["Hollow Knight", "GameSync"], done.LeftOutNames);
        Assert.EndsWith("Left out, as you chose: Hollow Knight, GameSync.", new FolderScan(folder, ["Cuphead", "Mystery"], 1) { LeftOut = done.LeftOutNames }.Sentence, StringComparison.Ordinal);

        // Nothing found: the folder can still be added, for games installed there later.
        var empty = new FolderPickViewModel(new FolderLookResult(folder, []), picks => done = picks);
        Assert.Equal((true, false), (empty.IsEmpty, empty.HasCandidates));
        empty.AddAnywayCommand.Execute(null);
        Assert.Empty(done!.LeaveOut);
    }

    private static void Exe(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[5000]);
    }
}
