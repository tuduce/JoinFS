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
        Dictionary<FrameKind, List<double>> times = [];
        List<int> positions = [];
        for (int i = 0; i < Frames.Count; i++)
        {
            RecordedFrame frame = Frames[i];
            first = Math.Min(first, frame.Time);
            last = Math.Max(last, frame.Time);

            FrameKind kind = FrameKinds.Of(frame.Type);
            if (!times.TryGetValue(kind, out List<double>? list))
            {
                times[kind] = list = [];
            }

            list.Add(frame.Time);
            if (kind == FrameKind.Position)
            {
                positions.Add(i);
            }
        }

        FirstTime = Frames.Count == 0 ? 0 : first;
        LastTime = Frames.Count == 0 ? 0 : last;
        Kinds = FrameKinds.All.Where(times.ContainsKey).ToArray();
        TimesByKind = times.ToDictionary(pair => pair.Key, pair => SortedCopy(pair.Value));
        positionIndexes = positions.ToArray();
        Info = BuildInfo(source);
    }

    private readonly int[] positionIndexes;

    /// <summary>Frame times of each kind, ascending. The timeline counts frames per pixel column from these.</summary>
    public IReadOnlyDictionary<FrameKind, double[]> TimesByKind { get; }

    /// <summary>True when the cursor at <paramref name="time"/> is inside this lane's extent (first to last frame).</summary>
    public bool HasDataAt(double time) => Frames.Count > 0 && time >= FirstTime && time <= LastTime;

    /// <summary>Moves a time into this lane's extent: before it gives the first frame time, after it the last.</summary>
    public double ClampToExtent(double time) => time < FirstTime ? FirstTime : (time > LastTime ? LastTime : time);

    /// <summary>
    /// The frame to show for a time cursor: the last position frame at or before the time, else the first position
    /// frame; for a lane without position frames, the last frame at or before the time, else the first. -1 for an empty lane.
    /// </summary>
    public int FrameIndexAt(double time)
    {
        if (Frames.Count == 0)
        {
            return -1;
        }

        if (positionIndexes.Length > 0)
        {
            int position = LastAtOrBefore(positionIndexes.Length, i => Frames[positionIndexes[i]].Time, time);
            return positionIndexes[Math.Max(position, 0)];
        }

        return Math.Max(LastAtOrBefore(Frames.Count, i => Frames[i].Time, time), 0);
    }

    // Index of the last item whose time is <= time, or -1.
    private static int LastAtOrBefore(int count, Func<int, double> timeAt, double time)
    {
        int low = 0;
        int high = count - 1;
        int found = -1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (timeAt(mid) <= time)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }

    private static double[] SortedCopy(List<double> values)
    {
        double[] array = values.ToArray();
        Array.Sort(array);
        return array;
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
