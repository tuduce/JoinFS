using System.Collections.ObjectModel;

namespace RecordingXRay.Tests;

/// <summary>Builders for in-memory frames and lanes.</summary>
internal static class TestFrames
{
    public static AircraftPositionFrame Position(double time) => new()
    {
        Type = FrameType.AircraftPosition,
        Time = time,
        Latitude = 0.9674238236590125,
        Longitude = 0.231041585664082,
        Altitude = 46.31249673649731,
        Pitch = -0.19611156f,
        Bank = -0.007834598f,
        Heading = 2.874476f,
        VelocityX = 0.75756854f,
        VelocityY = -0.015327258f,
        VelocityZ = -2.8728685f,
        RudderRaw = -1,
        ElevatorRaw = 712,
        AileronRaw = -1471,
        BrakeLeftRaw = 0,
        BrakeRightRaw = 16384,
        Elevation = 44.945507f,
        Ground = true,
        ElevationCorrection = false,
    };

    public static ObjectPositionFrame ObjectPosition(double time) => new()
    {
        Type = FrameType.ObjectPosition,
        Time = time,
        Height = 3.5f,
        Ground = false,
        ElevationCorrection = true,
    };

    public static IntegerVariablesFrame Integers(double time, params (uint Id, int Value)[] values) => new()
    {
        Type = FrameType.IntegerVariables,
        Time = time,
        Variables = new ReadOnlyDictionary<uint, int>(values.ToDictionary(v => v.Id, v => v.Value)),
    };

    public static FloatVariablesFrame Floats(double time, params (uint Id, float Value)[] values) => new()
    {
        Type = FrameType.FloatVariables,
        Time = time,
        Variables = new ReadOnlyDictionary<uint, float>(values.ToDictionary(v => v.Id, v => v.Value)),
    };

    public static String8VariablesFrame Strings(double time, params (uint Id, string Value)[] values) => new()
    {
        Type = FrameType.String8Variables,
        Time = time,
        Variables = new ReadOnlyDictionary<uint, string>(values.ToDictionary(v => v.Id, v => v.Value)),
    };

    public static SimEventFrame Event(double time) => new() { Type = FrameType.SimEvent, Time = time, EventId = 7, Data = 9 };

    public static RecordedAircraft Aircraft(string callsign, params RecordedFrame[] frames)
    {
        RecordedAircraft aircraft = new() { Callsign = callsign, Model = "Tiger Moth TIGER-4 01001011110", Plane = true, TypeRole = 1 };
        aircraft.Frames.AddRange(frames);
        return aircraft;
    }

    /// <summary>A lane shaped like the start of the sample recording: positions, then integer / float / string frames at 0.154 s.</summary>
    public static RecordedAircraft SampleAircraft(string callsign = "YR-SCD") => Aircraft(
        callsign,
        Position(0.026),
        Position(0.101),
        Position(0.146),
        Integers(0.154, (1, 5), (2, -3)),
        Floats(0.154, (3, 1.5f)),
        Strings(0.154, (4, "abc")),
        Position(0.207),
        Event(0.3));
}
