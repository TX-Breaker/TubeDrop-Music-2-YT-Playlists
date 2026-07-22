using System.Globalization;
using System.Windows.Data;

namespace TubeDrop.App.Converters;

/// <summary>Two-way bool binding for a specific enum value (radio-style toggles).</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null
            ? Enum.Parse(targetType, parameter.ToString()!)
            : Binding.DoNothing;
}
