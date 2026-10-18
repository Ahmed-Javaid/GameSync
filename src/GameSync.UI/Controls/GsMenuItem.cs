using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace GameSync.UI.Controls;

/// <summary>
/// A menu item as the design system's Menu draws it, that a screen reader can press (A11Y-03). Avalonia's tells UI
/// Automation only that it can be toggled, so Narrator's default action and UI Automation's Invoke did nothing on a
/// menu's commands, such as a game's More menu. Pressing it here does what Enter does: an item with a submenu opens it;
/// any other raises its Click and runs its command, then its menu closes. Toggling a checkable item presses it the same
/// way, so a sort option is chosen, not only ticked. A checkable item's choice shows as the design's <c>check</c> in
/// <c>secondary</c> where its icon goes, in place of Fluent's own tick; it still says it's checked. A <see cref="Hint"/>,
/// such as how many games a library view holds, shows on its right as the design's hint. Otherwise it's a MenuItem,
/// styled as one.
/// </summary>
public class GsMenuItem : MenuItem
{
    public static readonly StyledProperty<string?> HintProperty = AvaloniaProperty.Register<GsMenuItem, string?>(nameof(Hint));

    private TextBlock? _hint;

    protected override Type StyleKeyOverride => typeof(MenuItem);

    /// <summary>
    /// A count on the item's right, where a shortcut goes, in the design's hint: mono, <c>ink-faint</c> (the owner, 18 Oct
    /// 2026, of the library's views as "Installed · 37": "This should be more in theme"). A screen reader hears it after
    /// the item's name.
    /// </summary>
    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        e.NameScope.Find<Control>("PART_ToggleIconPresenter")?.SetValue(IsVisibleProperty, false);
        _hint = e.NameScope.Find<TextBlock>("PART_InputGestureText");
        ShowCheck();
        ShowHint();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCheckedProperty || change.Property == ToggleTypeProperty)
        {
            ShowCheck();
        }
        else if (change.Property == HintProperty)
        {
            ShowHint();
        }
    }

    private void ShowCheck()
    {
        if (ToggleType != MenuItemToggleType.None)
        {
            Icon = IsChecked ? new GsIcon { Icon = "check", Size = 16, StrokeWidth = 2 } : null;
        }
    }

    /// <summary>The hint in the place Fluent keeps for a shortcut, which no item of GameSync's shows.</summary>
    private void ShowHint()
    {
        if (_hint is null || InputGesture is not null)
        {
            return;
        }

        _hint.Text = Hint;
        _hint.IsVisible = !string.IsNullOrEmpty(Hint);
    }

    private sealed class Peer(GsMenuItem owner) : MenuItemAutomationPeer(owner), IInvokeProvider, IToggleProvider
    {
        protected override string? GetNameCore() =>
            base.GetNameCore() is { Length: > 0 } name && owner.Hint is { Length: > 0 } hint ? $"{name}, {hint}" : base.GetNameCore();

        public void Invoke()
        {
            if (!owner.IsEffectivelyEnabled)
            {
                throw new ElementNotEnabledException();
            }

            if (owner.HasSubMenu)
            {
                owner.Open();
                return;
            }

            owner.RaiseEvent(new RoutedEventArgs(ClickEvent));
            if (!owner.StaysOpenOnClick)
            {
                owner.FindAncestorOfType<MenuBase>()?.Close();
            }
        }

        void IToggleProvider.Toggle() => Invoke();
    }
}
