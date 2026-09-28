using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GameSync.UI.Controls;

/// <summary>True when every value is equal, as for "this rail item is the current page".</summary>
public sealed class AllEqualConverter : IMultiValueConverter
{
    public static AllEqualConverter Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count > 0 && values.All(v => Equals(v, values[0]));
}

/// <summary>True when the value is the parameter, as for "this choice is the one picked" in a menu of choices.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public static EqualsConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A colour token's brush by name (<c>warn</c>, <c>play</c>); the theme recolours the same brush, so it stays live.</summary>
public sealed class TokenBrushConverter : IValueConverter
{
    public static TokenBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && Application.Current?.TryGetResource(key, null, out var brush) == true ? brush as IBrush : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
