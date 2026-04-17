using System.Globalization;

namespace allyza.Converters;

public class RatioToHeightConverter : IValueConverter
{
    public double MaxHeight { get; set; } = 80;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double ratio ? Math.Max(ratio * MaxHeight, 2) : 2;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}