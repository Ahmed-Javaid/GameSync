using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Model;
using GameSync.Core.Storage;
using GameSync.Core.Sync;
using GameSync.Host;

namespace GameSync.UI.ViewModels;

/// <summary>Opening KAN-61's dialog: the game, and whether it syncs between PCs or is backed up only (from Back up now and the like, KAN-63).</summary>
public sealed record KeptCopiesStart(GameId Id, string Title, bool Backup);

/// <summary>
/// A game's live save beside copies kept by hand (design system → KeptCopiesDialog; KAN-61): only the live save syncs
/// (or is backed up), and each copy comes in as a named save in the same step; Sync all of it instead keeps the whole
/// folder as one save. The folders are never changed. Over the page, like Add a place.
/// </summary>
public sealed partial class KeptCopiesViewModel(KeptCopiesStart start, LauncherActions? actions) : ObservableObject
{
    /// <summary>looking, review, keeping or done.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Looking), nameof(Reviewing), nameof(Keeping), nameof(Done), nameof(CanKeep), nameof(ShowsChoice), nameof(CloseLabel), nameof(CloseTip),
        nameof(ShowsPromise))]
    private string _stage = "looking";

    /// <summary>KAN-80: how far the look is, each copy read of how many; null until the copies are counted.</summary>
    [ObservableProperty]
    private double? _lookValue;

    [ObservableProperty]
    private string? _lookDetail;

    /// <summary>KAN-80: what the keeping is doing, and how far, how fast and how long is left.</summary>
    [ObservableProperty]
    private string _keepTitle = "Bringing the copies in";

    [ObservableProperty]
    private double? _keepValue;

    [ObservableProperty]
    private string? _keepDetail;

    [ObservableProperty]
    private string? _keepSpeed;

    [ObservableProperty]
    private string? _keepLeft;

    /// <summary>Done, with a cloud: the upload follows, shown on the game's saves (KAN-80, KAN-88).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUploadNote))]
    private string? _uploadNote;

    private DateTime _keepingSince;
    private long _keepingFrom;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle), nameof(BringLabel))]
    private IReadOnlyList<KeptItem> _items = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSkipped))]
    private IReadOnlyList<string> _skipped = [];

    /// <summary>Bring the copies in as named saves: ticked at first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(SizeText), nameof(KeepLabel), nameof(KeepTip), nameof(LeftNote))]
    private bool _bring = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveName), nameof(KeepLabel), nameof(KeepTip))]
    private string _livePath = "";

    [ObservableProperty]
    private string _liveMeta = "";

    [ObservableProperty]
    private string _liveSize = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    [ObservableProperty]
    private string _doneText = "";

    [ObservableProperty]
    private string _doneNote = "";

    private long _liveBytes;
    private long _wholeBytes;

    public GameId Id => start.Id;

    public bool Backup => start.Backup;

    public string Heading => $"{(Backup ? "Back up" : "Sync")} {start.Title}'s saves";

    public string Subtitle => Items.Count == 0
        ? "Its save folder holds your live save and copies of it you kept by hand."
        : $"Its save folder holds your live save and {Copies(Items.Count)} of it you kept by hand.";

    /// <summary>Over the live save: how it's kept.</summary>
    public string LiveHead => Backup ? "Backed up, not synced between your PCs" : "Syncs between your PCs";

    public string BringLabel => $"Bring the {Copies(Items.Count)} beside it in as named saves";

    public bool Looking => Stage == "looking";

    public bool Reviewing => Stage == "review";

    public bool Keeping => Stage == "keeping";

    public bool Done => Stage == "done";

    /// <summary>The choice and its buttons: while reviewing, and while it's being kept.</summary>
    public bool ShowsChoice => Stage is "review" or "keeping";

    /// <summary>The foot's promise, until the keeping's progress takes its place.</summary>
    public bool ShowsPromise => Stage == "review";

    /// <summary>KAN-80: while it's being kept, closing doesn't stop it: Cancel becomes Close.</summary>
    public string CloseLabel => Keeping ? "Close" : "Cancel";

    public string? CloseTip => Keeping ? "It carries on: the game's saves show how far it is" : null;

    /// <summary>What the primary says while busy.</summary>
    public string BusyLabel => Backup ? "Backing up" : "Syncing";

    public bool HasUploadNote => UploadNote is not null;

    public bool CanKeep => Stage == "review";

    public bool HasSkipped => Skipped.Count > 0;

    public bool HasError => Error is not null;

    /// <summary>How many copies become named saves: one the same as another isn't named twice.</summary>
    public int NamedCount => Items.Count(i => i.Same is null);

    public string Summary
    {
        get
        {
            if (!Bring || NamedCount == 0)
            {
                return "The live save alone";
            }

            var twins = Items.Count - NamedCount;
            return $"The live save and {Named(NamedCount)}{(twins > 0 ? $" · {twins.ToString(CultureInfo.InvariantCulture)} the same as another, not kept twice" : "")}";
        }
    }

    public string SizeText => Cli.FormatSize(_liveBytes + (Bring ? Items.Where(i => i.Same is null).Sum(i => i.Bytes) : 0));

    /// <summary>KAN-79: says what it keeps, naming the live save's folder: "Sync SPRJ0005 + 36 named saves".</summary>
    public string KeepLabel => Bring && NamedCount > 0 ? $"{Verb} {LiveName} + {Named(NamedCount)}" : $"{Verb} {LiveName}";

    public string KeepTip => (Backup ? $"Backs up the live save, {LiveName}, on this PC and in the cloud" : $"Syncs the live save, {LiveName}, between your PCs") +
        (Bring && NamedCount > 0 ? ", and keeps each copy as a named save you can restore" : "");

    public string KeepIcon => Backup ? "upload" : "sync";

    public string WholeLabel => $"{Verb} the whole folder as one save";

    public string WholeTip => $"Every version would carry all {(Items.Count + 1).ToString(CultureInfo.InvariantCulture)} folders, {Cli.FormatSize(_wholeBytes)}: the live save and every copy, as one save";

    /// <summary>KAN-79 (the owner: "What exactly is syncing? Is it the same as backup?"): said first, in the dialog.</summary>
    public string Explain => Backup
        ? "Back up keeps every version, on this PC and in the cloud; it isn't synced between your PCs."
        : "Sync keeps every version, on this PC and in the cloud, and brings the newest to your other PCs.";

    /// <summary>The live save's folder by its own name: SPRJ0005.</summary>
    public string LiveName => Path.GetFileName(Path.TrimEndingDirectorySeparator(LivePath)) is { Length: > 0 } name ? name : "the live save";

    /// <summary>Unticked: what becomes of the copies.</summary>
    public string? LeftNote => Bring ? null : "They stay in their folders, not backed up. Import kept saves… brings them in whenever you like.";

    private string Verb => Backup ? "Back up" : "Sync";

    /// <summary>The live save and the copies, read as the dialog opens: each copy's files are read to find the ones the same.</summary>
    public async void Load()
    {
        if (actions?.LookKept is not { } look)
        {
            return;
        }

        try
        {
            // KAN-80: each copy's files are read to find the ones the same as another, so the look says how far it is.
            var progress = new Progress<WorkProgress>(p =>
            {
                LookValue = p.Total > 0 ? p.Done * 100.0 / p.Total : null;
                LookDetail = p.Total > 0
                    ? $"{p.Done.ToString(CultureInfo.InvariantCulture)} of {p.Total.ToString(CultureInfo.InvariantCulture)} {(p.Total == 1 ? "folder" : "folders")} · {TransferView.OfSize(p.BytesDone, Math.Max(p.BytesTotal, p.BytesDone))} read"
                    : null;
            });
            var seen = await look(Id, progress, CancellationToken.None);
            Show(seen, DateTime.Now);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException)
        {
            Error = e.Message;
            Stage = "review";
        }
    }

    /// <summary>What the look found: the live save, then each copy oldest first, the ones the same as another said so.</summary>
    public void Show(KeptLook seen, DateTime nowLocal)
    {
        var kept = seen.Kept;
        _liveBytes = kept.LiveBytes;
        _wholeBytes = seen.WholeBytes;
        LivePath = kept.LiveFolder;
        LiveMeta = string.Join(" · ", new[]
        {
            "Your live save",
            kept.LiveNewestUtc is { } newest ? GameSavesViewModel.When(newest, nowLocal) : null,
            kept.LiveFiles == 1 ? "1 file" : $"{kept.LiveFiles.ToString(CultureInfo.InvariantCulture)} files",
        }.OfType<string>());
        LiveSize = Cli.FormatSize(kept.LiveBytes);
        Items = seen.Items.OrderBy(i => i.SavedUtc).Select(i => new KeptItem(i.Name,
            $"{GameSavesViewModel.When(i.SavedUtc, nowLocal)} · {(i.Files == 1 ? "1 file" : $"{i.Files.ToString(CultureInfo.InvariantCulture)} files")}",
            Cli.FormatSize(i.Bytes), i.SameAs) { Bytes = i.Bytes }).ToList();
        Skipped = seen.Skipped;
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(KeepLabel));
        OnPropertyChanged(nameof(NamedCount));
        Stage = "review";
    }

    /// <summary>The primary: only the live save, and the copies as named saves when ticked.</summary>
    [RelayCommand]
    private async Task Keep()
    {
        if (!CanKeep || actions?.ApplyKept is not { } apply)
        {
            return;
        }

        Error = null;
        Stage = "keeping";
        KeepTitle = Bring && NamedCount > 0 ? "Bringing the copies in" : Backup ? "Backing up the live save" : "Keeping the live save";
        (KeepValue, KeepDetail, KeepSpeed, KeepLeft, UploadNote) = (null, null, null, null, null);
        _keepingSince = DateTime.MinValue;
        try
        {
            var done = await apply(Id, Bring, Backup, new Progress<WorkProgress>(Kept), CancellationToken.None);
            UploadNote = actions?.HasCloud?.Invoke() == true
                ? "It uploads to the cloud now, beside whatever you do next: the game's saves show how far it is."
                : null;
            DoneText = Backup ? $"{start.Title}'s live save is backed up." : $"{start.Title} syncs its live save now.";
            DoneNote = Bring && done.Named > 0
                ? $"{Named(done.Named)} {(done.Named == 1 ? "is" : "are")} under Named saves, on every PC. Restore brings one back, keeping the save there now first."
                : "The copies stay in their folders, not backed up. Import kept saves… brings them in whenever you like.";
            Skipped = done.Skipped;
            Stage = "done";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException or CloudException)
        {
            Error = e.Message;
            Stage = "review";
        }
    }

    /// <summary>
    /// KAN-80: how far bringing the copies in is: reading them (quick, as the look read them a moment ago) is the first
    /// fifth of the bar, keeping each one the rest, by its bytes.
    /// </summary>
    private void Kept(WorkProgress p)
    {
        var copies = $"{p.Done.ToString(CultureInfo.InvariantCulture)} of {p.Total.ToString(CultureInfo.InvariantCulture)} {(p.Total == 1 ? "copy" : "copies")}";
        if (p.Step == "reading")
        {
            KeepValue = p.Total > 0 ? p.Done * 20.0 / p.Total : null;
            KeepDetail = $"Reading {copies}";
            return;
        }

        var now = DateTime.UtcNow;
        if (_keepingSince == DateTime.MinValue)
        {
            (_keepingSince, _keepingFrom) = (now, p.BytesDone);
        }

        KeepValue = 20 + (p.BytesTotal > 0 ? p.BytesDone * 80.0 / p.BytesTotal : p.Total > 0 ? p.Done * 80.0 / p.Total : 80);
        KeepDetail = p.BytesTotal > 0 ? $"{copies} · {TransferView.OfSize(p.BytesDone, p.BytesTotal)}" : copies;
        var seconds = (now - _keepingSince).TotalSeconds;
        var rate = seconds >= 1 ? (p.BytesDone - _keepingFrom) / seconds : 0;
        KeepSpeed = rate > 0 ? $"{Cli.FormatSize((long)rate)}/s" : null;
        KeepLeft = rate > 0 && p.BytesTotal > p.BytesDone ? TransferView.LeftOf(TimeSpan.FromSeconds((p.BytesTotal - p.BytesDone) / rate)) : null;
    }

    /// <summary>Sync all of it instead: the whole folder as one save, as the scan found it.</summary>
    [RelayCommand]
    private async Task Whole()
    {
        if (!CanKeep)
        {
            return;
        }

        if (Backup)
        {
            if (actions?.Keep is { } keep)
            {
                await keep(Id);
            }
        }
        else if (actions?.SyncGame is { } sync)
        {
            _ = sync(Id);
        }

        actions?.CloseDialog?.Invoke();
    }

    [RelayCommand]
    private void Close() => actions?.CloseDialog?.Invoke();

    [RelayCommand]
    private void Open()
    {
        if (LivePath.Length > 0)
        {
            actions?.OpenFolder?.Invoke(LivePath);
        }
    }

    private static string Copies(int count) => count == 1 ? "1 copy" : $"{count.ToString(CultureInfo.InvariantCulture)} copies";

    private static string Named(int count) => count == 1 ? "1 named save" : $"{count.ToString(CultureInfo.InvariantCulture)} named saves";
}
