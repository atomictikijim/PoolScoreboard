using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PoolScoreboard.Controller.Converters;

/// <summary>True collapses, false shows — the opposite of the built-in BooleanToVisibilityConverter.</summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}
