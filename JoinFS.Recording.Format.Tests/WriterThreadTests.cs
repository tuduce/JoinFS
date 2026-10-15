namespace JoinFS.Recording.Format.Tests;

/// <summary>
/// The writer thread and the flush to disk (spec 9.1 and 9.2, README limit 5): the caller's thread encodes chunks, the writer thread
/// deflates and writes them, so the disk never blocks the caller; the file is the same either way.
/// </summary>
public class WriterThreadTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("jfs2-writer-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(folder, true); } catch (IOException) { }
    }

    static readonly byte[] Some = { 1, 2, 3 };

    [Theory]
    [InlineData(ChunkCompression.None, true)]
    [InlineData(ChunkCompression.Deflate, true)]
    [InlineData(ChunkCompression.Deflate, false)]
    public void A_file_written_on_a_writer_thread_has_the_same_bytes(ChunkCompression compression, bool finish)
    {
        Assert.Equal(SampleFile.Build(compression, finish).Bytes, SampleFile.Build(compression, finish, writerThread: true).Bytes);
    }

    [Fact]
    public void A_disk_that_stalls_does_not_block_the_caller()
    {
        var stream = new GatedStream();
        var w = RecordingWriter.Create(stream, SampleFile.Header(), new RecordingWriterOptions { WriterThread = true });
        uint open = w.BindKind("track-open", 1);
        uint pos = w.BindKind("position", 1);
        w.Add(open, 1, 0, Some);
        w.Add(pos, 1, 0, Some);

        // the writer thread is stuck in the first write; ending a chunk returns all the same
        Assert.True(stream.Writing.Wait(TimeSpan.FromSeconds(5)));
        w.EndChunk();
        w.Add(pos, 1, 3_000_000, Some);
        w.EndChunk();
        Assert.Equal(0, w.ChunkCount);

        stream.Gate.Set();
        w.Finish();
        w.Dispose();
        // and Finish's chunk that closes the track
        Assert.Equal(3, RecordingReader.Open(new MemoryStream(stream.ToArray())).Chunks.Count);
    }

    [Fact]
    public void A_write_that_fails_on_the_writer_thread_is_thrown_by_a_later_call()
    {
        var stream = new FailingStream { FailAfter = 3 };
        var w = RecordingWriter.Create(stream, SampleFile.Header(), new RecordingWriterOptions { WriterThread = true });
        uint open = w.BindKind("track-open", 1);
        uint pos = w.BindKind("position", 1);
        w.Add(open, 1, 0, Some);
        w.Add(pos, 1, 0, Some);
        w.EndChunk();

        var thrown = Assert.Throws<IOException>(() => w.Finish());
        Assert.Contains("disk full", thrown.Message);
        Assert.Throws<IOException>(() => w.Add(pos, 1, 1, Some));
        w.Dispose();
    }

    [Fact]
    public void Dispose_without_Finish_leaves_the_chunks_handed_to_the_writer_thread_in_the_file()
    {
        string path = Path.Combine(folder, "a.jfs2");
        var w = RecordingWriter.Create(path, SampleFile.Header(), new RecordingWriterOptions { WriterThread = true, Compression = ChunkCompression.Deflate });
        uint open = w.BindKind("track-open", 1);
        uint pos = w.BindKind("position", 1);
        w.Add(open, 1, 0, Some);
        for (int i = 0; i < 5; i++)
        {
            w.Add(pos, 1, i * 1_000_000L, SampleFile.Body(i, 60));
            w.EndChunk();
        }
        w.Dispose();

        using var r = RecordingReader.Open(path);
        Assert.False(r.IsFinished);
        Assert.Equal(5, r.Chunks.Count);
    }

    [Fact]
    public void The_file_is_asked_to_reach_the_disk_every_interval_and_when_finished()
    {
        string path = Path.Combine(folder, "b.jfs2");
        var stream = new SyncCountingStream(path);
        var w = RecordingWriter.Create(stream, SampleFile.Header(), new RecordingWriterOptions { WriterThread = true, SyncInterval = TimeSpan.Zero });
        uint open = w.BindKind("track-open", 1);
        uint pos = w.BindKind("position", 1);
        w.Add(open, 1, 0, Some);
        w.EndChunk();
        w.Add(pos, 1, 1, Some);
        w.EndChunk();
        w.Finish();
        w.Dispose();

        // a sync after each chunk (Finish's that closes the track too), one when finished
        Assert.Equal(4, stream.Syncs);
    }

    [Fact]
    public void Without_an_interval_the_file_is_asked_to_reach_the_disk_only_when_finished()
    {
        string path = Path.Combine(folder, "c.jfs2");
        var stream = new SyncCountingStream(path);
        var w = RecordingWriter.Create(stream, SampleFile.Header());
        uint open = w.BindKind("track-open", 1);
        w.Add(open, 1, 0, Some);
        w.EndChunk();
        w.Finish();
        w.Dispose();

        Assert.Equal(1, stream.Syncs);
    }

    /// <summary>Holds the first write until the gate opens.</summary>
    sealed class GatedStream : MemoryStream
    {
        public readonly ManualResetEventSlim Gate = new();
        public readonly ManualResetEventSlim Writing = new();

        public override void Write(byte[] buffer, int offset, int count)
        {
            Writing.Set();
            Gate.Wait();
            base.Write(buffer, offset, count);
        }
    }

    /// <summary>Fails every write after the first few.</summary>
    sealed class FailingStream : MemoryStream
    {
        public int FailAfter;
        int writes;

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (++writes > FailAfter)
                throw new IOException("disk full");
            base.Write(buffer, offset, count);
        }
    }

    sealed class SyncCountingStream(string path) : FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)
    {
        public int Syncs;

        public override void Flush(bool flushToDisk)
        {
            if (flushToDisk)
                Syncs++;
            base.Flush(flushToDisk);
        }
    }
}
