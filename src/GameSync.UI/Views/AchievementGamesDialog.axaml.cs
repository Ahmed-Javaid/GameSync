using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>Achievements by game: Find a game has the cursor as it opens; Esc clears the search, then closes.</summary>
public partial class AchievementGamesDialog : UserControl
{
    public AchievementGamesDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is AchievementGamesViewModel model)
            {
                model.Escape();
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => SearchBox.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
    }
}
