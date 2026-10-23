using JoinFS.Net;
using JoinFS.Recording;
using RecordingXRay.Services;

namespace RecordingXRay.Tests;

/// <summary>The viewer opens .jfs2 files (docs/recording/jfs2) into the model it uses for .jfs files.</summary>
public sealed class Jfs2ReaderTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("xray-jfs2-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(folder, true); } catch (IOException) { }
    }

    private static IdentityUpdate Identity(string callsign, string model, bool aircraft) => new()
    {
        IsAircraft = aircraft,
        IsPlane = aircraft,
        Callsign = callsign,
        Model = model,
        Livery = "Livery",
        IcaoType = "DH82",
        IcaoAirline = "",
        Registration = "",
        FlightNumber = "",
        ClassCode = "",
        Wtc = "L",
        TypeRole = 3,
    };

    private string WriteFile()
    {
        string path = Path.Combine(folder, "a.jfs2");
        var capture = Jfs2Capture.Create(path, "test", "MSFS2024", new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc), _ => { });
        capture.OpenTrack(1, 0.0, TrackOwner.Local, "nick", "test", "MSFS2024", Identity("YR-SCD", "Tiger Moth", aircraft: true));
        capture.OpenTrack(2, 0.0, TrackOwner.Network, "", "test", "MSFS2024", Identity("", "Ferry", aircraft: false));

        PositionUpdate position = new()
        {
            Latitude = 0.9674238236590125, Longitude = 0.231041585664082, Altitude = 46.5, Heading = 0.5f,
            Rudder = 0.25f, Elevator = -0.5f, Aileron = 0.125f, BrakeLeft = 0f, BrakeRight = 1f,
            Elevation = 44.9f, StaticCgToGround = float.NaN, StateFlags = PositionStateFlags.OnGround,
        };
        capture.Position(1, 1.0, ref position);
        capture.Variables(1, 1.1, new Dictionary<uint, int> { [10] = 2 }, new Dictionary<uint, float> { [20] = 1.5f }, null);
        capture.Event(1, 1.2, 7, 8);
        ObjectPositionUpdate boat = new() { Latitude = 0.5, Longitude = 0.1, Altitude = 0, Height = 3, StateFlags = PositionStateFlags.OnGround };
        capture.ObjectPosition(2, 1.0, ref boat);
        capture.Finish();
        return path;
    }

    [Fact]
    public void A_jfs2_file_opens_into_aircraft_and_objects()
    {
        RecordingFile file = RecordingReader.Read(WriteFile());

        RecordedAircraft aircraft = Assert.Single(file.Aircraft);
        Assert.Equal(("YR-SCD", "nick", "Tiger Moth", "DH82"), (aircraft.Callsign, aircraft.Nickname, aircraft.Model, aircraft.IcaoType));
        Assert.True(aircraft.Plane);
        RecordedObject boat = Assert.Single(file.Objects);
        Assert.Equal("Ferry", boat.Model);
        Assert.Equal("JFS2", RecordingSummary.From(file).Version);
    }

    [Fact]
    public void The_frames_of_one_track_keep_their_kinds_times_and_values()
    {
        RecordedAircraft aircraft = Assert.Single(RecordingReader.Read(WriteFile()).Aircraft);

        Assert.Collection(aircraft.Frames,
            f =>
            {
                AircraftPositionFrame p = Assert.IsType<AircraftPositionFrame>(f);
                Assert.Equal(1.0, p.Time);
                Assert.Equal(0.9674238236590125, p.Latitude);
                Assert.Equal((short)4096, p.RudderRaw); // 0.25 * 16384
                Assert.Equal((short)-8192, p.ElevatorRaw);
                Assert.Equal((short)16384, p.BrakeRightRaw);
                Assert.True(p.Ground);
            },
            f => Assert.Equal(2, Assert.IsType<IntegerVariablesFrame>(f).Variables[10]),
            f => Assert.Equal(1.5f, Assert.IsType<FloatVariablesFrame>(f).Variables[20]),
            f =>
            {
                SimEventFrame e = Assert.IsType<SimEventFrame>(f);
                Assert.Equal((7u, 8u), (e.EventId, e.Data));
            });
    }

    /// <summary>README limit 13: a callsign or model change during a recording is a frame of its own.</summary>
    [Fact]
    public void An_identity_change_is_a_frame_with_the_new_identity()
    {
        string path = Path.Combine(folder, "renamed.jfs2");
        var capture = Jfs2Capture.Create(path, "test", "MSFS2024", new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc), _ => { });
        capture.OpenTrack(1, 0.0, TrackOwner.Local, "nick", "test", "MSFS2024", Identity("YR-SCD", "Tiger Moth", aircraft: true));
        PositionUpdate position = new() { Latitude = 0.9, Longitude = 0.2, StaticCgToGround = float.NaN };
        capture.Position(1, 1.0, ref position);
        capture.Identity(1, 2.0, Identity("DLH1", "Bonanza", aircraft: true));
        capture.Finish();

        RecordedAircraft aircraft = Assert.Single(RecordingReader.Read(path).Aircraft);

        // the aircraft is listed as it opened; the change is a frame at its time
        Assert.Equal(("YR-SCD", "Tiger Moth"), (aircraft.Callsign, aircraft.Model));
        IdentityFrame change = Assert.IsType<IdentityFrame>(aircraft.Frames[^1]);
        Assert.Equal((FrameType.Identity, 2.0), (change.Type, change.Time));
        Assert.Equal(("DLH1", "Bonanza", "DH82", "Livery", 3), (change.Callsign, change.Model, change.IcaoType, change.Livery, change.TypeRole));
        Assert.True(change.Aircraft && change.Plane);
        Assert.Contains("Model: Bonanza", FrameFormatter.FormatFrame(change, _ => "unknown"));
    }

    [Fact]
    public void A_boat_has_object_position_frames()
    {
        RecordedObject boat = Assert.Single(RecordingReader.Read(WriteFile()).Objects);

        ObjectPositionFrame p = Assert.IsType<ObjectPositionFrame>(Assert.Single(boat.Frames));
        Assert.Equal((3f, true), (p.Height, p.Ground));
    }

    [Fact]
    public void A_file_that_is_not_a_recording_says_why()
    {
        string path = Path.Combine(folder, "broken.jfs2");
        File.WriteAllBytes(path, [0xFA, 0x00, 1, 2, 3]);

        Assert.Throws<InvalidDataException>(() => RecordingReader.Read(path));
    }

    [Fact]
    public void An_old_jfs_file_still_opens_as_before()
    {
        string path = RecordingFiles.WriteSingleAircraft(folder, 1.0, 2.0);

        RecordingFile file = RecordingReader.Read(path);

        Assert.Equal(2, Assert.Single(file.Aircraft).Frames.Count);
        Assert.Equal("21005", RecordingSummary.From(file).Version);
    }
}
