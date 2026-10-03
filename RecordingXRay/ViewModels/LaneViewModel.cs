using System.Globalization;
using RecordingXRay.Services;

namespace RecordingXRay.ViewModels;

public sealed record InfoRow(string Key, string Value);

/// <summary>One aircraft or object in the recording: a "lane" of frames.</summary>
public sealed class LaneViewModel
{
    public LaneViewModel(RecordedObject source)
    {
        Source = source;
        IsAircraft = source is RecordedAircraft;
        Name = source is RecordedAircraft { Callsign.Length: > 0 } aircraft ? aircraft.Callsign : source.Model;
        if (Name.Length == 0)
        {
            Name = IsAircraft ? "Aircraft" : "Object";
        }

        Frames = source.Frames;
        FrameCountText = Frames.Count.ToString("N0", CultureInfo.InvariantCulture);

        double first = double.PositiveInfinity;
        double last = double.NegativeInfinity;
        HashSet<FrameKind> kinds = [];
        foreach (RecordedFrame frame in Frames)
        {
            first = Math.Min(first, frame.Time);
            last = Math.Max(last, frame.Time);
            kinds.Add(FrameKinds.Of(frame.Type));
        }

        FirstTime = Frames.Count == 0 ? 0 : first;
        LastTime = Frames.Count == 0 ? 0 : last;
        Kinds = FrameKinds.All.Where(kinds.Contains).ToArray();
        Info = BuildInfo(source);
    }

    public RecordedObject Source { get; }

    public bool IsAircraft { get; }

    /// <summary>The callsign for an aircraft, the model for an object.</summary>
    public string Name { get; }

    public string Model => Source.Model;

    public IReadOnlyList<RecordedFrame> Frames { get; }

    public string FrameCountText { get; }

    /// <summary>Time of the first and last frame: the extent of this lane.</summary>
    public double FirstTime { get; }

    public double LastTime { get; }

    /// <summary>The frame kinds this lane actually contains, in display order.</summary>
    public IReadOnlyList<FrameKind> Kinds { get; }

    /// <summary>Details shown in the aircraft popover.</summary>
    public IReadOnlyList<InfoRow> Info { get; }

    public override string ToString() => Name;

    private static IReadOnlyList<InfoRow> BuildInfo(RecordedObject source)
    {
        List<InfoRow> rows = [];
        if (source is RecordedAircraft aircraft)
        {
            rows.Add(new("Callsign", aircraft.Callsign));
            rows.Add(new("Nickname", aircraft.Nickname));
            rows.Add(new("Plane", aircraft.Plane ? "Yes" : "No"));
        }

        rows.Add(new("Model", source.Model));
        rows.Add(new("Type role", $"{source.TypeRole} ({FrameFormatter.TypeRoleToText(source.TypeRole)})"));
        rows.Add(new("Frames", source.Frames.Count.ToString("N0", CultureInfo.InvariantCulture)));
        AddIfSet(rows, "Livery", source.Livery);
        AddIfSet(rows, "ICAO type", source.IcaoType);
        AddIfSet(rows, "ICAO airline", source.IcaoAirline);
        return rows;
    }

    private static void AddIfSet(List<InfoRow> rows, string key, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            rows.Add(new(key, value));
        }
    }
}
