using System.Globalization;
using System.Windows;
using System.Windows.Data;
namespace CaptureFlow.Desktop.Converters;
public sealed class StudioHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Math.Max(660, value is double height ? height - 230 : 660);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class StudioWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Math.Max(760, value is double width ? width - 264 : 760);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class NavigationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Equals(value, parameter);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? parameter : Binding.DoNothing;
}
public sealed class CardWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var width = value is double size ? size : 800;
        // Leave rounding slack: two rounded cards must not toggle the parent's
        // automatic scrollbar when fractional-DPI widths differ by one pixel.
        return Math.Max(240, width >= 820 ? (width - 20) / 2 : width - 20);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}


