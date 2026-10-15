using JoinFS.Net;
using JoinFS.Recording;

namespace JoinFS.Tests.Recording;

/// <summary>
/// The file the recorder keeps while a recording is unsaved (docs/jfs2/README.md, stage 5): it goes when the recording is saved or given up,
/// and what is left at the next start is offered back.
/// </summary>
public sealed class WorkingFileTests : IDisposable
{
    readonly string documents = Directory.CreateTempSubdirectory("jfs2-working-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(documents, true); } catch (IOException) { }
    }

    string Working(string name)
    {
        string folder = Recorder.WorkingFolder(documents);
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, name);
    }

    static List<SceneTrack> OneAircraft() => [Jfs2ImportTests.Track(true, "DLH1", "a", "")];

    [Fact]
    public void WithoutAWorkingFolder_NothingIsLeft()
    {
        Assert.Empty(new Recorder(null!).FindUnsavedWorkingFiles(documents));
    }

    [Fact]
    public void LeftFiles_AreListedOldestFirst_AndOtherFilesAreNot()
    {
        string newer = Working("b.jfs2"), older = Working("a.jfs2");
        File.WriteAllText(newer, "x");
        File.WriteAllText(older, "x");
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 10, 1));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 10, 2));
        File.WriteAllText(Working("notes.txt"), "x");

        Assert.Equal([older, newer], new Recorder(null!).FindUnsavedWorkingFiles(documents));
    }

    [Fact]
    public void AFileThatIsBeingWritten_IsNotOffered()
    {
        string path = Working("live.jfs2");
        // the capture keeps its file open for reading by others only, as another JoinFS on this machine would
        using var writing = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);

        Assert.Empty(new Recorder(null!).FindUnsavedWorkingFiles(documents));
    }

    [Fact]
    public void ARecoveredRecording_KeepsItsFileUntilItIsSavedOrGivenUp()
    {
        string path = Working("lost.jfs2");
        File.WriteAllText(path, "x");
        var recorder = new Recorder(null!);

        recorder.Load(OneAircraft(), false, path);
        Assert.Empty(recorder.FindUnsavedWorkingFiles(documents)); // it is the recording being held, not a leftover
        Assert.True(File.Exists(path));

        recorder.DiscardWorkingFile();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void LoadingAnotherRecording_ReleasesTheFileOfTheOneItReplaces()
    {
        string path = Working("lost.jfs2");
        File.WriteAllText(path, "x");
        var recorder = new Recorder(null!);
        recorder.Load(OneAircraft(), false, path);

        recorder.Load(OneAircraft(), false);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ARecordingThatWasOpenedFromAFile_HasNothingToRelease()
    {
        string other = Working("other.jfs2");
        File.WriteAllText(other, "x");
        var recorder = new Recorder(null!);

        recorder.Load(OneAircraft(), false);
        recorder.DiscardWorkingFile();

        Assert.True(File.Exists(other)); // not this recording's, so not touched
    }

    /// <summary>A real JFS2 file of one aircraft with positions from <paramref name="from"/> to <paramref name="to"/> seconds</summary>
    static void WriteRecording(string path, string callsign, double from, double to, string? baseFile = null)
    {
        var capture = Jfs2Capture.Create(path, "JoinFS test", "", new DateTime(2026, 10, 15, 18, 0, 0, DateTimeKind.Utc), _ => { },
            baseFile == null ? null : [new(Recorder.BaseKey, baseFile)]);
        SceneTrack track = Jfs2ImportTests.Track(true, callsign, "me", "Cessna");
        capture.OpenTrack(1, from, track.Owner, "me", "x", "", track.Identity);
        for (double t = from; t <= to + 1e-9; t += 0.5)
        {
            var position = new PositionUpdate { Latitude = 47 + t * 0.001, Longitude = 8, Altitude = 1000, StaticCgToGround = float.NaN };
            capture.Position(1, t, ref position);
        }
        capture.Finish();
    }

    static List<SceneTrack> Read(string path, List<string>? warnings = null)
    {
        using FileStream stream = File.OpenRead(path);
        return new Recorder(null!).Parse(new BinaryReader(stream), warnings)!;
    }

    [Fact]
    public void SavingAnUneditedRecording_MovesItsWorkingFile()
    {
        string working = Working("take.jfs2");
        WriteRecording(working, "DLH1", 0, 10);
        byte[] recorded = File.ReadAllBytes(working);
        string target = Path.Combine(documents, "saved.jfs2");

        new Recorder(null!).SaveJfs2(target, new Recorder.SaveCopy([], working), DateTime.Now, false);

        Assert.False(File.Exists(working));
        Assert.Equal(recorded, File.ReadAllBytes(target));
    }

    [Fact]
    public void AnOverdub_IsReadOverTheRecordingItAddsTo_AtItsOwnTimes()
    {
        string recording = Working("take.jfs2"), overdub = Working("overdub.jfs2");
        WriteRecording(recording, "DLH1", 0, 10);
        WriteRecording(overdub, "DLH2", 3, 6, recording);

        List<string> warnings = [];
        List<SceneTrack> tracks = Read(overdub, warnings);

        Assert.Empty(warnings);
        Assert.Equal(["DLH1", "DLH2"], tracks.Select(t => t.Identity.Callsign));
        Assert.Equal(3.0, tracks[1].Frames.OfType<ScenePositionFrame>().First().Time, 6);
    }

    [Fact]
    public void AnOverdubWhoseRecordingIsGone_IsReadAlone_AndSaysSo()
    {
        string overdub = Working("overdub.jfs2");
        WriteRecording(overdub, "DLH2", 3, 6, Working("gone.jfs2"));

        List<string> warnings = [];
        List<SceneTrack> tracks = Read(overdub, warnings);

        Assert.Equal(["DLH2"], tracks.Select(t => t.Identity.Callsign));
        Assert.Contains("gone.jfs2", Assert.Single(warnings));
    }

    [Fact]
    public void RecoveringAnOverdub_TakesOverTheWorkingFilesUnderIt_ButNotASavedRecording()
    {
        string saved = Path.Combine(documents, "saved.jfs2");
        string first = Working("first.jfs2"), second = Working("second.jfs2"), third = Working("third.jfs2");
        WriteRecording(saved, "DLH0", 0, 10);
        WriteRecording(first, "DLH1", 1, 2, saved);
        WriteRecording(second, "DLH2", 3, 4, first);
        WriteRecording(third, "DLH3", 5, 6, second);
        Assert.Equal([first, second, third], Recorder.WorkingFileChain(third));
        var recorder = new Recorder(null!);

        recorder.Load(Read(third), false, third);
        Assert.Equal(4, recorder.CopyForSave().Tracks.Count);
        Assert.Empty(recorder.FindUnsavedWorkingFiles(documents));
        // what is loaded is written on Save, not moved: it is more than one file
        Assert.Null(recorder.CopyForSave().File);

        recorder.DiscardWorkingFile();
        Assert.False(File.Exists(first) || File.Exists(second) || File.Exists(third));
        Assert.True(File.Exists(saved));
    }
}
