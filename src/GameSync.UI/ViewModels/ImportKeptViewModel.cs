using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Model;
using GameSync.Core.Storage;
using GameSync.Core.Sync;
using GameSync.Host;

namespace GameSync.UI.ViewModels;

/// <summary>
/// A kept copy found in the folder: its name, when it was saved and its files, and its size, or what it's the same as:
/// another copy here ("Same as Before Rom"), or a version already kept, which it only names.
/// </summary>
public sealed record KeptItem(string Name, string Meta, string Size, string? Same)
{
    /// <summary>Its size in bytes, for a sum of what's kept (KAN-61).</summary>
    public long Bytes { get; init; }

    public bool HasSame => Same is not null;

    /// <summary>It becomes a named save: a copy of its own, or one already in the history, which only gets the name.</summary>
    public bool BecomesNamed => Same is null || Same.StartsWith("version ", StringComparison.Ordinal);

    /// <summary>The engine says "'Before Rom'" for another copy and "version …" for one already in the history.</summary>
    public string SameText => Same is null ? "" : Same.StartsWith("version ", StringComparison.Ordinal) ? "Already kept" : $"Same as {Same.Trim('\'')}";

    public override string ToString() => string.Join(", ", new[] { Name, Meta, Same is null ? Size : $"{SameText}, stored once" });
}

/// <summary>
/// Import kept saves (design system → ImportKeptSavesDialog, BAK-19): the folder holding copies of a game's saves kept by
/// hand, each copy by name, then Import: each becomes a named save on every PC, never current by itself. The folders
/// are never changed. Over the page, like Properties.
/// </summary>
public sealed partial class ImportKeptViewModel : ObservableObject
{
    private readonly LauncherActions? _actions;
    private int _reads;

    /// <summary>pick, looking, review, importing or done.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Picking), nameof(Reviewing), nameof(Busy), nameof(Looking), nameof(Importing), nameof(Done), nameof(CanImport), nameof(ShowsFolder),
        nameof(ImportLabel))]
    private string _stage = "pick";

    [ObservableProperty]
    private string? _folder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport), nameof(ImportLabel), nameof(FoundText), nameof(NamedCount))]
    private IReadOnlyList<KeptItem> _items = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSkipped))]
    private IReadOnlyList<string> _skipped = [];

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private string _sizeText = "";

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoots))]
    private IReadOnlyList<Choice> _roots = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RootLabel))]
    private string? _root;

    [ObservableProperty]
    private string _doneText = "";

    public ImportKeptViewModel(GameId id, string title, LauncherActions? actions)
    {
        Id = id;
        Title = title;
        _actions = actions;
        ImportCommand = new RelayCommand(Import);
        CloseCommand = new RelayCommand(() => _actions?.CloseDialog?.Invoke());
        SelectRootCommand = new RelayCommand<string>(root =>
        {
            if (Roots.Any(r => r.Id == root) && root != Root)
            {
                Root = root;
                if (Folder is { } folder)
                {
                    Read(folder);
                }
            }
        });
        OpenCommand = new RelayCommand(() =>
        {
            if (Folder is { } folder)
            {
                _actions?.OpenFolder?.Invoke(folder);
            }
        }, () => _actions?.OpenFolder is not null);
    }

    public GameId Id { get; }

    public string Title { get; }

    public string Subtitle => $"{Title}: copies you kept by hand, as named saves";

    public string Intro => $"Pick the folder that holds the copies you kept of {Title}'s saves: each copy in a folder named after the moment, beside the live save or anywhere else.";

    public bool Picking => Stage == "pick";

    public bool ShowsFolder => Stage is "looking" or "review" or "importing";

    public bool Reviewing => Stage == "review";

    public bool Busy => Stage is "looking" or "importing";

    /// <summary>KAN-80: the folder's copies are being read.</summary>
    public bool Looking => Stage == "looking";

    /// <summary>KAN-80: the copies are coming in: Import says Importing….</summary>
    public bool Importing => Stage == "importing";

    public bool Done => Stage == "done";

    public bool CanImport => Stage == "review" && NamedCount > 0;

    /// <summary>How many copies become named saves: one the same as another copy, or as a named save, isn't named again.</summary>
    public int NamedCount => Items.Count(i => i.BecomesNamed);

    public bool HasRoots => Roots.Count > 1;

    public bool HasSkipped => Skipped.Count > 0;

    public string RootLabel => Roots.FirstOrDefault(r => r.Id == Root)?.Label ?? "";

    public string FoundText => Items.Count == 1 ? "1 kept copy found" : $"{Items.Count.ToString(CultureInfo.InvariantCulture)} kept copies found";

    public string ImportLabel => NamedCount switch { 0 => "Import", 1 => "Import 1 named save", var n => $"Import {n.ToString(CultureInfo.InvariantCulture)} named saves" };

    public ICommand ImportCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand SelectRootCommand { get; }

    public ICommand OpenCommand { get; }

    /// <summary>The game's places, read as the dialog opens: a choice shows only when it has more than one.</summary>
    public async void Load()
    {
        if (_actions?.SaveRoots is not { } roots)
        {
            return;
        }

        try
        {
            Roots = (await roots(Id, CancellationToken.None)).Select(r => new Choice(r.Key, Short(r.Portable.Replace('/', '\\')))).ToList();
            Root ??= Roots.FirstOrDefault(r => r.Id == "saves")?.Id ?? Roots.FirstOrDefault()?.Id;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException)
        {
            Error = e.Message;
        }
    }

    /// <summary>A place's folder short enough for its menu: the end of it, which names the game's own folders.</summary>
    public static string Short(string folder) => folder.Length <= 48 ? folder : $"…{folder[^47..]}";

    /// <summary>The folder picked: what it holds, read without importing anything.</summary>
    public async void Read(string folder)
    {
        if (_actions?.KeptSaves is not { } read)
        {
            return;
        }

        var ticket = ++_reads;
        Folder = folder;
        Error = null;
        Stage = "looking";
        try
        {
            var report = await read(Id, folder, HasRoots ? Root : null, false, CancellationToken.None);
            if (ticket == _reads)
            {
                Show(report, DateTime.Now);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException or CloudException)
        {
            if (ticket == _reads)
            {
                Error = e.Message;
                Items = [];
                Stage = "review";
            }
        }
    }

    /// <summary>What a read of the folder found: each copy newest last, the ones the same as another said so.</summary>
    public void Show(ImportReport report, DateTime nowLocal)
    {
        Items = report.Items.OrderBy(i => i.SavedUtc).Select(i => new KeptItem(i.Name,
            $"{GameSavesViewModel.When(i.SavedUtc, nowLocal)} · {(i.Files == 1 ? "1 file" : $"{i.Files.ToString(CultureInfo.InvariantCulture)} files")}",
            Cli.FormatSize(i.Bytes), i.SameAs)).ToList();
        Skipped = report.Skipped;
        var again = Items.Count(i => !i.BecomesNamed);
        Summary = (NamedCount == 1 ? "1 named save" : $"{NamedCount.ToString(CultureInfo.InvariantCulture)} named saves") +
            (again == 0 ? "" : $" · {again.ToString(CultureInfo.InvariantCulture)} the same as another, not kept twice");
        SizeText = Cli.FormatSize(report.Items.Where(i => i.SameAs is null).Sum(i => i.Bytes));
        if (Items.Count == 0 && Error is null)
        {
            Error = "No kept copies are in this folder: each copy is a folder of its own holding the save's files, named after the moment.";
        }

        Stage = "review";
    }

    private async void Import()
    {
        if (!CanImport || Folder is not { } folder || _actions?.KeptSaves is not { } import)
        {
            return;
        }

        Stage = "importing";
        Error = null;
        try
        {
            await import(Id, folder, HasRoots ? Root : null, true, CancellationToken.None);
            DoneText = NamedCount == 1 ? "Imported 1 named save." : $"Imported {NamedCount.ToString(CultureInfo.InvariantCulture)} named saves.";
            Stage = "done";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException or CloudException)
        {
            Error = $"Nothing was imported: {e.Message}";
            Stage = "review";
        }
    }
}
