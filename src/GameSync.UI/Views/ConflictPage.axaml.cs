using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GameSync.UI.Views;

public partial class ConflictPage : UserControl
{
    public ConflictPage() => InitializeComponent();

    /// <summary>A button inside a flyout (Keep, Swap) closes the flyout it's in once it's done its job.</summary>
    private void CloseFlyout(object? sender, RoutedEventArgs e) => GamePage.CloseFlyoutOf(sender);
}
