using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GameSync.Core.State;

namespace GameSync.UI.Controls;

/// <summary>
/// Pill button (design system → Button). Classes: <c>primary</c> (one per view: the recommended action),
/// <c>secondary</c> (the default), <c>ghost</c> (Cancel, Done), <c>danger</c> (deleting a version), and <c>sm</c>.
/// </summary>
public class GsButton : Button
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<GsButton, string?>(nameof(Icon));

    public static readonly StyledProperty<string?> IconAfterProperty = AvaloniaProperty.Register<GsButton, string?>(nameof(IconAfter));

    public static readonly StyledProperty<double> IconSizeProperty = AvaloniaProperty.Register<GsButton, double>(nameof(IconSize), 18);

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
}

/// <summary>40px round icon button; its label is the tooltip and what a screen reader says (A11Y-03). Classes: <c>glass</c>, <c>sm</c>.</summary>
public class GsIconButton : Button
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<GsIconButton, string?>(nameof(Icon));

    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<GsIconButton, string?>(nameof(Label));

    public static readonly StyledProperty<double> IconSizeProperty = AvaloniaProperty.Register<GsIconButton, double>(nameof(IconSize), 18);

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
        if (change.Property == LabelProperty)
        {
            ToolTip.SetTip(this, Label);
            AutomationProperties.SetName(this, Label);
        }
    }
}

/// <summary>The rounded <c>bg-200</c> panel every launcher widget and settings group lives in, with an optional title, subtitle and action.</summary>
public class GsCard : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<GsCard, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty = AvaloniaProperty.Register<GsCard, string?>(nameof(Subtitle));

    public static readonly StyledProperty<object?> ActionProperty = AvaloniaProperty.Register<GsCard, object?>(nameof(Action));

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
        if (change.Property == TitleProperty || change.Property == ActionProperty || change.Property == SubtitleProperty)
        {
            PseudoClasses.Set(":header", Title is not null || Action is not null);
            PseudoClasses.Set(":subtitle", Subtitle is not null);
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

    private string _iconName = "check";
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
        GameStatus.Synced => ("ok", "check", "Synced"),
        GameStatus.Playing => ("play", "play", "Playing"),
        GameStatus.UploadPending => ("ok", "upload", "Upload pending"),
        GameStatus.NewerInCloud => ("ok", "download", "Newer in cloud"),
        GameStatus.Conflict => ("warn", "alert", "Conflict"),
        GameStatus.HeldForReview => ("warn", "pause", "Held for review"),
        GameStatus.FilesInUse => ("warn", "lock", "Files in use"),
        GameStatus.SavesMissing or GameStatus.NoSaves => ("warn", "search", "Saves not found"),
        GameStatus.NotAvailable => ("neutral", "unplug", "Not available"),
        GameStatus.Blocked or GameStatus.Error => ("danger", "block", "Blocked"),
        GameStatus.BackupOnly => ("neutral", "archive", "Backup only"),
        null => ("neutral", "cloud", "Not syncing yet"),
        _ => ("ok", "check", "Synced"),
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

/// <summary>A quiet line with an icon, in <c>ink-muted</c>: a reason, a reassurance, what happens next.</summary>
public class GsNote : TemplatedControl
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<GsNote, string?>(nameof(Icon), "info");

    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<GsNote, string?>(nameof(Text));

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

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
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
public sealed record NavItem(string Id, string Label, string? Icon = null, string? Count = null) : IHasId
{
    /// <summary>What a screen reader says for the tab (A11Y-03): its label, and the count when there is one.</summary>
    public override string ToString() => Count is null ? Label : $"{Label}, {Count}";
}

/// <summary>Pill tabs that switch a view in place; the selected one fills with <c>secondary-soft</c>. Bind <see cref="SelectedId"/>.</summary>
public class GsPillTabs : ListBox
{
    public static readonly StyledProperty<string?> SelectedIdProperty =
        AvaloniaProperty.Register<GsPillTabs, string?>(nameof(SelectedId), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

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
}

/// <summary>A button of the side rail; the current page fills with <c>secondary-soft</c> and its icon turns <c>secondary</c>.</summary>
public class GsRailButton : Button
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<GsRailButton, string?>(nameof(Icon));

    public static readonly StyledProperty<bool> IsCurrentProperty = AvaloniaProperty.Register<GsRailButton, bool>(nameof(IsCurrent));

    public static readonly StyledProperty<IBrush?> DotBrushProperty = AvaloniaProperty.Register<GsRailButton, IBrush?>(nameof(DotBrush));

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
    }
}

/// <summary>Uppercase text, for tags and console column headers.</summary>
public sealed class UpperConverter : IValueConverter
{
    public static UpperConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string)?.ToUpperInvariant();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
