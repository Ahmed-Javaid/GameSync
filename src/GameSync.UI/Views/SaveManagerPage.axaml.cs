using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
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

    /// <summary>The Back of the page showing: the conflict's, or the game's saves'; none over every game's saves.</summary>
    private static ICommand? BackOf(object? context) => context switch
    {
        SaveManagerViewModel { Conflict: { } conflict } => conflict.BackCommand,
        SaveManagerViewModel { Game: not null } page => page.BackCommand,
        _ => null,
    };
}
