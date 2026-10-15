namespace JoinFS.Recording.Format.Tests;

public class WriterRuleTests
{
    static (RecordingWriter w, uint open, uint close, uint pos) NewWriter()
    {
        var w = RecordingWriter.Create(new MemoryStream(), SampleFile.Header(), leaveOpen: true);
        return (w, w.BindKind("track-open", 1), w.BindKind("track-close", 1), w.BindKind("position", 1));
    }

    static readonly byte[] Some = { 1, 2, 3 };

    [Fact]
    public void BindKind_is_idempotent_and_keeps_versions_apart()
    {
        var (w, open, _, pos) = NewWriter();
        Assert.Equal(open, w.BindKind("track-open", 1));
        Assert.NotEqual(pos, w.BindKind("position", 2));
        Assert.Throws<ArgumentException>(() => w.BindKind("", 1));
        Assert.Throws<ArgumentException>(() => w.BindKind("x", 0));
    }

    [Fact]
    public void An_unbound_kind_is_refused()
    {
        var (w, _, _, _) = NewWriter();
        Assert.Throws<ArgumentException>(() => w.Add(99, 1, 0, Some));
    }

    [Fact]
    public void A_track_starts_with_track_open_ends_with_track_close_and_is_never_reused()
    {
        var (w, open, close, pos) = NewWriter();
        Assert.Throws<InvalidOperationException>(() => w.Add(pos, 1, 0, Some)); // not opened
        w.Add(open, 1, 0, Some);
        Assert.Throws<InvalidOperationException>(() => w.Add(open, 1, 1, Some)); // opened twice
        w.Add(pos, 1, 5, Some);
        w.Add(close, 1, 6, ReadOnlySpan<byte>.Empty);
        Assert.Throws<InvalidOperationException>(() => w.Add(pos, 1, 7, Some)); // after close
        Assert.Throws<InvalidOperationException>(() => w.Add(open, 1, 7, Some)); // id reused
        Assert.Throws<ArgumentException>(() => w.Add(open, 0, 7, Some)); // track 0 cannot open
    }

    [Fact]
    public void Times_of_a_track_do_not_decrease_but_tracks_may_skew()
    {
        var (w, open, _, pos) = NewWriter();
        w.Add(open, 1, 0, Some);
        w.Add(open, 2, 0, Some);
        w.Add(pos, 1, 100, Some);
        w.Add(pos, 2, 50, Some); // another track, earlier: allowed
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Add(pos, 1, 99, Some));
    }

    [Fact]
    public void A_record_may_not_precede_the_previous_chunks_tmin()
    {
        var (w, open, _, pos) = NewWriter();
        w.Add(open, 1, 1000, Some);
        w.Add(open, 2, 1000, Some);
        w.EndChunk();
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Add(pos, 2, 999, Some));
        w.Add(pos, 2, 1000, Some);
    }

    [Fact]
    public void Snapshots_go_first_in_a_keyframe_chunk_and_only_for_open_tracks()
    {
        var (w, open, close, pos) = NewWriter();
        w.Add(open, 1, 0, Some);
        w.Add(open, 2, 0, Some);
        w.Add(close, 2, 1, ReadOnlySpan<byte>.Empty);
        w.EndChunk();
        Assert.Throws<InvalidOperationException>(() => w.AddSnapshot(pos, 1, 0, Some)); // no chunk started
        w.StartKeyframeChunk();
        Assert.Throws<InvalidOperationException>(() => w.AddSnapshot(pos, 2, 0, Some)); // closed
        Assert.Throws<InvalidOperationException>(() => w.AddSnapshot(pos, 9, 0, Some)); // unknown
        w.AddSnapshot(pos, 1, 0, Some);
        w.Add(pos, 1, 5, Some);
        Assert.Throws<InvalidOperationException>(() => w.AddSnapshot(pos, 1, 0, Some)); // after a record
    }

    [Fact]
    public void A_chunk_with_no_records_is_not_written()
    {
        var (w, open, _, pos) = NewWriter();
        w.Add(open, 1, 0, Some);
        w.EndChunk();
        long before = w.Position;
        w.StartKeyframeChunk();
        w.AddSnapshot(pos, 1, 0, Some);
        w.EndChunk();
        Assert.Equal(before, w.Position);
        Assert.Equal(1, w.ChunkCount);
    }

    [Fact]
    public void A_finished_file_takes_no_more()
    {
        var (w, open, _, _) = NewWriter();
        w.Finish();
        w.Finish(); // harmless
        Assert.Throws<InvalidOperationException>(() => w.Add(open, 1, 0, Some));
    }

    [Fact]
    public void Header_metadata_keys_must_be_unique_lower_case()
    {
        static RecordingHeader With(params string[] keys) => new()
        {
            Metadata = keys.Select(k => new KeyValuePair<string, string>(k, "v")).ToArray(),
        };
        Assert.Throws<ArgumentException>(() => RecordingWriter.Create(new MemoryStream(), With("App"), leaveOpen: true));
        Assert.Throws<ArgumentException>(() => RecordingWriter.Create(new MemoryStream(), With("app", "app"), leaveOpen: true));
    }

    [Fact]
    public void Every_block_is_one_write()
    {
        var counting = new CountingStream();
        var w = RecordingWriter.Create(counting, SampleFile.Header(), leaveOpen: true);
        Assert.Equal(1, counting.Writes); // magic and header together
        uint open = w.BindKind("track-open", 1);
        Assert.Equal(2, counting.Writes);
        w.Add(open, 1, 0, Some);
        w.Add(open, 2, 0, Some);
        Assert.Equal(2, counting.Writes); // nothing reaches the stream until the chunk ends
        w.EndChunk();
        Assert.Equal(3, counting.Writes);
        w.Finish();
        Assert.Equal(6, counting.Writes); // track-close schema, close chunk, index+footer
    }

    sealed class CountingStream : MemoryStream
    {
        public int Writes;
        public override void Write(byte[] buffer, int offset, int count)
        {
            Writes++;
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Writes++;
            base.Write(buffer);
        }
    }
}
