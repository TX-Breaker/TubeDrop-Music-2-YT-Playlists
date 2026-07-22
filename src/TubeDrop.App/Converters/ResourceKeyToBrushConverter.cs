using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TubeDrop.App.Converters;

/// <summary>
/// Resolves a resource-key string (e.g. "TubeDrop.Brush.Good") to the live brush
/// from application resources, so viewmodels can name a status colour without
/// referencing a Brush — keeps them skin-agnostic (§12.1).
/// </summary>
public sealed class ResourceKeyToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key && Application.Current?.TryFindResource(key) is Brush brush)
        {
            return brush;
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
