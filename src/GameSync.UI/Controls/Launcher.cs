using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using GameSync.Core.State;

namespace GameSync.UI.Controls;

/// <summary>Lays out one child at a fixed height-to-width ratio, as the 2:3 tiles are.</summary>
public sealed class AspectPanel : Panel
{
    public static readonly StyledProperty<double> RatioProperty = AvaloniaProperty.Register<AspectPanel, double>(nameof(Ratio), 1.5);

    static AspectPanel() => AffectsMeasure<AspectPanel>(RatioProperty);

    /// <summary>Height over width: 1.5 for Steam's 600×900 capsules.</summary>
    public double Ratio
    {
        get => GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 160 : availableSize.Width;
        var size = new Size(width, width * Ratio);
        foreach (var child in Children)
        {
            child.Measure(size);
        }

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            child.Arrange(new Rect(finalSize));
        }

        return finalSize;
    }
}

/// <summary>
/// A 2:3 cover tile with the game's name, a meta line, and a status badge unless the game is synced, so problems stand
/// out. No art makes a title cover: the name on <c>bg-300</c> with its first letter large behind it, never an empty box.
/// </summary>
public class GsGameTile : Button
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<GsGameTile, string?>(nameof(Title));

    public static readonly StyledProperty<IImage?> ArtProperty = AvaloniaProperty.Register<GsGameTile, IImage?>(nameof(Art));

    public static readonly StyledProperty<GameStatus?> StatusProperty = AvaloniaProperty.Register<GsGameTile, GameStatus?>(nameof(Status));

    public static readonly StyledProperty<string?> StatusLabelProperty = AvaloniaProperty.Register<GsGameTile, string?>(nameof(StatusLabel));

    public static readonly StyledProperty<string?> MetaProperty = AvaloniaProperty.Register<GsGameTile, string?>(nameof(Meta));

    public static readonly DirectProperty<GsGameTile, string> InitialProperty =
        AvaloniaProperty.RegisterDirect<GsGameTile, string>(nameof(Initial), t => t.Initial);

    public static readonly DirectProperty<GsGameTile, bool> ShowsStatusProperty =
        AvaloniaProperty.RegisterDirect<GsGameTile, bool>(nameof(ShowsStatus), t => t.ShowsStatus);

    private string _initial = "?";
    private bool _showsStatus;

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public IImage? Art
    {
        get => GetValue(ArtProperty);
        set => SetValue(ArtProperty, value);
    }

    public GameStatus? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>The badge's words in place of the status's own, such as "Synced by Steam".</summary>
    public string? StatusLabel
    {
        get => GetValue(StatusLabelProperty);
        set => SetValue(StatusLabelProperty, value);
    }

    public string? Meta
    {
        get => GetValue(MetaProperty);
        set => SetValue(MetaProperty, value);
    }

    public string Initial
    {
        get => _initial;
        private set => SetAndRaise(InitialProperty, ref _initial, value);
    }

    public bool ShowsStatus
    {
        get => _showsStatus;
        private set => SetAndRaise(ShowsStatusProperty, ref _showsStatus, value);
    }

    /// <summary>The first letter or digit of a title, for its title cover.</summary>
    public static string InitialOf(string? title) => title?.FirstOrDefault(char.IsLetterOrDigit) is { } c and not '\0' ? char.ToUpperInvariant(c).ToString() : "?";

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty)
        {
            Initial = InitialOf(Title);
        }
        else if (change.Property == StatusProperty)
        {
            // Synced games and games not syncing yet show no badge, so the problems stand out.
            ShowsStatus = Status is { } s && s != GameStatus.Synced;
        }

        if (change.Property == TitleProperty || change.Property == StatusProperty || change.Property == StatusLabelProperty)
        {
            // What a screen reader says: the name, and the badge's words when there's a badge (A11Y-03).
            Avalonia.Automation.AutomationProperties.SetName(this, ShowsStatus ? $"{Title}, {StatusLabel ?? GsStatusBadge.Describe(Status).Word}" : Title);
        }
        else if (change.Property == ArtProperty)
        {
            PseudoClasses.Set(":art", Art is not null);
        }
    }
}

/// <summary>
/// A game in the library's list (design system → GameList): its small cover and name, the status under the name when
/// it needs the person or the game is running, dimmed when the game isn't installed here, and <c>secondary-soft</c>
/// while its page is open.
/// </summary>
public class GsGameRow : Button
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<GsGameRow, string?>(nameof(Title));

    public static readonly StyledProperty<IImage?> ArtProperty = AvaloniaProperty.Register<GsGameRow, IImage?>(nameof(Art));

    public static readonly StyledProperty<GameStatus?> StatusProperty = AvaloniaProperty.Register<GsGameRow, GameStatus?>(nameof(Status));

    public static readonly StyledProperty<string?> StatusLabelProperty = AvaloniaProperty.Register<GsGameRow, string?>(nameof(StatusLabel));

    public static readonly StyledProperty<bool> ShowsStatusProperty = AvaloniaProperty.Register<GsGameRow, bool>(nameof(ShowsStatus));

    public static readonly StyledProperty<bool> IsInstalledProperty = AvaloniaProperty.Register<GsGameRow, bool>(nameof(IsInstalled), true);

    public static readonly StyledProperty<bool> IsCurrentProperty = AvaloniaProperty.Register<GsGameRow, bool>(nameof(IsCurrent));

    public static readonly DirectProperty<GsGameRow, string> InitialProperty =
        AvaloniaProperty.RegisterDirect<GsGameRow, string>(nameof(Initial), r => r.Initial);

    private string _initial = "?";

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public IImage? Art
    {
        get => GetValue(ArtProperty);
        set => SetValue(ArtProperty, value);
    }

    public GameStatus? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string? StatusLabel
    {
        get => GetValue(StatusLabelProperty);
        set => SetValue(StatusLabelProperty, value);
    }

    /// <summary>Show the status under the name: only when the game needs the person or is running, so those stand out.</summary>
    public bool ShowsStatus
    {
        get => GetValue(ShowsStatusProperty);
        set => SetValue(ShowsStatusProperty, value);
    }

    public bool IsInstalled
    {
        get => GetValue(IsInstalledProperty);
        set => SetValue(IsInstalledProperty, value);
    }

    /// <summary>Its page is the one open beside the list.</summary>
    public bool IsCurrent
    {
        get => GetValue(IsCurrentProperty);
        set => SetValue(IsCurrentProperty, value);
    }

    public string Initial
    {
        get => _initial;
        private set => SetAndRaise(InitialProperty, ref _initial, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty)
        {
            Initial = GsGameTile.InitialOf(Title);
        }
        else if (change.Property == IsCurrentProperty)
        {
            PseudoClasses.Set(":current", IsCurrent);
        }
        else if (change.Property == IsInstalledProperty)
        {
            PseudoClasses.Set(":away", !IsInstalled);
        }
        else if (change.Property == ArtProperty)
        {
            PseudoClasses.Set(":art", Art is not null);
        }
    }
}

/// <summary>A group's heading in the library's list: <c>overline</c> with its count in mono and a chevron; a click opens or closes the group.</summary>
public class GsListHeading : Button
{
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<GsListHeading, string?>(nameof(Label));

    public static readonly StyledProperty<string?> CountProperty = AvaloniaProperty.Register<GsListHeading, string?>(nameof(Count));

    public static readonly StyledProperty<bool> IsOpenProperty = AvaloniaProperty.Register<GsListHeading, bool>(nameof(IsOpen), true);

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsOpenProperty)
        {
            PseudoClasses.Set(":closed", !IsOpen);
        }
    }
}

/// <summary>
/// The library's covers: as many columns as fit with none narrower than <see cref="MinItemWidth"/>, the tiles sharing
/// the width, wrapping to rows. A bigger window shows more games in a row, not the same ones bigger.
/// </summary>
public sealed class TilesPanel : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty = AvaloniaProperty.Register<TilesPanel, double>(nameof(MinItemWidth), 150);

    public static readonly StyledProperty<double> ColumnGapProperty = AvaloniaProperty.Register<TilesPanel, double>(nameof(ColumnGap), 16);

    public static readonly StyledProperty<double> RowGapProperty = AvaloniaProperty.Register<TilesPanel, double>(nameof(RowGap), 20);

    static TilesPanel() => AffectsMeasure<TilesPanel>(MinItemWidthProperty, ColumnGapProperty, RowGapProperty);

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double ColumnGap
    {
        get => GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    public double RowGap
    {
        get => GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    /// <summary>How many tiles share a row of this width: as many as fit at the least width, never fewer than two.</summary>
    public static int Columns(double width, double minItemWidth, double gap) =>
        double.IsInfinity(width) ? 6 : Math.Max(2, (int)Math.Floor((width + gap) / (minItemWidth + gap)));

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = Columns(availableSize.Width, MinItemWidth, ColumnGap);
        var width = double.IsInfinity(availableSize.Width) ? columns * MinItemWidth + (columns - 1) * ColumnGap : availableSize.Width;
        var cell = (width - ColumnGap * (columns - 1)) / columns;
        double height = 0, row = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(cell, double.PositiveInfinity));
            row = Math.Max(row, Children[i].DesiredSize.Height);
            if (i % columns == columns - 1 || i == Children.Count - 1)
            {
                height += row + (height > 0 ? RowGap : 0);
                row = 0;
            }
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Columns(finalSize.Width, MinItemWidth, ColumnGap);
        var cell = (finalSize.Width - ColumnGap * (columns - 1)) / columns;
        double y = 0, row = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var column = i % columns;
            Children[i].Arrange(new Rect(column * (cell + ColumnGap), y, cell, Children[i].DesiredSize.Height));
            row = Math.Max(row, Children[i].DesiredSize.Height);
            if (column == columns - 1)
            {
                y += row + RowGap;
                row = 0;
            }
        }

        return finalSize;
    }
}

/// <summary>
/// A game's page below its play bar (design system → GameDetailScreen), from three children: the main column (About,
/// and Achievements once they're read), the Saves card and On this PC. On a page from <see cref="WideFrom"/> wide, the
/// main column takes the left and the other two stack in a column of <see cref="SideWidth"/> on the right; narrower,
/// one column: Saves, the main column, On this PC.
/// </summary>
public sealed class GameDetailLayout : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<GameDetailLayout, double>(nameof(Spacing), 16);

    public static readonly StyledProperty<double> SideWidthProperty = AvaloniaProperty.Register<GameDetailLayout, double>(nameof(SideWidth), 316);

    public static readonly StyledProperty<double> WideFromProperty = AvaloniaProperty.Register<GameDetailLayout, double>(nameof(WideFrom), 800);

    static GameDetailLayout() => AffectsMeasure<GameDetailLayout>(SpacingProperty, SideWidthProperty, WideFromProperty);

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double SideWidth
    {
        get => GetValue(SideWidthProperty);
        set => SetValue(SideWidthProperty, value);
    }

    /// <summary>The page width from which the side column appears.</summary>
    public double WideFrom
    {
        get => GetValue(WideFromProperty);
        set => SetValue(WideFromProperty, value);
    }

    private bool Wide(double width) => !double.IsInfinity(width) && width >= WideFrom;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 3)
        {
            return base.MeasureOverride(availableSize);
        }

        var width = double.IsInfinity(availableSize.Width) ? 1280 : availableSize.Width;
        var (main, saves, pc) = (Children[0], Children[1], Children[2]);
        if (Wide(width))
        {
            main.Measure(new Size(width - SideWidth - Spacing, double.PositiveInfinity));
            saves.Measure(new Size(SideWidth, double.PositiveInfinity));
            pc.Measure(new Size(SideWidth, double.PositiveInfinity));
            return new Size(width, Math.Max(HeightOf(main), Stacked(saves, pc)));
        }

        foreach (var child in Children)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
        }

        return new Size(width, Stacked(saves, main, pc));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 3)
        {
            return base.ArrangeOverride(finalSize);
        }

        var width = finalSize.Width;
        var (main, saves, pc) = (Children[0], Children[1], Children[2]);
        if (Wide(width))
        {
            main.Arrange(new Rect(0, 0, width - SideWidth - Spacing, HeightOf(main)));
            var left = width - SideWidth;
            saves.Arrange(new Rect(left, 0, SideWidth, HeightOf(saves)));
            pc.Arrange(new Rect(left, HeightOf(saves) + (saves.IsVisible ? Spacing : 0), SideWidth, HeightOf(pc)));
            return finalSize;
        }

        var y = 0.0;
        foreach (var child in new[] { saves, main, pc })
        {
            child.Arrange(new Rect(0, y, width, HeightOf(child)));
            y += child.IsVisible ? HeightOf(child) + Spacing : 0;
        }

        return finalSize;
    }

    /// <summary>The height of cards stacked with the spacing between the visible ones.</summary>
    private double Stacked(params Control[] children)
    {
        var shown = children.Where(c => c.IsVisible).ToList();
        return shown.Sum(HeightOf) + Math.Max(0, shown.Count - 1) * Spacing;
    }

    private static double HeightOf(Control child) => child.IsVisible ? child.DesiredSize.Height : 0;
}

/// <summary>A pill on cover art (the hero's playtime): <c>glass</c> behind <c>on-art</c> text, the same in every theme.</summary>
public class GsChip : TemplatedControl
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<GsChip, string?>(nameof(Icon));

    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<GsChip, string?>(nameof(Text));

    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}

/// <summary>
/// The wide banner for a game (design system → HeroBanner): Steam's library hero behind, the game's logo or title, the
/// playtime chip, the save status and a sentence about the save, Manage saves, Game settings and the primary Play.
/// Its text sits on the art, so it uses <c>on-art</c> over the <c>art-scrim</c> fade in every theme.
/// </summary>
public class GsHeroBanner : TemplatedControl
{
    public static readonly StyledProperty<IImage?> ArtProperty = AvaloniaProperty.Register<GsHeroBanner, IImage?>(nameof(Art));

    public static readonly StyledProperty<IImage?> LogoProperty = AvaloniaProperty.Register<GsHeroBanner, IImage?>(nameof(Logo));

    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<GsHeroBanner, string?>(nameof(Title));

    public static readonly StyledProperty<string?> EyebrowProperty = AvaloniaProperty.Register<GsHeroBanner, string?>(nameof(Eyebrow));

    public static readonly StyledProperty<string?> ChipProperty = AvaloniaProperty.Register<GsHeroBanner, string?>(nameof(Chip));

    public static readonly StyledProperty<string?> BlurbProperty = AvaloniaProperty.Register<GsHeroBanner, string?>(nameof(Blurb));

    public static readonly StyledProperty<GameStatus?> StatusProperty = AvaloniaProperty.Register<GsHeroBanner, GameStatus?>(nameof(Status));

    public static readonly StyledProperty<string?> StatusLabelProperty = AvaloniaProperty.Register<GsHeroBanner, string?>(nameof(StatusLabel));

    public static readonly StyledProperty<string> PlayLabelProperty = AvaloniaProperty.Register<GsHeroBanner, string>(nameof(PlayLabel), "Continue playing");

    public static readonly StyledProperty<ICommand?> PlayCommandProperty = AvaloniaProperty.Register<GsHeroBanner, ICommand?>(nameof(PlayCommand));

    public static readonly StyledProperty<ICommand?> SavesCommandProperty = AvaloniaProperty.Register<GsHeroBanner, ICommand?>(nameof(SavesCommand));

    public static readonly StyledProperty<ICommand?> SettingsCommandProperty = AvaloniaProperty.Register<GsHeroBanner, ICommand?>(nameof(SettingsCommand));

    public static readonly StyledProperty<object?> ActionsProperty = AvaloniaProperty.Register<GsHeroBanner, object?>(nameof(Actions));

    public static readonly StyledProperty<bool> ShowsPlayProperty = AvaloniaProperty.Register<GsHeroBanner, bool>(nameof(ShowsPlay), true);

    public static readonly StyledProperty<string> PlayIconProperty = AvaloniaProperty.Register<GsHeroBanner, string>(nameof(PlayIcon), "play");

    public static readonly StyledProperty<double> RoomyHeightProperty = AvaloniaProperty.Register<GsHeroBanner, double>(nameof(RoomyHeight), RoomyAt);

    public static readonly StyledProperty<bool> ShowsStatusProperty = AvaloniaProperty.Register<GsHeroBanner, bool>(nameof(ShowsStatus), true);

    /// <summary>False leaves the status out: a game's page shows it in its play bar instead.</summary>
    public bool ShowsStatus
    {
        get => GetValue(ShowsStatusProperty);
        set => SetValue(ShowsStatusProperty, value);
    }

    /// <summary>The glass buttons beside the primary, in place of Manage saves and Game settings: a game's page has the favourite star and More.</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    /// <summary>False leaves the primary out, for a game not installed on this PC.</summary>
    public bool ShowsPlay
    {
        get => GetValue(ShowsPlayProperty);
        set => SetValue(ShowsPlayProperty, value);
    }

    public string PlayIcon
    {
        get => GetValue(PlayIconProperty);
        set => SetValue(PlayIconProperty, value);
    }

    /// <summary>Below this height the chip and blurb give way; 0 keeps them, as a game's page, sized by its content, does.</summary>
    public double RoomyHeight
    {
        get => GetValue(RoomyHeightProperty);
        set => SetValue(RoomyHeightProperty, value);
    }

    public IImage? Art
    {
        get => GetValue(ArtProperty);
        set => SetValue(ArtProperty, value);
    }

    public IImage? Logo
    {
        get => GetValue(LogoProperty);
        set => SetValue(LogoProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Eyebrow
    {
        get => GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    public string? Chip
    {
        get => GetValue(ChipProperty);
        set => SetValue(ChipProperty, value);
    }

    public string? Blurb
    {
        get => GetValue(BlurbProperty);
        set => SetValue(BlurbProperty, value);
    }

    public GameStatus? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string? StatusLabel
    {
        get => GetValue(StatusLabelProperty);
        set => SetValue(StatusLabelProperty, value);
    }

    public string PlayLabel
    {
        get => GetValue(PlayLabelProperty);
        set => SetValue(PlayLabelProperty, value);
    }

    public ICommand? PlayCommand
    {
        get => GetValue(PlayCommandProperty);
        set => SetValue(PlayCommandProperty, value);
    }

    public ICommand? SavesCommand
    {
        get => GetValue(SavesCommandProperty);
        set => SetValue(SavesCommandProperty, value);
    }

    public ICommand? SettingsCommand
    {
        get => GetValue(SettingsCommandProperty);
        set => SetValue(SettingsCommandProperty, value);
    }

    public static readonly DirectProperty<GsHeroBanner, bool> HasRoomProperty =
        AvaloniaProperty.RegisterDirect<GsHeroBanner, bool>(nameof(HasRoom), b => b.HasRoom);

    /// <summary>Under this height Home's banner drops its playtime chip and blurb, so the title, status and Play still fit.</summary>
    public const double RoomyAt = 280;

    private bool _hasRoom = true;

    /// <summary>Tall enough for the playtime chip and the blurb.</summary>
    public bool HasRoom
    {
        get => _hasRoom;
        private set => SetAndRaise(HasRoomProperty, ref _hasRoom, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == RoomyHeightProperty)
        {
            HasRoom = Bounds.Height >= RoomyHeight;
        }
        else if (change.Property == LogoProperty)
        {
            PseudoClasses.Set(":logo", Logo is not null);
        }
        else if (change.Property == ArtProperty)
        {
            PseudoClasses.Set(":art", Art is not null);
        }
        else if (change.Property == TitleProperty)
        {
            Avalonia.Automation.AutomationProperties.SetName(this, Title);
        }
    }
}

/// <summary>
/// The play-time calendar (design system → ActivityGrid): a cell a day, Monday first. Level 0 none, 1 under 2 hours
/// (hatched: some, but not much), 2 two to four hours (<c>secondary</c>), 3 over four (<c>primary</c>).
/// </summary>
public class GsActivityGrid : Control
{
    public static readonly StyledProperty<IReadOnlyList<int>> DaysProperty = AvaloniaProperty.Register<GsActivityGrid, IReadOnlyList<int>>(nameof(Days), []);

    public static readonly StyledProperty<int> StartWeekdayProperty = AvaloniaProperty.Register<GsActivityGrid, int>(nameof(StartWeekday));

    public static readonly StyledProperty<double> CellRatioProperty = AvaloniaProperty.Register<GsActivityGrid, double>(nameof(CellRatio), 1 / 1.45);

    private const double Gap = 6;

    static GsActivityGrid()
    {
        AffectsRender<GsActivityGrid>(DaysProperty, StartWeekdayProperty);
        AffectsMeasure<GsActivityGrid>(DaysProperty, StartWeekdayProperty, CellRatioProperty);
    }

    /// <summary>The cells take their colours when drawn, so they're drawn again when Glossy or Solid swaps the colours.</summary>
    public GsActivityGrid() => ResourcesChanged += (_, _) => InvalidateVisual();

    /// <summary>Each day's level, 0 to 3, from the first of the month.</summary>
    public IReadOnlyList<int> Days
    {
        get => GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    /// <summary>The weekday of the first day, 0 for Monday.</summary>
    public int StartWeekday
    {
        get => GetValue(StartWeekdayProperty);
        set => SetValue(StartWeekdayProperty, value);
    }

    /// <summary>A cell's height over its width.</summary>
    public double CellRatio
    {
        get => GetValue(CellRatioProperty);
        set => SetValue(CellRatioProperty, value);
    }

    private int Rows => (StartWeekday + Days.Count + 6) / 7;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 280 : availableSize.Width;
        var cell = (width - Gap * 6) / 7;
        return new Size(width, Math.Max(0, Rows * cell * CellRatio + (Rows - 1) * Gap));
    }

    public override void Render(DrawingContext context)
    {
        var cellWidth = (Bounds.Width - Gap * 6) / 7;
        var cellHeight = cellWidth * CellRatio;
        for (var i = 0; i < Days.Count; i++)
        {
            var slot = StartWeekday + i;
            var rect = new Rect(slot % 7 * (cellWidth + Gap), slot / 7 * (cellHeight + Gap), cellWidth, cellHeight);
            ActivityLevels.Paint(this, context, new RoundedRect(rect, 10), Days[i]);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DaysProperty)
        {
            // The cells are drawn, so the grid says what they hold (A11Y-03).
            var played = Days.Count(d => d > 0);
            Avalonia.Automation.AutomationProperties.SetName(this, $"Played on {played} of {Days.Count} days, {Days.Count(d => d == 3)} of them over 4 hours");
        }
    }
}

/// <summary>The play-time calendar with its legend above and the weekday names below, as the design system shows it.</summary>
public class GsActivity : TemplatedControl
{
    public static readonly StyledProperty<IReadOnlyList<int>> DaysProperty = AvaloniaProperty.Register<GsActivity, IReadOnlyList<int>>(nameof(Days), []);

    public static readonly StyledProperty<int> StartWeekdayProperty = AvaloniaProperty.Register<GsActivity, int>(nameof(StartWeekday));

    public IReadOnlyList<int> Days
    {
        get => GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    public int StartWeekday
    {
        get => GetValue(StartWeekdayProperty);
        set => SetValue(StartWeekdayProperty, value);
    }
}

/// <summary>A day of play for <see cref="GsActivityGrid"/>: 0 none, 1 under 2 hours, 2 two to four, 3 over four.</summary>
public static class ActivityLevels
{
    public static int Of(TimeSpan played) => played <= TimeSpan.Zero ? 0 : played < TimeSpan.FromHours(2) ? 1 : played <= TimeSpan.FromHours(4) ? 2 : 3;

    /// <summary>How a cell of each level is painted: <c>bg-300</c>, hatched <c>bg-400</c>, <c>secondary</c> at 60%, <c>primary</c>.</summary>
    public static void Paint(Control owner, DrawingContext context, RoundedRect cell, int level)
    {
        IBrush? Brush(string key) => owner.TryFindResource(key, owner.ActualThemeVariant, out var value) ? value as IBrush : null;
        var rect = cell.Rect;
        switch (level)
        {
            case 1:
                context.DrawRectangle(Brush("bg-300"), null, cell);
                using (context.PushClip(cell))
                {
                    var pen = new Pen(Brush("bg-400"), 3);
                    for (var x = -rect.Height; x < rect.Width; x += 7)
                    {
                        context.DrawLine(pen, new Point(rect.X + x, rect.Bottom), new Point(rect.X + x + rect.Height, rect.Y));
                    }
                }

                break;
            case 2:
                using (context.PushOpacity(0.6))
                {
                    context.DrawRectangle(Brush("secondary"), null, cell);
                }

                break;
            case 3:
                context.DrawRectangle(Brush("primary"), null, cell);
                break;
            default:
                context.DrawRectangle(Brush("bg-300"), null, cell);
                break;
        }
    }
}

/// <summary>A legend dot for one activity level.</summary>
public sealed class GsLevelDot : Control
{
    public static readonly StyledProperty<int> LevelProperty = AvaloniaProperty.Register<GsLevelDot, int>(nameof(Level));

    static GsLevelDot() => AffectsRender<GsLevelDot>(LevelProperty);

    public GsLevelDot() => ResourcesChanged += (_, _) => InvalidateVisual();

    public int Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(12, 12);

    public override void Render(DrawingContext context) => ActivityLevels.Paint(this, context, new RoundedRect(new Rect(0, 0, 12, 12), 6), Level);
}

/// <summary>
/// Cover art filling its space the way CSS <c>background-size: cover</c> does: scaled to cover, centred, cut to fit.
/// It never sets the size of what holds it, so a banner's height comes from its content.
/// </summary>
public sealed class GsCoverImage : Control
{
    public static readonly StyledProperty<IImage?> SourceProperty = AvaloniaProperty.Register<GsCoverImage, IImage?>(nameof(Source));

    static GsCoverImage() => AffectsRender<GsCoverImage>(SourceProperty);

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => default;

    public override void Render(DrawingContext context)
    {
        if (Source is not { } image || image.Size.Width <= 0 || image.Size.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var scale = Math.Max(Bounds.Width / image.Size.Width, Bounds.Height / image.Size.Height);
        var shown = new Size(Bounds.Width / scale, Bounds.Height / scale);
        var source = new Rect((image.Size.Width - shown.Width) / 2, (image.Size.Height - shown.Height) / 2, shown.Width, shown.Height);
        context.DrawImage(image, source, new Rect(Bounds.Size));
    }
}

/// <summary>Lays out children in fixed columns with a gap, wrapping to rows, for tiles in a card or a library grid.</summary>
/// <summary>
/// One row of tiles, as many as fit across and never fewer than <see cref="MinColumns"/>: none wider than
/// <see cref="MaxItemWidth"/>, and when the height is limited, none taller than it; the rest are hidden. Home's Jump
/// back in: a bigger window shows more games, not the same ones bigger.
/// </summary>
public sealed class ShelfPanel : Panel
{
    public static readonly StyledProperty<double> GapProperty = AvaloniaProperty.Register<ShelfPanel, double>(nameof(Gap), 12);

    public static readonly StyledProperty<int> MinColumnsProperty = AvaloniaProperty.Register<ShelfPanel, int>(nameof(MinColumns), 3);

    public static readonly StyledProperty<double> MaxItemWidthProperty = AvaloniaProperty.Register<ShelfPanel, double>(nameof(MaxItemWidth), 200);

    public static readonly StyledProperty<double> BelowArtProperty = AvaloniaProperty.Register<ShelfPanel, double>(nameof(BelowArt), 24);

    private int _columns = 3;

    static ShelfPanel() => AffectsMeasure<ShelfPanel>(GapProperty, MinColumnsProperty, MaxItemWidthProperty, BelowArtProperty);

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public int MinColumns
    {
        get => GetValue(MinColumnsProperty);
        set => SetValue(MinColumnsProperty, value);
    }

    public double MaxItemWidth
    {
        get => GetValue(MaxItemWidthProperty);
        set => SetValue(MaxItemWidthProperty, value);
    }

    /// <summary>A tile's height under its 2:3 art: the name, and the gap above it.</summary>
    public double BelowArt
    {
        get => GetValue(BelowArtProperty);
        set => SetValue(BelowArtProperty, value);
    }

    /// <summary>How many tiles share the width: enough that none is wider than the most, nor taller than a limited height.</summary>
    public static int Columns(Size available, double gap, double maxItemWidth, double belowArt, int minColumns)
    {
        if (double.IsInfinity(available.Width))
        {
            return minColumns;
        }

        int Across(double widest) => (int)Math.Min(12, Math.Ceiling((available.Width + gap) / (Math.Max(widest, 1) + gap)));
        var byHeight = double.IsInfinity(available.Height) ? minColumns : Across((available.Height - belowArt) / 1.5);
        return Math.Max(minColumns, Math.Max(Across(maxItemWidth), byHeight));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = _columns = Columns(availableSize, Gap, MaxItemWidth, BelowArt, MinColumns);
        var width = double.IsInfinity(availableSize.Width) ? columns * 160 + (columns - 1) * Gap : availableSize.Width;
        var cell = (width - Gap * (columns - 1)) / columns;
        double height = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.IsVisible = i < columns;
            if (child.IsVisible)
            {
                child.Measure(new Size(cell, double.PositiveInfinity));
                height = Math.Max(height, child.DesiredSize.Height);
            }
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var shown = Children.Where(c => c.IsVisible).ToList();
        var cell = (finalSize.Width - Gap * (_columns - 1)) / _columns;
        for (var i = 0; i < shown.Count; i++)
        {
            shown[i].Arrange(new Rect(i * (cell + Gap), 0, cell, shown[i].DesiredSize.Height));
        }

        return finalSize;
    }
}

/// <summary>
/// Home at any window size: the top bar, the banner and the row of cards, in that order. The top bar and the cards take
/// the height their content needs at this width (covers keep their 2:3 shape, so they can't stretch to fill); the banner,
/// whose art is cropped to fit, takes the rest, so the page is always full. In a window too short for that, the cards
/// give way instead, down to the banner's least height.
/// </summary>
public sealed class HomeLayout : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<HomeLayout, double>(nameof(Spacing), 16);

    public static readonly StyledProperty<double> MinBannerProperty = AvaloniaProperty.Register<HomeLayout, double>(nameof(MinBanner), 200);

    private double _banner;

    static HomeLayout() => AffectsMeasure<HomeLayout>(SpacingProperty, MinBannerProperty);

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double MinBanner
    {
        get => GetValue(MinBannerProperty);
        set => SetValue(MinBannerProperty, value);
    }

    /// <summary>The banner's height for a page: what the top bar and the cards leave, at least the least, at most 3:4 of the width.</summary>
    public static double Banner(double width, double height, double top, double cards, double spacing, double least) =>
        double.IsInfinity(height)
            ? Math.Max(least, width / 3.4)
            : Math.Min(Math.Max(least, height - top - cards - 2 * spacing), width * 0.75);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 3)
        {
            return base.MeasureOverride(availableSize);
        }

        var (top, banner, cards) = (Children[0], Children[1], Children[2]);
        var width = availableSize.Width;
        top.Measure(new Size(width, double.PositiveInfinity));
        cards.Measure(new Size(width, double.PositiveInfinity));
        _banner = Banner(width, availableSize.Height, top.DesiredSize.Height, cards.DesiredSize.Height, Spacing, Math.Max(MinBanner, banner.MinHeight));
        if (!double.IsInfinity(availableSize.Height))
        {
            var left = availableSize.Height - top.DesiredSize.Height - _banner - 2 * Spacing;
            if (left < cards.DesiredSize.Height)
            {
                // Too short for the cards at their natural size: they shrink to what the least banner leaves.
                cards.Measure(new Size(width, Math.Max(0, left)));
            }
        }

        banner.Measure(new Size(width, _banner));
        var height = double.IsInfinity(availableSize.Height)
            ? top.DesiredSize.Height + _banner + cards.DesiredSize.Height + 2 * Spacing
            : availableSize.Height;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 3)
        {
            return base.ArrangeOverride(finalSize);
        }

        var (top, banner, cards) = (Children[0], Children[1], Children[2]);
        var y = 0.0;
        top.Arrange(new Rect(0, y, finalSize.Width, top.DesiredSize.Height));
        y += top.DesiredSize.Height + Spacing;
        banner.Arrange(new Rect(0, y, finalSize.Width, _banner));
        y += _banner + Spacing;
        cards.Arrange(new Rect(0, y, finalSize.Width, Math.Max(0, finalSize.Height - y)));
        return finalSize;
    }
}

public sealed class ColumnsPanel : Panel
{
    public static readonly StyledProperty<int> ColumnsProperty = AvaloniaProperty.Register<ColumnsPanel, int>(nameof(Columns), 3);

    public static readonly StyledProperty<double> GapProperty = AvaloniaProperty.Register<ColumnsPanel, double>(nameof(Gap), 12);

    static ColumnsPanel() => AffectsMeasure<ColumnsPanel>(ColumnsProperty, GapProperty);

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    private double CellWidth(double width) => (width - Gap * (Columns - 1)) / Columns;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? Columns * 160 + (Columns - 1) * Gap : availableSize.Width;
        var cell = CellWidth(width);
        double height = 0, row = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(cell, double.PositiveInfinity));
            row = Math.Max(row, Children[i].DesiredSize.Height);
            if (i % Columns == Columns - 1 || i == Children.Count - 1)
            {
                height += row + (height > 0 ? Gap : 0);
                row = 0;
            }
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cell = CellWidth(finalSize.Width);
        double y = 0, row = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var column = i % Columns;
            Children[i].Arrange(new Rect(column * (cell + Gap), y, cell, Children[i].DesiredSize.Height));
            row = Math.Max(row, Children[i].DesiredSize.Height);
            if (column == Columns - 1)
            {
                y += row + Gap;
                row = 0;
            }
        }

        return finalSize;
    }
}
