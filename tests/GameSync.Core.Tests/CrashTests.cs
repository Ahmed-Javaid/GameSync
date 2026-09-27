using GameSync.Core.Model;
using GameSync.Core.Sync;

namespace GameSync.Core.Tests;

/// <summary>GameSync "dies" at the worst moments; a fresh start on the same folders must leave everything whole.</summary>
public class CrashTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task BAK_12_and_BAK_13_a_crash_between_contents_and_record_leaves_no_half_version_and_the_job_resumes()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        desktop.AddGame("game");
        desktop.Write("game", "slot.sav", "progress");

        CrashPoints.Arm(CrashPoints.AfterBlobsBeforeRecord);
        await Assert.ThrowsAsync<SimulatedCrashException>(() => desktop.Service().SyncAsync(null, Ct));

        Assert.Empty(await Cloud.VersionsAsync(world, "game"));
        Assert.Single(desktop.State.GetUnfinishedJobs());

        var resumed = Assert.Single(await desktop.Service().RecoverAsync(Ct));

        Assert.Equal(SyncAction.Upload, resumed.Action);
        Assert.Single(await Cloud.VersionsAsync(world, "game"));
        Assert.Empty(desktop.State.GetUnfinishedJobs());
    }

    [Fact]
    public async Task BAK_08_a_crash_after_staging_finishes_the_swap_on_restart()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = await PreparedLaptopAsync(world, desktop);

        CrashPoints.Arm(CrashPoints.AfterStagingBeforeSwap);
        await Assert.ThrowsAsync<SimulatedCrashException>(() => laptop.Service().SyncAsync(null, Ct));

        Assert.Equal("one", laptop.Read("game", "slot.sav"));
        Assert.NotEmpty(Directory.EnumerateFiles(laptop.Folder("game"), "*.gs-new-*", SearchOption.AllDirectories));

        await laptop.Service().RecoverAsync(Ct);

        await AssertMatchesDesktopAsync(desktop, laptop);
    }

    [Fact]
    public async Task BAK_08_a_crash_in_the_middle_of_the_swap_finishes_it_on_restart()
    {
        using var world = new TestWorld();
        using var desktop = world.Pc("DESKTOP");
        using var laptop = await PreparedLaptopAsync(world, desktop);

        CrashPoints.Arm(CrashPoints.MidSwap);
        await Assert.ThrowsAsync<SimulatedCrashException>(() => laptop.Service().SyncAsync(null, Ct));

        await laptop.Service().RecoverAsync(Ct);

        await AssertMatchesDesktopAsync(desktop, laptop);
    }

    /// <summary>Both PCs agree on "one"; then the desktop plays, changing a file, adding one and removing one.</summary>
    private static async Task<TestPc> PreparedLaptopAsync(TestWorld world, TestPc desktop)
    {
        var laptop = world.Pc("LAPTOP");
        desktop.AddGame("game");
        laptop.AddGame("game");
        desktop.Write("game", "slot.sav", "one");
        desktop.Write("game", "old.sav", "will be removed");
        await desktop.SyncAsync();
        await laptop.SyncAsync();

        desktop.Played("game");
        desktop.Write("game", "slot.sav", "two");
        desktop.Write("game", "Sub/new.sav", "added");
        File.Delete(Path.Combine(desktop.Folder("game"), "old.sav"));
        await desktop.SyncAsync();
        return laptop;
    }

    private static async Task AssertMatchesDesktopAsync(TestPc desktop, TestPc laptop)
    {
        Assert.Equal(desktop.Tree("game"), laptop.Tree("game"));
        Assert.Empty(Directory.EnumerateFiles(laptop.Folder("game"), "*.gs-new-*", SearchOption.AllDirectories));
        Assert.Equal(SyncAction.None, Assert.Single(await laptop.SyncAsync()).Action);
    }
}
