using GameSync.Core.Model;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>New named save as a dialog (BAK-18, KAN-77): what it shows of the save, the name it takes, and keeping it.</summary>
public class NamedSaveDialogTests
{
    private static readonly GameId Ds3 = GameId.Parse("dark-souls-iii");

    [Fact]
    public void KAN_77_it_shows_where_the_save_is_what_is_there_and_how_many_more_places()
    {
        var start = NamedSaveStart.For(Ds3, "Dark Souls III",
        [
            (null, null, "Not on this PC"),
            (@"C:\Users\You\Documents\DS3", "Settings", "2 files · 4 KB"),
            (@"C:\Users\You\AppData\Roaming\DarkSoulsIII\0110000100000666", null, "1 file · 8.9 MB · newest 7 Sep 23:27"),
        ], ["Before Nameless King"], null);
        Assert.Equal(@"C:\Users\You\AppData\Roaming\DarkSoulsIII\0110000100000666", start.Folder);
        Assert.Equal("1 file · 8.9 MB · newest 7 Sep 23:27 · and 1 more place", start.Meta);

        // A game not syncing yet, with nothing found here: no place to show, and what keeping it starts with.
        var none = new NamedSaveViewModel(NamedSaveStart.For(Ds3, "Dark Souls III", [], [], "GameSync starts keeping its saves."), null);
        Assert.False(none.HasSave);
        Assert.True(none.HasKeepLine);
        Assert.Equal("Dark Souls III's save as it is now, kept under a name", none.Subtitle);
    }

    [Fact]
    public async Task KAN_77_a_name_is_needed_one_the_game_has_is_turned_away_and_keeping_closes_or_says_why()
    {
        var kept = new List<(GameId Game, string Name)>();
        var closed = 0;
        string? answer = "Dark Souls III has no save files on this PC to keep.";
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
        {
            KeepNamed = (game, name) =>
            {
                kept.Add((game, name));
                return Task.FromResult<string?>(answer);
            },
            CloseDialog = () => closed++,
        };
        var dialog = new NamedSaveViewModel(new NamedSaveStart(Ds3, "Dark Souls III", null, null, ["Before Nameless King"], null), actions);
        Assert.False(dialog.CanKeep);
        Assert.Equal("Give it a name first", dialog.KeepTip);

        dialog.Name = "  before nameless king ";
        Assert.False(dialog.CanKeep);
        Assert.Contains("has a named save called “before nameless king” already", dialog.Taken);
        Assert.Equal("Give it another name", dialog.KeepTip);

        // It couldn't be kept: the dialog stays, saying why.
        dialog.Name = "Before the Twin Princes";
        Assert.True(dialog.CanKeep);
        await dialog.KeepCommand.ExecuteAsync(null);
        Assert.Equal((Ds3, "Before the Twin Princes"), Assert.Single(kept));
        Assert.Equal((0, answer, false), (closed, dialog.Error, dialog.Keeping));

        // Kept: it closes.
        answer = null;
        await dialog.KeepCommand.ExecuteAsync(null);
        Assert.Equal((1, (string?)null), (closed, dialog.Error));
    }
}
