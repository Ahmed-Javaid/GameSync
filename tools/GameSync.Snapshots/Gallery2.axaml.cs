using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.State;
using GameSync.UI.Controls;

namespace GameSync.Snapshots;

public sealed record SampleRow(string Name, GameStatus Status, string Size);

/// <summary>The launcher and console components with sample content: hero, tiles, activity, a console table, the log, folders.</summary>
public partial class Gallery2 : UserControl
{
    public Gallery2()
    {
        InitializeComponent();
        Rail.Items = Gallery.RailItems;
        Hero.Art = SampleArt.Hero();
        Tile1.Art = SampleArt.Cover(1);
        Tile2.Art = SampleArt.Cover(2);
        Tile3.Art = SampleArt.Cover(3);
        Activity.Days = [0, 1, 2, 1, 3, 0, 0, 1, 2, 3, 2, 1, 1, 3, 0, 2, 1, 0, 3, 3, 2, 1, 0, 1, 2, 3, 1, 0];
        var rows = new SampleRow[]
        {
            new("Sekiro: Shadows Die Twice", GameStatus.Conflict, "2.1 MB"), new("Terraria", GameStatus.Synced, "326.0 MB"),
            new("Black Myth: Wukong", GameStatus.HeldForReview, "30.0 MB"),
        };
        Rows.ItemsSource = rows;
        Rows.SelectedItems!.Add(rows[0]);
        Rows.SelectedItems.Add(rows[2]);
        Log.Lines =
        [
            new("20:00:02", LogLevel.Info, "backup", "Daily backup started, 12 games."),
            new("20:00:09", LogLevel.Ok, "upload", "Terraria v52, 326.0 MB, 3 files changed."),
            new("20:00:11", LogLevel.Warn, "held", "Black Myth: Wukong changed while the game wasn't running. Held for review."),
            new("21:04:40", LogLevel.Error, "upload", "Sekiro: Google Drive is full. The save is kept on this PC and waits to upload."),
        ];
        Folders.Items = [new FolderItem(@"E:\Games", "16 games"), new FolderItem(@"G:\", "3 games"), new FolderItem(@"D:\OldPC\userdata", "Subfolders are Steam app IDs", "By game ID")];
        Folders.AddCommand = new RelayCommand(() => { });
        Folders.RemoveCommand = new RelayCommand<FolderItem>(_ => { });
    }
}
