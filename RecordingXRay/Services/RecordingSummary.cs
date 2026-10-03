using System.Globalization;

namespace RecordingXRay.Services;

/// <summary>The figures shown in the summary strip, already formatted for display.</summary>
public sealed record RecordingSummary(
    string Version,
    string Aircraft,
    string Objects,
    string Frames,
    string Duration,
    string DurationClock,
    double DurationSeconds = 0)
{
    /// <summary>What the strip shows while no recording is loaded.</summary>
    public const string Placeholder = "—";

    public static RecordingSummary Empty { get; } =
        new(Placeholder, Placeholder, Placeholder, Placeholder, Placeholder, string.Empty);

    public static RecordingSummary From(RecordingFile recording)
    {
        int totalFrames = 0;
        double duration = 0;
        foreach (RecordedObject lane in recording.Aircraft.Concat<RecordedObject>(recording.Objects))
        {
            totalFrames += lane.Frames.Count;
            foreach (RecordedFrame frame in lane.Frames)
            {
                if (frame.Time > duration)
                {
                    duration = frame.Time;
                }
            }
        }

        return new RecordingSummary(
            recording.Version.ToString(CultureInfo.InvariantCulture),
            recording.Aircraft.Count.ToString(CultureInfo.InvariantCulture),
            recording.Objects.Count.ToString(CultureInfo.InvariantCulture),
            totalFrames.ToString("N0", CultureInfo.InvariantCulture),
            TimeFormat.Seconds(duration),
            TimeFormat.Clock(duration),
            duration);
    }
}
