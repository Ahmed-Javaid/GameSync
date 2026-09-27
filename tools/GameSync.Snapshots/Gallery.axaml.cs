using Avalonia.Controls;
using GameSync.UI.Controls;

namespace GameSync.Snapshots;

/// <summary>Every component with sample content, as the design system's previews show them.</summary>
public partial class Gallery : UserControl
{
    public static readonly IReadOnlyList<RailItem> RailItems =
    [
        new RailItem("search", "search", "Search"),
        new RailItem("home", "home", "Home", Separator: true),
        new RailItem("library", "library", "Game library", Dot: "play", DotLabel: "Slay the Spire 2 is running"),
        new RailItem("saves", "saves", "Save manager", Separator: true, Dot: "warn", DotLabel: "2 games need you"),
        new RailItem("log", "terminal", "Console"),
        new RailItem("settings", "settings", "Settings", Bottom: true),
    ];

    public Gallery()
    {
        InitializeComponent();
        Rail.Items = RailItems;
        Tabs.ItemsSource = new NavItem[]
        {
            new("saves", "Saves", "saves"), new("plan", "Plan", "chevronsRight"), new("versions", "Versions", "clock"), new("attn", "Needs you", "alert", "2"),
        };
        Sections.ItemsSource = new NavItem[]
        {
            new("appearance", "Appearance", "palette"), new("storage", "Storage and folders", "drive", "1"), new("backup", "Backup and sync", "sync"),
            new("cloud", "Cloud", "cloud"), new("devices", "Devices", "monitor"), new("notifications", "Notifications", "bell"), new("safety", "Safety", "shield"),
        };
    }
}
