namespace RecordingXRay.Tests;

/// <summary>Writes small, valid .jfs files (format: docs/recording-protocol.md) for tests.</summary>
internal static class RecordingFiles
{
    public const short Version = 21005;

    /// <summary>One aircraft ("YR-SCD") with an AircraftPosition frame at each of <paramref name="times"/>.</summary>
    public static string WriteSingleAircraft(string directory, params double[] times)
    {
        string path = Path.Combine(directory, $"{Guid.NewGuid():N}.jfs");
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);

        writer.Write(Version);
        writer.Write(1); // aircraft count
        writer.Write(true); // plane
        writer.Write("YR-SCD");
        writer.Write("nick");
        writer.Write("Tiger Moth TIGER-4 01001011110");
        writer.Write((byte)1);

        writer.Write(times.Length);
        foreach (double time in times)
        {
            writer.Write((byte)FrameType.AircraftPosition);
            writer.Write(time);
            writer.Write(0.9674238236590125); // latitude
            writer.Write(0.231041585664082); // longitude
            writer.Write(46.31249673649731); // altitude
            for (int i = 0; i < 12; i++)
            {
                writer.Write(0.5f); // pitch, bank, heading, velocity, angular velocity, acceleration
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
        return path;
    }
}
