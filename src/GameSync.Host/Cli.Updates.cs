using System.Globalization;
using GameSync.Windows;

namespace GameSync.Host;

public static partial class Cli
{
    /// <summary>
    /// PKG-03, R19: <c>gamesync update</c> asks GitHub for a newer GameSync and gets it ready, as Settings → Updates does;
    /// <c>--install</c> then installs it: GameSync closes, updates and opens again. Nothing installs that GameSync's own
    /// release signature doesn't check out for.
    /// </summary>
    private static async Task<int> UpdateAsync(string dataDir, List<string> rest)
    {
        var install = rest.Remove("--install");
        var background = rest.Remove("--background");
        using var updates = new Updates(dataDir);
        Console.WriteLine(Updates.IsInstalledCopy
            ? $"GameSync {updates.Current.ToString(3)}."
            : $"GameSync {updates.Current.ToString(3)}, running from {AppContext.BaseDirectory.TrimEnd('\\')}; the installer puts GameSync in {Updates.InstallFolder}.");

        var shown = -1;
        var view = await updates.CheckAndGetAsync(new Progress<UpdateProgress>(p =>
        {
            var tenth = (int)(p.Done * 10 / Math.Max(1, p.Total));
            if (tenth > shown)
            {
                shown = tenth;
                Console.WriteLine($"  {p.Done / 1048576.0:0.0} of {p.Total / 1048576.0:0.0} MB");
            }
        }), CancellationToken.None);

        if (view.Ready is { } ready)
        {
            Console.WriteLine($"GameSync {ready.Version.ToString(3)} is ready.");
            foreach (var line in ready.Notes)
            {
                Console.WriteLine($"  - {line}");
            }

            if (!install)
            {
                Console.WriteLine("Install it with: gamesync update --install (or Restart to update in GameSync).");
                return 0;
            }

            updates.StartInstall(ready, showWindow: !background).Dispose();
            Console.WriteLine($"Installing GameSync {ready.Version.ToString(3)}: GameSync closes, updates and opens again in a few seconds{(background ? ", in the tray" : "")}.");
            return 0;
        }

        if (view.Refused is { } refused && refused.Version > updates.Current)
        {
            Console.Error.WriteLine($"GameSync {refused.Version.ToString(3)} wasn't installed: {refused.Reason}.");
            return 1;
        }

        if (view.Problem is { } problem && problem.AtUtc >= (view.CheckedUtc ?? DateTime.MinValue))
        {
            Console.Error.WriteLine($"Couldn't check for updates: {problem.Reason}.");
            return 1;
        }

        Console.WriteLine($"You have the latest version (checked {DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture)}).");
        return 0;
    }

    /// <summary>PKG-05: <c>gamesync uninstalling</c>, which the uninstaller runs before it removes GameSync's files.</summary>
    private static async Task<int> UninstallingAsync(string dataDir)
    {
        foreach (var line in await Install.CleanUpAsync(AppContext.BaseDirectory, dataDir, SignInStart.ForThisUser(), Schedule.CommandOf, Schedule.Remove))
        {
            Console.WriteLine(line);
        }

        return 0;
    }
}
