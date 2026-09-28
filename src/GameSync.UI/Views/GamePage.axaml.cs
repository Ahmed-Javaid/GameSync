using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace GameSync.UI.Views;

public partial class GamePage : UserControl
{
    public GamePage() => InitializeComponent();

    /// <summary>A button inside a flyout (Save, Restore) closes the flyout it's in once it's done its job.</summary>
    private void CloseFlyout(object? sender, RoutedEventArgs e) => CloseFlyoutOf(sender);

    /// <summary>Closes the flyout a control sits in, as its own buttons do when they've acted.</summary>
    public static void CloseFlyoutOf(object? sender)
    {
        if ((sender as Control)?.FindLogicalAncestorOfType<Popup>() is { PlacementTarget: Button { Flyout: { } flyout } })
        {
            flyout.Hide();
        }
    }
}
