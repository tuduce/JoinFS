namespace RecordingXRay;

/// <summary>
/// Serialises a (potentially edited) <see cref="RecordingFile"/> back to the JoinFS
/// binary format.  Call <see cref="RecordingEditSession.CommitEditsToObject"/> on all
/// active sessions before invoking <see cref="Write"/>.
/// The output byte layout is the exact inverse of <see cref="RecordingReader"/>.
/// </summary>
public static class RecordingWriter
{
    /// <summary>
    /// Saves <paramref name="recording"/> to <paramref name="filePath"/>, preserving
    /// the original file version so JoinFS can read the result unchanged.
    /// </summary>
    public static void Write(RecordingFile recording, string filePath)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        // Write to a temp file first so a failure leaves the original intact.
        string tmp = filePath + ".tmp";
        try
        {
            using (FileStream stream = File.Open(tmp, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new(stream))
            {
                short version = recording.Version;

                writer.Write(version);

                writer.Write(recording.Aircraft.Count);
                foreach (RecordedAircraft aircraft in recording.Aircraft)
                    WriteAircraft(writer, aircraft, version);

                writer.Write(recording.Objects.Count);
                foreach (RecordedObject obj in recording.Objects)
                    WriteObject(writer, obj, version);
            }

            File.Move(tmp, filePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            throw;
        }
    }

    // ── Aircraft / Object ────────────────────────────────────────────────────────

    private static void WriteAircraft(BinaryWriter writer, RecordedAircraft aircraft, short version)
    {
        writer.Write(aircraft.Plane);
        writer.Write(aircraft.Callsign);
        writer.Write(aircraft.Nickname);
        WriteObjectBody(writer, aircraft, version);
    }

    private static void WriteObject(BinaryWriter writer, RecordedObject obj, short version)
    {
        WriteObjectBody(writer, obj, version);
    }

    private static void WriteObjectBody(BinaryWriter writer, RecordedObject obj, short version)
    {
        writer.Write(obj.Model);
        writer.Write((byte)obj.TypeRole);

        writer.Write(obj.Frames.Count);
        foreach (RecordedFrame frame in obj.Frames)
            WriteFrame(writer, frame, version);

        // Conditional trailing fields — must match RecordingReader version checks exactly.
        if (version >= 21004)
            writer.Write(obj.Livery);

        if (version >= 21005)
        {
            writer.Write(obj.IcaoType);
            writer.Write(obj.IcaoAirline);
        }
    }

    // ── Frames ───────────────────────────────────────────────────────────────────

    private static void WriteFrame(BinaryWriter writer, RecordedFrame frame, short version)
    {
        writer.Write((byte)frame.Type);
        writer.Write(frame.Time);

        switch (frame)
        {
            case ObjectPositionFrame f:
                WriteObjectPositionFrame(writer, f, version);
                break;
            case AircraftPositionFrame f:
                WriteAircraftPositionFrame(writer, f, version);
                break;
            case SimEventFrame f:
                writer.Write(f.EventId);
                writer.Write(f.Data);
                break;
            case IntegerVariablesFrame f:
                WriteIntVariables(writer, f.Variables);
                break;
            case FloatVariablesFrame f:
                WriteFloatVariables(writer, f.Variables);
                break;
            case String8VariablesFrame f:
                WriteStringVariables(writer, f.Variables);
                break;
            default:
                throw new InvalidDataException($"Cannot serialise unsupported frame type '{frame.Type}'.");
        }
    }

    private static void WriteObjectPositionFrame(BinaryWriter writer, ObjectPositionFrame f, short version)
    {
        writer.Write(f.Latitude);
        writer.Write(f.Longitude);
        writer.Write(f.Altitude);
        writer.Write(f.Pitch);
        writer.Write(f.Bank);
        writer.Write(f.Heading);
        writer.Write(f.VelocityX);
        writer.Write(f.VelocityY);
        writer.Write(f.VelocityZ);
        writer.Write(f.AngularVelocityX);
        writer.Write(f.AngularVelocityY);
        writer.Write(f.AngularVelocityZ);
        writer.Write(f.AccelerationX);
        writer.Write(f.AccelerationY);
        writer.Write(f.AccelerationZ);
        if (version >= 10023)
        {
            writer.Write(f.Height);
            byte flags = 0;
            if (f.Ground) flags |= 0x01;
            if (f.ElevationCorrection) flags |= 0x02;
            writer.Write(flags);
        }
    }

    private static void WriteAircraftPositionFrame(BinaryWriter writer, AircraftPositionFrame f, short version)
    {
        writer.Write(f.Latitude);
        writer.Write(f.Longitude);
        writer.Write(f.Altitude);
        writer.Write(f.Pitch);
        writer.Write(f.Bank);
        writer.Write(f.Heading);
        writer.Write(f.VelocityX);
        writer.Write(f.VelocityY);
        writer.Write(f.VelocityZ);
        writer.Write(f.AngularVelocityX);
        writer.Write(f.AngularVelocityY);
        writer.Write(f.AngularVelocityZ);
        writer.Write(f.AccelerationX);
        writer.Write(f.AccelerationY);
        writer.Write(f.AccelerationZ);
        writer.Write(f.RudderRaw);
        writer.Write(f.ElevatorRaw);
        writer.Write(f.AileronRaw);
        writer.Write(f.BrakeLeftRaw);
        writer.Write(f.BrakeRightRaw);
        if (version >= 10023)
        {
            writer.Write(f.Elevation);
            byte flags = 0;
            if (f.Ground) flags |= 0x01;
            if (f.ElevationCorrection) flags |= 0x02;
            writer.Write(flags);
        }
    }

    // ── Variable dictionaries ────────────────────────────────────────────────────

    private static void WriteIntVariables(BinaryWriter writer, IReadOnlyDictionary<uint, int> vars)
    {
        writer.Write((ushort)vars.Count);
        foreach (var kv in vars)
        {
            writer.Write(kv.Key);
            writer.Write(kv.Value);
        }
    }

    private static void WriteFloatVariables(BinaryWriter writer, IReadOnlyDictionary<uint, float> vars)
    {
        writer.Write((ushort)vars.Count);
        foreach (var kv in vars)
        {
            writer.Write(kv.Key);
            writer.Write(kv.Value);
        }
    }

    private static void WriteStringVariables(BinaryWriter writer, IReadOnlyDictionary<uint, string> vars)
    {
        writer.Write((ushort)vars.Count);
        foreach (var kv in vars)
        {
            writer.Write(kv.Key);
            writer.Write(kv.Value);
        }
    }
}
