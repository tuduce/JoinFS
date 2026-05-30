using System.Collections.ObjectModel;

namespace RecordingXRay;

public enum FrameType : byte
{
    ObjectPosition,
    AircraftPosition,
    PlaneState,
    HelicopterState,
    AircraftState,
    PistonEngineState,
    TurbineEngineState,
    ObjectSmoke,
    AircraftFuel,
    AircraftPayload,
    SimEvent,
    IntegerVariables,
    FloatVariables,
    String8Variables,
}

public sealed class RecordingFile
{
    public short Version { get; init; }
    public List<RecordedAircraft> Aircraft { get; } = [];
    public List<RecordedObject> Objects { get; } = [];
}

public class RecordedObject
{
    public string Model { get; set; } = string.Empty;
    public int TypeRole { get; set; }
    public string Livery { get; set; } = string.Empty;
    public List<RecordedFrame> Frames { get; } = [];
}

public sealed class RecordedAircraft : RecordedObject
{
    public bool Plane { get; set; }
    public string Callsign { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
}

public abstract class RecordedFrame
{
    public FrameType Type { get; init; }
    public double Time { get; init; }
}

public sealed class ObjectPositionFrame : RecordedFrame
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double Altitude { get; init; }
    public float Pitch { get; init; }
    public float Bank { get; init; }
    public float Heading { get; init; }
    public float VelocityX { get; init; }
    public float VelocityY { get; init; }
    public float VelocityZ { get; init; }
    public float AngularVelocityX { get; init; }
    public float AngularVelocityY { get; init; }
    public float AngularVelocityZ { get; init; }
    public float AccelerationX { get; init; }
    public float AccelerationY { get; init; }
    public float AccelerationZ { get; init; }
    public float Height { get; init; }
    public bool Ground { get; init; }
    public bool ElevationCorrection { get; init; }
}

public sealed class AircraftPositionFrame : RecordedFrame
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double Altitude { get; init; }
    public float Pitch { get; init; }
    public float Bank { get; init; }
    public float Heading { get; init; }
    public float VelocityX { get; init; }
    public float VelocityY { get; init; }
    public float VelocityZ { get; init; }
    public float AngularVelocityX { get; init; }
    public float AngularVelocityY { get; init; }
    public float AngularVelocityZ { get; init; }
    public float AccelerationX { get; init; }
    public float AccelerationY { get; init; }
    public float AccelerationZ { get; init; }
    public short RudderRaw { get; init; }
    public short ElevatorRaw { get; init; }
    public short AileronRaw { get; init; }
    public short BrakeLeftRaw { get; init; }
    public short BrakeRightRaw { get; init; }
    public float Elevation { get; init; }
    public bool Ground { get; init; }
    public bool ElevationCorrection { get; init; }
}

public sealed class SimEventFrame : RecordedFrame
{
    public uint EventId { get; init; }
    public uint Data { get; init; }
}

public sealed class IntegerVariablesFrame : RecordedFrame
{
    public ReadOnlyDictionary<uint, int> Variables { get; init; } = new(new Dictionary<uint, int>());
}

public sealed class FloatVariablesFrame : RecordedFrame
{
    public ReadOnlyDictionary<uint, float> Variables { get; init; } = new(new Dictionary<uint, float>());
}

public sealed class String8VariablesFrame : RecordedFrame
{
    public ReadOnlyDictionary<uint, string> Variables { get; init; } = new(new Dictionary<uint, string>());
}

public static class RecordingReader
{
    public static RecordingFile Read(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        using BinaryReader reader = new(stream);

        short version = reader.ReadInt16();
        if (version < 10022)
        {
            throw new InvalidDataException($"Unsupported JoinFS recording version: {version}");
        }

        RecordingFile recording = new() { Version = version };

        int aircraftCount = reader.ReadInt32();
        for (int i = 0; i < aircraftCount; i++)
        {
            recording.Aircraft.Add(ReadAircraft(reader, version));
        }

        if (stream.Position < stream.Length)
        {
            int objectCount = reader.ReadInt32();
            for (int i = 0; i < objectCount; i++)
            {
                recording.Objects.Add(ReadObject(reader, version));
            }
        }

        return recording;
    }

    private static RecordedAircraft ReadAircraft(BinaryReader reader, short version)
    {
        RecordedAircraft aircraft = new()
        {
            Plane = reader.ReadBoolean(),
            Callsign = reader.ReadString(),
            Nickname = reader.ReadString(),
            Model = reader.ReadString(),
            TypeRole = reader.ReadByte(),
        };

        int frameCount = reader.ReadInt32();
        for (int i = 0; i < frameCount; i++)
        {
            aircraft.Frames.Add(ReadFrame(reader, version));
        }

        if (version >= 21004)
        {
            aircraft.Livery = reader.ReadString();
        }

        return aircraft;
    }

    private static RecordedObject ReadObject(BinaryReader reader, short version)
    {
        RecordedObject obj = new()
        {
            Model = reader.ReadString(),
            TypeRole = reader.ReadByte(),
        };

        int frameCount = reader.ReadInt32();
        for (int i = 0; i < frameCount; i++)
        {
            obj.Frames.Add(ReadFrame(reader, version));
        }

        if (version >= 21004)
        {
            obj.Livery = reader.ReadString();
        }

        return obj;
    }

    private static RecordedFrame ReadFrame(BinaryReader reader, short version)
    {
        FrameType type = (FrameType)reader.ReadByte();
        double time = reader.ReadDouble();

        return type switch
        {
            FrameType.ObjectPosition => ReadObjectPositionFrame(reader, version, time),
            FrameType.AircraftPosition => ReadAircraftPositionFrame(reader, version, time),
            FrameType.SimEvent => new SimEventFrame
            {
                Type = type,
                Time = time,
                EventId = reader.ReadUInt32(),
                Data = reader.ReadUInt32(),
            },
            FrameType.IntegerVariables => new IntegerVariablesFrame
            {
                Type = type,
                Time = time,
                Variables = new ReadOnlyDictionary<uint, int>(ReadIntVariables(reader))
            },
            FrameType.FloatVariables => new FloatVariablesFrame
            {
                Type = type,
                Time = time,
                Variables = new ReadOnlyDictionary<uint, float>(ReadFloatVariables(reader))
            },
            FrameType.String8Variables => new String8VariablesFrame
            {
                Type = type,
                Time = time,
                Variables = new ReadOnlyDictionary<uint, string>(ReadStringVariables(reader))
            },
            _ => throw new InvalidDataException($"Unsupported frame type in recording: {type}")
        };
    }

    private static ObjectPositionFrame ReadObjectPositionFrame(BinaryReader reader, short version, double time)
    {
        float height = 0.0f;
        bool ground = false;
        bool elevationCorrection = false;

        double latitude = reader.ReadDouble();
        double longitude = reader.ReadDouble();
        double altitude = reader.ReadDouble();
        float pitch = reader.ReadSingle();
        float bank = reader.ReadSingle();
        float heading = reader.ReadSingle();
        float velocityX = reader.ReadSingle();
        float velocityY = reader.ReadSingle();
        float velocityZ = reader.ReadSingle();
        float angularVelocityX = reader.ReadSingle();
        float angularVelocityY = reader.ReadSingle();
        float angularVelocityZ = reader.ReadSingle();
        float accelerationX = reader.ReadSingle();
        float accelerationY = reader.ReadSingle();
        float accelerationZ = reader.ReadSingle();

        if (version >= 10023)
        {
            height = reader.ReadSingle();
            byte flags = reader.ReadByte();
            ground = (flags & 0x01) != 0;
            elevationCorrection = (flags & 0x02) != 0;
        }

        return new ObjectPositionFrame
        {
            Type = FrameType.ObjectPosition,
            Time = time,
            Latitude = latitude,
            Longitude = longitude,
            Altitude = altitude,
            Pitch = pitch,
            Bank = bank,
            Heading = heading,
            VelocityX = velocityX,
            VelocityY = velocityY,
            VelocityZ = velocityZ,
            AngularVelocityX = angularVelocityX,
            AngularVelocityY = angularVelocityY,
            AngularVelocityZ = angularVelocityZ,
            AccelerationX = accelerationX,
            AccelerationY = accelerationY,
            AccelerationZ = accelerationZ,
            Height = height,
            Ground = ground,
            ElevationCorrection = elevationCorrection
        };
    }

    private static AircraftPositionFrame ReadAircraftPositionFrame(BinaryReader reader, short version, double time)
    {
        float elevation = 0.0f;
        bool ground = false;
        bool elevationCorrection = false;

        double latitude = reader.ReadDouble();
        double longitude = reader.ReadDouble();
        double altitude = reader.ReadDouble();
        float pitch = reader.ReadSingle();
        float bank = reader.ReadSingle();
        float heading = reader.ReadSingle();
        float velocityX = reader.ReadSingle();
        float velocityY = reader.ReadSingle();
        float velocityZ = reader.ReadSingle();
        float angularVelocityX = reader.ReadSingle();
        float angularVelocityY = reader.ReadSingle();
        float angularVelocityZ = reader.ReadSingle();
        float accelerationX = reader.ReadSingle();
        float accelerationY = reader.ReadSingle();
        float accelerationZ = reader.ReadSingle();
        short rudder = reader.ReadInt16();
        short elevatorRaw = reader.ReadInt16();
        short aileron = reader.ReadInt16();
        short brakeLeft = reader.ReadInt16();
        short brakeRight = reader.ReadInt16();

        if (version >= 10023)
        {
            elevation = reader.ReadSingle();
            byte flags = reader.ReadByte();
            ground = (flags & 0x01) != 0;
            elevationCorrection = (flags & 0x02) != 0;
        }

        return new AircraftPositionFrame
        {
            Type = FrameType.AircraftPosition,
            Time = time,
            Latitude = latitude,
            Longitude = longitude,
            Altitude = altitude,
            Pitch = pitch,
            Bank = bank,
            Heading = heading,
            VelocityX = velocityX,
            VelocityY = velocityY,
            VelocityZ = velocityZ,
            AngularVelocityX = angularVelocityX,
            AngularVelocityY = angularVelocityY,
            AngularVelocityZ = angularVelocityZ,
            AccelerationX = accelerationX,
            AccelerationY = accelerationY,
            AccelerationZ = accelerationZ,
            RudderRaw = rudder,
            ElevatorRaw = elevatorRaw,
            AileronRaw = aileron,
            BrakeLeftRaw = brakeLeft,
            BrakeRightRaw = brakeRight,
            Elevation = elevation,
            Ground = ground,
            ElevationCorrection = elevationCorrection,
        };
    }

    private static Dictionary<uint, int> ReadIntVariables(BinaryReader reader)
    {
        ushort count = reader.ReadUInt16();
        Dictionary<uint, int> variables = new(count);
        for (int i = 0; i < count; i++)
        {
            variables[reader.ReadUInt32()] = reader.ReadInt32();
        }

        return variables;
    }

    private static Dictionary<uint, float> ReadFloatVariables(BinaryReader reader)
    {
        ushort count = reader.ReadUInt16();
        Dictionary<uint, float> variables = new(count);
        for (int i = 0; i < count; i++)
        {
            variables[reader.ReadUInt32()] = reader.ReadSingle();
        }

        return variables;
    }

    private static Dictionary<uint, string> ReadStringVariables(BinaryReader reader)
    {
        ushort count = reader.ReadUInt16();
        Dictionary<uint, string> variables = new(count);
        for (int i = 0; i < count; i++)
        {
            variables[reader.ReadUInt32()] = reader.ReadString();
        }

        return variables;
    }
}
