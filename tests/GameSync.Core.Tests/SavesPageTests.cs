using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// A game's saves after the owner's review of 1 Oct 2026 (design system version 31): the Current save card (KAN-92),
/// Export as a folder (KAN-87), the named saves' search and order (KAN-82) and Share… (KAN-84).
/// </summary>
public class SavesPageTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly GameId Lantern = GameId.Parse("lantern-keep");

    [Fact]
    public async Task KAN_92_the_current_save_says_when_it_was_written_whether_its_backed_up_and_what_its_the_same_as()
    {
        using var world = new TestWorld();
        var (data, slot) = Setup(world);
        var output = new Quiet();

        var first = (await GameDetails.ReadAsync(data, Lantern, Ct))!.Current!;
        Assert.Equal((1, (long)"at the lighthouse door".Length), (first.Files, first.Bytes));
        Assert.NotNull(first.BackedUpUtc);
        Assert.True(first.Uploaded);
        Assert.Null(first.SameAs);

        // A named save of it: the same as that, kept here and not uploaded yet.
        Assert.Null(await AppActions.SaveAsAsync(data, Lantern, "Before the lighthouse", output, Ct));
        var named = (await GameDetails.ReadAsync(data, Lantern, Ct))!;
        Assert.Equal("Before the lighthouse", named.Current!.SameAs);

        // Played on: changed since its last backup, and the same as nothing.
        File.WriteAllText(slot, "past the lighthouse");
        var changed = (await GameDetails.ReadAsync(data, Lantern, Ct))!.Current!;
        Assert.Null(changed.BackedUpUtc);
        Assert.Null(changed.SameAs);

        // Restored: the same as the named save again, and when it was restored.
        Assert.Null(await AppActions.RestoreAsync(data, Lantern, named.NamedSaves[0].Version, "Before the lighthouse", output, Ct));
        var restored = (await GameDetails.ReadAsync(data, Lantern, Ct))!.Current!;
        Assert.Equal("Before the lighthouse", restored.SameAs);
        Assert.NotNull(restored.RestoredUtc);

        // The page says it in words.
        var page = new GameSavesViewModel(Launcher(), null, new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }));
        page.Show((await GameDetails.ReadAsync(data, Lantern, Ct))!, DateTime.Now);
        Assert.Equal(["Last written", "Size", "Backed up", "Same as"], page.CurrentFacts.Select(f => f.Label));
        Assert.StartsWith("“Before the lighthouse”, restored ", page.CurrentSameAs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KAN_87_a_named_save_exports_as_a_plain_folder_named_after_it_never_writing_over_anything()
    {
        using var world = new TestWorld();
        var (data, slot) = Setup(world);
        var output = new Quiet();
        var saved = File.GetLastWriteTimeUtc(slot);
        Assert.Null(await AppActions.SaveAsAsync(data, Lantern, "Before: the lighthouse?", output, Ct));
        var named = Assert.Single((await GameDetails.ReadAsync(data, Lantern, Ct))!.NamedSaves);
        var parent = Directory.CreateDirectory(Path.Combine(world.Root, "exports")).FullName;

        var (folder, problem) = await AppActions.ExportFolderAsync(data, Lantern, named.Version, named.Name, parent, output, Ct);

        // The name made fit for a folder; the save folder by its own name inside it, the file with its own date.
        Assert.Null(problem);
        Assert.Equal(Path.Combine(parent, "Before the lighthouse"), folder);
        var exported = Path.Combine(folder!, "Lantern Keep", "slot.sav");
        Assert.Equal("at the lighthouse door", File.ReadAllText(exported));
        Assert.Equal(saved, File.GetLastWriteTimeUtc(exported), TimeSpan.FromSeconds(1));

        // Again: beside it, never over it; nothing half-written is left.
        var (again, _) = await AppActions.ExportFolderAsync(data, Lantern, named.Version, named.Name, parent, output, Ct);
        Assert.Equal(Path.Combine(parent, "Before the lighthouse (2)"), again);
        Assert.Equal(["Before the lighthouse", "Before the lighthouse (2)"], Directory.EnumerateDirectories(parent).Select(Path.GetFileName).Order());

        // A folder that isn't there says so.
        var (none, missing) = await AppActions.ExportFolderAsync(data, Lantern, named.Version, named.Name, Path.Combine(parent, "gone"), output, Ct);
        Assert.Null(none);
        Assert.Contains("isn't there", missing, StringComparison.Ordinal);
    }

    [Fact]
    public void KAN_82_named_saves_are_found_by_name_and_listed_newest_first_or_by_name()
    {
        var sorts = new List<string>();
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { }) { SetNamedSort = sorts.Add };
        var page = new GameSavesViewModel(Launcher(), actions, new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }));
        var now = DateTime.UtcNow;
        GameNamedSave Named(string name, int daysAgo) => new(name, VersionId.New(now.AddDays(-daysAgo), "DESKTOP"), now.AddDays(-daysAgo), "DESKTOP", true);
        page.Show(new GameDetail { Id = Lantern, Syncs = true, NamedSaves = [Named("Before Orphan", 1), Named("after ludwig", 4), Named("befo ludwig", 7)] }, DateTime.Now);

        Assert.Equal("Named saves · 3", page.NamedTitle);
        Assert.True(page.HasNamedTools);
        Assert.Equal(["Before Orphan", "after ludwig", "befo ludwig"], page.ShownNamedSaves.Select(n => n.Name));
        Assert.Empty(sorts);

        page.NamedSearch = "LUD";
        Assert.Equal(["after ludwig", "befo ludwig"], page.ShownNamedSaves.Select(n => n.Name));

        page.NamedSearch = "";
        page.NamedSort = "name";
        Assert.Equal(["after ludwig", "befo ludwig", "Before Orphan"], page.ShownNamedSaves.Select(n => n.Name));
        Assert.Equal(["name"], sorts);

        page.NamedSearch = "kos";
        Assert.True(page.NoNamedMatch);
        Assert.Equal("No named save is called anything like “kos”.", page.NoMatchText);
    }

    [Fact]
    public void KAN_84_Share_opens_the_share_window_with_the_game_and_says_why_not_for_an_anti_cheat_game()
    {
        var opened = new List<ShareStart>();
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { }) { OpenShare = opened.Add };
        var page = new GameSavesViewModel(Launcher(), actions, new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }));
        page.Show(new GameDetail { Id = Lantern, Syncs = true }, DateTime.Now);

        Assert.True(page.CanShare);
        page.ShareCommand.Execute(null);
        var start = Assert.Single(opened);
        Assert.Equal((ShareStart.Pick, Lantern), (start.Mode, Assert.Single(start.Games)));

        page.Show(new GameDetail { Id = Lantern, Syncs = true, HasAntiCheat = true }, DateTime.Now);
        Assert.False(page.CanShare);
        Assert.Equal("It ships an anti-cheat, so its saves can't be shared", page.ShareTip);
    }

    /// <summary>Lantern Keep syncing through a folder cloud, its one save backed up and uploaded.</summary>
    private static (string Data, string Slot) Setup(TestWorld world)
    {
        var data = Path.Combine(world.Root, "data");
        var saves = Path.Combine(world.Root, "Saves");
        var slot = Path.Combine(saves, "Lantern Keep", "slot.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(slot)!);
        File.WriteAllText(slot, "at the lighthouse door");
        new AppConfig
        {
            Remote = world.Cloud,
            Games = [new GameDefinition { Id = Lantern, Title = "Lantern Keep", Roots = new Dictionary<string, string> { ["saves"] = Path.GetDirectoryName(slot)! }, Rules = [new SaveRule { Root = "saves" }] }],
        }.Save(data);
        var output = new Quiet();
        Task.Run(async () =>
        {
            await SyncPlans.RunAsync(data, (await SyncPlans.CheckAsync(data, output, Ct)).Changes, output, Ct);
            await Uploads.RunAsync(data, null, Ct);
        }).GetAwaiter().GetResult();
        return (data, slot);
    }

    private static LauncherGame Launcher() => new() { Id = Lantern, Title = "Lantern Keep", Syncs = true, Status = GameStatus.Synced, Cloud = "your cloud folder" };

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
