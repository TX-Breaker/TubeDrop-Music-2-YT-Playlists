using System.Globalization;
using System.Windows.Data;

namespace TubeDrop.App.Converters;

public sealed class ConfidenceToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d and > 0 ? d.ToString("0.00", culture) : "—";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
