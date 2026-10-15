// Stage 6 measurements (docs/jfs2/README.md): load time and memory of long recordings, Deflate per chunk.
// Not in JoinFS.sln; run with: dotnet run -c Release --project docs/jfs2/tools/jfs2bench -- <command> ...
// convert <out-dir> <a.jfs>...      convert old files, report sizes
// synth <template.jfs2> <out.jfs2> [aircraft] [seconds]   build a long recording with Jfs2Capture from a real one's frames
// load <f.jfs2>...                  Jfs2Scene.Load: time (best of 3 after a cold run), retained and allocated memory
// deflate <in.jfs2> <out.jfs2>      copy chunk by chunk with Deflate; sizes, time per chunk written
// copy <in.jfs2> <out.jfs2>         the same without Deflate, the baseline
using System.Diagnostics;
using JoinFS.Net;
using JoinFS.Recording;
using JoinFS.Recording.Format;

static class Program
{
    static int Main(string[] args)
    {
        switch (args[0])
        {
            case "convert": Convert(args[1], args.Skip(2)); break;
            case "synth": Synth(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 10, args.Length > 4 ? double.Parse(args[4]) : 3600); break;
            case "load": foreach (string f in args.Skip(1)) Load(f); break;
            case "deflate": Deflate(args[1], args[2], deflate: true); break;
            case "copy": Deflate(args[1], args[2], deflate: false); break;
        }
        return 0;
    }

    static void Convert(string outDir, IEnumerable<string> files)
    {
        foreach (string f in files)
        {
            var sw = Stopwatch.StartNew();
            LegacyJfsImporter.Result result;
            using (var s = File.OpenRead(f)) result = LegacyJfsImporter.Read(s);
            double import = sw.Elapsed.TotalSeconds;
            string target = Path.Combine(outDir, Path.GetFileNameWithoutExtension(f) + ".jfs2");
            sw.Restart();
            var w = Jfs2SceneWriter.Write(target, result.Scene.Tracks, "bench", result.Scene.StartUtc, Console.Error.WriteLine);
            double write = sw.Elapsed.TotalSeconds;
            double length = result.Scene.Tracks.Max(t => t.Frames.Count > 0 ? t.Frames[^1].Time : 0);
            Console.WriteLine($"{Path.GetFileName(f)}: {new FileInfo(f).Length / 1e6:F1} MB -> {new FileInfo(target).Length / 1e6:F1} MB, {w.Tracks} tracks, {w.Frames} frames, {length / 60:F1} min; import {import:F2} s, write {write:F2} s");
        }
    }

    static void Load(string f)
    {
        long size = new FileInfo(f).Length;
        Jfs2Scene scene = null;
        var times = new List<double>();
        for (int run = 0; run < 4; run++)
        {
            scene = null;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = GC.GetTotalMemory(true);
            long allocBefore = GC.GetTotalAllocatedBytes(true);
            var sw = Stopwatch.StartNew();
            using (var s = File.OpenRead(f)) scene = Jfs2Scene.Load(s);
            sw.Stop();
            long alloc = GC.GetTotalAllocatedBytes(true) - allocBefore;
            long retained = GC.GetTotalMemory(true) - before;
            if (run > 0) times.Add(sw.Elapsed.TotalMilliseconds);
            if (run == 3)
            {
                int frames = scene.Tracks.Sum(t => t.Frames.Count);
                double length = scene.Tracks.Max(t => t.Frames.Count > 0 ? t.Frames[^1].Time : 0);
                Console.WriteLine($"{Path.GetFileName(f)}: {size / 1e6:F1} MB, {scene.Tracks.Count} tracks, {frames} frames, {length / 60:F1} min; " +
                    $"load {times.Min():F0} ms (first {times[0]:F0}), retained {retained / 1e6:F0} MB, allocated {alloc / 1e6:F0} MB, warnings {scene.Warnings.Count}");
            }
            GC.KeepAlive(scene);
        }
    }

    // Each of n aircraft replays the template's longest track, phase-shifted and offset in latitude, looped to the length.
    static void Synth(string template, string target, int aircraft, double seconds)
    {
        Jfs2Scene scene;
        using (var s = File.OpenRead(template)) scene = Jfs2Scene.Load(s);
        SceneTrack t = scene.Tracks.OrderByDescending(x => x.Frames.Count).First();
        double span = t.Frames[^1].Time - t.Frames[0].Time + 0.1;
        double t0 = t.Frames[0].Time;
        Console.WriteLine($"template track {t.Id}: {t.Frames.Count} frames, {span / 60:F1} min");

        var sw = Stopwatch.StartNew();
        var capture = Jfs2Capture.Create(target, "bench", "FS2024", DateTime.UtcNow, Console.Error.WriteLine);
        // phase shift: aircraft a starts at frame a * count / n, so they do not all do the same thing at once
        int count = t.Frames.Count;
        double Time(int i, int loop) => loop * span + (t.Frames[i].Time - t0);
        // (aircraft, frame index, loop) by recording time
        var queue = new PriorityQueue<(int a, int i, int loop), double>();
        var offset = new double[aircraft];
        for (int a = 0; a < aircraft; a++)
        {
            var id = t.Identity;
            id.Callsign = $"SYN{a:D2}";
            capture.OpenTrack((uint)(a + 1), 0, TrackOwner.Network, $"pilot{a}", "JoinFS", "FS2024", id);
            int start = a * count / aircraft;
            offset[a] = -Time(start, 0);
            queue.Enqueue((a, start, 0), 0);
        }
        while (queue.TryDequeue(out var item, out double now))
        {
            if (now > seconds) break;
            uint track = (uint)(item.a + 1);
            SceneFrame frame = t.Frames[item.i];
            switch (frame)
            {
                case ScenePositionFrame p:
                    var pos = p.Position; pos.Latitude += item.a * 0.01;
                    capture.Position(track, now, ref pos); break;
                case SceneObjectPositionFrame o:
                    var op = o.Position; capture.ObjectPosition(track, now, ref op); break;
                case SceneVariablesFrame v:
                    var (ints, floats, strings) = v.ByKind();
                    capture.Variables(track, now, ints, floats, strings); break;
                case SceneEventFrame e: capture.Event(track, now, e.EventId, e.Data); break;
                case SceneIdentityFrame id:
                    var idu = id.Identity; idu.Callsign = $"SYN{item.a:D2}"; capture.Identity(track, now, idu); break;
            }
            int next = item.i + 1, loop = item.loop;
            if (next >= count) { next = 0; loop++; }
            double nextTime = Time(next, loop) + offset[item.a];
            if (nextTime < now) nextTime = now;
            queue.Enqueue((item.a, next, loop), nextTime);
        }
        capture.Finish(v => $"VAR {v:x8}");
        Console.WriteLine($"synth: {aircraft} aircraft, {seconds / 60:F0} min, {new FileInfo(target).Length / 1e6:F1} MB, positions {capture.PositionCount}, variable records {capture.VariableRecordCount}, events {capture.EventCount}, keyframes {capture.KeyframeCount}, held {capture.HeldCount}; {sw.Elapsed.TotalSeconds:F1} s");
    }

    // Copies a file chunk by chunk into a writer with Deflate (or none, for the baseline), timing the writes (the deflating is in EndChunk).
    static void Deflate(string source, string target, bool deflate)
    {
        using var reader = RecordingReader.Open(source);
        var options = new RecordingWriterOptions { Compression = deflate ? ChunkCompression.Deflate : ChunkCompression.None };
        using var writer = RecordingWriter.Create(target, reader.Header, options);
        var map = new Dictionary<uint, uint>();
        foreach (var k in reader.Kinds) map[k.Id] = writer.BindKind(k.Name, k.Version);
        long raw = 0; int chunks = 0;
        var endTimes = new List<double>();
        for (int c = 0; c < reader.Chunks.Count; c++)
        {
            if (!reader.TryReadChunk(c, out var chunk)) throw new Exception($"chunk {c}");
            if (c > 0 && chunk.Info.IsKeyframe) writer.StartKeyframeChunk();
            foreach (var r in chunk.Records)
            {
                raw += r.Body.Length;
                if (r.IsSnapshot) writer.AddSnapshot(map[r.Kind], r.Track, r.Time, r.Body.Span);
                else writer.Add(map[r.Kind], r.Track, r.Time, r.Body.Span);
            }
            var sw = Stopwatch.StartNew();
            writer.EndChunk();
            endTimes.Add(sw.Elapsed.TotalMilliseconds);
            chunks++;
        }
        foreach (var t in reader.Tracks) writer.SetTrackLabel(t.Id, t.Callsign, t.Model);
        foreach (var a in reader.Attachments) writer.AddAttachment(a.Name, a.MediaType, a.Bytes);
        writer.Finish();
        endTimes.Sort();
        long before = new FileInfo(source).Length, after = new FileInfo(target).Length;
        Console.WriteLine($"{Path.GetFileName(source)}: {before / 1e6:F1} MB -> deflate {after / 1e6:F1} MB ({100.0 * after / before:F0}%), {chunks} chunks, avg chunk {before / chunks / 1024.0:F1} KB; " +
            $"EndChunk median {endTimes[endTimes.Count / 2]:F2} ms, p99 {endTimes[(int)(endTimes.Count * 0.99)]:F2} ms, max {endTimes[^1]:F2} ms");
    }
}
