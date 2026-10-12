namespace JoinFS.Recording.Format.Tests;

public class RoundTripTests
{
    /// <summary>The bytes of the spec's Appendix A. Never regenerate this file to make a test pass.</summary>
    [Fact]
    public void Writer_produces_the_golden_example_byte_for_byte()
    {
        var ms = new MemoryStream();
        var header = new RecordingHeader
        {
            StartUtcMicroseconds = 1_790_000_000_000_000,
            Metadata = new[] { new KeyValuePair<string, string>("app", "JoinFS") },
        };
        using (var w = RecordingWriter.Create(ms, header, leaveOpen: true))
        {
            uint open = w.BindKind("track-open", 1);
            uint close = w.BindKind("track-close", 1);
            w.Add(open, 1, 0, new byte[] { 0, 1, 0x41, 0, 0 });
            w.Add(close, 1, 1_000_000, ReadOnlySpan<byte>.Empty);
            w.Finish();
        }
        var golden = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "example.jfs2"));
        Assert.Equal(221, golden.Length);
        Assert.Equal(Convert.ToHexString(golden), Convert.ToHexString(ms.ToArray()));
    }

    [Fact]
    public void Reader_reads_the_golden_example()
    {
        using var r = RecordingReader.Open(Path.Combine(AppContext.BaseDirectory, "Fixtures", "example.jfs2"));
        Assert.True(r.IsFinished);
        Assert.Empty(r.Damage);
        Assert.Equal("JoinFS", r.Header.GetMetadata("app"));
        Assert.Equal(1_790_000_000_000_000, r.Header.StartUtcMicroseconds);
        Assert.Equal(new[] { "track-open", "track-close" }, r.Kinds.OrderBy(k => k.Id).Select(k => k.Name));
        var chunk = Assert.Single(r.Chunks);
        Assert.Equal((92L, 0L, 1_000_000L, true), (chunk.Offset, chunk.TMin, chunk.TMax, chunk.IsKeyframe));
        var track = Assert.Single(r.Tracks);
        Assert.Equal((1u, 0L, 1_000_000L), (track.Id, track.FirstTime, track.LastTime));
        Assert.True(r.TryReadChunk(0, out var c));
        Assert.Equal(new[] { 0L, 1_000_000L }, c!.Records.Select(x => x.Time));
    }

    [Theory]
    [InlineData(ChunkCompression.None)]
    [InlineData(ChunkCompression.Deflate)]
    public void Records_round_trip(ChunkCompression compression)
    {
        var sample = SampleFile.Build(compression);
        using var r = RecordingReader.Open(new MemoryStream(sample.Bytes));

        Assert.True(r.IsFinished);
        Assert.Empty(r.Damage);
        Assert.Equal("hällö €", r.Header.GetMetadata("note"));
        // Finish() closes track 1, which the sample did not close itself: one more record in a fourth chunk.
        Assert.Equal(4, r.Chunks.Count);
        Assert.Equal(new[] { true, false, true, false }, r.Chunks.Select(c => c.IsKeyframe));

        var read = SampleFile.ReadAll(r);
        Assert.Equal(4, read.Count);
        for (int i = 0; i < 3; i++)
            Assert.Equal(sample.Chunks[i], read[i]);
        Assert.Equal(new[] { new Expected("track-close", 1, 300_000, "", false) }, read[3]);
    }

    [Fact]
    public void Index_lists_tracks_with_labels_and_times()
    {
        using var r = RecordingReader.Open(new MemoryStream(SampleFile.Build().Bytes));
        Assert.Equal(new[] { 1u, 2u }, r.Tracks.Select(t => t.Id));
        Assert.Equal(("TIGER1", "Piper Tiger"), (r.Tracks[0].Callsign, r.Tracks[0].Model));
        Assert.Equal((0L, 300_000L), (r.Tracks[0].FirstTime, r.Tracks[0].LastTime));
        Assert.Equal((50_000L, 300_000L), (r.Tracks[1].FirstTime, r.Tracks[1].LastTime));
        Assert.All(r.Tracks, t => Assert.False(t.IsOpen));
    }

    [Fact]
    public void Deflate_is_used_only_when_it_helps()
    {
        var plain = SampleFile.Build(ChunkCompression.None).Bytes.Length;
        var deflated = SampleFile.Build(ChunkCompression.Deflate).Bytes.Length;
        Assert.True(deflated < plain, $"{deflated} should be smaller than {plain}");
    }

    [Fact]
    public void FindKeyframeChunk_picks_the_last_keyframe_at_or_before_the_time()
    {
        using var r = RecordingReader.Open(new MemoryStream(SampleFile.Build().Bytes));
        // chunk TMins: 0 (key), 200_000, 290_000 (key), 300_000
        Assert.Equal(0, r.FindKeyframeChunk(-5));
        Assert.Equal(0, r.FindKeyframeChunk(0));
        Assert.Equal(0, r.FindKeyframeChunk(289_999));
        Assert.Equal(2, r.FindKeyframeChunk(290_000));
        Assert.Equal(2, r.FindKeyframeChunk(10_000_000));
    }

    [Fact]
    public void Attachment_and_metadata_survive_and_the_newest_attachment_wins()
    {
        var ms = new MemoryStream();
        var w = RecordingWriter.Create(ms, SampleFile.Header(), leaveOpen: true);
        w.AddAttachment("variable-names.txt", "text/plain; charset=utf-8", "1\tA\n"u8);
        w.AddAttachment("variable-names.txt", "text/plain; charset=utf-8", "2\tB\n"u8);
        w.Finish();
        using var r = RecordingReader.Open(new MemoryStream(ms.ToArray()));
        Assert.Equal(2, r.Attachments.Count);
        Assert.Equal("2\tB\n", System.Text.Encoding.UTF8.GetString(r.GetAttachment("variable-names.txt")!.Bytes));
        Assert.Empty(r.Chunks);
        Assert.True(r.IsFinished);
    }

    [Fact]
    public void Dump_names_kinds_tracks_and_records()
    {
        using var r = RecordingReader.Open(new MemoryStream(SampleFile.Build().Bytes));
        var text = new StringWriter();
        RecordingDump.Write(text, r, includeRecords: true);
        var s = text.ToString();
        Assert.Contains("finished", s);
        Assert.Contains("3 position v1", s);
        Assert.Contains("1 0..0.3 s TIGER1 Piper Tiger", s);
        Assert.Contains("snap t=0.2 track=1 position/v1 len=40", s);
    }
}
