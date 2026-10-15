using JoinFS.Recording;

namespace JoinFS.Tests.Recording;

/// <summary>
/// The player gives an object the variables played so far again every 0.2 s (README limit 17, the field test of 2026-10-15: a Bonanza
/// whose gear went down once, 0.85 s after it appeared, taxied with its gear up, because the simulator refuses values for a few seconds
/// after it has created an aircraft, and the file holds changes only).
/// </summary>
public sealed class PlayedVariablesTests
{
    [Fact]
    public void TheVariablesPlayedSoFar_AreMerged()
    {
        var played = new PlayedVariables();
        played.Merge(Jfs2ImportTests.Variables(0, new() { [1] = 1, [2] = 0 }, new() { [3] = 0.5f }));
        played.Merge(Jfs2ImportTests.Variables(1, new() { [2] = 2 }, null, new() { [4] = "N1" }));

        Assert.Equal(new Dictionary<uint, int> { [1] = 1, [2] = 2 }, played.Integers);
        Assert.Equal(new Dictionary<uint, float> { [3] = 0.5f }, played.Floats);
        Assert.Equal(new Dictionary<uint, string> { [4] = "N1" }, played.Strings);
    }

    [Fact]
    public void TheSetIsDueAtOnce_ThenEveryInterval()
    {
        var played = new PlayedVariables();
        played.Merge(Jfs2ImportTests.Variables(0, new() { [1] = 1 }));

        Assert.True(played.Due(10.0));
        Assert.False(played.Due(10.1));
        Assert.True(played.Due(10.0 + PlayedVariables.Interval));
        Assert.False(played.Due(10.3));
    }

    [Fact]
    public void NothingPlayed_IsNeverDue()
    {
        var played = new PlayedVariables();

        Assert.False(played.Due(10.0));
        Assert.Null(played.Integers);
    }

    [Fact]
    public void AJump_SetsTheStateOfThatTime_WithoutChangingTheTrack()
    {
        SceneTrack track = SceneStateTests.Flight();
        var played = new PlayedVariables();
        played.Merge(Jfs2ImportTests.Variables(9, new() { [99] = 9 }));

        SceneState state = track.StateAt(5.5);
        played.Set(state);
        played.Integers![1] = 7;

        // the gear (1) went up at 4 and flaps (2) to 2 at 3; what was played before the jump is gone
        Assert.Equal(new Dictionary<uint, int> { [1] = 7, [2] = 2 }, played.Integers);
        Assert.Equal(0, state.Integers![1]);
        Assert.Equal(0, track.StateAt(5.5).Integers![1]);
    }

    [Fact]
    public void PlayingAgain_StartsWithNothing()
    {
        var played = new PlayedVariables();
        played.Merge(Jfs2ImportTests.Variables(0, new() { [1] = 1 }));
        Assert.True(played.Due(1.0));

        played.Clear();

        Assert.Null(played.Integers);
        Assert.False(played.Due(1.05));
    }
}
