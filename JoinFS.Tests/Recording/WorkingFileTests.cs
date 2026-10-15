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
}
