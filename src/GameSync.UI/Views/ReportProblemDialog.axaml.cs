using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>Report a problem: the cursor starts in What happened, and Esc closes it.</summary>
public partial class ReportProblemDialog : UserControl
{
    public ReportProblemDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is ReportProblemViewModel model && !model.Opening)
            {
                model.CloseCommand.Execute(null);
                e.Handled = true;
            }
        });
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => WordsBox.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
    }
}
