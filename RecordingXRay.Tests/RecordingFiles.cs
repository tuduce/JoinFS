namespace RecordingXRay.Tests;

/// <summary>Writes small, valid .jfs files (format: docs/recording-protocol.md) for tests.</summary>
internal static class RecordingFiles
{
    public const short Version = 21005;

    /// <summary>One aircraft ("YR-SCD") with an AircraftPosition frame at each of <paramref name="times"/>.</summary>
    public static string WriteSingleAircraft(string directory, params double[] times) =>
        WriteAircraft(directory, ("YR-SCD", times));

    /// <summary>One aircraft per entry, each with an AircraftPosition frame at each of its times (all at the same place).</summary>
    public static string WriteAircraft(string directory, params (string Callsign, double[] Times)[] aircraft) =>
        WritePositions(directory, aircraft.Select(a => (a.Callsign, a.Times.Select(t => (t, 0.9674238236590125, 0.231041585664082, 0.5f)).ToArray())).ToArray());

    /// <summary>One aircraft per entry, each with an AircraftPosition frame per (time, latitude, longitude, heading), angles in radians.</summary>
    public static string WritePositions(string directory, params (string Callsign, (double Time, double Latitude, double Longitude, float Heading)[] Points)[] aircraft)
    {
        string path = Path.Combine(directory, $"{Guid.NewGuid():N}.jfs");
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);

        writer.Write(Version);
        writer.Write(aircraft.Length);
        foreach ((string callsign, (double Time, double Latitude, double Longitude, float Heading)[] points) in aircraft)
        {
            writer.Write(true); // plane
            writer.Write(callsign);
            writer.Write("nick");
            writer.Write("Tiger Moth TIGER-4 01001011110");
            writer.Write((byte)1);

            writer.Write(points.Length);
            foreach ((double time, double latitude, double longitude, float heading) in points)
            {
                writer.Write((byte)FrameType.AircraftPosition);
                writer.Write(time);
                writer.Write(latitude);
                writer.Write(longitude);
                writer.Write(46.31249673649731); // altitude
                writer.Write(0.5f); // pitch
                writer.Write(0.5f); // bank
                writer.Write(heading);
                for (int i = 0; i < 9; i++)
                {
                    writer.Write(0.5f); // velocity, angular velocity, acceleration
                }

                for (int i = 0; i < 5; i++)
                {
                    writer.Write((short)0); // control surfaces and brakes
                }

                writer.Write(44.945507f); // elevation
                writer.Write((byte)0x03); // ground + elevation correction
            }

            writer.Write(string.Empty); // livery
            writer.Write("DH82"); // ICAO type
            writer.Write(string.Empty); // ICAO airline
        }

        return path;
    }

    /// <summary>One aircraft ("MIX") with position frames and IntegerVariables frames, merged in time order.</summary>
    public static string WriteMixed(string directory, double[] positionTimes, double[] integerTimes)
    {
        string path = Path.Combine(directory, $"{Guid.NewGuid():N}.jfs");
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);

        writer.Write(Version);
        writer.Write(1);
        writer.Write(true);
        writer.Write("MIX");
        writer.Write("nick");
        writer.Write("model");
        writer.Write((byte)1);

        var frames = positionTimes.Select(t => (Time: t, Position: true))
            .Concat(integerTimes.Select(t => (Time: t, Position: false)))
            .OrderBy(f => f.Time)
            .ToList();
        writer.Write(frames.Count);
        foreach ((double time, bool position) in frames)
        {
            if (position)
            {
                writer.Write((byte)FrameType.AircraftPosition);
                writer.Write(time);
                writer.Write(0.9674238236590125);
                writer.Write(0.231041585664082);
                writer.Write(46.0);
                for (int i = 0; i < 12; i++)
                {
                    writer.Write(0.5f);
                }

                for (int i = 0; i < 5; i++)
                {
                    writer.Write((short)0);
                }

                writer.Write(44.0f);
                writer.Write((byte)0x03);
            }
            else
            {
                writer.Write((byte)FrameType.IntegerVariables);
                writer.Write(time);
                writer.Write((ushort)1);
                writer.Write(42u);
                writer.Write(7);
            }
        }

        writer.Write(string.Empty);
        writer.Write(string.Empty);
        writer.Write(string.Empty);
        return path;
    }

    /// <summary>Evenly spaced times: start, start + step, ... (count values).</summary>
    public static double[] Times(double start, double step, int count) =>
        Enumerable.Range(0, count).Select(i => Math.Round(start + i * step, 6)).ToArray();
}
