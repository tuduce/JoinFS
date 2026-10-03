namespace RecordingXRay.ViewModels;

/// <summary>Frame types grouped the way the UI colours and filters them.</summary>
public enum FrameKind
{
    Position,
    Integer,
    Float,
    String8,
    SimEvent,
    Other,
}

public static class FrameKinds
{
    public static FrameKind Of(FrameType type) => type switch
    {
        FrameType.ObjectPosition or FrameType.AircraftPosition => FrameKind.Position,
        FrameType.IntegerVariables => FrameKind.Integer,
        FrameType.FloatVariables => FrameKind.Float,
        FrameType.String8Variables => FrameKind.String8,
        FrameType.SimEvent => FrameKind.SimEvent,
        _ => FrameKind.Other,
    };

    /// <summary>Short chip / legend label.</summary>
    public static string Label(FrameKind kind) => kind switch
    {
        FrameKind.Position => "Position",
        FrameKind.Integer => "Integer",
        FrameKind.Float => "Float",
        FrameKind.String8 => "String8",
        FrameKind.SimEvent => "Events",
        _ => "Other",
    };

    /// <summary>Display order for chips.</summary>
    public static readonly FrameKind[] All =
    [
        FrameKind.Position, FrameKind.Integer, FrameKind.Float, FrameKind.String8, FrameKind.SimEvent, FrameKind.Other,
    ];
}
