using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>
/// The Achievements page. Every game is one Tab stop: Up and Down move through it, and Enter or a click opens a game's
/// achievements (A11Y-02). With a game's achievements open, Esc, Alt+Left and the mouse's back button come back here.
/// </summary>
public partial class TrophyRoomPage : UserControl
{
    public TrophyRoomPage()
    {
        InitializeComponent();
        GameRows.AddHandler(TappedEvent, (_, e) =>
        {
            if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is TrophyGame game)
            {
                Open(game);
            }
        });
        GameRows.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && GameRows.SelectedItem is TrophyGame game)
            {
                Open(game);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnPointer, RoutingStrategies.Tunnel);

        // The band's trophies (KAN-105) need room beside its numbers: a narrow window leaves them out.
        Band.SizeChanged += (_, e) =>
        {
            var room = e.NewSize.Width >= TrophiesFrom;
            Trophies.IsVisible = room;
            BandStats.Margin = new Thickness(28, 24, room ? 276 : 28, 24);
        };
    }

    /// <summary>The band's width from which its trophies fit beside the ring and the four numbers.</summary>
    private const double TrophiesFrom = 1060;

    private TrophyRoomViewModel? Model => DataContext as TrophyRoomViewModel;

    private void Open(TrophyGame game) => Model?.OpenGameCommand.Execute(game.Id);

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (Model is { Game: { } open } && (e.Key == Key.Escape || (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt)) && e.Source is not TextBox)
        {
            open.BackCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnPointer(object? sender, PointerPressedEventArgs e)
    {
        if (Model is { Game: { } open } && e.GetCurrentPoint(this).Properties.IsXButton1Pressed)
        {
            open.BackCommand.Execute(null);
            e.Handled = true;
        }
    }
}
