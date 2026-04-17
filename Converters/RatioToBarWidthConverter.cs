using System.Globalization;

namespace allyza.Converters;

public class RatioToBarWidthConverter : IValueConverter
{
    public double MaxWidth { get; set; } = 280;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double ratio ? Math.Max(ratio * MaxWidth, 4) : 4;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}