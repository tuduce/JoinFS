using RecordingXRay.Services;

namespace RecordingXRay.ViewModels;

/// <summary>Where an aircraft is at one moment.</summary>
public readonly record struct MapSample(double Latitude, double Longitude, double X, double Y, double Heading);

/// <summary>
/// The path of one lane on the map, built once from its position frames: every point (for exact positions at the cursor)
/// and a thinned list of indexes (for drawing the trail).
/// </summary>
public sealed class MapTrack
{
    private const double MinTrailSpacingMeters = 3;

    private readonly double[] times;
    private readonly double[] latitudes;
    private readonly double[] longitudes;
    private readonly double[] xs;
    private readonly double[] ys;
    private readonly double[] headings;
    private readonly int[] trail;

    private MapTrack(LaneViewModel lane, List<(double Time, double Lat, double Lon, double Heading)> points)
    {
        Lane = lane;
        times = points.Select(p => p.Time).ToArray();
        latitudes = points.Select(p => p.Lat).ToArray();
        longitudes = points.Select(p => p.Lon).ToArray();
        headings = points.Select(p => p.Heading).ToArray();
        xs = points.Select(p => MapProjection.WorldX(p.Lon)).ToArray();
        ys = points.Select(p => MapProjection.WorldY(p.Lat)).ToArray();

        MinX = xs.Min();
        MaxX = xs.Max();
        MinY = ys.Min();
        MaxY = ys.Max();
        trail = Thin();
    }

    public LaneViewModel Lane { get; }

    public int PointCount => times.Length;

    /// <summary>Bounds of the whole path in world units.</summary>
    public double MinX { get; }

    public double MaxX { get; }

    public double MinY { get; }

    public double MaxY { get; }

    /// <summary>Number of points kept for drawing.</summary>
    public int TrailCount => trail.Length;

    /// <summary>Builds the path of a lane, or null when it has no usable position frames.</summary>
    public static MapTrack? Create(LaneViewModel lane)
    {
        List<(double, double, double, double)> points = [];
        foreach (RecordedFrame frame in lane.Frames)
        {
            (double Lat, double Lon, double Heading)? position = frame switch
            {
                AircraftPositionFrame ap => (ap.Latitude, ap.Longitude, ap.Heading),
                ObjectPositionFrame op => (op.Latitude, op.Longitude, op.Heading),
                _ => null,
            };

            // Skip frames from before the sim had a fix (0, 0) and anything that is not a coordinate.
            if (position is { } p
                && double.IsFinite(p.Lat) && double.IsFinite(p.Lon)
                && !(p.Lat == 0 && p.Lon == 0)
                && Math.Abs(p.Lat) <= Math.PI / 2 && Math.Abs(p.Lon) <= Math.PI)
            {
                points.Add((frame.Time, p.Lat, p.Lon, p.Heading));
            }
        }

        return points.Count == 0 ? null : new MapTrack(lane, points);
    }

    /// <summary>Position at a time: interpolated between the neighbouring frames, held at the first / last point outside the path.</summary>
    public MapSample SampleAt(double time)
    {
        int after = FirstAfter(time);
        if (after <= 0)
        {
            return Point(0);
        }

        if (after >= times.Length)
        {
            return Point(times.Length - 1);
        }

        int before = after - 1;
        double span = times[after] - times[before];
        double t = span > 0 ? (time - times[before]) / span : 0;
        return new MapSample(
            Lerp(latitudes[before], latitudes[after], t),
            Lerp(longitudes[before], longitudes[after], t),
            Lerp(xs[before], xs[after], t),
            Lerp(ys[before], ys[after], t),
            LerpAngle(headings[before], headings[after], t));
    }

    /// <summary>
    /// The thinned trail split at a time: points up to the time (past) and after it (future), as world coordinates.
    /// Both lists include the interpolated point at the time, so the two halves meet.
    /// </summary>
    public (List<(double X, double Y)> Past, List<(double X, double Y)> Future) Split(double time)
    {
        MapSample now = SampleAt(time);
        List<(double, double)> past = [];
        List<(double, double)> future = [];

        foreach (int index in trail)
        {
            if (times[index] <= time)
            {
                past.Add((xs[index], ys[index]));
            }
            else
            {
                future.Add((xs[index], ys[index]));
            }
        }

        // Join the halves at the cursor, but only when the cursor is inside the path.
        if (time >= times[0] && time <= times[^1])
        {
            (double, double) here = (now.X, now.Y);
            if (past.Count == 0 || past[^1] != here)
            {
                past.Add(here);
            }

            if (future.Count == 0 || future[0] != here)
            {
                future.Insert(0, here);
            }
        }

        return (past, future);
    }

    private MapSample Point(int index) => new(latitudes[index], longitudes[index], xs[index], ys[index], headings[index]);

    // Index of the first point with a time greater than "time" (Length when none).
    private int FirstAfter(double time)
    {
        int low = 0;
        int high = times.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (times[mid] <= time)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private int[] Thin()
    {
        double metersPerUnit = MapProjection.EarthCircumferenceMeters * Math.Max(Math.Cos(latitudes[0]), 0.05);
        double minimum = MinTrailSpacingMeters / metersPerUnit;
        List<int> kept = [0];
        double lastX = xs[0];
        double lastY = ys[0];
        for (int i = 1; i < times.Length - 1; i++)
        {
            if (Math.Abs(xs[i] - lastX) >= minimum || Math.Abs(ys[i] - lastY) >= minimum)
            {
                kept.Add(i);
                lastX = xs[i];
                lastY = ys[i];
            }
        }

        if (times.Length > 1)
        {
            kept.Add(times.Length - 1);
        }

        return kept.ToArray();
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    // Shortest way round the circle: 350 degrees to 10 degrees goes through 0.
    private static double LerpAngle(double from, double to, double t)
    {
        double difference = (to - from) % (2 * Math.PI);
        if (difference > Math.PI)
        {
            difference -= 2 * Math.PI;
        }
        else if (difference < -Math.PI)
        {
            difference += 2 * Math.PI;
        }

        return from + difference * t;
    }
}
