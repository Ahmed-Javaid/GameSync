using Avalonia.Controls;
using Avalonia.Input;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>
/// Every achievement of a game. The list is one Tab stop; Up and Down move through it and the details follow (A11Y-02).
/// Esc in the search clears it first; then Esc, Alt+Left and the mouse's back button go back, as the library's pages do.
/// </summary>
public partial class AchievementsPage : UserControl
{
    public AchievementsPage()
    {
        InitializeComponent();
        FindBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && !string.IsNullOrEmpty(FindBox.Text))
            {
                FindBox.Text = "";
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>A click, or Up and Down: the details show the one picked.</summary>
    private void Picked(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is AchievementsViewModel model && Rows.SelectedItem is AchievementItem item && !ReferenceEquals(model.Picked, item))
        {
            model.Picked = item;
        }
    }
}
