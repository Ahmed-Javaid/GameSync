using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GameSync.UI.Views;

public partial class HomePage : UserControl
{
    public HomePage() => InitializeComponent();

    private void CloseFlyout(object? sender, RoutedEventArgs e) => GamePage.CloseFlyoutOf(sender);
}
