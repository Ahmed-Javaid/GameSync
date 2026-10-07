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
/// <c>secondary</c> where its icon goes, in place of Fluent's own tick; it still says it's checked. Otherwise it's a
/// MenuItem, styled as one.
/// </summary>
public class GsMenuItem : MenuItem
{
    protected override Type StyleKeyOverride => typeof(MenuItem);

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        e.NameScope.Find<Control>("PART_ToggleIconPresenter")?.SetValue(IsVisibleProperty, false);
        ShowCheck();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCheckedProperty || change.Property == ToggleTypeProperty)
        {
            ShowCheck();
        }
    }

    private void ShowCheck()
    {
        if (ToggleType != MenuItemToggleType.None)
        {
            Icon = IsChecked ? new GsIcon { Icon = "check", Size = 16, StrokeWidth = 2 } : null;
        }
    }

    private sealed class Peer(GsMenuItem owner) : MenuItemAutomationPeer(owner), IInvokeProvider, IToggleProvider
    {
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
