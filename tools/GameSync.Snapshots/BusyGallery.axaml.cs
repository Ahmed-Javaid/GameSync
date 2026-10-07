using Avalonia.Controls;

namespace GameSync.Snapshots;

/// <summary>KAN-80's busy states, as the design system draws them: buttons, the job progress row and a busy note.</summary>
public partial class BusyGallery : UserControl
{
    public BusyGallery()
    {
        InitializeComponent();
        Rail.Items = Gallery.RailItems;
    }
}
