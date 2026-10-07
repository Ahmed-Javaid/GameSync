using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GameSync.Core.Model;
using GameSync.Core.Storage;
using GameSync.Host;

namespace GameSync.UI.ViewModels;

/// <summary>
/// An upload or a download of one game's saves as its saves show it (design system → JobProgress; KAN-80): what it's
/// doing, how far, how fast and how long is left, then how it ended, for a few seconds once it's done. One per game for
/// as long as the app runs, so it's there whenever the page opens, whether or not the dialog that started it is.
/// </summary>
public sealed partial class TransferView : ObservableObject
{
    /// <summary>
    /// What a file costs beside its bytes: each one is a call to the cloud (a version's record or a pin is nothing but
    /// that), so the bar and the time left count it as this many bytes.
    /// </summary>
    private const long FileWeight = 256 * 1024;

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan DoneFor = TimeSpan.FromSeconds(6);
    private readonly Queue<(DateTime At, long Bytes, double Units)> _samples = new();
    private DateTime _endedUtc = DateTime.MaxValue;
    private TransferDirection _direction;

    /// <summary>A transfer is under way, or ended a moment ago: the page shows it.</summary>
    [ObservableProperty]
    private bool _isShown;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private double? _value;

    [ObservableProperty]
    private string? _detail;

    [ObservableProperty]
    private string? _speed;

    [ObservableProperty]
    private string? _left;

    /// <summary><c>running</c>, <c>paused</c>, <c>done</c> or <c>failed</c>, as GsJobProgress takes it.</summary>
    [ObservableProperty]
    private string _state = "running";

    [ObservableProperty]
    private string? _note;

    /// <summary>The game's name, when the engine said it.</summary>
    public string? GameTitle { get; private set; }

    /// <summary>Running, uploading or downloading: the tray's tooltip and the window can say so.</summary>
    public bool IsRunning => IsShown && State == "running";

    /// <summary>"Uploading 37%", for the tray icon's tooltip.</summary>
    public string Short => $"{(_direction == TransferDirection.Down ? "downloading" : "uploading")}{(Value is { } v ? $" {Math.Round(v).ToString(CultureInfo.InvariantCulture)}%" : "")}";

    /// <summary>The engine's word on how far it is, or how it ended; <paramref name="cloud"/> names where it goes ("Google Drive").</summary>
    public void Apply(TransferUpdate update, string cloud, DateTime nowUtc)
    {
        var p = update.Progress;
        if (p.Direction != _direction || State != "running" || !IsShown)
        {
            _samples.Clear();
        }

        _direction = p.Direction;
        GameTitle = update.Title ?? GameTitle;
        var up = p.Direction == TransferDirection.Up;
        var units = p.BytesTotal + (double)p.FilesTotal * FileWeight;
        var done = p.BytesDone + (double)p.FilesDone * FileWeight;
        Value = units > 0 ? Math.Clamp(done * 100 / units, 0, 100) : null;
        Detail = DetailOf(p);
        switch (update.State)
        {
            case TransferState.Running:
                _endedUtc = DateTime.MaxValue;
                State = "running";
                Title = up ? $"Uploading to {cloud}" : $"Downloading from {cloud}";
                Note = null;
                Rate(nowUtc, p.BytesDone, done, units);
                break;
            case TransferState.Paused:
                _endedUtc = DateTime.MaxValue;
                State = "paused";
                Title = up ? "Upload paused" : "Download paused";
                Note = update.Note;
                (Speed, Left) = (null, null);
                break;
            case TransferState.Failed:
                _endedUtc = DateTime.MaxValue;
                State = "failed";
                Title = up ? "The upload waits" : "The download didn't finish";
                Note = update.Note;
                (Speed, Left) = (null, null);
                break;
            default:
                _endedUtc = nowUtc;
                State = "done";
                Title = up ? $"Uploaded to {cloud}" : $"Downloaded from {cloud}";
                Note = update.Note;
                (Speed, Left) = (null, null);
                break;
        }

        IsShown = true;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(Short));
    }

    /// <summary>A done transfer goes a few seconds after it ended; true when it went.</summary>
    public bool Expire(DateTime nowUtc)
    {
        if (!IsShown || State != "done" || nowUtc - _endedUtc < DoneFor)
        {
            return false;
        }

        IsShown = false;
        OnPropertyChanged(nameof(IsRunning));
        return true;
    }

    /// <summary>"42.1 of 113.0 MB · 412 of 1,108 files"; files alone when only records and pins move.</summary>
    public static string DetailOf(TransferProgress p)
    {
        var files = $"{p.FilesDone.ToString("N0", CultureInfo.InvariantCulture)} of {p.FilesTotal.ToString("N0", CultureInfo.InvariantCulture)} {(p.FilesTotal == 1 ? "file" : "files")}";
        return p.BytesTotal > 0 ? $"{OfSize(p.BytesDone, p.BytesTotal)} · {files}" : files;
    }

    /// <summary>"42.1 of 113.0 MB", or "812.0 KB of 1.2 GB" when they're in different units.</summary>
    public static string OfSize(long done, long total)
    {
        var (a, b) = (Cli.FormatSize(done), Cli.FormatSize(total));
        var unit = b[(b.LastIndexOf(' ') + 1)..];
        return a.EndsWith(" " + unit, StringComparison.Ordinal) ? $"{a[..^(unit.Length + 1)]} of {b}" : $"{a} of {b}";
    }

    /// <summary>"about 12 s left", "about 3 min left".</summary>
    public static string LeftOf(TimeSpan left) => left.TotalSeconds switch
    {
        < 1.5 => "a moment left",
        < 90 => $"about {Math.Ceiling(left.TotalSeconds).ToString(CultureInfo.InvariantCulture)} s left",
        < 5400 => $"about {Math.Round(left.TotalMinutes).ToString(CultureInfo.InvariantCulture)} min left",
        _ => $"about {Math.Round(left.TotalHours, 1).ToString(CultureInfo.InvariantCulture)} h left",
    };

    /// <summary>The speed and the time left, over the last few seconds, so they don't jump with every file; once there's a second to go on.</summary>
    private void Rate(DateTime nowUtc, long bytes, double done, double units)
    {
        _samples.Enqueue((nowUtc, bytes, done));
        while (_samples.Count > 2 && nowUtc - _samples.Peek().At > Window)
        {
            _samples.Dequeue();
        }

        var first = _samples.Peek();
        var span = (nowUtc - first.At).TotalSeconds;
        if (span < 1)
        {
            return;
        }

        var bytesPerSecond = (bytes - first.Bytes) / span;
        var unitsPerSecond = (done - first.Units) / span;
        Speed = bytesPerSecond > 0 ? $"{Cli.FormatSize((long)bytesPerSecond)}/s" : null;
        Left = unitsPerSecond > 0 && units > done ? LeftOf(TimeSpan.FromSeconds((units - done) / unitsPerSecond)) : null;
    }
}

/// <summary>
/// Every game's upload or download, as the app hears of them (KAN-80): the window's pages each show their game's, and the
/// tray icon's tooltip the one under way. Used on the UI thread.
/// </summary>
public sealed class TransferBoard
{
    private readonly Dictionary<GameId, TransferView> _views = [];

    /// <summary>Where saves go, as the titles say it: "Google Drive".</summary>
    public string Cloud { get; set; } = "the cloud";

    /// <summary>The game's, made the first time it's asked for and kept, so a page bound to it sees every transfer after.</summary>
    public TransferView For(GameId game)
    {
        if (!_views.TryGetValue(game, out var view))
        {
            _views[game] = view = new TransferView();
        }

        return view;
    }

    public void Apply(TransferUpdate update, DateTime nowUtc) => For(update.Game).Apply(update, Cloud, nowUtc);

    /// <summary>Done transfers go a few seconds after they ended; true when one went.</summary>
    public bool Tick(DateTime nowUtc) => _views.Values.Aggregate(false, (went, view) => view.Expire(nowUtc) | went);

    /// <summary>A transfer done a moment ago, still shown until it goes.</summary>
    public bool Expiring => _views.Values.Any(v => v.IsShown && v.State == "done");

    /// <summary>The game whose transfer is under way, if any, with it.</summary>
    public (GameId Game, TransferView View)? Running => _views.Where(v => v.Value.IsRunning).Select(v => ((GameId, TransferView)?)(v.Key, v.Value)).FirstOrDefault();
}
