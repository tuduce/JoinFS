using JoinFS.Recording;

namespace JoinFS.Tests.Recording;

/// <summary>
/// A recorded object's track starts with its first position that counts, and an aircraft at exactly 0/0 does not count (README limit 16,
/// the field test of 2026-10-15: MSFS lists the user's aircraft there for about 25 s while it loads a flight).
/// </summary>
public sealed class TrackStartTests
{
    static Recorder.Obj Linked() => new(new SceneTrack { Identity = Jfs2ImportTests.Track(true, "HB-TDX", "me", "Bonanza").Identity });

    static Sim.AircraftPosition At(double latitude, double longitude) => new() { latitude = latitude, longitude = longitude, altitude = 500 };

    [Fact]
    public void AnAircraftThatStaysAtZeroZero_LeavesNoTrack()
    {
        var recorder = new Recorder(null!);
        Recorder.Obj loading = Linked();

        for (int i = 0; i < 10; i++)
        {
            Sim.AircraftPosition position = At(0, 0);
            recorder.Record(loading, i * 0.05, ref position);
            recorder.Record(loading, new Dictionary<uint, int> { [1] = 1 });
            recorder.Record(loading, 66000, 1);
        }

        Assert.True(recorder.Empty);
        Assert.False(loading.started);
        Assert.Empty(loading.track.Frames);
    }

    [Fact]
    public void TheTrackStartsWithTheFirstPositionAwayFromZeroZero()
    {
        var recorder = new Recorder(null!);
        Recorder.Obj aircraft = Linked();
        Sim.AircraftPosition loading = At(0, 0);
        recorder.Record(aircraft, 0.0, ref loading);
        recorder.Record(aircraft, new Dictionary<uint, int> { [1] = 1 });

        Sim.AircraftPosition parked = At(0.8, 0.1);
        recorder.Record(aircraft, 0.05, ref parked);
        recorder.Record(aircraft, new Dictionary<uint, int> { [1] = 1 });

        Assert.False(recorder.Empty);
        Assert.True(aircraft.started);
        Assert.Collection(aircraft.track.Frames,
            f => Assert.Equal(0.8, Assert.IsType<ScenePositionFrame>(f).Position.Latitude),
            f => Assert.IsType<SceneVariablesFrame>(f));
    }

    [Fact]
    public void ALaterPositionAtZeroZero_IsLeftOut()
    {
        var recorder = new Recorder(null!);
        Recorder.Obj aircraft = Linked();
        foreach (var (latitude, longitude) in new[] { (0.8, 0.1), (0.0, 0.0), (0.8, 0.0), (0.0, 0.1) })
        {
            Sim.AircraftPosition position = At(latitude, longitude);
            recorder.Record(aircraft, 0.0, ref position);
        }

        // only exactly 0/0 is left out; one of the two at 0 is a real place
        Assert.Equal(3, aircraft.track.Frames.Count);
    }

    [Fact]
    public void AnObjectThatIsNotAnAircraft_StartsWithItsFirstPosition_WhereverItIs()
    {
        var recorder = new Recorder(null!);
        Recorder.Obj boat = new(new SceneTrack { Identity = Jfs2ImportTests.Track(false, "", "", "Ferry").Identity });
        Sim.ObjectPositionVelocity position = new() { latitude = 0, longitude = 0 };

        recorder.Record(boat, 0.0, ref position);

        Assert.True(boat.started);
        Assert.False(recorder.Empty);
    }
}
