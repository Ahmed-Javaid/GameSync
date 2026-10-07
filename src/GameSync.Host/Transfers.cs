using System.Globalization;
using GameSync.Core.Model;
using GameSync.Core.Storage;
using GameSync.Core.Sync;

namespace GameSync.Host;

/// <summary>Where an upload or a download of a game's saves stands (KAN-80).</summary>
public enum TransferState
{
    Running,
    Paused,
    Failed,
    Done,
}

/// <summary>
/// An upload or a download as the app shows it on the game's saves (KAN-80): how far it is, and how it stands;
/// <see cref="Note"/> says why it's paused or how it ended ("113 MB in 19 s").
/// </summary>
public sealed record TransferUpdate(TransferProgress Progress, TransferState State, string? Note = null)
{
    public GameId Game => Progress.Game;

    /// <summary>The game's name, for the tray icon's tooltip, when the run knows it.</summary>
    public string? Title { get; init; }
}

/// <summary>
/// Passes a run's uploads and downloads on to the app as they go (KAN-80), at most ten times a second a game, and says
/// how each ended: done, with what it moved and how long it took; paused while a game that syncs is played; or failed,
/// with when it's tried again. The engine reports from several threads at once.
/// </summary>
public sealed class TransferRelay(IAgentOutput output) : IProgress<TransferProgress>
{
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(100);
    private readonly object _gate = new();
    private readonly Dictionary<GameId, Open> _open = [];

    /// <summary>Each game's name, once the run knows its games.</summary>
    public Func<GameId, string?>? TitleOf { get; set; }

    /// <summary>Whether anything moved, or is moving.</summary>
    public bool Any
    {
        get
        {
            lock (_gate)
            {
                return _open.Count > 0;
            }
        }
    }

    public void Report(TransferProgress value)
    {
        if (value.FilesTotal == 0 && value.BytesTotal == 0)
        {
            return;
        }

        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (!_open.TryGetValue(value.Game, out var open) || open.Last.Direction != value.Direction)
            {
                open = new Open(value, now, DateTime.MinValue);
            }

            // The first report, the last, and every tenth of a second between them.
            var send = now - open.SentUtc >= Every || value.Finished || open.SentUtc == DateTime.MinValue;
            _open[value.Game] = open with { Last = value, SentUtc = send ? now : open.SentUtc };
            if (send)
            {
                output.Transfer(new TransferUpdate(value, TransferState.Running) { Title = TitleOf?.Invoke(value.Game) });
            }
        }
    }

    /// <summary>
    /// A run is over: each transfer it started ends as its game's result says, failed with the cloud's problem and when
    /// it's tried again (<paramref name="retryLocal"/>), or done.
    /// </summary>
    public void End(IReadOnlyList<GameResult> results, DateTime? retryLocal)
    {
        foreach (var (game, open) in Take())
        {
            var title = TitleOf?.Invoke(game);
            if (results.FirstOrDefault(r => r.Game == game) is { CloudProblem: not null } failed)
            {
                output.Transfer(new TransferUpdate(open.Last, TransferState.Failed, Failed(Problem(failed.Message), retryLocal)) { Title = title ?? failed.Title });
            }
            else
            {
                output.Transfer(new TransferUpdate(open.Last, TransferState.Done, Moved(open)) { Title = title });
            }
        }
    }

    /// <summary>The cloud's problem alone, from "Kept on this PC; the upload waits: Google Drive can't be reached".</summary>
    private static string Problem(string message)
    {
        const string Waits = "Kept on this PC; the upload waits: ";
        var problem = message.StartsWith(Waits, StringComparison.Ordinal) ? message[Waits.Length..] : message;
        return problem.Length > 0 ? char.ToUpperInvariant(problem[0]) + problem[1..] : "The cloud couldn't be reached.";
    }

    /// <summary>A run stopped part way: each transfer it started is paused (a game that syncs started) or failed, with why.</summary>
    public void Stop(TransferState state, string note)
    {
        foreach (var (game, open) in Take())
        {
            output.Transfer(new TransferUpdate(open.Last, state, note) { Title = TitleOf?.Invoke(game) });
        }
    }

    /// <summary>"Google Drive can't be reached. It tries again at 21:14; your saves are safe on this PC."</summary>
    public static string Failed(string problem, DateTime? retryLocal)
    {
        var said = problem.TrimEnd();
        if (!said.EndsWith('.'))
        {
            said += ".";
        }

        return retryLocal is { } at
            ? $"{said} It tries again at {at.ToString("HH:mm", CultureInfo.InvariantCulture)}; your saves are safe on this PC."
            : $"{said} Your saves are safe on this PC.";
    }

    /// <summary>"113 MB in 19 s", or "36 named saves" when nothing but records went.</summary>
    private static string Moved(Open open)
    {
        var took = DateTime.UtcNow - open.StartedUtc;
        var time = took < TimeSpan.FromSeconds(1) ? "under a second"
            : took < TimeSpan.FromMinutes(1) ? $"{(int)took.TotalSeconds} s"
            : $"{(int)took.TotalMinutes} min {took.Seconds} s";
        var files = open.Last.FilesDone == 1 ? "1 file" : $"{open.Last.FilesDone.ToString("N0", CultureInfo.InvariantCulture)} files";
        return open.Last.BytesDone > 0 ? $"{Cli.FormatSize(open.Last.BytesDone)} in {time}" : $"{files} in {time}";
    }

    private List<(GameId Game, Open Open)> Take()
    {
        lock (_gate)
        {
            var open = _open.Select(o => (o.Key, o.Value)).ToList();
            _open.Clear();
            return open;
        }
    }

    private sealed record Open(TransferProgress Last, DateTime StartedUtc, DateTime SentUtc);
}

/// <summary>An <see cref="IProgress{T}"/> that reports on the thread reporting, in order, unlike <see cref="Progress{T}"/>.</summary>
public sealed class DirectProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
