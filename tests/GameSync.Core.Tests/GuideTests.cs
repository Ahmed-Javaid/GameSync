using GameSync.Core.State;
using GameSync.UI.Controls;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// ONB-08 (design system version 56, GuideScreen; a friend, 8 Oct 2026: "Add a tutorial and a guide", "It should show not
/// synced, instead of uploading pending"): the Guide in the rail, the tour after first run, and the status words.
/// </summary>
public class GuideTests
{
    [Fact]
    public void ONB_08_the_tour_asks_first_then_goes_stop_by_stop_and_ends_once()
    {
        var ended = 0;
        var tour = new TourViewModel(() => ended++);
        Assert.Equal((true, "Show me around", "Not now", false), (tour.IsAsking, tour.NextLabel, tour.EndLabel, tour.HasCount));

        tour.NextCommand.Execute(null);
        Assert.Equal(("Your games", "rail:library", "1 of 5", "End the tour"), (tour.Stop.Title, tour.Stop.Target, tour.CountText, tour.EndLabel));
        Assert.False(tour.BackCommand.CanExecute(null));
        tour.NextCommand.Execute(null);
        Assert.True(tour.BackCommand.CanExecute(null));
        tour.BackCommand.Execute(null);
        Assert.Equal(1, tour.Index);

        // The Guide's Take the tour again starts at the first stop; its last says Done, and Done ends it.
        var again = new TourViewModel(() => ended++, startAt: 1);
        Assert.Equal("Your games", again.Stop.Title);
        for (var i = 0; i < 4; i++)
        {
            again.NextCommand.Execute(null);
        }

        Assert.Equal(("The Guide", "Done"), (again.Stop.Title, again.NextLabel));
        again.NextCommand.Execute(null);
        Assert.Equal(1, ended);
        tour.EndCommand.Execute(null);
        Assert.Equal(2, ended);
    }

    [Fact]
    public void ONB_08_the_Guide_is_in_the_rail_and_says_every_status_in_words()
    {
        var rail = ShellViewModel.DefaultRail(null, null);
        Assert.Equal(["guide", "settings"], rail.Where(r => r.Bottom).Select(r => r.Id));
        Assert.Equal(("help", "Guide"), (rail.Single(r => r.Id == "guide").Icon, rail.Single(r => r.Id == "guide").Label));

        var started = 0;
        var guide = new GuideViewModel(@"C:\Users\You\AppData\Local\GameSync\history", () => started++, "statuses");
        Assert.True(guide.IsStatuses);
        Assert.Equal(["Synced", "Not synced", "Sending", "Newer on another PC", "Held for review", "Conflict", "Synced by Steam", "Not backed up", "Playing"],
            guide.Statuses.Select(s => s.Label ?? (s.Cloudless is { } c ? GsStatusBadge.Describe(s.Status, c) : GsStatusBadge.Describe(s.Status)).Word));
        guide.Topic = "folder";
        Assert.Equal(@"C:\Users\You\AppData\Local\GameSync\history. Move it in Settings, Storage and folders.", guide.BackupFolderLine);
        Assert.Contains(guide.FolderRows, r => r.Path.EndsWith("--pc-cb75de22\\", StringComparison.Ordinal) && r.Meaning.Contains("never synced", StringComparison.Ordinal));
        guide.StartTourCommand.Execute(null);
        Assert.Equal(1, started);
    }

    [Fact]
    public void ONB_07_with_no_cloud_a_game_backed_up_here_reads_Not_synced_and_with_one_Sending()
    {
        try
        {
            GsStatusBadge.NoCloud = true;
            Assert.Equal(("neutral", "monitor", "Not synced"), GsStatusBadge.Describe(GameStatus.UploadPending));
            GsStatusBadge.NoCloud = false;
            Assert.Equal(("ok", "upload", "Sending"), GsStatusBadge.Describe(GameStatus.UploadPending));
            Assert.Equal("Not backed up", GsStatusBadge.Describe(null).Word);
            Assert.Equal("Not synced", GsStatusBadge.Describe(GameStatus.UploadPending, cloudless: true).Word);
        }
        finally
        {
            GsStatusBadge.NoCloud = false;
        }
    }
}
