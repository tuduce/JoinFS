using JoinFS.Net;
using JoinFS.Recording;

namespace JoinFS.Tests.Recording;

/// <summary>
/// What the player shows after a jump (<see cref="SceneTrack.StateAt"/>, design 5.4): the latest position, the variables merged and the
/// latest identity, so gear, flaps and lights are right wherever playing starts (README limit 3). And identity changes, which the player
/// applies, survive a save (README limits 1 and 2).
/// </summary>
public sealed class SceneStateTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("jfs2-state-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(folder, true); } catch (IOException) { }
    }

    static Sim.AircraftPosition At(double latitude) => new() { latitude = latitude, longitude = 0.1, altitude = 500 };

    static IdentityUpdate Renamed(SceneTrack track, string callsign)
    {
        IdentityUpdate identity = track.Identity;
        identity.Callsign = callsign;
        return identity;
    }

    /// <summary>Positions every second from 0 to 10; gear (1) down at 0 and up at 4, flaps (2) at 3, a string at 6; renamed at 5; an event at 7.</summary>
    internal static SceneTrack Flight()
    {
        SceneTrack track = Jfs2ImportTests.Track(true, "N1", "me", "Cessna 172");
        for (int i = 0; i <= 10; i++)
        {
            track.Frames.Add(Jfs2ImportTests.Position(i, At(0.8 + i * 1e-5)));
            switch (i)
            {
                case 0: track.Frames.Add(Jfs2ImportTests.Variables(i, new() { [1] = 1, [2] = 0 }, new() { [3] = 0.5f })); break;
                case 3: track.Frames.Add(Jfs2ImportTests.Variables(i, new() { [2] = 2 })); break;
                case 4: track.Frames.Add(Jfs2ImportTests.Variables(i, new() { [1] = 0 })); break;
                case 5: track.Frames.Add(new SceneIdentityFrame(i, Renamed(track, "DLH1"))); break;
                case 6: track.Frames.Add(Jfs2ImportTests.Variables(i, null, null, new() { [4] = "N123" })); break;
                case 7: track.Frames.Add(new SceneEventFrame(i, 66000, 1)); break;
            }
        }
        return track;
    }

    [Fact]
    public void AJumpForward_HasTheVariablesOfEverythingBefore_MergedInOrder()
    {
        SceneState state = Flight().StateAt(5.5);

        Assert.Equal(new Dictionary<uint, int> { [1] = 0, [2] = 2 }, state.Integers);
        Assert.Equal(new Dictionary<uint, float> { [3] = 0.5f }, state.Floats);
        Assert.Null(state.Strings);
        Assert.Equal("DLH1", state.Identity.Callsign);
        Assert.Equal(5.0, Assert.IsType<ScenePositionFrame>(state.Position).Time);
    }

    [Fact]
    public void AJumpBack_HasTheStateOfThatTime_NotOfWherePlayingWas()
    {
        SceneTrack track = Flight();
        track.StateAt(9.5);

        SceneState state = track.StateAt(3.5);

        Assert.Equal(new Dictionary<uint, int> { [1] = 1, [2] = 2 }, state.Integers);
        Assert.Equal("N1", state.Identity.Callsign);
        Assert.Equal(3.0, state.Position!.Time);
    }

    [Fact]
    public void PlayingGoesOnAtTheFirstFrameNotBeforeTheTime()
    {
        SceneTrack track = Flight();

        SceneState state = track.StateAt(4.0);

        // frames at exactly 4 s are still to play: the player plays a frame once its time has passed
        Assert.Equal(4.0, track.Frames[state.Next].Time);
        Assert.True(track.Frames[state.Next - 1].Time < 4.0);
        Assert.Equal(1, state.Integers![1]);
    }

    [Fact]
    public void BeforeTheFirstFrame_ThereIsNoStateAndTheOpeningIdentity()
    {
        SceneState state = Flight().StateAt(0.0);

        Assert.Equal(0, state.Next);
        Assert.Null(state.Position);
        Assert.Null(state.Integers);
        Assert.Equal("N1", state.Identity.Callsign);
    }

    [Fact]
    public void PastTheEnd_EveryFrameIsPlayed()
    {
        SceneTrack track = Flight();

        SceneState state = track.StateAt(100);

        Assert.Equal(track.Frames.Count, state.Next);
        Assert.Equal("N123", state.Strings![4]);
        Assert.Equal(10.0, state.Position!.Time);
    }

    /// <summary>
    /// The field test of 2026-10-15: a change of aircraft during a recording leaves the user's aircraft as tracks that start one after
    /// another, and the player showed every one of them from the start, at its first position.
    /// </summary>
    [Fact]
    public void ATrackThatStartsLater_IsNotShownBeforeItsFirstPosition()
    {
        SceneTrack later = Flight();
        later.Shift(30);

        Assert.False(later.ShownAt(0.1));
        Assert.False(later.ShownAt(30), "its first position is at 30 and plays once that time has passed");
        Assert.True(later.ShownAt(30.001));
        Assert.True(later.ShownAt(40));
    }

    [Fact]
    public void ATrackIsNotShownAfterItsLastFrame()
    {
        SceneTrack track = Flight();

        Assert.True(track.ShownAt(10));
        Assert.False(track.ShownAt(10.001));
    }

    [Fact]
    public void ATrackWhoseFirstFramesAreNotPositions_IsShownFromItsFirstPosition()
    {
        SceneTrack track = Jfs2ImportTests.Track(true, "N1", "me", "Cessna 172");
        track.Frames.Add(Jfs2ImportTests.Variables(1, new() { [1] = 1 }));
        track.Frames.Add(Jfs2ImportTests.Position(2, At(0.8)));
        track.Frames.Add(Jfs2ImportTests.Position(3, At(0.8)));

        Assert.False(track.ShownAt(1.5));
        Assert.True(track.ShownAt(2.5));
    }

    [Fact]
    public void ATrackWithNoPosition_IsNeverShown()
    {
        SceneTrack track = Jfs2ImportTests.Track(true, "N1", "me", "Cessna 172");
        Assert.False(track.ShownAt(1));

        track.Frames.Add(Jfs2ImportTests.Variables(1, new() { [1] = 1 }));
        Assert.False(track.ShownAt(0.5));
        Assert.False(track.ShownAt(1));
    }

    [Fact]
    public void IdentityChanges_SurviveASave()
    {
        string path = Path.Combine(folder, "a.jfs2");

        Jfs2SceneWriter.Write(path, [Flight()], "JoinFS test", DateTime.UtcNow, _ => { });

        SceneTrack back;
        using (FileStream stream = File.OpenRead(path))
        {
            back = Assert.Single(Jfs2Scene.Load(stream).Tracks);
        }
        Assert.Equal("N1", back.Identity.Callsign);
        var change = Assert.Single(back.Frames.OfType<SceneIdentityFrame>());
        Assert.Equal((5.0, "DLH1"), (change.Time, change.Identity.Callsign));
        Assert.Equal("DLH1", back.StateAt(10).Identity.Callsign);
    }

    [Fact]
    public void ACopy_HasItsOwnFrameList()
    {
        SceneTrack track = Flight();

        SceneTrack copy = track.Copy();
        track.Frames.Clear();

        Assert.Equal(Flight().Frames.Count, copy.Frames.Count);
        Assert.Equal(("me", "N1"), (copy.Nickname, copy.Identity.Callsign));
    }
}
