using JoinFS.Recording;

namespace JoinFS.Tests;

/// <summary>
/// Pins the recorder rules the hotkeys (#155) depend on: which actions are allowed in which state,
/// overdub-on-empty being a plain record, and the dialog-free auto-save of an unsaved recording.
/// The helpers are static/pure so they run without a Main or a simulator.
/// </summary>
public class RecorderAutoSaveTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "joinfs-autosave-" + Guid.NewGuid().ToString("N"));

    public RecorderAutoSaveTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, true);

    static readonly DateTime Moment = new(2026, 10, 4, 7, 5, 9);

    // ---- file name ----

    [Fact]
    public void FileName_UsesDateTimeAndCallsign()
    {
        Assert.Equal("2026-10-04_070509_DLH123.jfs2", Recorder.BuildAutoSaveFileName(Moment, "DLH123"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FileName_FallsBackToRecording_WithoutCallsign(string? callsign)
    {
        Assert.Equal("2026-10-04_070509_recording.jfs2", Recorder.BuildAutoSaveFileName(Moment, callsign));
    }

    [Fact]
    public void FileName_ReplacesInvalidCharacters()
    {
        string name = Recorder.BuildAutoSaveFileName(Moment, "D/ALEX:1 *?");

        Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()));
        Assert.StartsWith("2026-10-04_070509_", name);
        Assert.EndsWith(".jfs2", name);
    }

    [Fact]
    public void FirstCallsign_IsTakenFromTheFirstAircraft()
    {
        var objects = new List<Recorder.Obj>
        {
            new Recorder.Obj("", "", "", "", 0, default(Sim.Obj.Owner)),
            new Recorder.Aircraft(true, "DLH1", "a", "", "", "", "", 0, default(Sim.Obj.Owner)),
            new Recorder.Aircraft(true, "DLH2", "b", "", "", "", "", 0, default(Sim.Obj.Owner)),
        };

        Assert.Equal("DLH1", Recorder.FirstCallsign(objects));
    }

    [Fact]
    public void FirstCallsign_IsEmpty_WithoutAircraft()
    {
        Assert.Equal("", Recorder.FirstCallsign([]));
    }

    // ---- unique path ----

    [Fact]
    public void UniquePath_ReturnsTheNameWhenFree()
    {
        Assert.Equal(Path.Combine(folder, "a.jfs2"), Recorder.UniquePath(folder, "a.jfs2"));
    }

    [Fact]
    public void UniquePath_AppendsCounter_AndNeverOverwrites()
    {
        File.WriteAllText(Path.Combine(folder, "a.jfs2"), "x");
        File.WriteAllText(Path.Combine(folder, "a-2.jfs2"), "x");

        Assert.Equal(Path.Combine(folder, "a-3.jfs2"), Recorder.UniquePath(folder, "a.jfs2"));
    }

    // ---- writing ----

    [Fact]
    public void AutoSave_WritesAJfs2FileThatPlaysBack()
    {
        var recorder = new Recorder(null!);
        var aircraft = new Recorder.Aircraft(true, "DLH1", "a", "", "", "", "", 0, default(Sim.Obj.Owner));
        aircraft.frames.Add(new Recorder.AircraftPositionFrame(1.0, ref Position));
        aircraft.frames.Add(new Recorder.AircraftPositionFrame(2.0, ref Position));

        string path = recorder.AutoSave(folder, [aircraft], Moment);

        Assert.Equal(Path.Combine(folder, "2026-10-04_070509_DLH1.jfs2"), path);
        Assert.Equal([path], Directory.GetFiles(folder));
        var loaded = Jfs2Loader.Load(new MemoryStream(File.ReadAllBytes(path)));
        var back = Assert.IsType<Recorder.Aircraft>(Assert.Single(loaded.Objects));
        Assert.Equal("DLH1", back.callsign);
        Assert.Equal(2, back.frames.OfType<Recorder.AircraftPositionFrame>().Count());
    }

    static Sim.AircraftPosition Position = new() { latitude = 47, longitude = 8, altitude = 500 };

    [Fact]
    public void SaveJfs2_ReplacesAnExistingFileOnlyWhenAsked()
    {
        var recorder = new Recorder(null!);
        string path = Path.Combine(folder, "x.jfs2");
        File.WriteAllText(path, "old");

        Assert.ThrowsAny<IOException>(() => recorder.SaveJfs2(path, [], Moment, false));
        Assert.Equal("old", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(folder));

        recorder.SaveJfs2(path, [], Moment, true);
        Assert.NotEqual("old", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(folder));
    }

    [Fact]
    public void AutoSave_Throws_WhenFolderIsMissing_SoTheCallerKeepsTheRecording()
    {
        var recorder = new Recorder(null!);

        Assert.ThrowsAny<IOException>(() => recorder.AutoSave(Path.Combine(folder, "missing"), [], Moment));
    }

    [Fact]
    public void SaveJfs2_LeavesNothingBehind_WhenWritingFails()
    {
        var recorder = new Recorder(null!);
        string path = Path.Combine(folder, "y.jfs2");
        File.WriteAllText(path, "old");

        Assert.ThrowsAny<Exception>(() => recorder.SaveJfs2(path, [null!], Moment, true));

        Assert.Equal("old", File.ReadAllText(path)); // the file being replaced survives
        Assert.Equal([path], Directory.GetFiles(folder));
    }

    // ---- what each action is allowed to do ----

    [Theory]
    //        recording playing empty  record overdub play  stop
    [InlineData(false, false, true,  true,  false, false, false)]
    [InlineData(false, false, false, true,  true,  true,  false)]
    [InlineData(true,  false, false, false, false, false, true)]
    [InlineData(false, true,  false, false, true,  true,  true)]
    public void ActionRules_MatchTheRecorderButtons(bool recording, bool playing, bool empty, bool record, bool overdub, bool play, bool stop)
    {
        Assert.Equal(record, Recorder.CanRecord(recording, playing));
        Assert.Equal(overdub, Recorder.CanOverdub(recording, empty));
        Assert.Equal(play, Recorder.CanPlay(recording, empty));
        Assert.Equal(stop, Recorder.CanStop(recording, playing));
    }

    // ---- record and overdub hotkeys while recording ----

    [Theory]
    [InlineData(true, true)]   // running recording: stop (and auto-save) before starting a new one
    [InlineData(false, false)] // idle or replaying: nothing to stop first
    public void Hotkey_StopsARunningRecordingFirst(bool recording, bool expected)
    {
        Assert.Equal(expected, Recorder.HotkeyRestartsRecording(recording));
    }

    // ---- overdub on an empty recorder ----

    [Fact]
    public void Overdub_OnEmptyRecorder_IsARecord()
    {
        Assert.False(Recorder.ResolveOverdub(requested: true, empty: true));
    }

    [Fact]
    public void Overdub_OnExistingTrack_StaysOverdub()
    {
        Assert.True(Recorder.ResolveOverdub(requested: true, empty: false));
    }

    [Fact]
    public void Record_StaysRecord()
    {
        Assert.False(Recorder.ResolveOverdub(requested: false, empty: false));
        Assert.False(Recorder.ResolveOverdub(requested: false, empty: true));
    }
}
