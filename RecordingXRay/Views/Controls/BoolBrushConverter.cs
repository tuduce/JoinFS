using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RecordingXRay.Views.Controls;

/// <summary>
/// Picks a token brush from a bool. The parameter is <c>"TrueKey|FalseKey"</c>, resource keys such as <c>OkBrush|DimBrush</c>;
/// the key <c>none</c> means no fill.
/// </summary>
public sealed class BoolBrushConverter : IValueConverter
{
    public static BoolBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string[] keys = (parameter as string ?? string.Empty).Split('|');
        string key = value is true ? keys[0] : (keys.Length > 1 ? keys[1] : "none");
        if (key == "none")
        {
            return Brushes.Transparent;
        }

        return Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out object? brush)
            ? brush as IBrush
            : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
