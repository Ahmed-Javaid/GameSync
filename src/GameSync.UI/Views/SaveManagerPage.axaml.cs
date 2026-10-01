using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

public partial class SaveManagerPage : UserControl
{
    public SaveManagerPage()
    {
        InitializeComponent();
        // Back from a game's saves or its conflict: Esc, Alt+Left and the mouse's back button, as the Back button does.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (BackOf(DataContext) is { } back && (e.Key == Key.Escape || (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt)) &&
                e.Source is not TextBox)
            {
                back.Execute(null);
                e.Handled = true;
            }
        }, handledEventsToo: false);
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.XButton1 && BackOf(DataContext) is { } back)
            {
                back.Execute(null);
                e.Handled = true;
            }
        }, handledEventsToo: true);
    }

    /// <summary>Copy the log (MGR-09): the lines shown go on the clipboard, and the button says Copied for a moment.</summary>
    private async void CopyLog(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SaveManagerViewModel { LogTab: { } log } || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(log.CopyText());
        if (sender is Controls.GsButton button)
        {
            button.Content = "Copied";
            button.Icon = "check";
            DispatcherTimer.RunOnce(() =>
            {
                button.Content = "Copy the log";
                button.Icon = "copy";
            }, TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>The Back of the page showing: the conflict's, or the game's saves'; none over every game's saves.</summary>
    private static ICommand? BackOf(object? context) => context switch
    {
        SaveManagerViewModel { Conflict: { } conflict } => conflict.BackCommand,
        SaveManagerViewModel { Game: not null } page => page.BackCommand,
        _ => null,
    };
}
