namespace JoinFS.Recording.Format.Tests;

/// <summary>What a record should read back as.</summary>
public sealed record Expected(string Kind, uint Track, long Time, string BodyHex, bool IsSnapshot);

/// <summary>
/// A multi-chunk file with two tracks, a keyframe with a snapshot and cross-track skew, built through the
/// writer while remembering what it wrote, chunk by chunk.
/// </summary>
public sealed class SampleFile
{
    public byte[] Bytes = Array.Empty<byte>();
    public List<List<Expected>> Chunks = new();

    public static RecordingHeader Header() => new()
    {
        StartUtcMicroseconds = 1_790_000_000_000_000,
        Metadata = new[] { new KeyValuePair<string, string>("app", "tests"), new KeyValuePair<string, string>("note", "hällö €") },
    };

    public static byte[] Body(int seed, int length)
    {
        // Repeating enough that Deflate gains something.
        var b = new byte[length];
        for (int i = 0; i < length; i++)
            b[i] = (byte)((seed + i / 4) * 7);
        return b;
    }

    public static SampleFile Build(ChunkCompression compression = ChunkCompression.None, bool finish = true)
    {
        var s = new SampleFile();
        var ms = new MemoryStream();
        var w = RecordingWriter.Create(ms, Header(), new RecordingWriterOptions { Compression = compression }, leaveOpen: true);
        uint open = w.BindKind("track-open", 1);
        uint close = w.BindKind("track-close", 1);
        uint pos = w.BindKind("position", 1);
        List<Expected> cur = new();

        void Add(string kind, uint id, uint track, long time, byte[] body, bool snapshot = false)
        {
            if (snapshot) w.AddSnapshot(id, track, time, body); else w.Add(id, track, time, body);
            cur.Add(new Expected(kind, track, time, Convert.ToHexString(body), snapshot));
        }
        void End()
        {
            w.EndChunk();
            s.Chunks.Add(cur);
            cur = new();
        }

        // Chunk 0: the first chunk is a keyframe with an empty snapshot.
        Add("track-open", open, 1, 0, new byte[] { 0, 1, 0x41, 0, 0 });
        Add("position", pos, 1, 0, Body(1, 40));
        Add("position", pos, 1, 100_000, Body(2, 40));
        Add("track-open", open, 2, 50_000, new byte[] { 1, 0, 0, 0 });
        Add("position", pos, 2, 100_000, Body(3, 40));
        End();

        // Chunk 1: plain.
        Add("position", pos, 1, 200_000, Body(4, 40));
        Add("position", pos, 2, 200_000, Body(5, 40));
        End();

        // Chunk 2: keyframe. Snapshot records keep the times they summarise; track 2 lags track 1 by 10 ms.
        w.StartKeyframeChunk();
        Add("track-open", open, 1, 0, new byte[] { 0, 1, 0x41, 0, 0 }, snapshot: true);
        Add("position", pos, 1, 200_000, Body(4, 40), snapshot: true);
        Add("track-open", open, 2, 50_000, new byte[] { 1, 0, 0, 0 }, snapshot: true);
        Add("position", pos, 2, 200_000, Body(5, 40), snapshot: true);
        Add("position", pos, 1, 300_000, Body(6, 40));
        Add("position", pos, 2, 290_000, Body(7, 40));
        Add("track-close", close, 2, 300_000, Array.Empty<byte>());
        End();

        w.SetTrackLabel(1, "TIGER1", "Piper Tiger");
        if (finish)
            w.Finish();
        else
            w.Sync();
        s.Bytes = ms.ToArray();
        return s;
    }

    /// <summary>Reads every chunk of the file as the reader sees it.</summary>
    public static List<List<Expected>> ReadAll(RecordingReader r)
    {
        var all = new List<List<Expected>>();
        for (int i = 0; i < r.Chunks.Count; i++)
        {
            var list = new List<Expected>();
            if (r.TryReadChunk(i, out var chunk))
                foreach (var rec in chunk!.Records)
                    list.Add(new Expected(r.GetKind(rec.Kind)!.Name, rec.Track, rec.Time, Convert.ToHexString(rec.Body.Span), rec.IsSnapshot));
            all.Add(list);
        }
        return all;
    }
}
