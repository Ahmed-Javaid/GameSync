using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

public partial class ConsolePage : UserControl
{
    private ConsoleViewModel? _watched;

    public ConsolePage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch(DataContext as ConsoleViewModel);
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => Log()?.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void Watch(ConsoleViewModel? console)
    {
        if (_watched is not null)
        {
            _watched.Lines.CollectionChanged -= Added;
        }

        _watched = console;
        if (console is not null)
        {
            console.Lines.CollectionChanged += Added;
        }
    }

    /// <summary>New lines keep the log at its end, unless the person scrolled up to read.</summary>
    private void Added(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Log() is { } log && log.Offset.Y >= log.Extent.Height - log.Viewport.Height - 24)
        {
            Dispatcher.UIThread.Post(log.ScrollToEnd, DispatcherPriority.Background);
        }
    }

    private ScrollViewer? Log() => this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
}
