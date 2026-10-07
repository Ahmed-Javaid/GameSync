using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Threading;
using GameSync.Core.State;

namespace GameSync.UI.Controls;

/// <summary>
/// Pill button (design system → Button). Classes: <c>primary</c> (one per view: the recommended action),
/// <c>secondary</c> (the default), <c>ghost</c> (Cancel, Done), <c>danger</c> (deleting a version), and <c>sm</c>.
/// <see cref="IsBusy"/> (KAN-80): it's doing its job. It keeps its colour, since a greyed-out button looks broken; the
/// spinner takes its icon's place and <see cref="BusyText"/> ("Syncing") its words, with dots counting up after them;
/// it keeps at least its width, takes no clicks, and stays busy at least 0.4 s so a quick job doesn't flicker.
/// </summary>
public class GsButton : Button
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<GsButton, string?>(nameof(Icon));

    public static readonly StyledProperty<string?> IconAfterProperty = AvaloniaProperty.Register<GsButton, string?>(nameof(IconAfter));

    public static readonly StyledProperty<double> IconSizeProperty = AvaloniaProperty.Register<GsButton, double>(nameof(IconSize), 18);

    public static readonly StyledProperty<bool> IsBusyProperty = AvaloniaProperty.Register<GsButton, bool>(nameof(IsBusy));

    public static readonly StyledProperty<string?> BusyTextProperty = AvaloniaProperty.Register<GsButton, string?>(nameof(BusyText));

    public static readonly DirectProperty<GsButton, bool> ShowsBusyProperty = AvaloniaProperty.RegisterDirect<GsButton, bool>(nameof(ShowsBusy), b => b.ShowsBusy);

    public static readonly DirectProperty<GsButton, bool> ShowsIconProperty = AvaloniaProperty.RegisterDirect<GsButton, bool>(nameof(ShowsIcon), b => b.ShowsIcon);

    public static readonly DirectProperty<GsButton, bool> ShowsIconAfterProperty =
        AvaloniaProperty.RegisterDirect<GsButton, bool>(nameof(ShowsIconAfter), b => b.ShowsIconAfter);

    public static readonly DirectProperty<GsButton, string?> BusyWordsProperty = AvaloniaProperty.RegisterDirect<GsButton, string?>(nameof(BusyWords), b => b.BusyWords);

    private static readonly TimeSpan LeastBusy = TimeSpan.FromMilliseconds(400);
    private bool _showsBusy;
    private bool _showsIcon;
    private bool _showsIconAfter;
    private string? _busyWords;
    private DateTime _busySince;
    private double _heldMinWidth = double.NaN;
    private IDisposable? _ending;

    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? IconAfter
    {
        get => GetValue(IconAfterProperty);
        set => SetValue(IconAfterProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>Its job is under way (KAN-80).</summary>
    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    /// <summary>What it says while busy, as a verb: "Syncing", "Restoring". Its own words when not given.</summary>
    public string? BusyText
    {
        get => GetValue(BusyTextProperty);
        set => SetValue(BusyTextProperty, value);
    }

    /// <summary>It shows as busy: from <see cref="IsBusy"/>, and for at least 0.4 s.</summary>
    public bool ShowsBusy
    {
        get => _showsBusy;
        private set => SetAndRaise(ShowsBusyProperty, ref _showsBusy, value);
    }

    public bool ShowsIcon
    {
        get => _showsIcon;
        private set => SetAndRaise(ShowsIconProperty, ref _showsIcon, value);
    }

    public bool ShowsIconAfter
    {
        get => _showsIconAfter;
        private set => SetAndRaise(ShowsIconAfterProperty, ref _showsIconAfter, value);
    }

    public string? BusyWords
    {
        get => _busyWords;
        private set => SetAndRaise(BusyWordsProperty, ref _busyWords, value);
    }

    protected override void OnClick()
    {
        // Busy: a second press would start the job again.
        if (!ShowsBusy)
        {
            base.OnClick();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsBusyProperty)
        {
            _ending?.Dispose();
            _ending = null;
            if (IsBusy)
            {
                if (!ShowsBusy)
                {
                    _busySince = DateTime.UtcNow;
                    Show(busy: true);
                }
            }
            else if (ShowsBusy)
            {
                var left = LeastBusy - (DateTime.UtcNow - _busySince);
                if (left > TimeSpan.Zero)
                {
                    _ending = DispatcherTimer.RunOnce(() => Show(busy: false), left);
                }
                else
                {
                    Show(busy: false);
                }
            }
        }
        else if (change.Property == IconProperty || change.Property == IconAfterProperty || change.Property == BusyTextProperty || change.Property == ContentProperty)
        {
            Show(ShowsBusy);
        }
    }

    private void Show(bool busy)
    {
        if (busy && !ShowsBusy && Bounds.Width > MinWidth)
        {
            // Nothing beside it moves while its words change.
            _heldMinWidth = MinWidth;
            MinWidth = Bounds.Width;
        }
        else if (!busy && !double.IsNaN(_heldMinWidth))
        {
            MinWidth = _heldMinWidth;
            _heldMinWidth = double.NaN;
        }

        ShowsBusy = busy;
        PseudoClasses.Set(":busy", busy);
        ShowsIcon = !busy && Icon is not null;
        ShowsIconAfter = !busy && IconAfter is not null;
        BusyWords = BusyText ?? Content as string;
        AutomationProperties.SetItemStatus(this, busy ? $"{BusyWords}…" : null);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        Show(ShowsBusy);
    }
}

/// <summary>A choice in a <see cref="GsSelect"/>: what it stands for, and what it says.</summary>
public sealed record SelectOption(string Id, string Label);

/// <summary>
/// A choice of three or more (design system → Select): the current choice on a small secondary button with a chevron,
/// and a menu of the choices under it with the current one checked. <see cref="Label"/> names it for a screen reader,
/// which hears "Game: All games".
/// </summary>
public class GsSelect : GsButton
{
    public static readonly StyledProperty<IReadOnlyList<SelectOption>> OptionsProperty =
        AvaloniaProperty.Register<GsSelect, IReadOnlyList<SelectOption>>(nameof(Options), []);

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<GsSelect, string?>(nameof(Value), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<GsSelect, string?>(nameof(Label));

    public GsSelect()
    {
        Classes.Add("sm");
        IconAfter = "chevronDown";
        Flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
    }

    public IReadOnlyList<SelectOption> Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>The chosen option's <see cref="SelectOption.Id"/>.</summary>
    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(GsButton);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OptionsProperty || change.Property == ValueProperty || change.Property == LabelProperty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        var current = Options.FirstOrDefault(o => o.Id == Value) ?? Options.FirstOrDefault();
        Content = current?.Label;
        AutomationProperties.SetName(this, Label is null ? current?.Label : $"{Label}: {current?.Label}");
        if (Flyout is not MenuFlyout menu)
        {
            return;
        }

        menu.Items.Clear();
        foreach (var option in Options)
        {
            var item = new GsMenuItem { Header = option.Label, ToggleType = MenuItemToggleType.CheckBox, IsChecked = option == current };
            item.Click += (_, _) => Value = option.Id;
            menu.Items.Add(item);
        }
    }
}

/// <summary>
/// 40px round icon button; its label is the tooltip and what a screen reader says (A11Y-03). Classes: <c>glass</c>,
/// <c>sm</c>. <see cref="IsOn"/> makes it a toggle that's on, such as the favourite star, which then fills.
/// </summary>
public class GsIconButton : Button
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<GsIconButton, string?>(nameof(Icon));

    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<GsIconButton, string?>(nameof(Label));

    public static readonly StyledProperty<double> IconSizeProperty = AvaloniaProperty.Register<GsIconButton, double>(nameof(IconSize), 18);

    public static readonly StyledProperty<bool> IsOnProperty = AvaloniaProperty.Register<GsIconButton, bool>(nameof(IsOn));

    public static readonly StyledProperty<bool> IsBusyProperty = AvaloniaProperty.Register<GsIconButton, bool>(nameof(IsBusy));

    public static readonly StyledProperty<string?> BusyLabelProperty = AvaloniaProperty.Register<GsIconButton, string?>(nameof(BusyLabel));

    /// <summary>KAN-80: its job is under way: its icon turns, and its tooltip says <see cref="BusyLabel"/>.</summary>
    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    /// <summary>What its tooltip says while busy: "Syncing".</summary>
    public string? BusyLabel
    {
        get => GetValue(BusyLabelProperty);
        set => SetValue(BusyLabelProperty, value);
    }

    /// <summary>A toggle that's on: its icon fills, in <c>secondary</c> off art and <c>on-art</c> on it.</summary>
    public bool IsOn
    {
        get => GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelProperty || change.Property == IsBusyProperty || change.Property == BusyLabelProperty)
        {
            ToolTip.SetTip(this, IsBusy && BusyLabel is { } busy ? $"{busy}…" : Label);
            AutomationProperties.SetName(this, Label);
            AutomationProperties.SetItemStatus(this, IsBusy && BusyLabel is { } status ? $"{status}…" : null);
            PseudoClasses.Set(":busy", IsBusy);
        }
        else if (change.Property == IsOnProperty)
        {
            PseudoClasses.Set(":on", IsOn);
        }
    }
}

/// <summary>The rounded <c>bg-200</c> panel every launcher widget and settings group lives in, with an optional title, subtitle and action.</summary>
public class GsCard : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<GsCard, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty = AvaloniaProperty.Register<GsCard, string?>(nameof(Subtitle));

    public static readonly StyledProperty<object?> ActionProperty = AvaloniaProperty.Register<GsCard, object?>(nameof(Action));

    /// <summary>Before the title: the Zenith badge on the Achievements page (design system version 35, Card's `lead`).</summary>
    public static readonly StyledProperty<object?> LeadProperty = AvaloniaProperty.Register<GsCard, object?>(nameof(Lead));

    /// <summary>
    /// Art behind the card's title and content, filling the card and clipped to its corners, never taking clicks: the
    /// Zenith's scene on the Achievements page (design system version 47).
    /// </summary>
    public static readonly StyledProperty<object?> BackdropProperty = AvaloniaProperty.Register<GsCard, object?>(nameof(Backdrop));

    public object? Backdrop
    {
        get => GetValue(BackdropProperty);
        set => SetValue(BackdropProperty, value);
    }

    public object? Lead
    {
        get => GetValue(LeadProperty);
        set => SetValue(LeadProperty, value);
    }

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

    public object? Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty || change.Property == ActionProperty || change.Property == SubtitleProperty || change.Property == LeadProperty)
        {
            PseudoClasses.Set(":header", Title is not null || Action is not null);
            PseudoClasses.Set(":subtitle", Subtitle is not null);
            PseudoClasses.Set(":lead", Lead is not null);
        }
        else if (change.Property == BackdropProperty)
        {
            PseudoClasses.Set(":backdrop", Backdrop is not null);
        }
    }
}

/// <summary>
/// A game's one status as an icon and a word (LOOK-08, A11Y-01), in colours that never follow the theme: <c>ok</c> for
/// fine or moving, <c>warn</c> for needs you, <c>play</c> while running, <c>danger</c> for Blocked, <c>neutral</c>.
/// </summary>
public class GsStatusBadge : TemplatedControl
{
    public static readonly StyledProperty<GameStatus?> StatusProperty = AvaloniaProperty.Register<GsStatusBadge, GameStatus?>(nameof(Status), GameStatus.Synced);

    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<GsStatusBadge, string?>(nameof(Label));

    public static readonly StyledProperty<bool> PlainProperty = AvaloniaProperty.Register<GsStatusBadge, bool>(nameof(Plain));

    public static readonly DirectProperty<GsStatusBadge, string> IconNameProperty =
        AvaloniaProperty.RegisterDirect<GsStatusBadge, string>(nameof(IconName), b => b.IconName);

    public static readonly DirectProperty<GsStatusBadge, string> TextProperty =
        AvaloniaProperty.RegisterDirect<GsStatusBadge, string>(nameof(Text), b => b.Text);

    private string _iconName = "cloudCheck";
    private string _text = "Synced";

    public GsStatusBadge() => Update();

    /// <summary>Null for a game found but not syncing yet: a neutral badge that says so.</summary>
    public GameStatus? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool Plain
    {
        get => GetValue(PlainProperty);
        set => SetValue(PlainProperty, value);
    }

    public string IconName
    {
        get => _iconName;
        private set => SetAndRaise(IconNameProperty, ref _iconName, value);
    }

    public string Text
    {
        get => _text;
        private set => SetAndRaise(TextProperty, ref _text, value);
    }

    /// <summary>The status's colour family, icon and word, as the design system's StatusBadge has them.</summary>
    public static (string Kind, string Icon, string Word) Describe(GameStatus? status) => status switch
    {
        // Synced is a cloud with a check, never a bare tick (design system version 35): in the cloud and on your PCs.
        GameStatus.Synced => ("ok", "cloudCheck", "Synced"),
        GameStatus.Playing => ("play", "play", "Playing"),
        GameStatus.UploadPending => ("ok", "upload", "Upload pending"),
        GameStatus.NewerInCloud => ("ok", "download", "Newer in cloud"),
        GameStatus.Conflict => ("warn", "alert", "Conflict"),
        GameStatus.HeldForReview => ("warn", "pause", "Held for review"),
        GameStatus.FilesInUse => ("warn", "lock", "Files in use"),
        GameStatus.SavesMissing or GameStatus.NoSaves => ("warn", "search", "Saves not found"),
        GameStatus.NotAvailable => ("neutral", "unplug", "Not available"),
        GameStatus.Blocked or GameStatus.Error => ("danger", "block", "Blocked"),
        GameStatus.BackupOnly => ("neutral", "shield", "Synced by its store"),
        null => ("neutral", "cloud", "Not syncing yet"),
        _ => ("ok", "cloudCheck", "Synced"),
    };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusProperty || change.Property == LabelProperty || change.Property == PlainProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        var (kind, icon, word) = Describe(Status);
        foreach (var k in new[] { "ok", "warn", "play", "danger", "neutral" })
        {
            PseudoClasses.Set(":" + k, k == kind);
        }

        PseudoClasses.Set(":plain", Plain);
        IconName = icon;
        Text = Label ?? word;
    }
}

/// <summary>A small uppercase label on <c>bg-300</c>: SAVES, BY GAME ID, LATER.</summary>
public class GsTag : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<GsTag, string?>(nameof(Text));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}

/// <summary>
/// A quiet line with an icon, in <c>ink-muted</c>: a reason, a reassurance, what happens next. <see cref="IsBusy"/>
/// makes it a wait with nothing to count (design system → BusyLine; KAN-80): the spinner for the icon and dots counting
/// up after the words.
/// </summary>
public class GsNote : TemplatedControl
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<GsNote, string?>(nameof(Icon), "info");

    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<GsNote, string?>(nameof(Text));

    public static readonly StyledProperty<bool> IsBusyProperty = AvaloniaProperty.Register<GsNote, bool>(nameof(IsBusy));

    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}

/// <summary>On/off for settings that apply straight away: <c>primary</c> when on.</summary>
public class GsSwitch : ToggleButton
{
}

/// <summary>A labelled progress bar: the label and the right-hand figure in mono above a 6px track.</summary>
public class GsProgress : TemplatedControl
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<GsProgress, double>(nameof(Value));

    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<GsProgress, string?>(nameof(Label));

    public static readonly StyledProperty<string?> RightProperty = AvaloniaProperty.Register<GsProgress, string?>(nameof(Right));

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Right
    {
        get => GetValue(RightProperty);
        set => SetValue(RightProperty, value);
    }
}

/// <summary>One setting: its title, description and an optional tag on the left, the control on the right; <c>IsStack</c> puts the control underneath.</summary>
public class GsSettingsRow : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<GsSettingsRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<GsSettingsRow, string?>(nameof(Description));

    public static readonly StyledProperty<string?> TagTextProperty = AvaloniaProperty.Register<GsSettingsRow, string?>(nameof(TagText));

    public static readonly StyledProperty<bool> IsStackProperty = AvaloniaProperty.Register<GsSettingsRow, bool>(nameof(IsStack));

    public static readonly StyledProperty<Avalonia.Media.IImage?> ThumbProperty = AvaloniaProperty.Register<GsSettingsRow, Avalonia.Media.IImage?>(nameof(Thumb));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>A small cover before the title, as Settings' games in your achievements have (design system version 35).</summary>
    public Avalonia.Media.IImage? Thumb
    {
        get => GetValue(ThumbProperty);
        set => SetValue(ThumbProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>A small tag beside the title, such as LATER.</summary>
    public string? TagText
    {
        get => GetValue(TagTextProperty);
        set => SetValue(TagTextProperty, value);
    }

    public bool IsStack
    {
        get => GetValue(IsStackProperty);
        set => SetValue(IsStackProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsStackProperty)
        {
            PseudoClasses.Set(":stack", IsStack);
        }
    }
}

/// <summary>Something picked by its id: a tab, a settings section, a theme, a swatch.</summary>
public interface IHasId
{
    string Id { get; }
}

/// <summary>A pill tab or a settings section: an id, a label, an icon, and a count (Needs you) or badge.</summary>
/// <param name="Tone"><c>warn</c>: Needs you while something does, in the warn colours with a caution mark (design system version 32).</param>
public sealed record NavItem(string Id, string Label, string? Icon = null, string? Count = null, string? Tone = null) : IHasId
{
    public bool IsWarn => Tone == "warn";

    /// <summary>The tab's own icon, which a warn tab's caution mark replaces.</summary>
    public string? PlainIcon => IsWarn ? null : Icon;

    /// <summary>What a screen reader says for the tab (A11Y-03): its label, and the count when there is one.</summary>
    public override string ToString() => Count is null ? Label : $"{Label}, {Count}";
}

/// <summary>Pill tabs that switch a view in place; the selected one fills with <c>secondary-soft</c>. Bind <see cref="SelectedId"/>.</summary>
public class GsPillTabs : ListBox
{
    public static readonly StyledProperty<string?> SelectedIdProperty =
        AvaloniaProperty.Register<GsPillTabs, string?>(nameof(SelectedId), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    // Tabs are always in view of themselves; selecting one, as a page loads, must never scroll the page it's on (a
    // Settings section opened scrolled down to its last pill tabs).
    public GsPillTabs() => AutoScrollToSelectedItem = false;

    // A warn tab (Needs you while something does) takes the warn colours on its pill.
    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        container.Classes.Set("warn", item is NavItem { IsWarn: true });
    }

    public string? SelectedId
    {
        get => GetValue(SelectedIdProperty);
        set => SetValue(SelectedIdProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedIdProperty || change.Property == ItemsSourceProperty)
        {
            SelectedItem = ItemsSource?.OfType<IHasId>().FirstOrDefault(i => i.Id == SelectedId);
        }
        else if (change.Property == SelectedItemProperty && SelectedItem is IHasId item && item.Id != SelectedId)
        {
            SelectedId = item.Id;
        }
    }
}

/// <summary>The settings sections, or first run's steps: a vertical list whose current item fills with <c>secondary-soft</c>.</summary>
public class GsSettingsNav : GsPillTabs
{
}

/// <summary>An item of the side rail: its icon, label, a separator before it, whether it sits at the bottom, and a status dot with what it means.</summary>
public sealed record RailItem(string Id, string Icon, string Label, bool Separator = false, bool Bottom = false, string? Dot = null, string? DotLabel = null)
{
    /// <summary>What the tooltip and a screen reader say: the dot never means something by colour alone (A11Y-01).</summary>
    public string FullLabel => DotLabel is null ? Label : $"{Label}: {DotLabel}";

    /// <summary>A warn dot shows as a caution mark, with the icon in warn (design system version 32).</summary>
    public bool IsCaution => Dot == "warn";

    /// <summary>The dot drawn as a dot: any but warn.</summary>
    public string? PlainDot => IsCaution ? null : Dot;
}

/// <summary>A button of the side rail; the current page fills with <c>secondary-soft</c> and its icon turns <c>secondary</c>.</summary>
public class GsRailButton : Button
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<GsRailButton, string?>(nameof(Icon));

    public static readonly StyledProperty<bool> IsCurrentProperty = AvaloniaProperty.Register<GsRailButton, bool>(nameof(IsCurrent));

    public static readonly StyledProperty<IBrush?> DotBrushProperty = AvaloniaProperty.Register<GsRailButton, IBrush?>(nameof(DotBrush));

    public static readonly StyledProperty<bool> IsCautionProperty = AvaloniaProperty.Register<GsRailButton, bool>(nameof(IsCaution));

    /// <summary>Something needs the person: a caution mark, and the icon in warn on every page.</summary>
    public bool IsCaution
    {
        get => GetValue(IsCautionProperty);
        set => SetValue(IsCautionProperty, value);
    }

    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool IsCurrent
    {
        get => GetValue(IsCurrentProperty);
        set => SetValue(IsCurrentProperty, value);
    }

    public IBrush? DotBrush
    {
        get => GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCurrentProperty)
        {
            PseudoClasses.Set(":current", IsCurrent);
        }
        else if (change.Property == IsCautionProperty)
        {
            PseudoClasses.Set(":caution", IsCaution);
        }
    }
}

/// <summary>Uppercase text, for tags and console column headers.</summary>
public sealed class UpperConverter : IValueConverter
{
    public static UpperConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string)?.ToUpperInvariant();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
