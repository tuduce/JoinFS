using System.Buffers.Binary;

namespace JoinFS.Recording.Format.Tests;

public class DamageTests
{
    static RecordingReader Open(byte[] bytes) => RecordingReader.Open(new MemoryStream(bytes));

    /// <summary>Recomputes the CRC of the block at the offset, so a test can change a body and keep the block valid.</summary>
    static void FixCrc(byte[] file, int blockOffset)
    {
        int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(blockOffset + 1));
        uint crc = Crc32.Compute(file.AsSpan(blockOffset, 5 + length));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(blockOffset + 5 + length), crc);
    }

    // ---- Opening ----

    [Fact]
    public void Not_a_jfs2_file_is_refused()
    {
        var e = Assert.Throws<Jfs2FormatException>(() => Open(new byte[] { 1, 2, 3 }));
        Assert.Equal(Jfs2Failure.NotJfs2, e.Reason);
        // A legacy .jfs starts with its version as an int16: 21008 here.
        e = Assert.Throws<Jfs2FormatException>(() => Open(new byte[] { 0x10, 0x52, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.Equal(Jfs2Failure.NotJfs2, e.Reason);
    }

    [Fact]
    public void A_damaged_header_is_refused()
    {
        var bytes = SampleFile.Build().Bytes;
        bytes[20] ^= 0xFF;
        Assert.Equal(Jfs2Failure.HeaderDamaged, Assert.Throws<Jfs2FormatException>(() => Open(bytes)).Reason);
    }

    [Fact]
    public void An_unknown_major_or_required_feature_is_refused_but_a_higher_minor_and_optional_features_are_not()
    {
        var bytes = SampleFile.Build().Bytes;

        var major = (byte[])bytes.Clone();
        major[8 + 5] = 2; // FormatMajor
        FixCrc(major, 8);
        Assert.Equal(Jfs2Failure.UnsupportedMajor, Assert.Throws<Jfs2FormatException>(() => Open(major)).Reason);

        var required = (byte[])bytes.Clone();
        required[8 + 7] = 1; // RequiredFeatures bit 0
        FixCrc(required, 8);
        Assert.Equal(Jfs2Failure.UnsupportedFeatures, Assert.Throws<Jfs2FormatException>(() => Open(required)).Reason);

        var tolerated = (byte[])bytes.Clone();
        tolerated[8 + 6] = 9; // FormatMinor
        tolerated[8 + 11] = 0xFF; // OptionalFeatures
        FixCrc(tolerated, 8);
        using var r = Open(tolerated);
        Assert.True(r.IsFinished);
        Assert.Equal(9, r.Header.FormatMinor);
    }

    [Fact]
    public void Metadata_that_runs_past_the_header_body_is_refused()
    {
        var bytes = SampleFile.Build().Bytes;
        bytes[8 + 5 + 18] = 0x7F; // MetadataCount: 127 entries cannot fit
        FixCrc(bytes, 8);
        Assert.Equal(Jfs2Failure.HeaderDamaged, Assert.Throws<Jfs2FormatException>(() => Open(bytes)).Reason);
    }

    // ---- Unfinished files ----

    [Fact]
    public void A_file_without_footer_is_unfinished_not_corrupt()
    {
        var sample = SampleFile.Build(finish: false);
        using var r = Open(sample.Bytes);
        Assert.False(r.IsFinished);
        Assert.Empty(r.Damage);
        Assert.Equal(sample.Chunks, SampleFile.ReadAll(r));
        Assert.Equal(new[] { 1u, 2u }, r.Tracks.Select(t => t.Id));
        Assert.Equal(new[] { true, false }, r.Tracks.Select(t => t.IsOpen)); // track 2 was closed by the sample
        Assert.Equal((0L, 300_000L), (r.Tracks[0].FirstTime, r.Tracks[0].LastTime));
    }

    [Fact]
    public void A_truncated_last_block_is_dropped_quietly()
    {
        var sample = SampleFile.Build(finish: false);
        using var r = Open(sample.Bytes.AsSpan(0, sample.Bytes.Length - 7).ToArray());
        Assert.Empty(r.Damage);
        Assert.Equal(2, r.Chunks.Count);
    }

    [Fact]
    public void Unknown_block_types_are_skipped()
    {
        var sample = SampleFile.Build(finish: false);
        var unknown = new ByteWriter();
        int start = unknown.BeginBlock((BlockType)9);
        unknown.Bytes("future"u8);
        unknown.EndBlock(start);
        int headerEnd = Jfs2.MagicLength + 5 + (int)BinaryPrimitives.ReadUInt32LittleEndian(sample.Bytes.AsSpan(9)) + 4;
        var spliced = sample.Bytes[..headerEnd].Concat(unknown.Span.ToArray()).Concat(sample.Bytes[headerEnd..]).ToArray();
        using var r = Open(spliced);
        Assert.Empty(r.Damage);
        Assert.Equal(sample.Chunks, SampleFile.ReadAll(r));
    }

    [Fact]
    public void A_block_with_a_bad_crc_is_skipped_and_the_scan_goes_on_and_the_next_chunk_is_marked()
    {
        var sample = SampleFile.Build(finish: false);
        using var clean = Open(sample.Bytes);
        var damaged = (byte[])sample.Bytes.Clone();
        damaged[(int)clean.Chunks[1].Offset + 20] ^= 0x55; // inside chunk 1
        using var r = Open(damaged);
        Assert.Equal(2, r.Chunks.Count);
        Assert.Equal(clean.Chunks[0], r.Chunks[0]);
        Assert.Equal(clean.Chunks[2].Offset, r.Chunks[1].Offset);
        Assert.True(r.Chunks[1].PrecededByGap);
        Assert.Equal(DamageKind.BlockCrc, Assert.Single(r.Damage).Kind);
    }

    [Fact]
    public void An_implausible_length_stops_the_scan_but_keeps_what_was_read()
    {
        var sample = SampleFile.Build(finish: false);
        using var clean = Open(sample.Bytes);
        var damaged = (byte[])sample.Bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(damaged.AsSpan((int)clean.Chunks[1].Offset + 1), 0x7FFFFFFF);
        using var r = Open(damaged);
        Assert.Single(r.Chunks);
        Assert.Equal(DamageKind.BlockLength, Assert.Single(r.Damage).Kind);
    }

    // ---- Finished files with damage ----

    [Fact]
    public void A_bad_chunk_in_a_finished_file_shows_when_it_is_read()
    {
        var sample = SampleFile.Build();
        using var clean = Open(sample.Bytes);
        var damaged = (byte[])sample.Bytes.Clone();
        damaged[(int)clean.Chunks[1].Offset + 50] ^= 0x55; // payload; the 30 bytes before it are read when the file opens
        using var r = Open(damaged);
        Assert.True(r.IsFinished);
        Assert.Empty(r.Damage);
        Assert.True(r.TryReadChunk(0, out _));
        Assert.False(r.TryReadChunk(1, out var gone));
        Assert.Null(gone);
        Assert.True(r.TryReadChunk(2, out _));
        Assert.Equal(DamageKind.BlockCrc, Assert.Single(r.Damage).Kind);
    }

    [Fact]
    public void A_bad_index_falls_back_to_a_scan()
    {
        var sample = SampleFile.Build();
        var damaged = (byte[])sample.Bytes.Clone();
        long indexAt = BinaryPrimitives.ReadInt64LittleEndian(damaged.AsSpan(damaged.Length - 20));
        damaged[indexAt + 8] ^= 0x01; // inside the Index body
        using var r = Open(damaged);
        Assert.False(r.IsFinished);
        Assert.Equal(4, r.Chunks.Count);
        Assert.All(r.Tracks, t => Assert.False(t.IsOpen));
    }

    [Fact]
    public void An_index_that_disagrees_with_the_chunks_loses()
    {
        var sample = SampleFile.Build();
        var altered = (byte[])sample.Bytes.Clone();
        long indexAt = BinaryPrimitives.ReadInt64LittleEndian(altered.AsSpan(altered.Length - 20));
        altered[indexAt + 5 + 1 + 8] ^= 0x01; // first chunk's TMin in the Index, CRC fixed
        FixCrc(altered, (int)indexAt);
        using var r = Open(altered);
        Assert.True(r.IsFinished);
        Assert.Equal(0, r.Chunks[0].TMin);
        Assert.Equal(DamageKind.IndexMismatch, Assert.Single(r.Damage).Kind);
        Assert.Equal(new[] { 1u, 2u }, r.Tracks.Select(t => t.Id)); // rebuilt from the chunks
    }

    [Fact]
    public void A_chunk_with_a_wrong_uncompressed_length_or_unknown_compression_is_skipped()
    {
        var sample = SampleFile.Build(finish: false);
        using var clean = Open(sample.Bytes);
        int chunk1 = (int)clean.Chunks[1].Offset;

        var length = (byte[])sample.Bytes.Clone();
        length[chunk1 + 5 + 26]++; // UncompressedLength
        FixCrc(length, chunk1);
        using (var r = Open(length))
        {
            Assert.Equal(2, r.Chunks.Count);
            Assert.Equal(DamageKind.ChunkData, Assert.Single(r.Damage).Kind);
        }

        var compression = (byte[])sample.Bytes.Clone();
        compression[chunk1 + 5 + 17] = 7;
        FixCrc(compression, chunk1);
        using (var r = Open(compression))
        {
            Assert.Equal(2, r.Chunks.Count);
            Assert.Equal(DamageKind.ChunkData, Assert.Single(r.Damage).Kind);
        }
    }

    [Fact]
    public void A_record_of_an_unbound_kind_ends_the_chunk_there()
    {
        var sample = SampleFile.Build(finish: false);
        using var clean = Open(sample.Bytes);
        int chunk1 = (int)clean.Chunks[1].Offset;
        var damaged = (byte[])sample.Bytes.Clone();
        // Record 0 is kind, track, delta, length (one byte each) and a 40-byte body; the delta is 0 from TMin.
        int second = chunk1 + 5 + 30 + 4 + 40;
        damaged[second] = 0x7E; // the second record's kind: past the three bound ones
        FixCrc(damaged, chunk1);
        using var r = Open(damaged);
        Assert.True(r.TryReadChunk(1, out var chunk));
        Assert.False(chunk!.IsComplete);
        Assert.Single(chunk.Records);
        Assert.Contains(r.Damage, d => d.Kind == DamageKind.ChunkRecords);
    }

    // ---- Repair ----

    [Fact]
    public void Repair_completes_an_unfinished_file()
    {
        var sample = SampleFile.Build(finish: false);
        var ms = new MemoryStream();
        ms.Write(sample.Bytes, 0, sample.Bytes.Length - 11); // the crash cut the last block
        ms.Write(new byte[] { 3, 0xFF }, 0, 2);
        var result = RecordingRepair.Repair(ms);

        Assert.False(result.WasFinished);
        Assert.True(result.BytesDropped > 2); // the torn chunk and the junk after it
        Assert.Equal(2, result.TracksClosed);
        using var r = Open(ms.ToArray());
        Assert.True(r.IsFinished);
        Assert.Empty(r.Damage);
        Assert.All(r.Tracks, t => Assert.False(t.IsOpen));
        Assert.Equal(new[] { 1u, 2u }, r.Tracks.Select(t => t.Id));
        // The chunks that survived are unchanged. Both tracks are closed at their last time in a final chunk.
        var read = SampleFile.ReadAll(r);
        Assert.Equal(3, read.Count);
        Assert.Equal(sample.Chunks[0], read[0]);
        Assert.Equal(sample.Chunks[1], read[1]);
        Assert.Equal(new[] { new Expected("track-close", 1, 200_000, "", false), new Expected("track-close", 2, 200_000, "", false) }, read[2]);
    }

    [Fact]
    public void Repair_leaves_a_finished_file_alone()
    {
        var sample = SampleFile.Build();
        var ms = new MemoryStream(sample.Bytes.ToArray(), writable: true);
        var result = RecordingRepair.Repair(ms);
        Assert.True(result.WasFinished);
        Assert.Equal(sample.Bytes, ms.ToArray());
    }

    [Fact]
    public void Repair_drops_an_index_whose_footer_is_missing_and_writes_a_new_one()
    {
        var sample = SampleFile.Build();
        var ms = new MemoryStream();
        ms.Write(sample.Bytes, 0, sample.Bytes.Length - 9); // footer cut, Index intact
        RecordingRepair.Repair(ms);
        using var r = Open(ms.ToArray());
        Assert.True(r.IsFinished);
        Assert.Equal(4, r.Chunks.Count);
    }

    // ---- Fuzzing: no exception but Jfs2FormatException, ever ----

    static void Exercise(byte[] bytes)
    {
        RecordingReader r;
        try
        {
            r = Open(bytes);
        }
        catch (Jfs2FormatException)
        {
            return;
        }
        using (r)
        {
            _ = r.FindKeyframeChunk(100_000);
            for (int i = 0; i < r.Chunks.Count; i++)
                if (r.TryReadChunk(i, out var chunk))
                    foreach (var rec in chunk!.Records)
                        _ = rec.Body.Span.Length;
            RecordingDump.Write(new StringWriter(), r, includeRecords: true);
        }
    }

    [Theory]
    [InlineData(ChunkCompression.None)]
    [InlineData(ChunkCompression.Deflate)]
    public void Every_truncation_opens_or_is_refused_and_repairs_to_a_valid_file(ChunkCompression compression)
    {
        var bytes = SampleFile.Build(compression).Bytes;
        int previousChunks = 0;
        for (int length = 0; length <= bytes.Length; length++)
        {
            var prefix = bytes[..length];
            Exercise(prefix);

            RecordingReader r;
            try
            {
                r = Open(prefix);
            }
            catch (Jfs2FormatException)
            {
                continue;
            }
            using (r)
                Assert.Equal(length == bytes.Length, r.IsFinished);

            var ms = new MemoryStream();
            ms.Write(prefix);
            var result = RecordingRepair.Repair(ms);
            using var repaired = Open(ms.ToArray());
            Assert.True(repaired.IsFinished, $"length {length}");
            Assert.Empty(repaired.Damage);
            Assert.All(repaired.Tracks, t => Assert.False(t.IsOpen));
            for (int i = 0; i < repaired.Chunks.Count; i++)
                Assert.True(repaired.TryReadChunk(i, out _), $"length {length} chunk {i}");

            int surviving = repaired.Chunks.Count - (result.TracksClosed > 0 ? 1 : 0); // repair adds one chunk to close tracks
            Assert.True(surviving >= previousChunks, $"length {length}: {surviving} < {previousChunks}");
            previousChunks = surviving;
        }
        Assert.Equal(4, previousChunks);
    }

    [Theory]
    [InlineData(ChunkCompression.None)]
    [InlineData(ChunkCompression.Deflate)]
    public void Random_corruption_never_throws_anything_but_a_format_exception(ChunkCompression compression)
    {
        var clean = SampleFile.Build(compression).Bytes;
        var unfinished = SampleFile.Build(compression, finish: false).Bytes;
        var rng = new Random(20261012);
        foreach (var source in new[] { clean, unfinished })
            for (int round = 0; round < 1500; round++)
            {
                var bytes = (byte[])source.Clone();
                int flips = rng.Next(1, 5);
                for (int i = 0; i < flips; i++)
                    bytes[rng.Next(bytes.Length)] = (byte)rng.Next(256);
                Exercise(bytes);
            }
    }
}
