using JoinFS.Recording;

namespace JoinFS.Tests.Recording;

/// <summary>
/// What a pass of a recording does with each sim object (README limit 7, design 5.3): a flagged object gets a track, and one that is
/// unflagged while it is recorded has its track ended, so flagging it again starts a new one.
/// </summary>
public sealed class RecordingStepTests
{
    static Recorder.Obj Recorded(bool playing = false) => new(new SceneTrack { Identity = Jfs2ImportTests.Track(true, "N1", "me", "Cessna 172").Identity }) { playing = playing };

    [Fact]
    public void AFlaggedObjectNotRecorded_StartsATrack()
    {
        Assert.Equal(Recorder.Step.Start, Recorder.RecordingStep(true, null));
    }

    [Fact]
    public void AFlaggedObjectRecorded_GoesOn()
    {
        Assert.Equal(Recorder.Step.None, Recorder.RecordingStep(true, Recorded()));
    }

    [Fact]
    public void AnObjectUnflaggedWhileRecorded_EndsItsTrack()
    {
        Assert.Equal(Recorder.Step.End, Recorder.RecordingStep(false, Recorded()));
    }

    [Fact]
    public void AnObjectNeitherFlaggedNorRecorded_IsLeftAlone()
    {
        Assert.Equal(Recorder.Step.None, Recorder.RecordingStep(false, null));
    }

    [Fact]
    public void AnObjectLinkedToOneThatPlays_IsNotARecordingOfIt()
    {
        Assert.Equal(Recorder.Step.None, Recorder.RecordingStep(false, Recorded(playing: true)));
    }
}
