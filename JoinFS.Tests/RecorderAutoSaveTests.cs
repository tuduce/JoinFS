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
        Assert.Equal("2026-10-04_070509_DLH123.jfs", Recorder.BuildAutoSaveFileName(Moment, "DLH123"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FileName_FallsBackToRecording_WithoutCallsign(string? callsign)
    {
        Assert.Equal("2026-10-04_070509_recording.jfs", Recorder.BuildAutoSaveFileName(Moment, callsign));
    }

    [Fact]
    public void FileName_ReplacesInvalidCharacters()
    {
        string name = Recorder.BuildAutoSaveFileName(Moment, "D/ALEX:1 *?");

        Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()));
        Assert.StartsWith("2026-10-04_070509_", name);
        Assert.EndsWith(".jfs", name);
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
        Assert.Equal(Path.Combine(folder, "a.jfs"), Recorder.UniquePath(folder, "a.jfs"));
    }

    [Fact]
    public void UniquePath_AppendsCounter_AndNeverOverwrites()
    {
        File.WriteAllText(Path.Combine(folder, "a.jfs"), "x");
        File.WriteAllText(Path.Combine(folder, "a-2.jfs"), "x");

        Assert.Equal(Path.Combine(folder, "a-3.jfs"), Recorder.UniquePath(folder, "a.jfs"));
    }

    // ---- writing ----

    [Fact]
    public void AutoSave_WritesARecordingFileWithVersionHeader()
    {
        var recorder = new Recorder(null!);
        var objects = new List<Recorder.Obj>
        {
            new Recorder.Aircraft(true, "DLH1", "a", "", "", "", "", 0, default(Sim.Obj.Owner)),
        };

        string path = recorder.AutoSave(folder, objects, Moment);

        Assert.Equal(Path.Combine(folder, "2026-10-04_070509_DLH1.jfs"), path);
        using var reader = new BinaryReader(File.OpenRead(path));
        Assert.Equal(Recorder.FileVersion, reader.ReadInt16());
        Assert.Equal(1, reader.ReadInt32());
    }

    [Fact]
    public void AutoSave_Throws_WhenFolderIsMissing_SoTheCallerKeepsTheRecording()
    {
        var recorder = new Recorder(null!);

        Assert.ThrowsAny<IOException>(() => recorder.AutoSave(Path.Combine(folder, "missing"), [], Moment));
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
