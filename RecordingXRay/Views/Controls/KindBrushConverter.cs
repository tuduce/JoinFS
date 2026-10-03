using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using RecordingXRay.ViewModels;

namespace RecordingXRay.Views.Controls;

/// <summary>Maps a <see cref="FrameKind"/> to its token brush (TypePositionBrush, ...).</summary>
public sealed class KindBrushConverter : IValueConverter
{
    public static KindBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            FrameKind.Position => "TypePositionBrush",
            FrameKind.Integer => "TypeIntegerBrush",
            FrameKind.Float => "TypeFloatBrush",
            FrameKind.String8 => "TypeString8Brush",
            FrameKind.SimEvent => "TypeSimEventBrush",
            _ => "DimBrush",
        };

        return Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out object? brush)
            ? brush as IBrush
            : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
