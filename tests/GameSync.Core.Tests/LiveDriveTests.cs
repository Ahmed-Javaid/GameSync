using GameSync.Storage.Drive;
using GameSync.Windows;
using Xunit.Abstractions;

namespace GameSync.Core.Tests;

/// <summary>Skips unless GAMESYNC_DRIVE_DATA names a GameSync data folder that's signed in to Google Drive.</summary>
public sealed class LiveDriveFactAttribute : FactAttribute
{
    public LiveDriveFactAttribute()
    {
        if (LiveDrive.DataFolder is null)
        {
            Skip = "Sign in with 'gamesync signin', then set GAMESYNC_DRIVE_DATA to GameSync's data folder to run this against the real Drive.";
        }
    }
}

internal static class LiveDrive
{
    public static string? DataFolder =>
        Environment.GetEnvironmentVariable("GAMESYNC_DRIVE_DATA") is { Length: > 0 } folder && File.Exists(Path.Combine(folder, "google-token.bin"))
            ? folder
            : null;

    public static GoogleAuth Auth() => new(
        GoogleClient.Parse(File.ReadAllBytes(Path.Combine(DataFolder!, "google-client.json"))),
        Path.Combine(DataFolder!, "google-token.bin"),
        new DpapiProtector());
}

/// <summary>
/// Milestone 2 against the real Google Drive, in a throwaway "GameSync test" folder that goes to the trash afterwards.
/// Its own mark keeps it apart from the real GameSync folder, which these tests never touch.
/// </summary>
public class LiveDriveTests(ITestOutputHelper output)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [LiveDriveFact]
    public async Task Every_decision_and_crash_scenario_holds_on_the_real_Drive()
    {
        using var client = new GoogleDriveClient(LiveDrive.Auth());
        var mark = $"test-{Guid.NewGuid():N}"[..21];
        using var saves = new TestWorld();
        var source = Directory.CreateDirectory(Path.Combine(saves.Root, "Live Drive Game")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(source, "world.sav"), Enumerable.Range(0, 12 * 1024 * 1024).Select(i => (byte)(i * 7 % 251)).ToArray());
        Directory.CreateDirectory(Path.Combine(source, "players"));
        await File.WriteAllTextAsync(Path.Combine(source, "players", "hero.plr"), "hero");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await FullScenario.RunAsync(source, Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories), () => new TestWorld(cloud: pc =>
                new DriveCloud(client, pc is null ? null : id => pc.Games.FirstOrDefault(g => g.Id == id)?.Title, rootName: $"GameSync test {mark}", rootMark: mark)));
            output.WriteLine($"The scenario passed on the real Drive in {clock.Elapsed.TotalSeconds:0} s.");
        }
        finally
        {
            // Tidying up can't hide the scenario's result: what can't be trashed is only reported.
            try
            {
                await TrashTestFolderAsync(client, mark);
            }
            catch (Exception e)
            {
                output.WriteLine($"Couldn't tidy up the test folder: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Trashes the test's folder. Drive refuses when another app (such as a Google Drive sync client on another PC) left
    /// something in it that GameSync can't see; then its contents go, and the empty-looking folder is left to delete by hand.
    /// </summary>
    private async Task TrashTestFolderAsync(GoogleDriveClient client, string mark)
    {
        foreach (var folder in await client.FindFoldersAsync(DriveCloud.MarkKey, mark, Ct))
        {
            try
            {
                await client.TrashAsync(folder.Id, Ct);
            }
            catch (GameSync.Core.Storage.CloudException)
            {
                foreach (var child in await client.ListChildrenAsync(folder.Id, Ct))
                {
                    await client.TrashAsync(child.Id, Ct);
                }

                output.WriteLine($"Left '{folder.Name}' in your Drive: another app put something in it that GameSync can't see or trash. Delete it at drive.google.com.");
            }
        }
    }

    [LiveDriveFact]
    public async Task R9_and_SYNC_08_the_real_Drive_grants_drive_file_only_and_reports_its_clock()
    {
        var auth = LiveDrive.Auth();
        using var client = new GoogleDriveClient(auth);

        var about = await client.AboutAsync(Ct);

        Assert.Equal(GoogleAuth.Scope, auth.GrantedScope);
        Assert.NotNull(about.Email);
        Assert.NotNull(about.ServerTimeUtc);
        Assert.True((DateTime.UtcNow - about.ServerTimeUtc!.Value).Duration() < TimeSpan.FromMinutes(10));
    }
}
