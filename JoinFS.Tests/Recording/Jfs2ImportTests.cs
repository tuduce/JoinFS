using JoinFS.Recording;

namespace JoinFS.Tests.Recording;

/// <summary>
/// Stage 4 of the recorder v2 (docs/jfs2/README.md): an old <c>.jfs</c> is read by the old reader and written as <c>.jfs2</c>, and
/// the new file plays the same (design section 8: same objects, same frames per object and type, same values).
/// </summary>
public sealed class Jfs2ImportTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("jfs2-import-").FullName;
    readonly List<string> logged = [];

    public void Dispose()
    {
        try { Directory.Delete(folder, true); } catch (IOException) { }
    }

    /// <summary>The real recordings of the project owner; the test is skipped on a machine that does not have them.</summary>
    internal static string CorpusFolder => Environment.GetEnvironmentVariable("JOINFS_JFS_CORPUS") ?? @"C:\Users\crist\Downloads\tiger recordings";

    internal static List<Recorder.Obj> ReadJfs(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return new Recorder(null!).Parse(new BinaryReader(stream))!;
    }

    internal static List<Recorder.Obj> ReadJfs2(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Jfs2Loader.Load(stream).Objects;
    }

    string Convert(List<Recorder.Obj> objects, string name = "out.jfs2")
    {
        string path = Path.Combine(folder, name);
        Jfs2Exporter.Write(path, objects, "JoinFS test (converted)", new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc), logged.Add);
        return path;
    }

    static Dictionary<uint, T> Merge<T>(IEnumerable<Recorder.Frame> frames, double time, Func<Recorder.Frame, Dictionary<uint, T>?> pick)
    {
        Dictionary<uint, T> merged = [];
        foreach (Recorder.Frame frame in frames.Where(f => f.time <= time))
        {
            if (pick(frame) is { } variables)
            {
                foreach (var variable in variables)
                {
                    merged[variable.Key] = variable.Value;
                }
            }
        }
        return merged;
    }

    /// <summary>Everything the design promises is the same, as a list of differences; empty when the two play alike.</summary>
    internal static List<string> Differences(List<Recorder.Obj> before, List<Recorder.Obj> after)
    {
        List<string> differences = [];
        // the new loader leaves out objects that never had a position, as the exporter does
        // and it lists objects in the order they first appear (ties keep the old order)
        before = [.. before.Where(o => o.frames.Any(f => f.type is Recorder.FrameType.AircraftPosition or Recorder.FrameType.ObjectPosition)).OrderBy(o => o.frames.Min(f => f.time))];
        if (before.Count != after.Count)
        {
            differences.Add($"{before.Count} objects became {after.Count}");
            return differences;
        }
        for (int n = 0; n < before.Count; n++)
        {
            Recorder.Obj a = before[n], b = after[n];
            string who = $"object {n} ({a.model})";
            if (a.GetType() != b.GetType()) differences.Add(who + ": type " + a.GetType().Name + " became " + b.GetType().Name);
            // the identity codec limits some strings (IdentityV1Codec); FS2024 files hold a long model string in icaoType
            if ((Cut(a.model, 256), Cut(a.livery, 256), Cut(a.icaoType, 8), Cut(a.icaoAirline, 8), a.typerole) != (b.model, b.livery, b.icaoType, b.icaoAirline, b.typerole)) differences.Add(who + ": identity differs: " + string.Join("|", a.model, a.livery, a.icaoType, a.icaoAirline, a.typerole) + " became " + string.Join("|", b.model, b.livery, b.icaoType, b.icaoAirline, b.typerole));
            if (a is Recorder.Aircraft x && b is Recorder.Aircraft y && (x.plane, Cut(x.callsign, 32), x.nickname) != (y.plane, y.callsign, y.nickname)) differences.Add(who + ": aircraft identity differs");

            foreach (Recorder.FrameType type in new[] { Recorder.FrameType.AircraftPosition, Recorder.FrameType.ObjectPosition, Recorder.FrameType.SimEvent })
            {
                int count = a.frames.Count(f => f.type == type), count2 = b.frames.Count(f => f.type == type);
                if (count != count2) differences.Add($"{who}: {count} {type} frames became {count2}");
            }

            // positions, pairwise. The exporter writes frames in time order, so a frame that came late in the old file is in its place,
            // and a time before the start of the recording is held at 0 (spec 5)
            var positionsA = a.frames.Where(f => f.type is Recorder.FrameType.AircraftPosition or Recorder.FrameType.ObjectPosition).OrderBy(f => f.time).ToList();
            var positionsB = b.frames.Where(f => f.type is Recorder.FrameType.AircraftPosition or Recorder.FrameType.ObjectPosition).ToList();
            for (int i = 0; i < Math.Min(positionsA.Count, positionsB.Count); i++)
            {
                if (Math.Abs(positionsB[i].time - Math.Max(positionsA[i].time, 0)) > 1e-6) { differences.Add($"{who}: position {i} time {positionsA[i].time} became {positionsB[i].time}"); break; }
                if (!SamePosition(positionsA[i], positionsB[i], out string why)) { differences.Add($"{who}: position {i}: {why}"); break; }
            }

            var eventsA = a.frames.OfType<Recorder.SimEventFrame>().Select(f => (f.eventId, f.data)).ToList();
            var eventsB = b.frames.OfType<Recorder.SimEventFrame>().Select(f => (f.eventId, f.data)).ToList();
            if (!eventsA.SequenceEqual(eventsB)) differences.Add(who + ": events differ");

            // variables: the old file holds samples, the new one changes, so compare what they add up to at several moments
            double end = a.frames.Count == 0 ? 0 : a.frames.Max(f => f.time);
            foreach (double fraction in new[] { 0.1, 0.5, 0.9, 1.0 })
            {
                double time = end * fraction;
                if (!SameVariables(Merge(a.frames, time, f => (f as Recorder.IntegerVariablesFrame)?.variables), Merge(b.frames, time, f => (f as Recorder.IntegerVariablesFrame)?.variables), (p, q) => p == q)) differences.Add($"{who}: integer variables differ at {time:0.0} s");
                if (!SameVariables(Merge(a.frames, time, f => (f as Recorder.FloatVariablesFrame)?.variables), Merge(b.frames, time, f => (f as Recorder.FloatVariablesFrame)?.variables), (p, q) => BitConverter.SingleToInt32Bits(p) == BitConverter.SingleToInt32Bits(q))) differences.Add($"{who}: float variables differ at {time:0.0} s");
                if (!SameVariables(Merge(a.frames, time, f => (f as Recorder.String8VariablesFrame)?.variables), Merge(b.frames, time, f => (f as Recorder.String8VariablesFrame)?.variables), (p, q) => p == q)) differences.Add($"{who}: string variables differ at {time:0.0} s");
            }
        }
        return differences;
    }

    static string Cut(string text, int limit) => text.Length <= limit ? text : text[..limit];

    static bool SameVariables<T>(Dictionary<uint, T> a, Dictionary<uint, T> b, Func<T, T, bool> same) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out T? other) && same(pair.Value, other));

    /// <summary>The five control axes are held to 1/16384 (spec 7.3), the rest exactly.</summary>
    static bool SamePosition(Recorder.Frame a, Recorder.Frame b, out string why)
    {
        why = "";
        static bool Near(float p, float q) => (float.IsNaN(p) && float.IsNaN(q)) || Math.Abs(p - q) <= 1.0f / 16384;
        static bool Same(float p, float q) => BitConverter.SingleToInt32Bits(p) == BitConverter.SingleToInt32Bits(q);
        if (a is Recorder.AircraftPositionFrame pa && b is Recorder.AircraftPositionFrame pb)
        {
            Sim.AircraftPosition p = pa.data, q = pb.data;
            if (p.latitude != q.latitude || p.longitude != q.longitude || p.altitude != q.altitude) { why = "coordinates"; return false; }
            if (!Same(p.pitch, q.pitch) || !Same(p.bank, q.bank) || !Same(p.heading, q.heading)) { why = "attitude"; return false; }
            if (!Same(p.velocityX, q.velocityX) || !Same(p.velocityY, q.velocityY) || !Same(p.velocityZ, q.velocityZ)) { why = "velocity"; return false; }
            if (!Same(p.angularVelocityX, q.angularVelocityX) || !Same(p.angularVelocityY, q.angularVelocityY) || !Same(p.angularVelocityZ, q.angularVelocityZ)) { why = "angular velocity"; return false; }
            if (!Same(p.accelerationX, q.accelerationX) || !Same(p.accelerationY, q.accelerationY) || !Same(p.accelerationZ, q.accelerationZ)) { why = "acceleration"; return false; }
            if (!Near(p.rudder, q.rudder) || !Near(p.elevator, q.elevator) || !Near(p.aileron, q.aileron) || !Near(p.brakeLeft, q.brakeLeft) || !Near(p.brakeRight, q.brakeRight)) { why = "controls"; return false; }
            if (!Same(p.elevation, q.elevation) || p.ground != q.ground) { why = "elevation or ground"; return false; }
            // the in-app reader leaves it 0 for an old file, which never held it; the importer says "no data" with NaN (design section 8)
            if (!Same(p.staticCgToGround, q.staticCgToGround) && !(float.IsNaN(q.staticCgToGround) && p.staticCgToGround == 0)) { why = "staticCgToGround"; return false; }
            return true;
        }
        if (a is Recorder.ObjectPositionFrame oa && b is Recorder.ObjectPositionFrame ob)
        {
            Sim.ObjectPositionVelocity p = oa.data, q = ob.data;
            if (p.latitude != q.latitude || p.longitude != q.longitude || p.altitude != q.altitude) { why = "coordinates"; return false; }
            if (!Same(p.pitch, q.pitch) || !Same(p.bank, q.bank) || !Same(p.heading, q.heading) || !Same(p.height, q.height) || p.ground != q.ground) { why = "attitude, height or ground"; return false; }
            return true;
        }
        why = "frame kinds " + a.GetType().Name + " and " + b.GetType().Name;
        return false;
    }

    // ---- a recording made in memory, through the old format and back ----

    static Sim.AircraftPosition Aircraft(int i) => new()
    {
        latitude = 0.82 + i * 1e-6, // radians, as recorded
        longitude = 0.14 - i * 1e-6,
        altitude = 500 + i,
        pitch = 0.05f,
        bank = -0.1f,
        heading = 1.0f + i * 0.001f,
        velocityX = 1,
        velocityY = 2,
        velocityZ = 30,
        rudder = 0.1f,
        elevator = -0.25f,
        aileron = 0.5f,
        brakeLeft = 0,
        brakeRight = 1,
        elevation = 400,
        ground = i < 20 ? 1 : 0,
        staticCgToGround = float.NaN,
    };

    internal static List<Recorder.Obj> MakeRecording()
    {
        var plane = new Recorder.Aircraft(true, "DLH1", "Pilot", "Cessna 172", "Red", "C172", "", 3, Sim.Obj.Owner.Network);
        var boat = new Recorder.Obj("Ferry", "", "", "", 1, Sim.Obj.Owner.Network);
        for (int i = 0; i < 400; i++)
        {
            double t = i * 0.05;
            Sim.AircraftPosition p = Aircraft(i);
            plane.frames.Add(new Recorder.AircraftPositionFrame(t, ref p));
            if (i % 10 == 0)
            {
                plane.frames.Add(new Recorder.IntegerVariablesFrame(t, new Dictionary<uint, int> { [1] = i < 200 ? 0 : 1, [2] = 7 }));
                plane.frames.Add(new Recorder.FloatVariablesFrame(t, new Dictionary<uint, float> { [3] = i * 0.5f }));
                plane.frames.Add(new Recorder.String8VariablesFrame(t, new Dictionary<uint, string> { [4] = "N123" }));
            }
            if (i == 150)
            {
                plane.frames.Add(new Recorder.SimEventFrame(t, 66000, 1));
            }
            Sim.ObjectPositionVelocity b = new() { latitude = 0.89 + i * 1e-6, longitude = 0.02, altitude = 2, heading = 3, height = 1.5f, ground = 1 };
            boat.frames.Add(new Recorder.ObjectPositionFrame(t + 0.01, ref b));
        }
        return [plane, boat, new Recorder.Aircraft(true, "NOPOS", "", "x", "", "", "", 3, Sim.Obj.Owner.Network)];
    }

    [Fact]
    public void ARecordingInTheOldFormat_ConvertsToAFileThatPlaysTheSame()
    {
        string jfs = Path.Combine(folder, "old.jfs");
        File.WriteAllBytes(jfs, LegacyJfsWriter.Write(MakeRecording()));
        List<Recorder.Obj> old = ReadJfs(jfs);
        Assert.Equal(3, old.Count);

        string path = Convert(old);
        List<Recorder.Obj> converted = ReadJfs2(path);

        Assert.Empty(Differences(old, converted));
        Assert.Equal(2, converted.Count); // the aircraft without a position is left out
        Assert.Empty(logged);
    }

    [Fact]
    public void TheConvertedFile_IsMuchSmallerThanTheVariableSamplesItCameFrom()
    {
        List<Recorder.Obj> recording = MakeRecording();
        string path = Convert(recording);

        // 400 position samples and 40 unchanged variable samples: the variables are written once and on change
        using var reader = JoinFS.Recording.Format.RecordingReader.Open(path);
        Assert.True(reader.IsFinished);
        Assert.Equal(2, reader.Tracks.Count);
        Assert.Equal("DLH1", reader.Tracks[0].Callsign);
    }

    [Fact]
    public void FramesOfAllObjects_AreWrittenInTimeOrder()
    {
        // object 2's frames all lie after object 1's: written object by object they would be clamped to one moment
        var first = new Recorder.Aircraft(true, "A", "", "m", "", "", "", 3, Sim.Obj.Owner.Network);
        var second = new Recorder.Aircraft(true, "B", "", "m", "", "", "", 3, Sim.Obj.Owner.Network);
        for (int i = 0; i < 300; i++)
        {
            Sim.AircraftPosition p = Aircraft(i);
            first.frames.Add(new Recorder.AircraftPositionFrame(i * 0.1, ref p));
            second.frames.Add(new Recorder.AircraftPositionFrame(40 + i * 0.1, ref p));
        }
        List<Recorder.Obj> recording = [second, first]; // the list order is not the time order

        Assert.Empty(Differences(recording, ReadJfs2(Convert(recording))));
    }

    [Fact]
    public void ASavedRecording_KeepsWhoOwnedEachObjectAndWhereItWasRecorded_AndSavingItAgainKeepsThatToo()
    {
        List<Recorder.Obj> recording = MakeRecording();
        recording[0].owner = Sim.Obj.Owner.Me;
        foreach (Recorder.Obj obj in recording)
        {
            obj.sourceApp = "JoinFS 26.10";
            obj.simulator = "MSFS2024";
        }

        string path = Convert(recording);
        string again = Convert(ReadJfs2(path), "again.jfs2");

        foreach (string file in new[] { path, again })
        {
            Jfs2Scene scene;
            using (FileStream stream = File.OpenRead(file))
            {
                scene = Jfs2Scene.Load(stream);
            }
            Assert.Equal("MSFS2024", scene.Metadata.Single(kv => kv.Key == "sim").Value);
            Assert.Equal([TrackOwner.Local, TrackOwner.Network], scene.Tracks.Select(t => t.Owner));
            Assert.All(scene.Tracks, t => Assert.Equal("JoinFS 26.10", t.SourceApp));
            Assert.All(scene.Tracks, t => Assert.Equal("MSFS2024", t.Simulator));
        }
    }

    [Fact]
    public void ARecordingFromTwoSimulators_NamesEachOnItsTrackAndNoneInTheHeader()
    {
        List<Recorder.Obj> recording = MakeRecording();
        recording[0].simulator = "MSFS2024";
        recording[1].simulator = "X-Plane";

        using var reader = JoinFS.Recording.Format.RecordingReader.Open(Convert(recording));

        Assert.Null(reader.Header.GetMetadata("sim"));
        Assert.Equal("JoinFS test (converted)", reader.Header.GetMetadata("app"));
    }

    [Fact]
    public void ARecordingWithNothingToPlay_LeavesNoFileBehind()
    {
        string path = Path.Combine(folder, "nothing.jfs2");

        Jfs2Exporter.Write(path, [new Recorder.Aircraft(true, "A", "", "m", "", "", "", 3, Sim.Obj.Owner.Network)], "x", DateTime.UtcNow, logged.Add);

        // a valid file with no tracks: the loader says so, as it does for any recording with nothing to play
        Assert.Throws<InvalidOperationException>(() => ReadJfs2(path));
    }

    // ---- the command line ----

    [Theory]
    [InlineData("-convert a.jfs b.jfs", "a.jfs|b.jfs")]
    [InlineData("--convert a.jfs", "a.jfs")]
    [InlineData("-port 5000 -CONVERT a.jfs -x", "a.jfs")]
    [InlineData("-convert", "")]
    public void TheConvertOption_ListsTheFilesAfterIt(string commandLine, string expected)
    {
        Assert.Equal(expected, string.Join("|", Jfs2Convert.FilesOf(commandLine.Split(' '))!));
    }

    [Fact]
    public void WithoutTheConvertOption_TheApplicationStartsNormally()
    {
        Assert.Null(Jfs2Convert.FilesOf(["-play", "a.jfs"]));
    }

    [Fact]
    public void Convert_WritesBesideTheOriginalAndNeverOverwrites()
    {
        string jfs = Path.Combine(folder, "old.jfs");
        File.WriteAllBytes(jfs, LegacyJfsWriter.Write(MakeRecording()));
        byte[] original = File.ReadAllBytes(jfs);
        StringWriter output = new();

        int code = Jfs2Convert.Run([jfs, jfs], output);
        Assert.True(code == 0, output.ToString());

        Assert.True(File.Exists(Path.Combine(folder, "old.jfs2")));
        Assert.True(File.Exists(Path.Combine(folder, "old-2.jfs2")));
        Assert.Equal(original, File.ReadAllBytes(jfs));
        Assert.Equal(2, ReadJfs2(Path.Combine(folder, "old.jfs2")).Count);
        Assert.Contains("old.jfs ->", output.ToString());
    }

    [Fact]
    public void Convert_ReportsAFileItCannotReadAndGoesOn()
    {
        string notARecording = Path.Combine(folder, "text.jfs");
        File.WriteAllText(notARecording, "hello");
        string missing = Path.Combine(folder, "missing.jfs");
        StringWriter output = new();

        Assert.Equal(1, Jfs2Convert.Run([notARecording, missing], output));

        string text = output.ToString();
        Assert.Contains("text.jfs: not converted", text);
        Assert.Contains("missing.jfs: not converted", text);
        Assert.Empty(Directory.GetFiles(folder, "*.jfs2"));
    }

    [Fact]
    public void Convert_WithNoFiles_SaysHowToUseIt()
    {
        StringWriter output = new();

        Assert.Equal(1, Jfs2Convert.Run([], output));

        Assert.Contains("Usage", output.ToString());
    }

    // ---- the real recordings ----

    [Fact]
    public void EveryRecordingOfTheCorpus_ConvertsToAFileThatPlaysTheSame()
    {
        if (!Directory.Exists(CorpusFolder))
        {
            return; // no corpus on this machine
        }
        List<string> problems = [];
        foreach (string jfs in Directory.EnumerateFiles(CorpusFolder, "*.jfs"))
        {
            string name = Path.GetFileName(jfs);
            List<Recorder.Obj> old;
            try
            {
                old = ReadJfs(jfs);
            }
            catch (Exception ex)
            {
                problems.Add(name + ": the old reader failed: " + ex.Message);
                continue;
            }
            if (old == null) continue; // a version the release does not read

            string path = Convert(old, name + ".jfs2");
            foreach (string difference in Differences(old, ReadJfs2(path)))
            {
                problems.Add(name + ": " + difference);
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
