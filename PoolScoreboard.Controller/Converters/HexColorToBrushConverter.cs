using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PoolScoreboard.Controller.Converters;

/// <summary>Converts an operator-entered hex color string (e.g. "#1a2332") to a brush for swatch previews.</summary>
public class HexColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex)
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch (Exception)
            {
                // Fall through: operator is still mid-edit of the hex value.
            }
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
