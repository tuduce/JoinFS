using JoinFS.Net;
using JoinFS.Recording;

namespace JoinFS.Tests.Recording;

/// <summary>
/// The importer of old <c>.jfs</c> recordings (docs/jfs2/recorder-v2-design.md section 8): the frozen old reader, free of Sim and of build
/// symbols. The in-app reader (<see cref="Recorder.Parse"/>) is its oracle for the files that reader gets right.
/// </summary>
public sealed class LegacyJfsImporterTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("jfs-importer-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(folder, true); } catch (IOException) { }
    }

    // ---- hand-made old files, written the way each build variant wrote them ----

    const short Version = 21005;

    static void WriteAircraftFrame(BinaryWriter w, double time, double latitude)
    {
        w.Write((byte)1); // AircraftPosition
        w.Write(time);
        w.Write(latitude);
        w.Write(0.14);
        w.Write(500.0);
        for (int i = 0; i < 12; i++) w.Write(0.25f + i); // attitude, velocity, angular velocity, acceleration
        w.Write((short)8192); // rudder 0.5
        w.Write((short)-16384); // elevator -1
        w.Write((short)0);
        w.Write((short)0);
        w.Write((short)16384); // brake right 1
        w.Write(321.5f); // elevation
        w.Write((byte)0x03); // on ground, elevation correction
    }

    static void WriteTail(BinaryWriter w, bool fs2024, string livery, string icaoType, string icaoAirline)
    {
        if (fs2024) w.Write(livery);
        w.Write(icaoType);
        w.Write(icaoAirline);
    }

    /// <summary>One aircraft with the given frame times, then (optionally) one plain object that records every kind of frame.</summary>
    static byte[] OldFile(bool fs2024, double[] times, bool withObject = false, short version = Version)
    {
        var stream = new MemoryStream();
        var w = new BinaryWriter(stream);
        w.Write(version);
        w.Write(1);
        w.Write(true); // plane
        w.Write("DLH1");
        w.Write("Pilot");
        w.Write("Cessna 172");
        w.Write((byte)3);
        w.Write(times.Length);
        foreach (double time in times)
        {
            WriteAircraftFrame(w, time, 0.82 + time * 1e-6);
        }
        WriteTail(w, fs2024, "Red", "C172", "DLH");

        if (withObject)
        {
            w.Write(1);
            w.Write("Ferry");
            w.Write((byte)1);
            w.Write(4);
            w.Write((byte)0); // ObjectPosition
            w.Write(1.0);
            w.Write(0.89);
            w.Write(0.02);
            w.Write(2.0);
            for (int i = 0; i < 12; i++) w.Write(1.0f + i);
            w.Write(1.5f); // height
            w.Write((byte)0x01);
            w.Write((byte)10); // SimEvent on a plain object (recording-protocol.md 7.2)
            w.Write(1.5);
            w.Write(66000u);
            w.Write(7u);
            w.Write((byte)11); // IntegerVariables
            w.Write(2.0);
            w.Write((ushort)2);
            w.Write(10u); w.Write(100);
            w.Write(11u); w.Write(-5);
            w.Write((byte)13); // String8Variables
            w.Write(2.5);
            w.Write((ushort)1);
            w.Write(12u); w.Write("N123");
            WriteTail(w, fs2024, "Blue", "", "");
        }
        return stream.ToArray();
    }

    static LegacyJfsImporter.Result Import(byte[] file) => LegacyJfsImporter.Read(new MemoryStream(file));

    // ---- the two layouts of the trailing strings ----

    [Fact]
    public void AnFs2024File_ReadsLiveryAndIcao()
    {
        var result = Import(OldFile(fs2024: true, [1.0, 2.0]));

        SceneTrack track = Assert.Single(result.Scene.Tracks);
        Assert.Equal(LegacyJfsImporter.TailLayout.Fs2024, result.Layout);
        Assert.Equal(("DLH1", "Cessna 172", "Red", "C172", "DLH", (byte)3), (track.Identity.Callsign, track.Identity.Model, track.Identity.Livery, track.Identity.IcaoType, track.Identity.IcaoAirline, track.Identity.TypeRole));
        Assert.Equal("Pilot", track.Nickname);
        Assert.True(track.Identity.IsAircraft && track.Identity.IsPlane);
    }

    [Fact]
    public void AFileFromAnotherBuild_ReadsTwoStringsAndNoLivery()
    {
        var result = Import(OldFile(fs2024: false, [1.0, 2.0]));

        Assert.Equal(LegacyJfsImporter.TailLayout.Other, result.Layout);
        SceneTrack track = Assert.Single(result.Scene.Tracks);
        Assert.Equal(("", "C172", "DLH"), (track.Identity.Livery, track.Identity.IcaoType, track.Identity.IcaoAirline));
    }

    [Fact]
    public void AnObjectAfterTheAircraft_IsReadInEitherLayout()
    {
        foreach (bool fs2024 in new[] { true, false })
        {
            var result = Import(OldFile(fs2024, [1.0], withObject: true));

            Assert.Equal(2, result.Scene.Tracks.Count);
            Assert.Equal(fs2024 ? LegacyJfsImporter.TailLayout.Fs2024 : LegacyJfsImporter.TailLayout.Other, result.Layout);
            Assert.Equal("Ferry", result.Scene.Tracks[1].Identity.Model);
            Assert.False(result.Scene.Tracks[1].Identity.IsAircraft);
        }
    }

    [Fact]
    public void BeforeTheTailExisted_ThereIsOnlyOneWayToRead()
    {
        var stream = new MemoryStream();
        var w = new BinaryWriter(stream);
        w.Write((short)21003);
        w.Write(1);
        w.Write(true); w.Write("A"); w.Write(""); w.Write("m"); w.Write((byte)0);
        w.Write(1);
        WriteAircraftFrame(w, 1.0, 0.8);

        var result = Import(stream.ToArray());

        Assert.Equal("m", Assert.Single(result.Scene.Tracks).Identity.Model);
        Assert.Empty(result.Scene.Warnings);
    }

    // ---- what the frames become ----

    [Fact]
    public void FramesBecomeCanonicalMessages()
    {
        SceneTrack track = Assert.Single(Import(OldFile(true, [2.0, 1.0])).Scene.Tracks);

        Assert.Equal([1.0, 2.0], track.Frames.Select(f => f.Time)); // the old file may hold frames out of order
        PositionUpdate position = Assert.IsType<ScenePositionFrame>(track.Frames[0]).Position;
        Assert.Equal(0.82 + 1e-6, position.Latitude);
        Assert.Equal((0.5f, -1f, 0f, 0f, 1f), (position.Rudder, position.Elevator, position.Aileron, position.BrakeLeft, position.BrakeRight));
        Assert.Equal(321.5f, position.Elevation);
        Assert.Equal(PositionStateFlags.OnGround | PositionStateFlags.ElevationCorrection, position.StateFlags);
        Assert.True(float.IsNaN(position.StaticCgToGround)); // the old format never held it
    }

    [Fact]
    public void ThePlainObjectKeepsEveryKindOfFrame_WhichTheInAppReaderLoses()
    {
        SceneTrack boat = Import(OldFile(true, [1.0], withObject: true)).Scene.Tracks[1];

        Assert.Collection(boat.Frames,
            f => Assert.Equal(1.5f, Assert.IsType<SceneObjectPositionFrame>(f).Position.Height),
            f => Assert.Equal((66000u, 7u), (Assert.IsType<SceneEventFrame>(f).EventId, ((SceneEventFrame)f).Data)),
            f => Assert.Equal([(10u, 100), (11u, -5)], Assert.IsType<SceneVariablesFrame>(f).Variables.Entries.Select(e => (e.Vuid, e.IntValue))),
            f => Assert.Equal("N123", Assert.Single(Assert.IsType<SceneVariablesFrame>(f).Variables.Entries).StringValue));
    }

    // ---- files it cannot read ----

    [Fact]
    public void AReservedFrameType_StopsWithAClearError()
    {
        byte[] file = OldFile(true, [1.0]);
        // the aircraft's first frame type byte follows version, count, plane, 3 strings, typerole and the frame count
        int at = 2 + 4 + 1 + (1 + 4) + (1 + 5) + (1 + 10) + 1 + 4;
        file[at] = 4; // AircraftState: no known layout

        var ex = Assert.Throws<InvalidDataException>(() => Import(file));

        Assert.Contains("frame type 4 has no known layout", ex.Message);
    }

    [Fact]
    public void ATruncatedFile_IsRefusedNotMisread()
    {
        byte[] file = OldFile(true, [1.0, 2.0, 3.0]);

        Assert.Throws<InvalidDataException>(() => Import(file[..(file.Length - 30)]));
    }

    [Fact]
    public void BytesAfterTheLastObject_AreRefused()
    {
        byte[] file = [.. OldFile(true, [1.0]), 1, 2, 3];

        var ex = Assert.Throws<InvalidDataException>(() => Import(file));

        Assert.Contains("cannot be read", ex.Message);
    }

    [Fact]
    public void AnOldVersionAndANonRecording_SayWhy()
    {
        Assert.Contains("older than JoinFS reads", Assert.Throws<InvalidDataException>(() => Import(OldFile(true, [1.0], version: 10000))).Message);
        Assert.Throws<InvalidDataException>(() => Import([]));
        Assert.Throws<InvalidDataException>(() => Import("hello, this is text"u8.ToArray()));
    }

    [Fact]
    public void TheStartIsTheEndMinusTheLengthOfTheRecording()
    {
        var ended = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

        var scene = LegacyJfsImporter.Read(new MemoryStream(OldFile(true, [1.0, 61.0])), ended).Scene;

        Assert.Equal(ended.AddSeconds(-61), scene.StartUtc);
    }

    // ---- the whole way: old file -> importer -> JFS2 file -> loader ----

    [Fact]
    public void ImportedAndWritten_PlaysTheSameAsTheInAppReader()
    {
        string jfs = Path.Combine(folder, "old.jfs");
        using (FileStream stream = File.Create(jfs))
        {
            new Recorder(null!).Write(new BinaryWriter(stream), Jfs2ImportTests.MakeRecording());
        }
        List<Recorder.Obj> oracle = Jfs2ImportTests.ReadJfs(jfs);

        string path = Path.Combine(folder, "out.jfs2");
        LegacyJfsImporter.Result imported = LegacyJfsImporter.Read(File.OpenRead(jfs));
        Jfs2SceneWriter.Write(path, imported.Scene.Tracks, "test", DateTime.UtcNow, _ => { });

        Assert.Empty(Jfs2ImportTests.Differences(oracle, Jfs2ImportTests.ReadJfs2(path)));
        Assert.Empty(imported.Scene.Warnings);
    }

    [Fact]
    public void EveryRecordingOfTheCorpus_IsImportedAsTheInAppReaderReadsIt()
    {
        if (!Directory.Exists(Jfs2ImportTests.CorpusFolder))
        {
            return; // no corpus on this machine
        }
        List<string> problems = [];
        foreach (string jfs in Directory.EnumerateFiles(Jfs2ImportTests.CorpusFolder, "*.jfs"))
        {
            string name = Path.GetFileName(jfs);
            try
            {
                List<Recorder.Obj> oracle = Jfs2ImportTests.ReadJfs(jfs);
                LegacyJfsImporter.Result imported;
                using (FileStream stream = File.OpenRead(jfs))
                {
                    imported = LegacyJfsImporter.Read(stream, File.GetLastWriteTimeUtc(jfs));
                }
                if (imported.Layout != LegacyJfsImporter.TailLayout.Fs2024) problems.Add(name + ": layout " + imported.Layout);
                foreach (string warning in imported.Scene.Warnings) problems.Add(name + ": " + warning); // the layout is not ambiguous
                string path = Path.Combine(folder, name + ".jfs2");
                Jfs2SceneWriter.Write(path, imported.Scene.Tracks, "test", imported.Scene.StartUtc, _ => { });
                foreach (string difference in Jfs2ImportTests.Differences(oracle, Jfs2ImportTests.ReadJfs2(path)))
                {
                    problems.Add(name + ": " + difference);
                }
            }
            catch (Exception ex)
            {
                problems.Add(name + ": " + ex.Message);
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
