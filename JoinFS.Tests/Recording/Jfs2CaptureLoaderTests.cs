using JoinFS.Net;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Recording;
using JoinFS.Recording.Format;

namespace JoinFS.Tests.Recording;

/// <summary>
/// The stage 2 tracer bullet of the recorder v2 (docs/jfs2/README.md): what <see cref="Jfs2Capture"/> writes,
/// <see cref="Jfs2Loader"/> reads back into the objects the old player plays.
/// </summary>
public sealed class Jfs2CaptureLoaderTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("jfs2-capture-").FullName;
    readonly List<string> logged = [];

    public void Dispose()
    {
        try { Directory.Delete(folder, true); } catch (IOException) { }
    }

    string PathOf(string name) => Path.Combine(folder, name);

    Jfs2Capture NewCapture(string name = "a.jfs2") =>
        Jfs2Capture.Create(PathOf(name), "JoinFS test", "MSFS2024", new DateTime(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc), logged.Add);

    static IdentityUpdate Identity(string callsign, string model, bool aircraft = true) => new()
    {
        IsAircraft = aircraft,
        IsPlane = true,
        Callsign = callsign,
        Model = model,
        Livery = "Livery",
        IcaoType = "C172",
        IcaoAirline = "",
        Registration = "N1",
        FlightNumber = "",
        ClassCode = "",
        Wtc = "L",
        TypeRole = 3,
    };

    static PositionUpdate Position(double step) => new()
    {
        Latitude = 47.0 + step * 0.001,
        Longitude = 8.0 - step * 0.001,
        Altitude = 1000 + step,
        Pitch = 0.1f,
        Bank = -0.2f,
        Heading = 1.5f,
        VelocityX = 10,
        VelocityY = 0,
        VelocityZ = 50,
        Rudder = 0.25f,
        Elevator = -0.5f,
        Aileron = 0.125f,
        BrakeLeft = 0f,
        BrakeRight = 1f,
        Elevation = 300,
        StaticCgToGround = float.NaN,
        StateFlags = PositionStateFlags.OnGround,
    };

    static Stream Open(string path) => new MemoryStream(File.ReadAllBytes(path));

    [Fact]
    public void RoundTrip_GivesTheObjectsTheOldPlayerPlays()
    {
        var capture = NewCapture();
        capture.OpenTrack(10000, 0.0, TrackOwner.Local, "me", "JoinFS test", "MSFS2024", Identity("DLH1", "Cessna 172"));
        capture.OpenTrack(10001, 0.5, TrackOwner.Network, "other", "JoinFS test", "MSFS2024", Identity("DLH2", "Piper"));
        for (int i = 0; i < 100; i++)
        {
            PositionUpdate a = Position(i);
            capture.Position(10000, i * 0.05, ref a);
            PositionUpdate b = Position(i + 1000);
            capture.Position(10001, 0.5 + i * 0.05, ref b);
        }
        capture.Finish();

        Assert.False(capture.Failed);
        Assert.Equal(2, capture.TrackCount);
        Assert.Equal(200, capture.PositionCount);

        var result = Jfs2Loader.Load(Open(PathOf("a.jfs2")));

        Assert.Empty(result.Notes);
        Assert.Equal(2, result.Objects.Count);
        var first = Assert.IsType<Recorder.Aircraft>(result.Objects[0]);
        var second = Assert.IsType<Recorder.Aircraft>(result.Objects[1]);
        Assert.Equal(("DLH1", "me", "Cessna 172", "Livery", "C172"), (first.callsign, first.nickname, first.model, first.livery, first.icaoType));
        Assert.Equal(("DLH2", "other", "Piper"), (second.callsign, second.nickname, second.model));
        Assert.Equal(3, first.typerole);
        Assert.Equal(Sim.Obj.Owner.Recorder, first.owner);
        Assert.Equal(100, first.frames.Count);
        Assert.Equal(100, second.frames.Count);

        var frame = Assert.IsType<Recorder.AircraftPositionFrame>(first.frames[40]);
        Assert.Equal(2.0, frame.time, 5);
        Assert.Equal(47.04, frame.data.latitude, 9);
        Assert.Equal(1040.0, frame.data.altitude, 9);
        Assert.Equal(1.5f, frame.data.heading);
        Assert.Equal(1, frame.data.ground);
        Assert.Equal(0.25f, frame.data.rudder, 4);
        Assert.Equal(-0.5f, frame.data.elevator, 4);
        Assert.True(float.IsNaN(frame.data.staticCgToGround));
        Assert.Equal(0.5, second.frames[0].time, 5);
    }

    [Fact]
    public void LongRecording_IsSeveralChunks()
    {
        var capture = NewCapture();
        capture.OpenTrack(10000, 0.0, TrackOwner.Local, "", "JoinFS test", "", Identity("A", "M"));
        for (int i = 0; i < 400; i++)
        {
            PositionUpdate p = Position(i);
            capture.Position(10000, i * 0.05, ref p);
        }
        capture.Finish();

        using var reader = RecordingReader.Open(PathOf("a.jfs2"));
        Assert.True(reader.IsFinished);
        Assert.True(reader.Chunks.Count >= 9, "20 s at about 2 s per chunk: " + reader.Chunks.Count);
        Assert.True(reader.Chunks[0].IsKeyframe);
        Assert.Equal("JoinFS test", reader.Header.GetMetadata("app"));
        Assert.Equal("MSFS2024", reader.Header.GetMetadata("sim"));
        Assert.Equal(new DateTime(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc), DateTime.UnixEpoch.AddTicks(reader.Header.StartUtcMicroseconds * 10));
        var track = Assert.Single(reader.Tracks);
        Assert.Equal(("A", "M"), (track.Callsign, track.Model));
    }

    [Fact]
    public void TheBodyOfAPosition_CarriesTheTrackAndTheTime()
    {
        var capture = NewCapture();
        capture.OpenTrack(77, 1.0, TrackOwner.Local, "", "JoinFS test", "", Identity("A", "M"));
        PositionUpdate p = Position(0);
        p.ObjectId = 12345; // a network id: not the track
        p.NetTime = 999_999;
        capture.Position(77, 2.5, ref p);
        capture.Finish();

        using var reader = RecordingReader.Open(PathOf("a.jfs2"));
        Assert.True(reader.TryReadChunk(0, out RecordChunk? chunk));
        uint position = reader.FindKind("position", 1)!.Value;
        RecordEntry entry = chunk!.Records.Single(r => r.Kind == position);
        PositionUpdate body = new PositionV1Codec().Decode(entry.Body.Span);
        Assert.Equal(77u, body.ObjectId);
        Assert.Equal(2.5, body.NetTime, 6);
        Assert.Equal(2_500_000, entry.Time);
    }

    [Fact]
    public void TimesThatGoBack_AreHeldNotRejected()
    {
        var capture = NewCapture();
        capture.OpenTrack(1, 5.0, TrackOwner.Local, "", "x", "", Identity("A", "M"));
        PositionUpdate p = Position(0);
        capture.Position(1, 6.0, ref p);
        capture.Position(1, 5.5, ref p); // earlier than the last: a clock step
        capture.Position(1, 7.0, ref p);
        capture.Finish();

        Assert.False(capture.Failed, string.Join(" ", logged));
        var result = Jfs2Loader.Load(Open(PathOf("a.jfs2")));
        var times = result.Objects.Cast<Recorder.Aircraft>().Single().frames.Select(f => f.time).ToArray();
        Assert.Equal([6.0, 6.0, 7.0], times);
    }

    [Fact]
    public void ATrackThatWasNeverOpened_IsIgnored()
    {
        var capture = NewCapture();
        PositionUpdate p = Position(0);
        capture.Position(5, 1.0, ref p);
        capture.OpenTrack(1, 1.0, TrackOwner.Local, "", "x", "", Identity("A", "M"));
        capture.Position(1, 1.0, ref p);
        capture.Finish();

        Assert.False(capture.Failed);
        Assert.Equal(1, capture.PositionCount);
    }

    [Fact]
    public void AFileThatWasNeverFinished_PlaysAsFarAsItWasWritten()
    {
        var capture = NewCapture();
        capture.OpenTrack(1, 0.0, TrackOwner.Local, "", "x", "", Identity("A", "M"));
        for (int i = 0; i < 200; i++)
        {
            PositionUpdate p = Position(i);
            capture.Position(1, i * 0.05, ref p);
        }
        // the program dies here: what is on disk is what the chunks that ended carried
        using var file = new FileStream(PathOf("a.jfs2"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var onDisk = new MemoryStream();
        file.CopyTo(onDisk);
        onDisk.Position = 0;
        capture.Dispose();

        var result = Jfs2Loader.Load(onDisk);

        Assert.Contains(result.Notes, n => n.Contains("not finished"));
        var aircraft = Assert.IsType<Recorder.Aircraft>(Assert.Single(result.Objects));
        Assert.InRange(aircraft.frames.Count, 60, 200);
    }

    [Fact]
    public void RecordsOfOtherKinds_AreSkippedAndCounted()
    {
        var path = PathOf("other.jfs2");
        using (var writer = RecordingWriter.Create(path, new RecordingHeader()))
        {
            var kinds = Jfs2KindIds.Bind(writer);
            uint weather = writer.BindKind("weather", 1);
            writer.Add(kinds.TrackOpen, 1, 0, new TrackOpenV1(TrackOwner.Local, "n", "app", "").Encode());
            var identity = Identity("A", "M");
            identity.ObjectId = 1;
            byte[] id = new byte[IdentityV1Codec.MaxSize];
            writer.Add(kinds.Identity, 1, 0, id.AsSpan(0, new IdentityV1Codec().Encode(identity, id)));
            byte[] pos = new byte[PositionV1Codec.Size];
            new PositionV1Codec().Encode(Position(0), pos);
            writer.Add(kinds.Position, 1, 10, pos);
            writer.Add(weather, 0, 20, new byte[] { 1, 2, 3 });
            writer.Add(weather, 0, 30, new byte[] { 1, 2, 3 });
            writer.Finish();
        }

        var result = Jfs2Loader.Load(Open(path));

        Assert.Single(result.Objects);
        Assert.Contains(result.Notes, n => n.StartsWith("2 'weather v1' records were skipped"));
    }

    [Fact]
    public void ARecordingWhoseEssentialKindIsNewer_SaysSoInsteadOfPlayingAnEmptyScene()
    {
        var path = PathOf("newer.jfs2");
        using (var writer = RecordingWriter.Create(path, new RecordingHeader()))
        {
            uint open = writer.BindKind(Jfs2Kinds.TrackOpen, 1);
            uint position2 = writer.BindKind("position", 2);
            writer.Add(open, 1, 0, new TrackOpenV1(TrackOwner.Local, "", "", "").Encode());
            writer.Add(position2, 1, 5, new byte[] { 1, 2, 3 });
            writer.Finish();
        }

        var ex = Assert.Throws<InvalidOperationException>(() => Jfs2Loader.Load(Open(path)));

        Assert.Contains("newer version", ex.Message);
    }

    [Fact]
    public void ARecordWithAShortBody_IsDamageNotACrash()
    {
        var path = PathOf("short.jfs2");
        using (var writer = RecordingWriter.Create(path, new RecordingHeader()))
        {
            var kinds = Jfs2KindIds.Bind(writer);
            writer.Add(kinds.TrackOpen, 1, 0, new TrackOpenV1(TrackOwner.Local, "", "", "").Encode());
            var identity = Identity("A", "M");
            byte[] id = new byte[IdentityV1Codec.MaxSize];
            writer.Add(kinds.Identity, 1, 0, id.AsSpan(0, new IdentityV1Codec().Encode(identity, id)));
            byte[] pos = new byte[PositionV1Codec.Size];
            new PositionV1Codec().Encode(Position(0), pos);
            writer.Add(kinds.Position, 1, 10, pos);
            writer.Add(kinds.Position, 1, 20, new byte[10]);
            writer.Finish();
        }

        var result = Jfs2Loader.Load(Open(path));

        Assert.Single(Assert.Single(result.Objects).frames);
        Assert.Contains(result.Notes, n => n.StartsWith("1 records could not be read"));
    }

    [Fact]
    public void ANonJfs2File_SaysWhy()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Jfs2Loader.Load(new MemoryStream(new byte[100])));

        Assert.Contains("cannot be opened", ex.Message);
    }

    [Fact]
    public void AfterFinish_NothingIsWrittenAndNothingIsReported()
    {
        var capture = NewCapture();
        capture.OpenTrack(1, 0.0, TrackOwner.Local, "", "x", "", Identity("A", "M"));
        PositionUpdate p = Position(0);
        capture.Position(1, 1.0, ref p);
        capture.Finish();

        capture.Position(1, 2.0, ref p);
        capture.OpenTrack(2, 2.0, TrackOwner.Local, "", "x", "", Identity("B", "M"));
        capture.Finish();

        Assert.False(capture.Failed);
        Assert.Empty(logged);
        Assert.Equal(1, capture.PositionCount);
        Assert.Equal(1, capture.TrackCount);
    }

    [Fact]
    public void TrackOpen_RoundTripsAndIgnoresWhatFollows()
    {
        var open = new TrackOpenV1(TrackOwner.Network, "Pilot é", new string('x', 300), "");
        byte[] body = open.Encode();

        Assert.Equal(open, TrackOpenV1.Decode(body));
        Assert.Equal(open, TrackOpenV1.Decode([.. body, 9, 9, 9]));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 0, 5, 65 })]
    [InlineData(new byte[] { 0, 0x80 })]
    public void TrackOpen_RejectsATruncatedBody(byte[] body)
    {
        Assert.Throws<FormatException>(() => TrackOpenV1.Decode(body));
    }
}
