using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace PoolScoreboard.Controller.Converters;

/// <summary>Converts a base64 data URI (e.g. an end-cap icon) into an ImageSource for preview.</summary>
public class DataUriToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string dataUri || string.IsNullOrEmpty(dataUri))
            return null;

        try
        {
            var commaIndex = dataUri.IndexOf(',');
            var header = commaIndex >= 0 ? dataUri[..commaIndex] : string.Empty;
            var base64 = commaIndex >= 0 ? dataUri[(commaIndex + 1)..] : dataUri;
            var bytes = System.Convert.FromBase64String(base64);

            if (header.Contains("image/svg+xml", StringComparison.OrdinalIgnoreCase))
            {
                using var svgStream = new MemoryStream(bytes);
                var reader = new FileSvgReader(new WpfDrawingSettings(), isEmbedded: false);
                var drawing = reader.Read(svgStream);
                var image = new DrawingImage(drawing);
                image.Freeze();
                return image;
            }

            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
