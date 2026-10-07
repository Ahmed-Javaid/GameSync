using GameSync.Windows;

namespace GameSync.Host;

/// <summary>
/// PKG-01, PKG-05: what GameSync's per-user installer leaves to GameSync itself. The installer puts the files in
/// <c>%LOCALAPPDATA%\Programs\GameSync</c>, its Start menu entry, its <c>gamesync://</c> links and <c>gamesync</c> in the
/// person's terminal; uninstalling, it runs <c>gamesync uninstalling</c> first, which quits GameSync and takes away what
/// Windows starts for this copy: the Run key's GameSync and the daily backup's tasks, when they start this copy and not
/// another. The data folder, the backups and the cloud copy are never touched.
/// </summary>
public static class Install
{
    /// <summary>PKG-05: what <c>gamesync uninstalling</c> does, as lines saying what it did.</summary>
    /// <param name="folder">The copy being uninstalled: the folder this program runs from.</param>
    public static async Task<IReadOnlyList<string>> CleanUpAsync(string folder, string dataDir, SignInStart signIn, Func<string, string?> taskCommand, Action<string> removeTask)
    {
        var said = new List<string>();
        if (await AppPipe.SendAsync(dataDir, "quit", TimeSpan.FromSeconds(2)) is not null)
        {
            // The app holds its claim on the data folder until it has ended.
            for (var waited = TimeSpan.Zero; waited < TimeSpan.FromSeconds(30); waited += TimeSpan.FromMilliseconds(250))
            {
                if (AppPipe.TryClaim(dataDir) is { } claim)
                {
                    claim.ReleaseMutex();
                    claim.Dispose();
                    break;
                }

                await Task.Delay(250);
            }

            said.Add("GameSync has quit.");
        }

        if (Starts(signIn.Command, folder))
        {
            signIn.TurnOff();
            said.Add("GameSync no longer starts when you sign in.");
        }

        foreach (var task in new[] { Schedule.DailyTask, Schedule.CatchUpTask, Schedule.BackgroundTask })
        {
            if (Starts(taskCommand(task), folder))
            {
                removeTask(task);
                said.Add($"Removed the task '{task}'.");
            }
        }

        said.Add("Your backups, your settings and your cloud copy stay where they are.");
        return said;
    }

    /// <summary>Whether <paramref name="command"/> (a Run key's command or a task's program) starts a program inside <paramref name="folder"/>.</summary>
    internal static bool Starts(string? command, string folder)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var text = command.Trim();
        var program = text.StartsWith('"') ? text[1..Math.Max(1, text.IndexOf('"', 1))] : text.Split(' ')[0];
        try
        {
            var inside = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            return Path.GetFullPath(program).StartsWith(inside, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
