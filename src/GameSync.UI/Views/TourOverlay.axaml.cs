using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameSync.UI.Controls;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>
/// The tour over the window (ONB-08): finds what each stop points at (a rail button, a named part of Home, Home's banner),
/// dims the window but for a rounded hole round it, and puts the box beside it: to its right for the rail, under it near
/// the top, over it lower down; in the middle when the stop points at nothing. Esc ends the tour; Enter goes on.
/// </summary>
public partial class TourOverlay : UserControl
{
    private const double Gap = 18;
    private const double Pad = 4;
    private TourViewModel? _model;
    private int _tries;

    public TourOverlay()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Place();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (_model is null)
            {
                return;
            }

            if (e.Key == Key.Escape)
            {
                _model.EndCommand.Execute(null);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.PropertyChanged -= OnStop;
        }

        _model = DataContext as TourViewModel;
        if (_model is not null)
        {
            _model.PropertyChanged += OnStop;
            PlaceSoon();
        }
    }

    private void OnStop(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TourViewModel.Index))
        {
            PlaceSoon();
        }
    }

    /// <summary>Once the page under it has been laid out: a page just opened may not be, so it tries a few times.</summary>
    private void PlaceSoon()
    {
        _tries = 0;
        Dispatcher.UIThread.Post(Place, DispatcherPriority.Loaded);
    }

    private void Place()
    {
        if (_model is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var size = Bounds.Size;
        var target = Find(_model.Stop.Target);
        if (target is null && _model.Stop.Target is not null && _tries++ < 10)
        {
            // Not laid out yet: once more in a moment.
            DispatcherTimer.RunOnce(Place, TimeSpan.FromMilliseconds(100));
        }

        var hole = target is { } t ? new Rect(t.X - Pad, t.Y - Pad, t.Width + (2 * Pad), t.Height + (2 * Pad)) : (Rect?)null;
        Dim.Data = Shape(size, hole);
        Hole.IsVisible = hole is not null;
        if (hole is { } h)
        {
            Canvas.SetLeft(Hole, h.X - 2);
            Canvas.SetTop(Hole, h.Y - 2);
            Hole.Width = h.Width + 4;
            Hole.Height = h.Height + 4;
        }

        Bubble.Measure(new Size(340, double.PositiveInfinity));
        var bubble = Bubble.DesiredSize;
        double x, y;
        if (hole is not { } r)
        {
            (x, y) = ((size.Width - bubble.Width) / 2, (size.Height - bubble.Height) / 2.6);
        }
        else if (r.X < 120)
        {
            // A rail button: to its right.
            (x, y) = (r.Right + Gap, r.Y - 20);
        }
        else if (r.Y < 140)
        {
            // Near the top: under it, its right edge with the hole's.
            (x, y) = (r.Right - bubble.Width, r.Bottom + Gap);
        }
        else
        {
            // Lower down: under it when there's room, else over it.
            (x, y) = (r.X + 40, r.Bottom + Gap + bubble.Height < size.Height ? r.Bottom + Gap : r.Y - bubble.Height - Gap);
        }
        Canvas.SetLeft(Bubble, Math.Clamp(x, 16, Math.Max(16, size.Width - bubble.Width - 16)));
        Canvas.SetTop(Bubble, Math.Clamp(y, 16, Math.Max(16, size.Height - bubble.Height - 16)));
        NextButton.Focus(NavigationMethod.Tab);
    }

    /// <summary>What a stop points at, in this overlay's own place, or null when it isn't on screen.</summary>
    private Rect? Find(string? target)
    {
        if (target is null || TopLevel.GetTopLevel(this) is not Visual root)
        {
            return null;
        }

        Control? found = target switch
        {
            _ when target.StartsWith("rail:", StringComparison.Ordinal) =>
                root.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.DataContext is RailItem item && item.Id == target[5..] && b.IsEffectivelyVisible),
            _ when target.StartsWith("name:", StringComparison.Ordinal) =>
                root.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == target[5..] && c.IsEffectivelyVisible),
            "hero" => root.GetVisualDescendants().OfType<GsHeroBanner>().FirstOrDefault(c => c.IsEffectivelyVisible),
            _ => null,
        };
        if (found is null || found.TranslatePoint(new Point(0, 0), this) is not { } at || found.Bounds.Width <= 0)
        {
            return null;
        }

        return new Rect(at, found.Bounds.Size);
    }

    /// <summary>The dim: the whole window but for a rounded hole, filled even-odd so the hole lets the page through.</summary>
    private static Geometry Shape(Size size, Rect? hole)
    {
        var shape = new StreamGeometry();
        using var draw = shape.Open();
        draw.SetFillRule(FillRule.EvenOdd);
        draw.BeginFigure(new Point(0, 0), true);
        draw.LineTo(new Point(size.Width, 0));
        draw.LineTo(new Point(size.Width, size.Height));
        draw.LineTo(new Point(0, size.Height));
        draw.EndFigure(true);
        if (hole is { } h)
        {
            var r = Math.Min(14, Math.Min(h.Width, h.Height) / 2);
            var corner = new Size(r, r);
            draw.BeginFigure(new Point(h.X + r, h.Y), true);
            draw.LineTo(new Point(h.Right - r, h.Y));
            draw.ArcTo(new Point(h.Right, h.Y + r), corner, 0, false, SweepDirection.Clockwise);
            draw.LineTo(new Point(h.Right, h.Bottom - r));
            draw.ArcTo(new Point(h.Right - r, h.Bottom), corner, 0, false, SweepDirection.Clockwise);
            draw.LineTo(new Point(h.X + r, h.Bottom));
            draw.ArcTo(new Point(h.X, h.Bottom - r), corner, 0, false, SweepDirection.Clockwise);
            draw.LineTo(new Point(h.X, h.Y + r));
            draw.ArcTo(new Point(h.X + r, h.Y), corner, 0, false, SweepDirection.Clockwise);
            draw.EndFigure(true);
        }

        return shape;
    }
}
