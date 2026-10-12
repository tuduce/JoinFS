namespace JoinFS.Recording.Format;

public sealed record RepairResult(bool WasFinished, long BytesDropped, int TracksClosed, int Chunks, IReadOnlyList<Jfs2Damage> Damage);

/// <summary>Completes an unfinished file in place (spec 8.3).</summary>
public static class RecordingRepair
{
    /// <summary>
    /// Truncates after the last valid block, closes every track still open, and writes the Index and Footer.
    /// A valid block is never changed; at most the last chunk is lost. A finished file is left alone.
    /// </summary>
    /// <exception cref="Jfs2FormatException">The file is not JFS2 or its header is damaged: there is nothing to repair from.</exception>
    public static RepairResult Repair(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        return Repair(stream);
    }

    /// <summary>The stream must be readable, writable and seekable.</summary>
    public static RepairResult Repair(Stream stream)
    {
        var model = RecordingModel.Load(stream);
        if (model.IsFinished)
            return new RepairResult(true, 0, 0, model.Chunks.Count, model.Damage);

        long dropped = stream.Length - model.ValidEnd;
        int open = model.Tracks.Count(t => t.IsOpen);
        using var writer = RecordingWriter.Resume(stream, model);
        writer.Finish();
        return new RepairResult(false, dropped, open, writer.ChunkCount, model.Damage);
    }
}
