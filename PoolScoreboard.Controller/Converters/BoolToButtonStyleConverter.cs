using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PoolScoreboard.Controller.Converters;

/// <summary>Picks ActiveButtonStyle/InactiveButtonStyle based on a bound bool, for on/off toggle buttons.</summary>
public class BoolToButtonStyleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isOn = value is true;
        var key = isOn ? "ActiveButtonStyle" : "InactiveButtonStyle";
        return Application.Current.TryFindResource(key)!;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
