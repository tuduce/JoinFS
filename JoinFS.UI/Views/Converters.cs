using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace JoinFS.UI.Views;

/// <summary>Turns a resource key like "IconHome" into the resource, so a view model can name an icon without referencing UI types.</summary>
public sealed class ResourceKeyConverter : IValueConverter
{
    public static readonly ResourceKeyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out object? resource)
            ? resource
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
