using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace GameSync.UI.Controls;

public enum FolderState
{
    Ok,

    /// <summary>A new backup folder is being filled: the old one stays in use until every hash matches.</summary>
    Moving,

    /// <summary>The drive is unplugged: backups pause, and nothing was deleted.</summary>
    Missing,

    /// <summary>The picked folder isn't allowed, with the reason.</summary>
    Refused,
}

/// <summary>
/// A folder the person can change (FOLD-10): the full path in mono, a meta line (size, versions, free space), Change…
/// and Open in Explorer. While moving it shows progress; a missing drive and a refused folder show their message.
/// </summary>
public class GsFolderField : TemplatedControl
{
    public static readonly StyledProperty<string?> PathProperty = AvaloniaProperty.Register<GsFolderField, string?>(nameof(Path));

    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<GsFolderField, string?>(nameof(Label));

    public static readonly StyledProperty<string?> MetaProperty = AvaloniaProperty.Register<GsFolderField, string?>(nameof(Meta));

    public static readonly StyledProperty<string> IconProperty = AvaloniaProperty.Register<GsFolderField, string>(nameof(Icon), "folder");

    public static readonly StyledProperty<FolderState> StateProperty = AvaloniaProperty.Register<GsFolderField, FolderState>(nameof(State));

    public static readonly StyledProperty<string?> MessageProperty = AvaloniaProperty.Register<GsFolderField, string?>(nameof(Message));

    public static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<GsFolderField, double>(nameof(Progress));

    public static readonly StyledProperty<string> ChangeLabelProperty = AvaloniaProperty.Register<GsFolderField, string>(nameof(ChangeLabel), "Change…");

    public static readonly StyledProperty<ICommand?> ChangeCommandProperty = AvaloniaProperty.Register<GsFolderField, ICommand?>(nameof(ChangeCommand));

    public static readonly StyledProperty<ICommand?> OpenCommandProperty = AvaloniaProperty.Register<GsFolderField, ICommand?>(nameof(OpenCommand));

    public string? Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Meta
    {
        get => GetValue(MetaProperty);
        set => SetValue(MetaProperty, value);
    }

    public string Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public FolderState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public string ChangeLabel
    {
        get => GetValue(ChangeLabelProperty);
        set => SetValue(ChangeLabelProperty, value);
    }

    public ICommand? ChangeCommand
    {
        get => GetValue(ChangeCommandProperty);
        set => SetValue(ChangeCommandProperty, value);
    }

    public ICommand? OpenCommand
    {
        get => GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty)
        {
            PseudoClasses.Set(":moving", State == FolderState.Moving);
            PseudoClasses.Set(":missing", State == FolderState.Missing);
            PseudoClasses.Set(":refused", State == FolderState.Refused);
        }
    }
}

/// <summary>A folder the person added: its path, a meta line, an optional tag such as BY GAME ID, and an icon.</summary>
public sealed record FolderItem(string Path, string? Meta = null, string? Tag = null, string Icon = "folder");

/// <summary>Folders the person added, each removable, with an Add button underneath (FOLD-07, FOLD-08).</summary>
public class GsFolderList : TemplatedControl
{
    public static readonly StyledProperty<IReadOnlyList<FolderItem>> ItemsProperty = AvaloniaProperty.Register<GsFolderList, IReadOnlyList<FolderItem>>(nameof(Items), []);

    public static readonly StyledProperty<ICommand?> RemoveCommandProperty = AvaloniaProperty.Register<GsFolderList, ICommand?>(nameof(RemoveCommand));

    public static readonly StyledProperty<ICommand?> AddCommandProperty = AvaloniaProperty.Register<GsFolderList, ICommand?>(nameof(AddCommand));

    public static readonly StyledProperty<string> AddLabelProperty = AvaloniaProperty.Register<GsFolderList, string>(nameof(AddLabel), "Add folder");

    public static readonly StyledProperty<string> EmptyProperty = AvaloniaProperty.Register<GsFolderList, string>(nameof(Empty), "None yet.");

    public IReadOnlyList<FolderItem> Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>Runs with the <see cref="FolderItem"/> to remove; without it, the rows have no Remove button.</summary>
    public ICommand? RemoveCommand
    {
        get => GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    /// <summary>Without it, there's no Add button.</summary>
    public ICommand? AddCommand
    {
        get => GetValue(AddCommandProperty);
        set => SetValue(AddCommandProperty, value);
    }

    public string AddLabel
    {
        get => GetValue(AddLabelProperty);
        set => SetValue(AddLabelProperty, value);
    }

    public string Empty
    {
        get => GetValue(EmptyProperty);
        set => SetValue(EmptyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty)
        {
            PseudoClasses.Set(":empty", Items.Count == 0);
        }
    }
}

/// <summary>
/// A dialog (share, import, a name for a save): <c>bg-200</c> with the only shadow in the app, a title and subtitle,
/// the body, and a footer of actions.
/// </summary>
public class GsDialog : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<GsDialog, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty = AvaloniaProperty.Register<GsDialog, string?>(nameof(Subtitle));

    public static readonly StyledProperty<object?> FooterProperty = AvaloniaProperty.Register<GsDialog, object?>(nameof(Footer));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }
}
