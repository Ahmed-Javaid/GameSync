using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace GameSync.UI.Controls;

/// <summary>
/// The collapsed 64px navigation rail (design system → SideRail): the logo, the pages with separators, a spacer, and
/// Settings at the bottom. A status dot says <c>play</c> while a game runs or <c>warn</c> when a save needs the person,
/// always with its label in the tooltip and for screen readers.
/// </summary>
public class GsSideRail : TemplatedControl
{
    public static readonly StyledProperty<IReadOnlyList<RailItem>> ItemsProperty =
        AvaloniaProperty.Register<GsSideRail, IReadOnlyList<RailItem>>(nameof(Items), []);

    public static readonly StyledProperty<string?> CurrentProperty =
        AvaloniaProperty.Register<GsSideRail, string?>(nameof(Current), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<ICommand?> NavigateCommandProperty = AvaloniaProperty.Register<GsSideRail, ICommand?>(nameof(NavigateCommand));

    public static readonly DirectProperty<GsSideRail, IReadOnlyList<RailItem>> TopItemsProperty =
        AvaloniaProperty.RegisterDirect<GsSideRail, IReadOnlyList<RailItem>>(nameof(TopItems), r => r.TopItems);

    public static readonly DirectProperty<GsSideRail, IReadOnlyList<RailItem>> BottomItemsProperty =
        AvaloniaProperty.RegisterDirect<GsSideRail, IReadOnlyList<RailItem>>(nameof(BottomItems), r => r.BottomItems);

    private IReadOnlyList<RailItem> _top = [];
    private IReadOnlyList<RailItem> _bottom = [];

    public GsSideRail() => PickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<string>(id => Pick(id!));

    /// <summary>What each rail button runs, with its item's id.</summary>
    public ICommand PickCommand { get; }

    public IReadOnlyList<RailItem> Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string? Current
    {
        get => GetValue(CurrentProperty);
        set => SetValue(CurrentProperty, value);
    }

    /// <summary>Runs with the item's id when the person picks it.</summary>
    public ICommand? NavigateCommand
    {
        get => GetValue(NavigateCommandProperty);
        set => SetValue(NavigateCommandProperty, value);
    }

    public IReadOnlyList<RailItem> TopItems
    {
        get => _top;
        private set => SetAndRaise(TopItemsProperty, ref _top, value);
    }

    public IReadOnlyList<RailItem> BottomItems
    {
        get => _bottom;
        private set => SetAndRaise(BottomItemsProperty, ref _bottom, value);
    }

    /// <summary>Called by a rail button.</summary>
    public void Pick(string id)
    {
        Current = id;
        if (NavigateCommand?.CanExecute(id) == true)
        {
            NavigateCommand.Execute(id);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty)
        {
            TopItems = Items.Where(i => !i.Bottom).ToList();
            BottomItems = Items.Where(i => i.Bottom).ToList();
        }
    }
}
