using Avalonia;

namespace JoinFS.UI.Views.Controls;

/// <summary>The Web Mercator projection of the slippy maps: pixel positions in the whole world at a zoom, and back.</summary>
internal static class WebMercator
{
    public const double TileSize = 256;
    public const double MaxLatitude = 85.0511287798;

    /// <summary>The width and the height of the world, in pixels, at this zoom.</summary>
    public static double WorldSize(double zoom) => TileSize * Math.Pow(2, zoom);

    /// <summary>Pixels east of the 180° west meridian.</summary>
    public static double X(double longitude, double zoom) => (longitude + 180.0) / 360.0 * WorldSize(zoom);

    /// <summary>Pixels south of the northernmost latitude the projection reaches.</summary>
    public static double Y(double latitude, double zoom)
    {
        double s = Math.Sin(Math.Clamp(latitude, -MaxLatitude, MaxLatitude) * Math.PI / 180.0);
        return (0.5 - Math.Log((1 + s) / (1 - s)) / (4 * Math.PI)) * WorldSize(zoom);
    }

    public static double Longitude(double x, double zoom) => WrapLongitude(x / WorldSize(zoom) * 360.0 - 180.0);

    public static double Latitude(double y, double zoom) =>
        Math.Clamp(Math.Atan(Math.Sinh(Math.PI - 2 * Math.PI * y / WorldSize(zoom))) * 180.0 / Math.PI, -MaxLatitude, MaxLatitude);

    /// <summary>Longitude brought into -180 to 180, for a position that went round the world.</summary>
    public static double WrapLongitude(double longitude)
    {
        double wrapped = (longitude + 180.0) % 360.0;
        if (wrapped < 0)
            wrapped += 360.0;
        return wrapped - 180.0;
    }
}

/// <summary>
/// Where the map is looking: its centre, its zoom and the size of the view. Everything the user does to the map (drag, wheel, fit)
/// is arithmetic on this, so it is kept apart from the control that draws it.
/// </summary>
internal sealed class MapViewport
{
    public const double MaxFitZoom = 15;
    public const double SingleAircraftZoom = 9;

    public double CenterLatitude { get; private set; } = 30;
    public double CenterLongitude { get; private set; }
    public double Zoom { get; private set; } = 1;
    public double Width { get; private set; } = 256;
    public double Height { get; private set; } = 256;

    /// <summary>The highest zoom the tile source has pictures for, and a little more: the last level is blown up.</summary>
    public double MaxZoom { get; set; } = 19;

    /// <summary>The least zoom that still fills the view from top to bottom, so no grey shows above or below the world.</summary>
    public double MinZoom => Math.Max(0, Math.Log2(Math.Max(Height, 1) / WebMercator.TileSize));

    public void Resize(double width, double height)
    {
        Width = width;
        Height = height;
        Zoom = Math.Clamp(Zoom, MinZoom, MaxZoom);
    }

    public void SetView(double latitude, double longitude, double zoom)
    {
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        CenterLatitude = Math.Clamp(latitude, -WebMercator.MaxLatitude, WebMercator.MaxLatitude);
        CenterLongitude = WebMercator.WrapLongitude(longitude);
    }

    /// <summary>Where a position is in the view, in pixels from its top left.</summary>
    public Point ToScreen(double latitude, double longitude)
    {
        double x = WebMercator.X(longitude, Zoom) - WebMercator.X(CenterLongitude, Zoom);
        // the world repeats sideways: take the copy that is nearest the centre
        double world = WebMercator.WorldSize(Zoom);
        x -= Math.Round(x / world) * world;
        double y = WebMercator.Y(latitude, Zoom) - WebMercator.Y(CenterLatitude, Zoom);
        return new Point(x + Width / 2, y + Height / 2);
    }

    /// <summary>The position under a point of the view.</summary>
    public (double Latitude, double Longitude) ToGeo(Point point)
    {
        double x = WebMercator.X(CenterLongitude, Zoom) + (point.X - Width / 2);
        double y = WebMercator.Y(CenterLatitude, Zoom) + (point.Y - Height / 2);
        return (WebMercator.Latitude(y, Zoom), WebMercator.Longitude(x, Zoom));
    }

    /// <summary>Drags the map: what was under the pointer stays under it.</summary>
    public void PanBy(double dx, double dy)
    {
        double x = WebMercator.X(CenterLongitude, Zoom) - dx;
        double y = WebMercator.Y(CenterLatitude, Zoom) - dy;
        double maxY = WebMercator.WorldSize(Zoom);
        CenterLongitude = WebMercator.Longitude(x, Zoom);
        CenterLatitude = WebMercator.Latitude(Math.Clamp(y, 0, maxY), Zoom);
    }

    /// <summary>Zooms in or out by <paramref name="delta"/> levels, keeping the position under <paramref name="anchor"/> where it is.</summary>
    public void ZoomAt(Point anchor, double delta)
    {
        var (latitude, longitude) = ToGeo(anchor);
        double zoom = Math.Clamp(Zoom + delta, MinZoom, MaxZoom);
        if (zoom == Zoom)
            return;
        Zoom = zoom;

        double x = WebMercator.X(longitude, Zoom) - (anchor.X - Width / 2);
        double y = WebMercator.Y(latitude, Zoom) - (anchor.Y - Height / 2);
        CenterLongitude = WebMercator.Longitude(x, Zoom);
        CenterLatitude = WebMercator.Latitude(Math.Clamp(y, 0, WebMercator.WorldSize(Zoom)), Zoom);
    }

    /// <summary>
    /// Shows all of the positions, with <paramref name="padding"/> pixels around them. One position is shown at a street-level-ish zoom,
    /// none shows the world.
    /// </summary>
    public void Fit(IReadOnlyList<(double Latitude, double Longitude)> positions, double padding = 40)
    {
        if (positions.Count == 0)
        {
            SetView(30, 0, 1.5);
            return;
        }

        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var (latitude, longitude) in positions)
        {
            // at zoom 0 the world is one 256 px tile
            double x = WebMercator.X(longitude, 0), y = WebMercator.Y(latitude, 0);
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        double spanX = maxX - minX, spanY = maxY - minY;
        double zoom;
        if (spanX < 1e-6 && spanY < 1e-6)
        {
            zoom = SingleAircraftZoom;
        }
        else
        {
            double availableX = Math.Max(Width - 2 * padding, 1), availableY = Math.Max(Height - 2 * padding, 1);
            double fitX = spanX < 1e-9 ? double.MaxValue : availableX / spanX;
            double fitY = spanY < 1e-9 ? double.MaxValue : availableY / spanY;
            zoom = Math.Min(Math.Log2(Math.Min(fitX, fitY)), MaxFitZoom);
        }

        double centreX = (minX + maxX) / 2, centreY = (minY + maxY) / 2;
        SetView(WebMercator.Latitude(centreY, 0), WebMercator.Longitude(centreX, 0), zoom);
    }
}
