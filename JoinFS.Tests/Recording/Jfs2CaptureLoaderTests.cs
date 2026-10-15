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

    // ---- stage 3: variables and events ----

    static Recorder.Aircraft OnlyAircraft(Jfs2Loader.Result result) => Assert.IsType<Recorder.Aircraft>(Assert.Single(result.Objects));

    /// <summary>The variables an old-player object would hold after playing its frames up to <paramref name="time"/>, in order.</summary>
    static (Dictionary<uint, int> Integers, Dictionary<uint, float> Floats, Dictionary<uint, string> Strings) Merged(Recorder.Obj obj, double time = double.MaxValue)
    {
        Dictionary<uint, int> integers = [];
        Dictionary<uint, float> floats = [];
        Dictionary<uint, string> strings = [];
        foreach (var frame in obj.frames.Where(f => f.time <= time))
        {
            switch (frame)
            {
                case Recorder.IntegerVariablesFrame f: foreach (var v in f.variables) integers[v.Key] = v.Value; break;
                case Recorder.FloatVariablesFrame f: foreach (var v in f.variables) floats[v.Key] = v.Value; break;
                case Recorder.String8VariablesFrame f: foreach (var v in f.variables) strings[v.Key] = v.Value; break;
            }
        }
        return (integers, floats, strings);
    }

    [Fact]
    public void Variables_AreStoredAsChanges_AndMergeBackToTheFullSet()
    {
        var capture = NewCapture();
        capture.OpenTrack(1, 0.0, TrackOwner.Local, "", "x", "", Identity("A", "M"));
        PositionUpdate p = Position(0);
        capture.Position(1, 0.0, ref p);
        Dictionary<uint, int> integers = new() { [10] = 1, [11] = 2, [12] = 3 };
        Dictionary<uint, float> floats = new() { [20] = 0.5f, [21] = float.NaN };
        Dictionary<uint, string> strings = new() { [30] = "N123" };
        capture.Variables(1, 0.1, integers, floats, strings);
        // the same values again: nothing is written
        capture.Variables(1, 0.2, integers, floats, strings);
        integers[11] = 20;
        floats[20] = 0.75f;
        capture.Variables(1, 0.3, integers, floats, strings);
        capture.Finish();

        Assert.False(capture.Failed, string.Join(" ", logged));
        // one record for the first call and one for the third; the call with the same values wrote nothing
        Assert.Equal(2, capture.VariableRecordCount);

        var aircraft = OnlyAircraft(Jfs2Loader.Load(Open(PathOf("a.jfs2"))));
        var after = Merged(aircraft, 0.15);
        Assert.Equal(new Dictionary<uint, int> { [10] = 1, [11] = 2, [12] = 3 }, after.Integers);
        Assert.Equal("N123", after.Strings[30]);
        Assert.True(float.IsNaN(after.Floats[21]));

        var last = Merged(aircraft);
        Assert.Equal(new Dictionary<uint, int> { [10] = 1, [11] = 20, [12] = 3 }, last.Integers);
        Assert.Equal(0.75f, last.Floats[20]);
        // the second sample stored one integer and one float, not the whole set
        var changed = aircraft.frames.OfType<Recorder.IntegerVariablesFrame>().Last();
        Assert.Equal(new Dictionary<uint, int> { [11] = 20 }, changed.variables);
        Assert.Equal(0.3, changed.time, 5);
    }

    [Fact]
    public void ALargeVariableUpdate_IsSplitInto255EntryRecords()
    {
        var capture = NewCapture();
        capture.OpenTrack(1, 0.0, TrackOwner.Local, "", "x", "", Identity("A", "M"));
        PositionUpdate p = Position(0);
        capture.Position(1, 0.0, ref p);
        Dictionary<uint, int> integers = Enumerable.Range(0, 600).ToDictionary(i => (uint)(1000 + i), i => i);
        Dictionary<uint, string> strings = Enumerable.Range(0, 255).ToDictionary(i => (uint)(5000 + i), i => new string('é', 128));
        capture.Variables(1, 0.1, integers, null, strings);
        capture.Finish();

        Assert.False(capture.Failed, string.Join(" ", logged));
        Assert.Equal(4, capture.VariableRecordCount); // 855 entries: 255 + 255 + 255 + 90
        var merged = Merged(OnlyAircraft(Jfs2Loader.Load(Open(PathOf("a.jfs2")))));
        Assert.Equal(integers, merged.Integers);
        Assert.Equal(255, merged.Strings.Count);
        Assert.Equal(new string('é', 128), merged.Strings[5000]);
    }

    [Fact]
    public void Events_RoundTripInOrderWithTheVariables()
    {
        var capture = NewCapture();
        capture.OpenTrack(1, 0.0, TrackOwner.Local, "", "x", "", Identity("A", "M"));
        PositionUpdate p = Position(0);
        capture.Position(1, 0.0, ref p);
        capture.Event(1, 0.5, 66000, 1);
        capture.Variables(1, 0.5, new Dictionary<uint, int> { [1] = 1 }, null, null);
        capture.Event(1, 0.6, 66001, 0xFFFFFFFF);
        capture.Event(99, 0.7, 1, 1); // a track that was never opened
        capture.Finish();

        Assert.Equal(2, capture.EventCount);
        var aircraft = OnlyAircraft(Jfs2Loader.Load(Open(PathOf("a.jfs2"))));
        var events = aircraft.frames.OfType<Recorder.SimEventFrame>().ToArray();
        Assert.Equal([(66000u, 1u), (66001u, 0xFFFFFFFFu)], events.Select(e => (e.eventId, e.data)).ToArray());
        Assert.Equal([Recorder.FrameType.AircraftPosition, Recorder.FrameType.SimEvent, Recorder.FrameType.IntegerVariables, Recorder.FrameType.SimEvent],
            aircraft.frames.Select(f => f.type).ToArray());
    }

    [Fact]
    public void VariablesOfATrackThatWasNeverOpened_AreIgnored()
    {
        var capture = NewCapture();
        capture.OpenTrack(1, 0.0, TrackOwner.Local, "", "x", "", Identity("A", "M"));
        capture.Variables(5, 0.1, new Dictionary<uint, int> { [1] = 1 }, null, null);
        capture.Finish();

        Assert.False(capture.Failed);
        Assert.Equal(0, capture.VariableRecordCount);
    }

    [Fact]
    public void ARecordingWithOnlyVariablesAndEvents_HoldsNothingToPlay()
    {
        var capture = NewCapture();
        capture.OpenTrack(1, 0.0, TrackOwner.Local, "", "x", "", Identity("A", "M"));
        capture.Variables(1, 0.1, new Dictionary<uint, int> { [1] = 1 }, null, null);
        capture.Event(1, 0.2, 1, 1);
        capture.Finish();

        var ex = Assert.Throws<InvalidOperationException>(() => Jfs2Loader.Load(Open(PathOf("a.jfs2"))));

        Assert.Contains("nothing that can be played", ex.Message);
    }

    // ---- stage 3: object-position and objects that are not aircraft ----

    static ObjectPositionUpdate ObjectPosition(double step) => new()
    {
        Latitude = 51.0 + step * 0.001,
        Longitude = -0.5,
        Altitude = 12.5,
        Pitch = 0.01f,
        Bank = 0.02f,
        Heading = 3.0f,
        VelocityX = 1,
        VelocityY = 2,
        VelocityZ = 3,
        AngularVelocityX = 4,
        AngularVelocityY = 5,
        AngularVelocityZ = 6,
        AccelerationX = 7,
        AccelerationY = 8,
        AccelerationZ = 9,
        Height = 2.5f,
        StateFlags = PositionStateFlags.OnGround | PositionStateFlags.Paused,
    };

    [Fact]
    public void ObjectPositionBody_Is89BytesAndRoundTripsEveryField()
    {
        var original = ObjectPosition(3);
        original.ObjectId = 77;
        original.NetTime = 12.25;
        byte[] body = new byte[ObjectPositionV1Codec.Size];

        Assert.Equal(89, ObjectPositionV1Codec.Encode(original, body));
        var decoded = ObjectPositionV1Codec.Decode(body);

        Assert.Equal(original, decoded);
        Assert.Equal(1, body[88] & 1); // OnGround is the low bit of the last byte, spec 7.2
        Assert.Throws<FormatException>(() => ObjectPositionV1Codec.Decode(body.AsSpan(0, 88)));
        Assert.Equal(original, ObjectPositionV1Codec.Decode([.. body, 1, 2]));
    }

    [Fact]
    public void ABoat_RecordsAndPlaysNextToAnAircraft()
    {
        var capture = NewCapture();
        capture.OpenTrack(1, 0.0, TrackOwner.Local, "me", "x", "", Identity("A", "Cessna"));
        capture.OpenTrack(2, 0.0, TrackOwner.Network, "other", "x", "", Identity("", "Ferry", aircraft: false));
        for (int i = 0; i < 20; i++)
        {
            PositionUpdate a = Position(i);
            capture.Position(1, i * 0.1, ref a);
            ObjectPositionUpdate b = ObjectPosition(i);
            capture.ObjectPosition(2, i * 0.1, ref b);
        }
        capture.Variables(2, 1.0, new Dictionary<uint, int> { [5] = 1 }, null, null);
        capture.Finish();

        Assert.False(capture.Failed, string.Join(" ", logged));
        Assert.Equal(20, capture.ObjectPositionCount);
        var result = Jfs2Loader.Load(Open(PathOf("a.jfs2")));

        Assert.Empty(result.Notes);
        Assert.Equal(2, result.Objects.Count);
        Assert.IsType<Recorder.Aircraft>(result.Objects[0]);
        var boat = result.Objects[1];
        Assert.IsNotType<Recorder.Aircraft>(boat);
        Assert.Equal(("Ferry", Sim.Obj.Owner.Recorder), (boat.model, boat.owner));
        var frames = boat.frames.OfType<Recorder.ObjectPositionFrame>().ToArray();
        Assert.Equal(20, frames.Length);
        Assert.Equal(51.005, frames[5].data.latitude, 9);
        Assert.Equal(0.5, frames[5].time, 5);
        Assert.Equal(1, frames[5].data.ground);
        Assert.Equal(2.5f, frames[5].data.height);
        Assert.Equal(9f, frames[5].data.accelerationZ);
        Assert.Single(boat.frames.OfType<Recorder.IntegerVariablesFrame>());
    }

    // ---- stage 3c: keyframes ----

    /// <summary>
    /// 30 s at 20 Hz. Track 1 (aircraft) has variables 10 and 11 from the start; variable 10 changes at 5.5 s. Track 2 (a boat)
    /// opens at 5.0 s, which lies in a chunk that is neither the first nor a keyframe.
    /// </summary>
    Jfs2Capture RecordThirtySeconds()
    {
        var capture = NewCapture();
        capture.OpenTrack(1, 0.0, TrackOwner.Local, "me", "x", "", Identity("A", "Cessna"));
        Dictionary<uint, int> integers = new() { [10] = 1, [11] = 7 };
        capture.Variables(1, 0.0, integers, null, null);
        for (int i = 0; i < 600; i++)
        {
            double t = i * 0.05;
            if (i == 100)
            {
                capture.OpenTrack(2, t, TrackOwner.Network, "", "x", "", Identity("", "Ferry", aircraft: false));
                capture.Variables(2, t, new Dictionary<uint, int> { [5] = 9 }, null, null);
            }
            if (i == 110)
            {
                integers[10] = 2;
                capture.Variables(1, t, integers, null, null);
            }
            PositionUpdate a = Position(i);
            capture.Position(1, t, ref a);
            if (i >= 100)
            {
                ObjectPositionUpdate b = ObjectPosition(i);
                capture.ObjectPosition(2, t, ref b);
            }
        }
        capture.Finish();
        Assert.False(capture.Failed, string.Join(" ", logged));
        return capture;
    }

    [Fact]
    public void EveryFifthChunk_IsAKeyframeWithTheStateOfTheOpenTracks()
    {
        var capture = RecordThirtySeconds();

        using var reader = RecordingReader.Open(PathOf("a.jfs2"));
        int[] keyframes = [.. Enumerable.Range(0, reader.Chunks.Count).Where(i => reader.Chunks[i].IsKeyframe)];
        Assert.Equal(capture.KeyframeCount + 1, keyframes.Length);
        Assert.True(keyframes.Length >= 3, "30 s at about 2 s per chunk: " + string.Join(",", keyframes));
        Assert.Equal([0, 5, 10], keyframes.Take(3));

        Assert.True(reader.TryReadChunk(keyframes[1], out RecordChunk? chunk));
        string[] snapshot = [.. chunk!.Records.Take(chunk.SnapshotCount).Select(r => $"{reader.GetKind(r.Kind)!.Name}:{r.Track}")];
        Assert.Equal(
            ["track-open:1", "identity:1", "position:1", "variables:1", "track-open:2", "identity:2", "object-position:2", "variables:2"],
            snapshot);
        Assert.All(chunk.Records.Take(chunk.SnapshotCount), r => Assert.True(r.IsSnapshot));
        // the snapshot keeps the time of what it summarises: the variables of track 1 changed at 5.5 s
        RecordEntry variables = chunk.Records.First(r => r.IsSnapshot && r.Track == 1 && reader.GetKind(r.Kind)!.Name == "variables");
        Assert.Equal(5_500_000, variables.Time);
        var merged = new VariableSyncV1Codec().Decode(variables.Body.Span).Entries.ToDictionary(e => e.Vuid, e => e.IntValue);
        Assert.Equal(new Dictionary<uint, int> { [10] = 2, [11] = 7 }, merged);
        Assert.True(chunk.Info.TMin > variables.Time);
    }

    [Fact]
    public void ACleanRead_DoesNotPlayTheSnapshotsAgain()
    {
        RecordThirtySeconds();

        var result = Jfs2Loader.Load(Open(PathOf("a.jfs2")));

        Assert.Empty(result.Notes);
        var aircraft = Assert.IsType<Recorder.Aircraft>(result.Objects[0]);
        Assert.Equal(600, aircraft.frames.OfType<Recorder.AircraftPositionFrame>().Count());
        Assert.Equal(2, aircraft.frames.OfType<Recorder.IntegerVariablesFrame>().Count());
        Assert.Equal(500, result.Objects[1].frames.OfType<Recorder.ObjectPositionFrame>().Count());
        Assert.Equal(aircraft.frames.Select(f => f.time).Order(), aircraft.frames.Select(f => f.time));
    }

    /// <summary>Damages the middle of a chunk of the finished file so its CRC fails.</summary>
    void Corrupt(int chunk)
    {
        long at;
        using (var reader = RecordingReader.Open(PathOf("a.jfs2")))
        {
            at = reader.Chunks[chunk].Offset + 60;
        }
        byte[] bytes = File.ReadAllBytes(PathOf("a.jfs2"));
        bytes[at] ^= 0xFF;
        File.WriteAllBytes(PathOf("a.jfs2"), bytes);
    }

    [Fact]
    public void AfterALostChunk_TheNextKeyframeRestoresTheState()
    {
        RecordThirtySeconds();
        Corrupt(2); // 4 s to 6 s: holds the opening of track 2 and the change of variable 10

        var result = Jfs2Loader.Load(Open(PathOf("a.jfs2")));

        Assert.Contains(result.Notes, n => n.StartsWith("Damaged recording"));
        Assert.Contains(result.Warnings, n => n.StartsWith("Damaged recording")); // the user is told
        Assert.Equal(2, result.Objects.Count); // track 2 opened in the lost chunk and came back with the keyframe
        var aircraft = (Recorder.Aircraft)result.Objects[0];
        var boat = result.Objects[1];
        Assert.Equal("Ferry", boat.model);

        // nothing from the chunks after the lost one up to the keyframe at chunk 5 (about 10 s)
        double[] times = [.. aircraft.frames.OfType<Recorder.AircraftPositionFrame>().Select(f => f.time)];
        Assert.DoesNotContain(times, t => t > 4.1 && t < 10.0);
        Assert.Contains(times, t => t > 12.0);
        // the state the lost chunk changed is back: variable 10 changed there, variable 11 never did
        var merged = Merged(aircraft);
        Assert.Equal(new Dictionary<uint, int> { [10] = 2, [11] = 7 }, merged.Integers);
        Assert.Equal(new Dictionary<uint, int> { [5] = 9 }, Merged(boat).Integers);
        // frames stay in time order, so the old player interpolates correctly
        Assert.Equal(aircraft.frames.Select(f => f.time).Order(), aircraft.frames.Select(f => f.time));
        Assert.Equal(boat.frames.Select(f => f.time).Order(), boat.frames.Select(f => f.time));
        // a snapshot's position that is a copy of one already read is not added twice
        Assert.Equal(times.Length, times.Distinct().Count());
    }

    [Fact]
    public void ALostKeyframe_WaitsForTheNextOne()
    {
        RecordThirtySeconds();
        Corrupt(5);

        var result = Jfs2Loader.Load(Open(PathOf("a.jfs2")));

        var aircraft = (Recorder.Aircraft)result.Objects[0];
        double[] times = [.. aircraft.frames.OfType<Recorder.AircraftPositionFrame>().Select(f => f.time)];
        Assert.Contains(times, t => t > 20.0);
        Assert.DoesNotContain(times, t => t > 10.5 && t < 19.0); // chunk 4 ends at about 10 s; the next keyframe is chunk 10, about 20 s
        Assert.Equal(new Dictionary<uint, int> { [10] = 2, [11] = 7 }, Merged(aircraft).Integers);
    }

    /// <summary>
    /// Changes bytes inside a chunk of the finished file and puts its CRC right, so the chunk reads but what is inside is damaged.
    /// <paramref name="change"/> gets the file bytes and the offset of the chunk's block.
    /// </summary>
    void DamageInside(int chunk, Action<byte[], long> change)
    {
        long at;
        using (var reader = RecordingReader.Open(PathOf("a.jfs2")))
        {
            at = reader.Chunks[chunk].Offset;
        }
        byte[] bytes = File.ReadAllBytes(PathOf("a.jfs2"));
        int length = BitConverter.ToInt32(bytes, (int)at + 1);
        change(bytes, at);
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes.AsSpan((int)at, 5 + length))
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        BitConverter.GetBytes(~crc).CopyTo(bytes, at + 5 + length);
        File.WriteAllBytes(PathOf("a.jfs2"), bytes);
    }

    [Fact]
    public void AChunkWhoseRecordsStopParsing_IsLostUntilTheNextKeyframe()
    {
        RecordThirtySeconds();
        // the first record of chunk 0, the track-open of track 1, names a kind no Schema binds, so chunk 0 yields nothing
        DamageInside(0, (bytes, at) => bytes[at + 5 + Jfs2.ChunkHeaderLength] = 0x7F);

        var result = Jfs2Loader.Load(Open(PathOf("a.jfs2")));

        Assert.Contains(result.Warnings, n => n.StartsWith("Damaged recording"));
        Assert.Equal(2, result.Objects.Count); // both tracks come back with the keyframe at chunk 5 (about 10 s)
        var aircraft = (Recorder.Aircraft)result.Objects[0];
        Assert.Equal("A", aircraft.callsign);
        double[] times = [.. aircraft.frames.OfType<Recorder.AircraftPositionFrame>().Select(f => f.time)];
        Assert.DoesNotContain(times, t => t < 9.0);
        Assert.Contains(times, t => t > 29.0);
        Assert.Equal(new Dictionary<uint, int> { [10] = 2, [11] = 7 }, Merged(aircraft).Integers);
        Assert.Equal(new Dictionary<uint, int> { [5] = 9 }, Merged(result.Objects[1]).Integers);
    }

    [Fact]
    public void ARecordThatCannotBeDecoded_IsHealedByTheNextKeyframe_WithoutLosingTheChunksBetween()
    {
        RecordThirtySeconds();
        // the variables record of track 1 at 5.5 s (chunk 2) claims 255 entries, so it does not decode
        DamageInside(2, (bytes, at) =>
        {
            using var reader = RecordingReader.Open(new MemoryStream(bytes));
            Assert.True(reader.TryReadChunk(2, out RecordChunk? chunk));
            RecordEntry variables = chunk!.Records.Single(r => !r.IsSnapshot && r.Track == 1 && reader.GetKind(r.Kind)!.Name == "variables");
            Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(variables.Body, out ArraySegment<byte> body));
            bytes[at + body.Offset + 4] = 255;
        });

        var result = Jfs2Loader.Load(Open(PathOf("a.jfs2")));

        Assert.Contains(result.Warnings, n => n.Contains("could not be read"));
        var aircraft = (Recorder.Aircraft)result.Objects[0];
        // only that record is lost: every position plays, and the keyframe at about 10 s sets the variables right again
        Assert.Equal(600, aircraft.frames.OfType<Recorder.AircraftPositionFrame>().Count());
        Assert.Equal(new Dictionary<uint, int> { [10] = 2, [11] = 7 }, Merged(aircraft).Integers);
        Assert.Equal(aircraft.frames.Select(f => f.time).Order(), aircraft.frames.Select(f => f.time));
    }

    [Fact]
    public void ObjectPositionOfATrackThatWasNeverOpened_IsIgnored()
    {
        var capture = NewCapture();
        ObjectPositionUpdate p = ObjectPosition(0);
        capture.ObjectPosition(9, 1.0, ref p);
        capture.Finish();

        Assert.False(capture.Failed);
        Assert.Equal(0, capture.ObjectPositionCount);
    }

    /// <summary>Identity records of a track in the finished file, with their times, snapshots included when asked.</summary>
    List<(long Time, string Callsign, bool Snapshot)> IdentityRecords(uint track, bool snapshots)
    {
        var found = new List<(long, string, bool)>();
        using var reader = RecordingReader.Open(PathOf("a.jfs2"));
        for (int i = 0; i < reader.Chunks.Count; i++)
        {
            Assert.True(reader.TryReadChunk(i, out RecordChunk? chunk));
            foreach (RecordEntry r in chunk!.Records)
            {
                if (r.Track == track && reader.GetKind(r.Kind)!.Name == "identity" && (snapshots || !r.IsSnapshot))
                {
                    found.Add((r.Time, new IdentityV1Codec().Decode(r.Body.Span).Callsign, r.IsSnapshot));
                }
            }
        }
        return found;
    }

    [Fact]
    public void AChangedIdentity_IsWrittenOnceAndAnUnchangedOneNotAtAll()
    {
        var capture = NewCapture();
        capture.OpenTrack(1, 0.0, TrackOwner.Local, "me", "JoinFS test", "MSFS2024", Identity("N1", "Cessna 172"));
        capture.Identity(1, 1.0, Identity("N1", "Cessna 172"));
        capture.Identity(1, 2.0, Identity("DLH1", "Cessna 172"));
        capture.Identity(1, 3.0, Identity("DLH1", "Cessna 172"));
        capture.Identity(7, 3.0, Identity("X", "Y")); // not a track
        capture.Finish();

        Assert.False(capture.Failed);
        Assert.Equal(1, capture.IdentityChangeCount);
        Assert.Equal([(0L, "N1", false), (2_000_000L, "DLH1", false)], IdentityRecords(1, false));
    }

    [Fact]
    public void AKeyframeAfterAnIdentityChange_HoldsTheLatestIdentityWithItsTime()
    {
        var capture = NewCapture();
        capture.OpenTrack(1, 0.0, TrackOwner.Local, "me", "JoinFS test", "MSFS2024", Identity("N1", "Cessna 172"));
        capture.Identity(1, 1.0, Identity("DLH1", "Cessna 172"));
        for (int i = 0; i < 400; i++)
        {
            PositionUpdate p = Position(i);
            capture.Position(1, 1.5 + i * 0.1, ref p);
        }
        capture.Finish();

        var snapshots = IdentityRecords(1, true).Where(r => r.Snapshot).ToList();
        Assert.NotEmpty(snapshots);
        Assert.All(snapshots, r => Assert.Equal(("DLH1", 1_000_000L), (r.Callsign, r.Time)));
    }

    [Fact]
    public void ALoaderReadingAnIdentityChange_KeepsTheOpeningIdentityAndSaysSo()
    {
        var capture = NewCapture();
        capture.OpenTrack(1, 0.0, TrackOwner.Local, "me", "JoinFS test", "MSFS2024", Identity("N1", "Cessna 172"));
        PositionUpdate p = Position(0);
        capture.Position(1, 0.5, ref p);
        capture.Identity(1, 1.0, Identity("DLH1", "Cessna 172"));
        capture.Finish();

        var result = Jfs2Loader.Load(Open(PathOf("a.jfs2")));

        Assert.Equal("N1", OnlyAircraft(result).callsign);
        Assert.Contains(result.Notes, n => n.Contains("1 identity changes were ignored"));
        Assert.Empty(result.Warnings); // for the log only
    }

    [Fact]
    public void TheScene_HoldsCanonicalMessagesPerTrack_ForAnyViewer()
    {
        var capture = NewCapture();
        capture.OpenTrack(10000, 0.0, TrackOwner.Local, "me", "JoinFS test", "MSFS2024", Identity("DLH1", "Cessna 172"));
        PositionUpdate p = Position(1);
        capture.Position(10000, 0.5, ref p);
        capture.Variables(10000, 0.6, new Dictionary<uint, int> { [10] = 1 }, null, null);
        capture.Event(10000, 0.7, 5, 6);
        capture.Finish();

        Jfs2Scene scene = Jfs2Scene.Load(Open(PathOf("a.jfs2")));

        SceneTrack track = Assert.Single(scene.Tracks);
        Assert.Equal(10000u, track.Id);
        Assert.Equal("me", track.Nickname);
        Assert.Equal("DLH1", track.Identity.Callsign);
        Assert.Collection(track.Frames,
            f => Assert.Equal(0.5, Assert.IsType<ScenePositionFrame>(f).Time),
            f => Assert.Equal(1, Assert.Single(Assert.IsType<SceneVariablesFrame>(f).Variables.Entries).IntValue),
            f => Assert.Equal((5u, 6u), (Assert.IsType<SceneEventFrame>(f).EventId, ((SceneEventFrame)f).Data)));
        Assert.Equal(new DateTime(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc), scene.StartUtc);
        Assert.Contains(scene.Metadata, m => m.Key == "app" && m.Value == "JoinFS test");
    }
}
