using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;

namespace GameSync.UI.Controls;

/// <summary>
/// The popup when an achievement unlocks while you play (ACH-09; design system version 35 → AchievementPopup): its badge
/// in its metal (a Zenith's medal for a game's 100%), "Achievement unlocked" in its metal, its name, its tier's word with
/// how rare it is, and how far the game is now. Its look is fixed, dark glass in every theme, as it sits on a game.
/// </summary>
public class GsAchievementPopup : TemplatedControl
{
    public static readonly StyledProperty<IImage?> IconProperty = AvaloniaProperty.Register<GsAchievementPopup, IImage?>(nameof(Icon));

    public static readonly StyledProperty<string?> EyebrowProperty = AvaloniaProperty.Register<GsAchievementPopup, string?>(nameof(Eyebrow));

    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<GsAchievementPopup, string?>(nameof(Title));

    public static readonly StyledProperty<string?> MetaProperty = AvaloniaProperty.Register<GsAchievementPopup, string?>(nameof(Meta));

    /// <summary><c>gold</c>, <c>silver</c>, <c>bronze</c> or <c>zenith</c>; null while its rarity isn't known.</summary>
    public static readonly StyledProperty<string?> TierProperty = AvaloniaProperty.Register<GsAchievementPopup, string?>(nameof(Tier));

    public static readonly StyledProperty<int> DoneProperty = AvaloniaProperty.Register<GsAchievementPopup, int>(nameof(Done));

    public static readonly StyledProperty<int> TotalProperty = AvaloniaProperty.Register<GsAchievementPopup, int>(nameof(Total));

    public static readonly DirectProperty<GsAchievementPopup, bool> IsZenithProperty =
        AvaloniaProperty.RegisterDirect<GsAchievementPopup, bool>(nameof(IsZenith), p => p.IsZenith);

    public static readonly DirectProperty<GsAchievementPopup, bool> ShowsProgressProperty =
        AvaloniaProperty.RegisterDirect<GsAchievementPopup, bool>(nameof(ShowsProgress), p => p.ShowsProgress);

    public static readonly DirectProperty<GsAchievementPopup, string> ProgressTextProperty =
        AvaloniaProperty.RegisterDirect<GsAchievementPopup, string>(nameof(ProgressText), p => p.ProgressText);

    public static readonly DirectProperty<GsAchievementPopup, double> FillWidthProperty =
        AvaloniaProperty.RegisterDirect<GsAchievementPopup, double>(nameof(FillWidth), p => p.FillWidth);

    /// <summary>The bar's width; the fill is its share of it.</summary>
    public const double BarWidth = 56;

    /// <summary>How far it slides in from its side.</summary>
    public const double Slide = 28;

    private bool _isZenith;
    private bool _showsProgress;
    private string _progressText = "";
    private double _fillWidth;

    public GsAchievementPopup() => RenderTransform = new TranslateTransform();

    public IImage? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Eyebrow
    {
        get => GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Meta
    {
        get => GetValue(MetaProperty);
        set => SetValue(MetaProperty, value);
    }

    public string? Tier
    {
        get => GetValue(TierProperty);
        set => SetValue(TierProperty, value);
    }

    public int Done
    {
        get => GetValue(DoneProperty);
        set => SetValue(DoneProperty, value);
    }

    public int Total
    {
        get => GetValue(TotalProperty);
        set => SetValue(TotalProperty, value);
    }

    public bool IsZenith
    {
        get => _isZenith;
        private set => SetAndRaise(IsZenithProperty, ref _isZenith, value);
    }

    /// <summary>How far the game is now, on the right: not on a Zenith's, which says every one.</summary>
    public bool ShowsProgress
    {
        get => _showsProgress;
        private set => SetAndRaise(ShowsProgressProperty, ref _showsProgress, value);
    }

    /// <summary>"124 / 171".</summary>
    public string ProgressText
    {
        get => _progressText;
        private set => SetAndRaise(ProgressTextProperty, ref _progressText, value);
    }

    public double FillWidth
    {
        get => _fillWidth;
        private set => SetAndRaise(FillWidthProperty, ref _fillWidth, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TierProperty)
        {
            foreach (var tier in (string[])["gold", "silver", "bronze", "zenith"])
            {
                PseudoClasses.Set(":" + tier, Tier == tier);
            }

            IsZenith = Tier == "zenith";
            Update();
        }
        else if (change.Property == DoneProperty || change.Property == TotalProperty)
        {
            Update();
        }
    }

    /// <summary>In from its side (<paramref name="fromLeft"/> for a left corner) over 0.28s; at once with Windows' animation effects off.</summary>
    public Task SlideInAsync(bool fromLeft) => Move(fromLeft ? -Slide : Slide, 0, 0, 1, TimeSpan.FromSeconds(0.28), new CubicEaseOut());

    /// <summary>Back out to its side over 0.22s.</summary>
    public Task SlideOutAsync(bool fromLeft) => Move(0, fromLeft ? -Slide : Slide, 1, 0, TimeSpan.FromSeconds(0.22), new CubicEaseIn());

    private void Update()
    {
        ShowsProgress = !IsZenith && Total > 0;
        ProgressText = $"{Done.ToString(CultureInfo.InvariantCulture)} / {Total.ToString(CultureInfo.InvariantCulture)}";
        FillWidth = Total > 0 ? Math.Round(BarWidth * Math.Clamp(Done, 0, Total) / Total) : 0;
    }

    private async Task Move(double fromX, double toX, double fromOpacity, double toOpacity, TimeSpan time, Easing easing)
    {
        if (!Motion.On)
        {
            Opacity = toOpacity;
            return;
        }

        var animation = new Animation
        {
            Duration = time,
            Easing = easing,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.XProperty, fromX), new Setter(OpacityProperty, fromOpacity) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.XProperty, toX), new Setter(OpacityProperty, toOpacity) } },
            },
        };
        await animation.RunAsync(this);
    }
}
