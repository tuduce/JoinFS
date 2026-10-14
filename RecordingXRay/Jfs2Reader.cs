using System.Collections.ObjectModel;
using JoinFS.Net;
using JoinFS.Recording;

namespace RecordingXRay;

/// <summary>
/// Reads a .jfs2 file (docs/jfs2) into the same model the old .jfs reader fills. The file is read by <see cref="Jfs2Scene"/>;
/// this maps its canonical messages onto the frames the viewer shows. Each record is one frame, so a <c>variables</c> record
/// shows what it changed (an upsert, spec 7.6), as the file holds it, and not the merged set.
/// </summary>
internal static class Jfs2Reader
{
    /// <summary>The old format stored the five control axes as 1/16384 in a short; the new one does too (spec 7.1).</summary>
    private const float AxisScale = 16384.0f;

    public static RecordingFile Read(Stream stream)
    {
        Jfs2Scene scene;
        try
        {
            scene = Jfs2Scene.Load(stream);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }

        RecordingFile recording = new() { Version = 250, FormatLabel = "JFS2" };
        foreach (SceneTrack track in scene.Tracks)
        {
            IdentityUpdate id = track.Identity;
            RecordedObject target;
            if (id.IsAircraft)
            {
                RecordedAircraft aircraft = new() { Plane = id.IsPlane, Callsign = id.Callsign, Nickname = track.Nickname };
                recording.Aircraft.Add(aircraft);
                target = aircraft;
            }
            else
            {
                target = new RecordedObject();
                recording.Objects.Add(target);
            }

            target.Model = id.Model;
            target.TypeRole = id.TypeRole;
            target.Livery = id.Livery;
            target.IcaoType = id.IcaoType;
            target.IcaoAirline = id.IcaoAirline;
            foreach (SceneFrame frame in track.Frames)
            {
                target.Frames.AddRange(Frames(frame));
            }
        }

        return recording;
    }

    private static IEnumerable<RecordedFrame> Frames(SceneFrame frame)
    {
        switch (frame)
        {
            case ScenePositionFrame position:
                yield return AircraftPosition(frame.Time, position.Position);
                break;
            case SceneObjectPositionFrame position:
                yield return ObjectPosition(frame.Time, position.Position);
                break;
            case SceneEventFrame sim:
                yield return new SimEventFrame { Type = FrameType.SimEvent, Time = frame.Time, EventId = sim.EventId, Data = sim.Data };
                break;
            case SceneVariablesFrame variables:
                foreach (RecordedFrame split in Variables(frame.Time, variables.Variables))
                {
                    yield return split;
                }
                break;
        }
    }

    private static AircraftPositionFrame AircraftPosition(double time, PositionUpdate p) => new()
    {
        Type = FrameType.AircraftPosition,
        Time = time,
        Latitude = p.Latitude,
        Longitude = p.Longitude,
        Altitude = p.Altitude,
        Pitch = p.Pitch,
        Bank = p.Bank,
        Heading = p.Heading,
        VelocityX = p.VelocityX,
        VelocityY = p.VelocityY,
        VelocityZ = p.VelocityZ,
        AngularVelocityX = p.AngularVelocityX,
        AngularVelocityY = p.AngularVelocityY,
        AngularVelocityZ = p.AngularVelocityZ,
        AccelerationX = p.AccelerationX,
        AccelerationY = p.AccelerationY,
        AccelerationZ = p.AccelerationZ,
        RudderRaw = Raw(p.Rudder),
        ElevatorRaw = Raw(p.Elevator),
        AileronRaw = Raw(p.Aileron),
        BrakeLeftRaw = Raw(p.BrakeLeft),
        BrakeRightRaw = Raw(p.BrakeRight),
        Elevation = p.Elevation,
        Ground = (p.StateFlags & PositionStateFlags.OnGround) != 0,
        ElevationCorrection = (p.StateFlags & PositionStateFlags.ElevationCorrection) != 0,
    };

    private static ObjectPositionFrame ObjectPosition(double time, ObjectPositionUpdate p) => new()
    {
        Type = FrameType.ObjectPosition,
        Time = time,
        Latitude = p.Latitude,
        Longitude = p.Longitude,
        Altitude = p.Altitude,
        Pitch = p.Pitch,
        Bank = p.Bank,
        Heading = p.Heading,
        VelocityX = p.VelocityX,
        VelocityY = p.VelocityY,
        VelocityZ = p.VelocityZ,
        AngularVelocityX = p.AngularVelocityX,
        AngularVelocityY = p.AngularVelocityY,
        AngularVelocityZ = p.AngularVelocityZ,
        AccelerationX = p.AccelerationX,
        AccelerationY = p.AccelerationY,
        AccelerationZ = p.AccelerationZ,
        Height = p.Height,
        Ground = (p.StateFlags & PositionStateFlags.OnGround) != 0,
        ElevationCorrection = (p.StateFlags & PositionStateFlags.ElevationCorrection) != 0,
    };

    private static short Raw(float axis) => (short)(axis * AxisScale);

    /// <summary>One variables record becomes up to three frames, one per value kind, as the old format stored them.</summary>
    private static IEnumerable<RecordedFrame> Variables(double time, VariableSyncUpdate update)
    {
        Dictionary<uint, int> integers = [];
        Dictionary<uint, float> floats = [];
        Dictionary<uint, string> strings = [];
        foreach (VariableEntry entry in update.Entries)
        {
            switch (entry.Kind)
            {
                case VariableKind.Int32: integers[entry.Vuid] = entry.IntValue; break;
                case VariableKind.Float32: floats[entry.Vuid] = entry.FloatValue; break;
                case VariableKind.String8: strings[entry.Vuid] = entry.StringValue ?? string.Empty; break;
            }
        }

        if (integers.Count > 0)
            yield return new IntegerVariablesFrame { Type = FrameType.IntegerVariables, Time = time, Variables = new ReadOnlyDictionary<uint, int>(integers) };
        if (floats.Count > 0)
            yield return new FloatVariablesFrame { Type = FrameType.FloatVariables, Time = time, Variables = new ReadOnlyDictionary<uint, float>(floats) };
        if (strings.Count > 0)
            yield return new String8VariablesFrame { Type = FrameType.String8Variables, Time = time, Variables = new ReadOnlyDictionary<uint, string>(strings) };
    }
}
