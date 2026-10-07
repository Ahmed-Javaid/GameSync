using Avalonia.Controls;
using Avalonia.Input;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>Where a game saves, from learn mode: Esc closes it, keeping what was found for later.</summary>
public partial class LearnFindsDialog : UserControl
{
    public LearnFindsDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is LearnFindsViewModel model)
            {
                model.CloseCommand.Execute(null);
                e.Handled = true;
            }
        });
    }
}
