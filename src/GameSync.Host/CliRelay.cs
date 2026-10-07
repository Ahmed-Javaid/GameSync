using System.Text;

namespace GameSync.Host;

/// <summary>
/// BG-09: a command-line job the running app does for <c>gamesync</c>. What the job prints goes back to the command line
/// that sent it and nowhere else: the app's console is routed per job, so jobs side by side, and the app's own work,
/// never mix.
/// </summary>
public static class CliRelay
{
    /// <summary>The commands that hand off to the app when it's open; with it closed they run as before.</summary>
    public static readonly IReadOnlyList<string> Commands = ["sync", "plan", "restore", "launch"];

    /// <summary>
    /// Whether the app does this job (<paramref name="args"/> without <c>--data</c>, which the command line has taken for
    /// the pipe's name). Never a launch with a command of its own after <c>--</c>: that's Steam's route, which runs in
    /// its own process, and the app starts only a game it knows (R12). Nor another data folder than the app's.
    /// </summary>
    public static bool Takes(IReadOnlyList<string> args) =>
        args is [var command, ..] && Commands.Contains(command) && !args.Contains("--data") && !args.Contains("--");

    private static readonly AsyncLocal<TextWriter?> Current = new();
    private static int _routed;

    /// <summary>Runs a command here, as <c>gamesync</c> would with <paramref name="args"/>, printing to <paramref name="output"/>.</summary>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output)
    {
        if (Interlocked.Exchange(ref _routed, 1) == 0)
        {
            Console.SetOut(new Router());
            Console.SetError(new Router());
        }

        Current.Value = output;
        try
        {
            return await Cli.RunHereAsync([.. args]);
        }
        finally
        {
            Current.Value = null;
        }
    }

    /// <summary>The app's console: the current job's writer, or nothing outside a job.</summary>
    private sealed class Router : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => Current.Value?.Write(value);

        public override void Write(string? value) => Current.Value?.Write(value);

        public override void WriteLine(string? value) => Current.Value?.WriteLine(value);

        public override void WriteLine() => Current.Value?.WriteLine();
    }
}
