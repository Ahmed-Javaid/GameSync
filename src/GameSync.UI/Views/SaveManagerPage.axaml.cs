using Avalonia.Controls;
using Avalonia.Input;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

public partial class SaveManagerPage : UserControl
{
    public SaveManagerPage()
    {
        InitializeComponent();
        // Back from a game's saves: Esc, Alt+Left and the mouse's back button, as the Back button does.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (DataContext is SaveManagerViewModel { Game: not null } page && (e.Key == Key.Escape || (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt)) &&
                e.Source is not TextBox)
            {
                page.BackCommand.Execute(null);
                e.Handled = true;
            }
        }, handledEventsToo: false);
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.XButton1 && DataContext is SaveManagerViewModel { Game: not null } page)
            {
                page.BackCommand.Execute(null);
                e.Handled = true;
            }
        }, handledEventsToo: true);
    }
}
