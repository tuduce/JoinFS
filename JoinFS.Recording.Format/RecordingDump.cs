using System.Globalization;

namespace JoinFS.Recording.Format;

/// <summary>A text listing of a file's structure for people and tools (the format library's <c>dump</c>). Records stay opaque.</summary>
public static class RecordingDump
{
    public static void Write(TextWriter o, RecordingReader r, bool includeRecords = false, int maxBodyBytes = 16)
    {
        var inv = CultureInfo.InvariantCulture;
        var h = r.Header;
        o.WriteLine($"JFS2 edition {h.FormatMajor}.{h.FormatMinor}, {(r.IsFinished ? "finished" : "UNFINISHED")}");
        o.WriteLine($"start {DateTimeOffset.UnixEpoch.AddTicks(h.StartUtcMicroseconds * 10):u}");
        foreach (var kv in h.Metadata)
            o.WriteLine($"  {kv.Key} = {kv.Value}");

        o.WriteLine("kinds");
        foreach (var k in r.Kinds.OrderBy(k => k.Id))
            o.WriteLine($"  {k.Id} {k.Name} v{k.Version}");

        o.WriteLine("tracks");
        foreach (var t in r.Tracks)
            o.WriteLine($"  {t.Id} {Seconds(t.FirstTime)}..{Seconds(t.LastTime)} s{(t.IsOpen ? " (open)" : "")} {t.Callsign} {t.Model}".TrimEnd());

        o.WriteLine("attachments");
        foreach (var a in r.Attachments)
            o.WriteLine($"  {a.Name} ({a.MediaType}, {a.Bytes.Length.ToString(inv)} bytes)");

        o.WriteLine("chunks");
        for (int i = 0; i < r.Chunks.Count; i++)
        {
            var c = r.Chunks[i];
            o.WriteLine($"  #{i} @{c.Offset} {Seconds(c.TMin)}..{Seconds(c.TMax)} s{(c.IsKeyframe ? " keyframe" : "")}{(c.PrecededByGap ? " after-gap" : "")}");
            if (!includeRecords)
                continue;
            if (!r.TryReadChunk(i, out var chunk))
            {
                o.WriteLine("    (unreadable)");
                continue;
            }
            foreach (var rec in chunk!.Records)
            {
                var body = rec.Body.Span;
                var shown = Convert.ToHexString(body.Slice(0, Math.Min(body.Length, maxBodyBytes)));
                var kind = r.GetKind(rec.Kind);
                o.WriteLine($"    {(rec.IsSnapshot ? "snap" : "    ")} t={Seconds(rec.Time)} track={rec.Track} {kind?.Name}/v{kind?.Version} len={body.Length} {shown}{(body.Length > maxBodyBytes ? "..." : "")}");
            }
            if (!chunk.IsComplete)
                o.WriteLine("    (records stop here: damaged)");
        }

        if (r.Damage.Count > 0)
        {
            o.WriteLine("damage");
            foreach (var d in r.Damage)
                o.WriteLine($"  @{d.Offset} {d.Kind}: {d.Message}");
        }
    }

    static string Seconds(long micros) => (micros / 1e6).ToString("0.######", CultureInfo.InvariantCulture);
}
