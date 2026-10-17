using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.State;
using GameSync.UI.Controls;
using GameSync.UI.Theming;

namespace GameSync.UI.ViewModels;

/// <summary>A status in the Guide: its badge as the app shows it, and what it means in words.</summary>
/// <param name="Cloudless">For Upload pending: shown as with no cloud (Not synced), or with one (Sending).</param>
public sealed record GuideStatus(GameStatus? Status, string? Label, string Meaning, bool? Cloudless = null);

/// <summary>A line of the Guide's backup folder: a folder or file there, and what it holds.</summary>
public sealed record GuideFolderRow(string Path, string Meaning, bool Top);

/// <summary>
/// The Guide (ONB-08; design system version 56, GuideScreen; a friend: "Add a tutorial and a guide", "How exactly are the
/// games saved. Why does each game have 2 saves in the backup folder?"): how GameSync works in a few short topics, from
/// the rail's Guide. What GameSync keeps, every status in words, the backup folder, another PC, an earlier save, sharing,
/// and the tour again.
/// </summary>
public sealed partial class GuideViewModel : ObservableObject, IPageSurface
{
    public static readonly IReadOnlyList<NavItem> TopicList =
    [
        new("keeps", "What GameSync keeps", "saves"),
        new("statuses", "What each status means", "info"),
        new("folder", "Your backup folder", "folder"),
        new("second", "Another PC", "monitor"),
        new("back", "Going back to a save", "reset"),
        new("share", "Sharing with a friend", "share"),
        new("tour", "Take the tour again", "play"),
    ];

    private readonly Action? _startTour;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKeeps), nameof(IsStatuses), nameof(IsFolder), nameof(IsSecond), nameof(IsBack), nameof(IsShare), nameof(IsTour))]
    private string _topic = "keeps";

    /// <param name="backupFolder">This PC's backup folder, as Settings → Storage and folders has it.</param>
    /// <param name="startTour">Take the tour again: Home, with the tour over it.</param>
    public GuideViewModel(string backupFolder, Action? startTour = null, string topic = "keeps")
    {
        BackupFolder = backupFolder;
        _startTour = startTour;
        _topic = TopicList.Any(t => t.Id == topic) ? topic : "keeps";
        StartTourCommand = new RelayCommand(() => _startTour?.Invoke(), () => _startTour is not null);
    }

    public IReadOnlyList<NavItem> Topics => TopicList;

    /// <summary>The calm glow, like Settings (LOOK-17).</summary>
    public GlassStrength Strength => GlassStrength.Glow;

    public string BackupFolder { get; }

    public string BackupFolderLine => $"{BackupFolder}. Move it in Settings, Storage and folders.";

    public bool IsKeeps => Topic == "keeps";

    public bool IsStatuses => Topic == "statuses";

    public bool IsFolder => Topic == "folder";

    public bool IsSecond => Topic == "second";

    public bool IsBack => Topic == "back";

    public bool IsShare => Topic == "share";

    public bool IsTour => Topic == "tour";

    public ICommand StartTourCommand { get; }

    /// <summary>Every status a game shows, as its badge does, with what it means and what to do.</summary>
    public IReadOnlyList<GuideStatus> Statuses { get; } =
    [
        new(GameStatus.Synced, null, "The newest save is on this PC and where your saves are kept. Nothing to do."),
        new(GameStatus.UploadPending, null, "Backed up on this PC only, as no cloud is connected. Connect Google Drive or a folder from Home, and it syncs.", Cloudless: true),
        new(GameStatus.UploadPending, null, "Backed up here, and on its way to your cloud. Shown only while it goes, or until you're back online.", Cloudless: false),
        new(GameStatus.NewerInCloud, "Newer on another PC", "You played it on another PC since. GameSync brings that save down before you play here."),
        new(GameStatus.HeldForReview, null, "Its saves changed while the game wasn't running. GameSync keeps them aside until you look."),
        new(GameStatus.Conflict, null, "Two PCs played it without syncing between. The newest save is in place and the other is kept; Swap if that's wrong."),
        new(GameStatus.BackupOnly, "Synced by Steam", "Steam syncs it between your PCs. GameSync keeps a copy of every version too."),
        new(null, null, "GameSync found its saves but isn't keeping them. Back them up from its page."),
        new(GameStatus.Playing, null, "Running now. It's backed up when you stop."),
    ];

    /// <summary>The backup folder, folder by folder: a game's saves, and the second folder that's its settings on this PC.</summary>
    public IReadOnlyList<GuideFolderRow> FolderRows { get; } =
    [
        new(@"games\dark-souls-iii\", "its saves", true),
        new(@"   versions\2026-10-07T21-44Z_DESKTOP.json", "one per version: when, on which PC, which files", false),
        new(@"   blobs\ab\ab12….gz", "each file once, however many versions share it", false),
        new(@"   pins\", "named saves, and versions kept for good", false),
        new(@"   outbox\", "versions waiting to go to your cloud", false),
        new(@"games\dark-souls-iii--pc-cb75de22\", "its settings on this PC (graphics, keys), never synced", true),
        new(@"devices\", "each PC that syncs, by name", true),
    ];
}
