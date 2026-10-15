using JoinFS.Net;
using JoinFS.Recording;

namespace JoinFS.Tests.Recording;

/// <summary>
/// Editing on the scene (player slice 3): a trim at the start puts the state at the cut first, as a keyframe would (README limit 12), a
/// trim at the end keeps what came before, and an object with nothing left is reported gone. The recorder's trims and Add are these.
/// </summary>
public sealed class SceneEditTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("jfs2-edit-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(folder, true); } catch (IOException) { }
    }

    static SceneTrack Flight() => SceneStateTests.Flight();

    [Fact]
    public void ATrimAtTheStart_PutsTheStateAtTheCutFirst()
    {
        SceneTrack track = Flight();

        Assert.True(track.TrimStart(5.5));

        // the identity changed at 5 s: the track opens with it now
        Assert.Equal("DLH1", track.Identity.Callsign);
        Assert.Empty(track.Frames.OfType<SceneIdentityFrame>());
        // then the variables merged and the latest position, at 0
        var variables = Assert.IsType<SceneVariablesFrame>(track.Frames[0]);
        var (integers, floats, strings) = variables.ByKind();
        Assert.Equal(0.0, variables.Time);
        Assert.Equal(new Dictionary<uint, int> { [1] = 0, [2] = 2 }, integers);
        Assert.Equal(new Dictionary<uint, float> { [3] = 0.5f }, floats);
        Assert.Null(strings);
        var position = Assert.IsType<ScenePositionFrame>(track.Frames[1]);
        Assert.Equal(0.0, position.Time);
        Assert.Equal(Flight().Frames.OfType<ScenePositionFrame>().Single(p => p.Time == 5.0).Position.Latitude, position.Position.Latitude);
        // the rest starts at the cut
        Assert.Equal(0.5, track.Frames[2].Time);
        Assert.Equal(1.5, Assert.Single(track.Frames.OfType<SceneEventFrame>()).Time);
    }

    [Fact]
    public void ATrimAtAPosition_DoesNotRepeatIt()
    {
        SceneTrack track = Flight();

        Assert.True(track.TrimStart(5.0));

        // the position at 5 s is kept as it is; the identity change at 5 s is still to play
        Assert.Single(track.Frames, frame => frame is ScenePositionFrame && frame.Time == 0.0);
        Assert.Equal("N1", track.Identity.Callsign);
        Assert.Equal("DLH1", Assert.Single(track.Frames.OfType<SceneIdentityFrame>()).Identity.Callsign);
    }

    [Fact]
    public void ATrimBeforeTheFirstFrame_OnlyMovesTheFrames()
    {
        SceneTrack track = Flight();
        track.Shift(2.0);

        Assert.True(track.TrimStart(1.0));

        Assert.Equal(Flight().Frames.Count, track.Frames.Count);
        Assert.Equal(1.0, track.Frames[0].Time);
    }

    [Fact]
    public void ATrimAtTheStartPastTheEnd_LeavesTheTrackAndSaysItIsGone()
    {
        SceneTrack track = Flight();

        Assert.False(track.TrimStart(10.5));

        Assert.Equal(Flight().Frames.Count, track.Frames.Count);
        Assert.Equal("N1", track.Identity.Callsign);
    }

    [Fact]
    public void ATrimAtTheEnd_KeepsWhatCameBefore()
    {
        SceneTrack track = Flight();

        Assert.True(track.TrimEnd(5.0));

        Assert.True(track.Frames.TrueForAll(frame => frame.Time < 5.0));
        Assert.Equal(4.0, track.Frames[^1].Time);
    }

    [Fact]
    public void ATrimAtTheEndBeforeTheObjectCame_SaysItIsGone()
    {
        SceneTrack track = Flight();
        track.Shift(3.0);

        Assert.False(track.TrimEnd(2.0));
    }

    [Fact]
    public void ATrimmedTrack_SavesWithItsStateAtTheStart()
    {
        SceneTrack track = Flight();
        track.TrimStart(5.5);
        string path = Path.Combine(folder, "trimmed.jfs2");

        Jfs2SceneWriter.Write(path, [track], "JoinFS test", DateTime.UtcNow, _ => { });

        SceneTrack back;
        using (FileStream stream = File.OpenRead(path))
        {
            back = Assert.Single(Jfs2Scene.Load(stream).Tracks);
        }
        Assert.Equal("DLH1", back.Identity.Callsign);
        SceneState state = back.StateAt(0.25);
        Assert.Equal(new Dictionary<uint, int> { [1] = 0, [2] = 2 }, state.Integers);
        Assert.Equal(0.0, state.Position!.Time);
        Assert.Equal("N123", back.StateAt(1.0).Strings![4]);
    }
}
