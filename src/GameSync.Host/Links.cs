using GameSync.Core.Discovery;
using GameSync.Core.Model;

namespace GameSync.Host;

/// <summary>
/// R12: <c>gamesync://</c> links, which the installer registers for this person alone: <c>gamesync://play/&lt;game&gt;</c>
/// starts a game the way <c>gamesync launch</c> does, its save checked first, and <c>gamesync://open/&lt;game&gt;</c> opens
/// its page. Only a game in this PC's library: a link naming any other, or shaped any other way, starts nothing. A link
/// carries nothing else, so nothing in one decides what runs.
/// </summary>
public static class Links
{
    public const string Scheme = "gamesync";

    public enum Action
    {
        Play,
        Open,
    }

    /// <summary>The link's action and game, or null for anything that isn't exactly one of the two.</summary>
    public static (Action Action, GameId Game)? Parse(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
            || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0 || !uri.IsDefaultPort)
        {
            return null;
        }

        Action? action = uri.Host.ToLowerInvariant() switch
        {
            "play" => Action.Play,
            "open" => Action.Open,
            _ => null,
        };
        var id = uri.AbsolutePath.Trim('/');
        return action is { } a && id.Length > 0 && !id.Contains('/') && !id.Contains('%') && GameId.TryParse(id, out var game) ? (a, game) : null;
    }

    /// <summary>
    /// What <c>GameSync.Tray.exe --link &lt;url&gt;</c> does: plays or opens a game this PC's library has, through the running
    /// app when it's open; anything else says so and starts nothing.
    /// </summary>
    public static async Task<int> OpenAsync(string dataDir, string url, IAgentOutput output)
    {
        if (Parse(url) is not { } link)
        {
            output.NeedsYou("GameSync", "That link isn't one GameSync opens, so nothing was started.");
            return 2;
        }

        if (!Knows(dataDir, link.Game))
        {
            output.NeedsYou("GameSync", "That link names a game GameSync doesn't know on this PC, so nothing was started.");
            return 2;
        }

        if (link.Action == Action.Play)
        {
            // The app checks and starts it when it's open (BG-09), as `gamesync launch` would; otherwise it's done here.
            return await AppPipe.RunAsync(dataDir, ["launch", link.Game.Value], TextWriter.Null) is { } code
                ? code
                : await Cli.LaunchAsync(dataDir, [link.Game.Value], output);
        }

        if (await AppPipe.SendAsync(dataDir, $"open {link.Game.Value}", TimeSpan.FromSeconds(2)) is not null)
        {
            return 0;
        }

        // GameSync isn't running: it starts, then opens the page.
        var start = new System.Diagnostics.ProcessStartInfo(Schedule.BackgroundProgram) { UseShellExecute = false };
        if (!Path.GetFullPath(dataDir).TrimEnd('\\').Equals(Path.GetFullPath(Engine.DefaultDataDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add("--data");
            start.ArgumentList.Add(dataDir);
        }

        System.Diagnostics.Process.Start(start)?.Dispose();
        return await AppPipe.SendAsync(dataDir, $"open {link.Game.Value}", TimeSpan.FromSeconds(30)) is not null ? 0 : 1;
    }

    /// <summary>R12: a game in this PC's library, not ignored or merged into another.</summary>
    public static bool Knows(string dataDir, GameId game)
    {
        try
        {
            using var engine = Engine.Open(dataDir);
            return engine.Library.All().Any(e => e.Id == game && e.State != LibraryState.Ignored && e.MergedInto is null);
        }
        catch (Exception e) when (e is UsageException or IOException or InvalidOperationException)
        {
            return false;
        }
    }
}
