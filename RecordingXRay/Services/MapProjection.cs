using System.Globalization;

namespace RecordingXRay.Services;

/// <summary>
/// Web Mercator in "world units": x and y both run 0..1 over the whole map (x east from longitude -180, y south from
/// about 85 degrees north). A view is a centre in world units plus a scale in pixels per world unit.
/// </summary>
public static class MapProjection
{
    public const double EarthCircumferenceMeters = 40_075_016.686;

    /// <summary>The latitude limit of the projection (85.05 degrees) in radians.</summary>
    public const double MaxLatitude = 1.4844222297453324;

    public const double MinScale = 256;

    public const double MaxScale = 1_073_741_824; // about 4 cm per pixel at the equator

    public static double WorldX(double longitudeRadians) => (longitudeRadians + Math.PI) / (2 * Math.PI);

    public static double WorldY(double latitudeRadians)
    {
        double lat = Math.Clamp(latitudeRadians, -MaxLatitude, MaxLatitude);
        return 0.5 - Math.Log(Math.Tan(Math.PI / 4 + lat / 2)) / (2 * Math.PI);
    }

    public static double LongitudeRadians(double worldX) => worldX * 2 * Math.PI - Math.PI;

    public static double LatitudeRadians(double worldY) => 2 * Math.Atan(Math.Exp((0.5 - worldY) * 2 * Math.PI)) - Math.PI / 2;

    /// <summary>Ground distance of one pixel, in meters, at a latitude.</summary>
    public static double MetersPerPixel(double scale, double latitudeRadians) =>
        Math.Cos(latitudeRadians) * EarthCircumferenceMeters / scale;

    /// <summary>"55.4293°N 13.2377°E" from radians.</summary>
    public static string Coordinates(double latitudeRadians, double longitudeRadians)
    {
        double lat = latitudeRadians * 180 / Math.PI;
        double lon = longitudeRadians * 180 / Math.PI;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Math.Abs(lat):0.0000}°{(lat < 0 ? "S" : "N")} {Math.Abs(lon):0.0000}°{(lon < 0 ? "W" : "E")}");
    }

    private static readonly double[] NiceDistances = BuildSteps(1, 10_000_000);

    private static readonly double[] NiceAngles = BuildSteps(1e-6, 90);

    /// <summary>The scale bar: the longest "round" distance that fits in <paramref name="maxPixels"/>, with its label and width.</summary>
    public static (double Meters, string Label, double Pixels) ScaleBar(double metersPerPixel, double maxPixels)
    {
        double meters = NiceDistances[0];
        foreach (double candidate in NiceDistances)
        {
            if (candidate / metersPerPixel > maxPixels)
            {
                break;
            }

            meters = candidate;
        }

        string label = meters >= 1000
            ? string.Create(CultureInfo.InvariantCulture, $"{meters / 1000:0.##} km")
            : string.Create(CultureInfo.InvariantCulture, $"{meters:0.##} m");
        return (meters, label, meters / metersPerPixel);
    }

    /// <summary>The smallest "round" graticule step in degrees (1, 2 or 5 times a power of ten) of at least <paramref name="degrees"/>.</summary>
    public static double NiceAngleStep(double degrees)
    {
        foreach (double step in NiceAngles)
        {
            if (step >= degrees)
            {
                return step;
            }
        }

        return NiceAngles[^1];
    }

    /// <summary>A graticule label: decimals follow the step, hemisphere letter from the sign.</summary>
    public static string AngleLabel(double degrees, double step, string positive, string negative)
    {
        int decimals = Math.Clamp((int)Math.Ceiling(-Math.Log10(step) - 1e-9), 0, 6);
        string number = Math.Abs(degrees).ToString("F" + decimals, CultureInfo.InvariantCulture);
        return number + "°" + (degrees < 0 ? negative : positive);
    }

    private static double[] BuildSteps(double from, double to)
    {
        List<double> steps = [];
        for (double decade = from; decade <= to * 1.0001; decade *= 10)
        {
            foreach (double factor in new[] { 1.0, 2.0, 5.0 })
            {
                double step = Math.Round(decade * factor, 9);
                if (step <= to * 1.0001)
                {
                    steps.Add(step);
                }
            }
        }

        return steps.ToArray();
    }
}
