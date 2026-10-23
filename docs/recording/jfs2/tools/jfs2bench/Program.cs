// Stage 6 measurements (docs/recording/jfs2/README.md): load time and memory of long recordings, Deflate per chunk.
// Not in JoinFS.sln; run with: dotnet run -c Release --project docs/recording/jfs2/tools/jfs2bench -- <command> ...
// convert <out-dir> <a.jfs>...      convert old files, report sizes
// synth <template.jfs2> <out.jfs2> [aircraft] [seconds]   build a long recording with Jfs2Capture from a real one's frames
// load <f.jfs2>...                  Jfs2Scene.Load: time (best of 3 after a cold run), retained and allocated memory
// deflate <in.jfs2> <out.jfs2>      copy chunk by chunk with Deflate; sizes, time per chunk written
// copy <in.jfs2> <out.jfs2>         the same without Deflate, the baseline
// rate <in.jfs2> <out-dir> <hz>...  rewrite at lower rates; sizes, interpolation error of the positions dropped
// quant <in.jfs2> <out.jfs2>        positions re-encoded as a quantised 52-byte candidate PositionV2; sizes, rounding error
using System.Buffers.Binary;
using System.Diagnostics;
using JoinFS.Net;
using JoinFS.Net.Jfp2.Codecs;
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
            case "rate": Rate(args[1], args[2], args.Skip(3).Select(double.Parse)); break;
            case "quant": Quant(args[1], args[2]); break;
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

    // Rewrites a recording at a lower rate with Jfs2Capture: a position is kept when at least 1/hz after the last one kept (5% slack), the
    // variables that changed in between are merged into one record at the next kept tick; events and identities pass. For every position
    // dropped, the error of interpolating it from the kept ones (linear, as the player does; angles by shortest delta) is measured.
    static void Rate(string source, string outDir, IEnumerable<double> rates)
    {
        Jfs2Scene scene;
        using (var s = File.OpenRead(source)) scene = Jfs2Scene.Load(s);
        foreach (double hz in rates)
        {
            string target = Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(source)}-{hz:0.#}hz.jfs2");
            var capture = Jfs2Capture.Create(target, "bench", "FS2024", scene.StartUtc, Console.Error.WriteLine);
            double step = 0.95 / hz;
            var horiz = new List<double>(); var vert = new List<double>(); var angle = new List<double>();
            foreach (SceneTrack t in scene.Tracks)
            {
                capture.OpenTrack(t.Id, 0, TrackOwner.Network, "pilot", "JoinFS", "FS2024", t.Identity);
                var kept = new List<ScenePositionFrame>(); var dropped = new List<ScenePositionFrame>();
                double lastPos = double.NegativeInfinity, lastVar = double.NegativeInfinity;
                Dictionary<uint, int> ints = new(); Dictionary<uint, float> floats = new(); Dictionary<uint, string> strings = new();
                bool pending = false;
                void Flush(double time)
                {
                    if (!pending) return;
                    capture.Variables(t.Id, time, ints, floats, strings);
                    ints = new(); floats = new(); strings = new(); pending = false; lastVar = time;
                }
                foreach (SceneFrame frame in t.Frames)
                {
                    if (pending && frame.Time - lastVar >= step) Flush(lastVar + step);
                    switch (frame)
                    {
                        case ScenePositionFrame p:
                            if (p.Time - lastPos >= step) { var pos = p.Position; capture.Position(t.Id, p.Time, ref pos); kept.Add(p); lastPos = p.Time; }
                            else dropped.Add(p);
                            break;
                        case SceneObjectPositionFrame o: var op = o.Position; capture.ObjectPosition(t.Id, o.Time, ref op); break;
                        case SceneVariablesFrame v:
                            var (i, f, str) = v.ByKind();
                            foreach (var kv in i ?? []) ints[kv.Key] = kv.Value;
                            foreach (var kv in f ?? []) floats[kv.Key] = kv.Value;
                            foreach (var kv in str ?? []) strings[kv.Key] = kv.Value;
                            pending = true;
                            if (v.Time - lastVar >= step) Flush(v.Time);
                            break;
                        case SceneEventFrame e: capture.Event(t.Id, e.Time, e.EventId, e.Data); break;
                        case SceneIdentityFrame id: capture.Identity(t.Id, id.Time, id.Identity); break;
                    }
                }
                if (pending) Flush(Math.Max(t.Frames[^1].Time, lastVar));
                // interpolation error of each dropped position between the kept ones around it
                int k = 0;
                foreach (var d in dropped)
                {
                    while (k + 1 < kept.Count && kept[k + 1].Time <= d.Time) k++;
                    if (k + 1 >= kept.Count || kept[k].Time > d.Time) continue;
                    PositionUpdate a = kept[k].Position, b = kept[k + 1].Position, x = d.Position;
                    double u = (d.Time - kept[k].Time) / (kept[k + 1].Time - kept[k].Time);
                    double lat = a.Latitude + (b.Latitude - a.Latitude) * u, lon = a.Longitude + (b.Longitude - a.Longitude) * u;
                    double dn = (lat - x.Latitude) * 111_320, de = (lon - x.Longitude) * 111_320 * Math.Cos(x.Latitude * Math.PI / 180);
                    horiz.Add(Math.Sqrt(dn * dn + de * de));
                    vert.Add(Math.Abs(a.Altitude + (b.Altitude - a.Altitude) * u - x.Altitude));
                    double AngErr(float p0, float p1, float px) => Math.Abs(Delta(px, p0 + Delta(p0, p1) * u)) * 180 / Math.PI;
                    angle.Add(Math.Max(AngErr(a.Pitch, b.Pitch, x.Pitch), Math.Max(AngErr(a.Bank, b.Bank, x.Bank), AngErr(a.Heading, b.Heading, x.Heading))));
                }
            }
            capture.Finish(v => $"VAR {v:x8}");
            Console.WriteLine($"{Path.GetFileName(target)}: {new FileInfo(target).Length / 1e6:F2} MB, positions {capture.PositionCount}, variable records {capture.VariableRecordCount}, " +
                $"dropped {horiz.Count}; horizontal m {Stats(horiz)}; altitude {Stats(vert)}; angle deg {Stats(angle)}");
        }
    }

    static double Delta(double from, double to) { double d = (to - from) % (2 * Math.PI); return d > Math.PI ? d - 2 * Math.PI : d < -Math.PI ? d + 2 * Math.PI : d; }

    static string Stats(List<double> v)
    {
        if (v.Count == 0) return "-";
        v.Sort();
        return $"mean {v.Average():F3} p99 {v[(int)(v.Count * 0.99)]:F3} max {v[^1]:F3}";
    }

    // A candidate PositionV2 for the size: the position record re-encoded, quantised, without ObjectId and NetTime (the record header
    // has them). 52 bytes against 103: lat/lon int32 of 1e-7 deg (1.1 cm), altitude int32 of 1/1000, pitch/bank/heading int16 of
    // pi/32768 rad (0.0055 deg), velocities, angular velocities and accelerations Half, the five axes int16 as now, elevation float,
    // static CG Half. Copies the file chunk by chunk (as Deflate does) with the position records swapped, and reports the rounding error.
    static void Quant(string source, string target)
    {
        var codec = new PositionV1Codec();
        using var reader = RecordingReader.Open(source);
        using var writer = RecordingWriter.Create(target, reader.Header, new RecordingWriterOptions { Compression = ChunkCompression.Deflate });
        var map = new Dictionary<uint, uint>();
        uint positionKind = uint.MaxValue;
        foreach (var k in reader.Kinds)
        {
            if (k.Name == Jfs2Kinds.Position) { positionKind = k.Id; map[k.Id] = writer.BindKind("bench.position-q", 1); }
            else map[k.Id] = writer.BindKind(k.Name, k.Version);
        }
        var horiz = new List<double>(); var vert = new List<double>(); var angle = new List<double>(); var vel = new List<double>();
        byte[] q = new byte[52];
        long positions = 0;
        static short A(float rad) => (short)Math.Clamp(Math.Round(Delta(0, rad) * 32768 / Math.PI), short.MinValue, short.MaxValue);
        static double E(float rad) => Math.Abs(Delta(rad, A(rad) * Math.PI / 32768)) * 180 / Math.PI;
        static double H(float v) => Math.Abs((float)(Half)v - v);
        for (int c = 0; c < reader.Chunks.Count; c++)
        {
            if (!reader.TryReadChunk(c, out var chunk)) throw new Exception($"chunk {c}");
            if (c > 0 && chunk.Info.IsKeyframe) writer.StartKeyframeChunk();
            foreach (var r in chunk.Records)
            {
                ReadOnlySpan<byte> body = r.Body.Span;
                if (r.Kind == positionKind)
                {
                    PositionUpdate p = codec.Decode(body);
                    int lat = (int)Math.Round(p.Latitude * 1e7), lon = (int)Math.Round(p.Longitude * 1e7), alt = (int)Math.Round(p.Altitude * 1000);
                    int o = 0;
                    void I32(int v) { BinaryPrimitives.WriteInt32LittleEndian(q.AsSpan(o), v); o += 4; }
                    void I16(short v) { BinaryPrimitives.WriteInt16LittleEndian(q.AsSpan(o), v); o += 2; }
                    void F16(float v) { BinaryPrimitives.WriteHalfLittleEndian(q.AsSpan(o), (Half)v); o += 2; }
                    I32(lat); I32(lon); I32(alt); I16(A(p.Pitch)); I16(A(p.Bank)); I16(A(p.Heading));
                    F16(p.VelocityX); F16(p.VelocityY); F16(p.VelocityZ); F16(p.AngularVelocityX); F16(p.AngularVelocityY); F16(p.AngularVelocityZ);
                    F16(p.AccelerationX); F16(p.AccelerationY); F16(p.AccelerationZ);
                    foreach (float axis in new[] { p.Rudder, p.Elevator, p.Aileron, p.BrakeLeft, p.BrakeRight }) I16((short)(axis * 16384.0));
                    BinaryPrimitives.WriteSingleLittleEndian(q.AsSpan(o), p.Elevation); o += 4; F16(p.StaticCgToGround);
                    double dn = (lat / 1e7 - p.Latitude) * 111_320, de = (lon / 1e7 - p.Longitude) * 111_320 * Math.Cos(p.Latitude * Math.PI / 180);
                    horiz.Add(Math.Sqrt(dn * dn + de * de)); vert.Add(Math.Abs(alt / 1000.0 - p.Altitude));
                    angle.Add(Math.Max(E(p.Pitch), Math.Max(E(p.Bank), E(p.Heading))));
                    vel.Add(Math.Max(H(p.VelocityX), Math.Max(H(p.VelocityY), H(p.VelocityZ))));
                    body = q.AsSpan(0, o); positions++;
                }
                if (r.IsSnapshot) writer.AddSnapshot(map[r.Kind], r.Track, r.Time, body);
                else writer.Add(map[r.Kind], r.Track, r.Time, body);
            }
            writer.EndChunk();
        }
        foreach (var t in reader.Tracks) writer.SetTrackLabel(t.Id, t.Callsign, t.Model);
        foreach (var a in reader.Attachments) writer.AddAttachment(a.Name, a.MediaType, a.Bytes);
        writer.Finish();
        long before = new FileInfo(source).Length, after = new FileInfo(target).Length;
        Console.WriteLine($"{Path.GetFileName(source)}: {before / 1e6:F2} MB -> {after / 1e6:F2} MB ({100.0 * after / before:F0}%), {positions} positions; " +
            $"horizontal m {Stats(horiz)}; altitude {Stats(vert)}; angle deg {Stats(angle)}; velocity {Stats(vel)}");
    }
}
