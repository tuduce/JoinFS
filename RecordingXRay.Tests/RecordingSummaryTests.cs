using RecordingXRay.Services;

namespace RecordingXRay.Tests;

public class RecordingSummaryTests
{
    [Fact]
    public void Empty_shows_placeholders()
    {
        RecordingSummary summary = RecordingSummary.Empty;

        Assert.Equal(RecordingSummary.Placeholder, summary.Version);
        Assert.Equal(RecordingSummary.Placeholder, summary.Frames);
        Assert.Equal(string.Empty, summary.DurationClock);
    }

    [Fact]
    public void From_counts_aircraft_objects_frames_and_takes_the_latest_frame_time_as_duration()
    {
        RecordingFile recording = new() { Version = 21005 };
        recording.Aircraft.Add(Lane<RecordedAircraft>(0.026, 100.5));
        recording.Aircraft.Add(Lane<RecordedAircraft>(0.5, 711.165, 12));
        recording.Objects.Add(Lane<RecordedObject>(1, 2));

        RecordingSummary summary = RecordingSummary.From(recording);

        Assert.Equal("21005", summary.Version);
        Assert.Equal("2", summary.Aircraft);
        Assert.Equal("1", summary.Objects);
        Assert.Equal("7", summary.Frames);
        Assert.Equal("711.165 s", summary.Duration);
        Assert.Equal("11:51.165", summary.DurationClock);
    }

    [Fact]
    public void From_formats_large_frame_counts_with_thousands_separators()
    {
        RecordingFile recording = new() { Version = 21005 };
        RecordedAircraft aircraft = new();
        for (int i = 0; i < 144_753; i++)
        {
            aircraft.Frames.Add(new SimEventFrame { Type = FrameType.SimEvent, Time = i / 1000.0 });
        }

        recording.Aircraft.Add(aircraft);

        Assert.Equal("144,753", RecordingSummary.From(recording).Frames);
    }

    private static T Lane<T>(params double[] times) where T : RecordedObject, new()
    {
        T lane = new();
        foreach (double time in times)
        {
            lane.Frames.Add(new SimEventFrame { Type = FrameType.SimEvent, Time = time });
        }

        return lane;
    }
}
