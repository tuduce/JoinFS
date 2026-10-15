using JoinFS.Net;
using JoinFS.Recording;

namespace JoinFS.Tests.Recording;

/// <summary>
/// Save and <c>JoinFS -convert</c> (docs/jfs2/README.md): recorded tracks, or an old <c>.jfs</c> read by the importer, are written as
/// <c>.jfs2</c> by <see cref="Jfs2SceneWriter"/>, and the new file plays the same (design section 8: same objects, same frames per object and
/// kind, same values).
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

    internal static List<SceneTrack> ReadJfs(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return new Recorder(null!).Parse(new BinaryReader(stream))!;
    }

    internal static List<SceneTrack> ReadJfs2(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Jfs2Scene.Load(stream).Tracks;
    }

    string Convert(List<SceneTrack> tracks, string name = "out.jfs2")
    {
        string path = Path.Combine(folder, name);
        Jfs2SceneWriter.Write(path, tracks, "JoinFS test (converted)", new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc), logged.Add);
        return path;
    }

    static Dictionary<uint, T> Merge<T>(SceneTrack track, double time, Func<SceneVariablesFrame, Dictionary<uint, T>?> pick)
    {
        Dictionary<uint, T> merged = [];
        foreach (SceneVariablesFrame frame in track.Frames.OfType<SceneVariablesFrame>().Where(f => f.Time <= time))
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

    static bool IsPosition(SceneFrame frame) => frame is ScenePositionFrame or SceneObjectPositionFrame;

    /// <summary>Everything the design promises is the same, as a list of differences; empty when the two play alike.</summary>
    internal static List<string> Differences(List<SceneTrack> before, List<SceneTrack> after)
    {
        List<string> differences = [];
        // the reader leaves out tracks that never had a position, as the writer does
        // and it lists them in the order they first appear (ties keep the old order)
        before = [.. before.Where(t => t.Frames.Any(IsPosition)).OrderBy(t => t.Frames.Min(f => f.Time))];
        if (before.Count != after.Count)
        {
            differences.Add($"{before.Count} objects became {after.Count}");
            return differences;
        }
        for (int n = 0; n < before.Count; n++)
        {
            SceneTrack a = before[n], b = after[n];
            IdentityUpdate x = a.Identity, y = b.Identity;
            string who = $"object {n} ({x.Model})";
            // the identity codec limits some strings (IdentityV1Codec); FS2024 files hold a long model string in icaoType
            if ((Cut(x.Model, 256), Cut(x.Livery, 256), Cut(x.IcaoType, 8), Cut(x.IcaoAirline, 8), x.TypeRole) != (y.Model, y.Livery, y.IcaoType, y.IcaoAirline, y.TypeRole)) differences.Add(who + ": identity differs: " + string.Join("|", x.Model, x.Livery, x.IcaoType, x.IcaoAirline, x.TypeRole) + " became " + string.Join("|", y.Model, y.Livery, y.IcaoType, y.IcaoAirline, y.TypeRole));
            if ((x.IsAircraft, x.IsAircraft && x.IsPlane, Cut(x.Callsign, 32), x.IsAircraft ? a.Nickname : "") != (y.IsAircraft, y.IsAircraft && y.IsPlane, y.Callsign ?? "", y.IsAircraft ? b.Nickname : "")) differences.Add(who + ": aircraft identity differs");

            foreach (var (kind, count, count2) in new[]
            {
                ("aircraft position", a.Frames.OfType<ScenePositionFrame>().Count(), b.Frames.OfType<ScenePositionFrame>().Count()),
                ("object position", a.Frames.OfType<SceneObjectPositionFrame>().Count(), b.Frames.OfType<SceneObjectPositionFrame>().Count()),
                ("event", a.Frames.OfType<SceneEventFrame>().Count(), b.Frames.OfType<SceneEventFrame>().Count()),
            })
            {
                if (count != count2) differences.Add($"{who}: {count} {kind} frames became {count2}");
            }

            // positions, pairwise. The writer writes frames in time order, so a frame that came late in the old file is in its place,
            // and a time before the start of the recording is held at 0 (spec 5)
            var positionsA = a.Frames.Where(IsPosition).OrderBy(f => f.Time).ToList();
            var positionsB = b.Frames.Where(IsPosition).ToList();
            for (int i = 0; i < Math.Min(positionsA.Count, positionsB.Count); i++)
            {
                if (Math.Abs(positionsB[i].Time - Math.Max(positionsA[i].Time, 0)) > 1e-6) { differences.Add($"{who}: position {i} time {positionsA[i].Time} became {positionsB[i].Time}"); break; }
                if (!SamePosition(positionsA[i], positionsB[i], out string why)) { differences.Add($"{who}: position {i}: {why}"); break; }
            }

            var eventsA = a.Frames.OfType<SceneEventFrame>().Select(f => (f.EventId, f.Data)).ToList();
            var eventsB = b.Frames.OfType<SceneEventFrame>().Select(f => (f.EventId, f.Data)).ToList();
            if (!eventsA.SequenceEqual(eventsB)) differences.Add(who + ": events differ");

            // variables: the old file holds samples, the new one changes, so compare what they add up to at several moments
            double end = a.Frames.Count == 0 ? 0 : a.Frames.Max(f => f.Time);
            foreach (double fraction in new[] { 0.1, 0.5, 0.9, 1.0 })
            {
                double time = end * fraction;
                if (!SameVariables(Merge(a, time, f => f.ByKind().Integers), Merge(b, time, f => f.ByKind().Integers), (p, q) => p == q)) differences.Add($"{who}: integer variables differ at {time:0.0} s");
                if (!SameVariables(Merge(a, time, f => f.ByKind().Floats), Merge(b, time, f => f.ByKind().Floats), (p, q) => BitConverter.SingleToInt32Bits(p) == BitConverter.SingleToInt32Bits(q))) differences.Add($"{who}: float variables differ at {time:0.0} s");
                if (!SameVariables(Merge(a, time, f => f.ByKind().Strings), Merge(b, time, f => f.ByKind().Strings), (p, q) => p == q)) differences.Add($"{who}: string variables differ at {time:0.0} s");
            }
        }
        return differences;
    }

    static string Cut(string text, int limit) => (text ??= "").Length <= limit ? text : text[..limit];

    static bool SameVariables<T>(Dictionary<uint, T> a, Dictionary<uint, T> b, Func<T, T, bool> same) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out T? other) && same(pair.Value, other));

    /// <summary>The five control axes are held to 1/16384 (spec 7.3), the rest exactly.</summary>
    static bool SamePosition(SceneFrame a, SceneFrame b, out string why)
    {
        why = "";
        static bool Near(float p, float q) => (float.IsNaN(p) && float.IsNaN(q)) || Math.Abs(p - q) <= 1.0f / 16384;
        static bool Same(float p, float q) => BitConverter.SingleToInt32Bits(p) == BitConverter.SingleToInt32Bits(q);
        if (a is ScenePositionFrame pa && b is ScenePositionFrame pb)
        {
            Sim.AircraftPosition p = SimMessageMapper.ToAircraftPosition(pa.Position), q = SimMessageMapper.ToAircraftPosition(pb.Position);
            if (p.latitude != q.latitude || p.longitude != q.longitude || p.altitude != q.altitude) { why = "coordinates"; return false; }
            if (!Same(p.pitch, q.pitch) || !Same(p.bank, q.bank) || !Same(p.heading, q.heading)) { why = "attitude"; return false; }
            if (!Same(p.velocityX, q.velocityX) || !Same(p.velocityY, q.velocityY) || !Same(p.velocityZ, q.velocityZ)) { why = "velocity"; return false; }
            if (!Same(p.angularVelocityX, q.angularVelocityX) || !Same(p.angularVelocityY, q.angularVelocityY) || !Same(p.angularVelocityZ, q.angularVelocityZ)) { why = "angular velocity"; return false; }
            if (!Same(p.accelerationX, q.accelerationX) || !Same(p.accelerationY, q.accelerationY) || !Same(p.accelerationZ, q.accelerationZ)) { why = "acceleration"; return false; }
            if (!Near(p.rudder, q.rudder) || !Near(p.elevator, q.elevator) || !Near(p.aileron, q.aileron) || !Near(p.brakeLeft, q.brakeLeft) || !Near(p.brakeRight, q.brakeRight)) { why = "controls"; return false; }
            if (!Same(p.elevation, q.elevation) || p.ground != q.ground) { why = "elevation or ground"; return false; }
            // an old file never held it; the importer says "no data" with NaN (design section 8)
            if (!Same(p.staticCgToGround, q.staticCgToGround) && !(float.IsNaN(q.staticCgToGround) && p.staticCgToGround == 0)) { why = "staticCgToGround"; return false; }
            return true;
        }
        if (a is SceneObjectPositionFrame oa && b is SceneObjectPositionFrame ob)
        {
            Sim.ObjectPositionVelocity p = SimMessageMapper.ToPositionVelocity(oa.Position), q = SimMessageMapper.ToPositionVelocity(ob.Position);
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

    /// <summary>A track as the recorder makes one; owner, source application and simulator can be set for a test.</summary>
    internal static SceneTrack Track(bool aircraft, string callsign, string nickname, string model, string livery = "", string icaoType = "", byte typeRole = 3,
        TrackOwner owner = TrackOwner.Network, string sourceApp = "", string simulator = "") => new()
    {
        Nickname = nickname,
        Owner = owner,
        SourceApp = sourceApp,
        Simulator = simulator,
        Identity = new IdentityUpdate { IsAircraft = aircraft, IsPlane = aircraft, Callsign = callsign, Model = model, Livery = livery, IcaoType = icaoType, IcaoAirline = "", TypeRole = typeRole },
    };

    /// <summary>The same track with other owner, source application or simulator</summary>
    static SceneTrack Retagged(SceneTrack track, TrackOwner? owner = null, string? sourceApp = null, string? simulator = null)
    {
        var copy = new SceneTrack
        {
            Nickname = track.Nickname,
            Owner = owner ?? track.Owner,
            SourceApp = sourceApp ?? track.SourceApp,
            Simulator = simulator ?? track.Simulator,
            Identity = track.Identity,
        };
        copy.Frames.AddRange(track.Frames);
        return copy;
    }

    internal static ScenePositionFrame Position(double time, Sim.AircraftPosition p) =>
        new(time, SimMessageMapper.ToPositionUpdate(0, time, ref p, p.ground != 0 ? PositionStateFlags.OnGround : PositionStateFlags.None));

    internal static SceneVariablesFrame Variables(double time, Dictionary<uint, int>? integers, Dictionary<uint, float>? floats = null, Dictionary<uint, string>? strings = null) =>
        new(time, new VariableSyncUpdate { Entries = SimMessageMapper.ToVariableEntries(integers, floats, strings) });

    internal static List<SceneTrack> MakeRecording()
    {
        SceneTrack plane = Track(true, "DLH1", "Pilot", "Cessna 172", "Red", "C172");
        SceneTrack boat = Track(false, "", "", "Ferry", typeRole: 1);
        for (int i = 0; i < 400; i++)
        {
            double t = i * 0.05;
            plane.Frames.Add(Position(t, Aircraft(i)));
            if (i % 10 == 0)
            {
                plane.Frames.Add(Variables(t, new Dictionary<uint, int> { [1] = i < 200 ? 0 : 1, [2] = 7 }, new Dictionary<uint, float> { [3] = i * 0.5f }, new Dictionary<uint, string> { [4] = "N123" }));
            }
            if (i == 150)
            {
                plane.Frames.Add(new SceneEventFrame(t, 66000, 1));
            }
            Sim.ObjectPositionVelocity b = new() { latitude = 0.89 + i * 1e-6, longitude = 0.02, altitude = 2, heading = 3, height = 1.5f, ground = 1 };
            boat.Frames.Add(new SceneObjectPositionFrame(t + 0.01, SimMessageMapper.ToObjectPositionUpdate(0, t + 0.01, ref b, PositionStateFlags.OnGround)));
        }
        return [plane, boat, Track(true, "NOPOS", "", "x")];
    }

    [Fact]
    public void ARecordingInTheOldFormat_ConvertsToAFileThatPlaysTheSame()
    {
        string jfs = Path.Combine(folder, "old.jfs");
        File.WriteAllBytes(jfs, LegacyJfsWriter.Write(MakeRecording()));
        List<SceneTrack> old = ReadJfs(jfs);
        Assert.Equal(3, old.Count);

        string path = Convert(old);
        List<SceneTrack> converted = ReadJfs2(path);

        Assert.Empty(Differences(old, converted));
        Assert.Empty(Differences(MakeRecording(), converted));
        Assert.Equal(2, converted.Count); // the aircraft without a position is left out
        Assert.Empty(logged);
    }

    [Fact]
    public void TheConvertedFile_IsMuchSmallerThanTheVariableSamplesItCameFrom()
    {
        List<SceneTrack> recording = MakeRecording();
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
        SceneTrack first = Track(true, "A", "", "m");
        SceneTrack second = Track(true, "B", "", "m");
        for (int i = 0; i < 300; i++)
        {
            first.Frames.Add(Position(i * 0.1, Aircraft(i)));
            second.Frames.Add(Position(40 + i * 0.1, Aircraft(i)));
        }
        List<SceneTrack> recording = [second, first]; // the list order is not the time order

        Assert.Empty(Differences(recording, ReadJfs2(Convert(recording))));
    }

    [Fact]
    public void ASavedRecording_KeepsWhoOwnedEachObjectAndWhereItWasRecorded_AndSavingItAgainKeepsThatToo()
    {
        List<SceneTrack> recording = [.. MakeRecording().Select((t, n) => Retagged(t, n == 0 ? TrackOwner.Local : null, "JoinFS 26.10", "MSFS2024"))];

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
        List<SceneTrack> recording = MakeRecording();
        recording[0] = Retagged(recording[0], simulator: "MSFS2024");
        recording[1] = Retagged(recording[1], simulator: "X-Plane");

        using var reader = JoinFS.Recording.Format.RecordingReader.Open(Convert(recording));

        Assert.Null(reader.Header.GetMetadata("sim"));
        Assert.Equal("JoinFS test (converted)", reader.Header.GetMetadata("app"));
    }

    [Fact]
    public void ARecordingWithNothingToPlay_LeavesNoFileBehind()
    {
        string path = Path.Combine(folder, "nothing.jfs2");

        Jfs2SceneWriter.Write(path, [Track(true, "A", "", "m")], "x", DateTime.UtcNow, logged.Add);

        // a valid file with no tracks: the reader says so, as it does for any recording with nothing to play
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
}
