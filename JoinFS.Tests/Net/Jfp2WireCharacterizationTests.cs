using System.Net;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Legacy;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// What the default JFP2 plugin puts on the wire, captured from the build before the message
    /// classes became descriptors of a per-instance profile: its Hello (offers in their order) and
    /// one sample message of each of the ten application classes (envelope flags, class byte and
    /// payload). A characterization, not a spec: <see cref="JoinFS.Tests.Jfp2.HandshakeGoldenTests"/>
    /// and the codec tests are the spec. It proves that the default profile sends exactly what the
    /// hand-kept tables did. The session ids are random, so they are left out.
    /// </summary>
    public class Jfp2WireCharacterizationTests
    {
        static readonly Guid SampleGuid = new("00112233-4455-6677-8899-aabbccddeeff");

        static (TestMesh Mesh, TestNode Hub, TestNode A) Pair()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin(build: "26.6.0 JoinFS-FS2024"));
            TestNode a = mesh.Add("198.51.100.2", 6112, new LegacyPlugin(), new Jfp2Plugin());
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);
            return (mesh, hub, a);
        }

        /// <summary>"flags class payload" of a JFP2 datagram, in hex; the payload's SelfAssignedId is zeroed for a handshake.</summary>
        static string Describe(byte[] datagram)
        {
            Envelope envelope = Envelope.ReadFrom(datagram, out int consumed);
            byte[] payload = datagram[consumed..];
            if (envelope.IsInternal && (envelope.RawMessageClass == MessageClasses.Hello || envelope.RawMessageClass == MessageClasses.HelloAck))
            {
                payload[10] = 0;
                payload[11] = 0;
            }
            return ((byte)envelope.Flags).ToString("X2") + " " + envelope.RawMessageClass.ToString("X2") + " " + Convert.ToHexString(payload);
        }

        static List<string> Jfp2From(TestMesh mesh, TestNode from, TestNode to) =>
            mesh.Network.Log.Where(d => d.From.Equals(from.EndPoint) && d.To.Equals(to.EndPoint) && d.Data[0] == Envelope.Magic)
                .Select(d => Describe(d.Data)).ToList();

        [Fact]
        public void DefaultPlugin_HelloIsUnchanged()
        {
            var (mesh, hub, a) = Pair();

            string hello = Assert.Single(mesh.Network.Log.Where(d => d.From.Equals(hub.EndPoint) && d.To.Equals(a.EndPoint) && d.Data[0] == Envelope.Magic
                && (d.Data[2] & (byte)EnvelopeFlags.Internal) != 0 && d.Data[7] == MessageClasses.Hello).Take(1).Select(d => Describe(d.Data)));
            Assert.Equal(
                "08 00 " +                                         // Internal, class Hello
                "0202" + "0000000000000000" + "0000" + "00" +      // ProtoMajor 2..2, no capabilities, SelfAssignedId (zeroed), Result
                "0A00" +                                           // ten offers, application classes at [1, 1], in this order:
                "00070101" + "00080101" + "00010101" + "00020101" + "00000101" + // Status, StatusRequest, Identity, VariableSync, Position
                "00030101" + "00040101" + "00050101" + "00060101" + "00090101" + // Event, FlightPlan, Notes, Weather, WeatherReply
                "01000700017100CBE01701" +                         // TLV Node: 203.0.113.1, port 6112, local 1
                "02001400" + "32362E362E30204A6F696E46532D465332303234", // TLV Build: "26.6.0 JoinFS-FS2024"
                hello);
        }

        [Fact]
        public void DefaultPlugin_EachApplicationClassIsEncodedAsBefore()
        {
            var (mesh, hub, a) = Pair();
            mesh.Network.Log.Clear();

            hub.Core.Objects.SetIdentity(hub.Id, new IdentityUpdate
            {
                ObjectId = 5, IsAircraft = true, IsPlane = true, Callsign = "CS1", Model = "M", Livery = "L", IcaoType = "C172",
                IcaoAirline = "AL", Registration = "R", FlightNumber = "1", ClassCode = "L1P", Wtc = "L", TypeRole = 1,
            });
            hub.Core.SendTo(a.Id, new PositionUpdate
            {
                ObjectId = 5, NetTime = 2.5, Latitude = 51.5, Longitude = -0.25, Altitude = 1000, Pitch = 1, Bank = 2, Heading = 3,
                VelocityX = 4, VelocityY = 5, VelocityZ = 6, Rudder = 0.5f, BrakeLeft = 1, Elevation = 7, StaticCgToGround = 8,
                StateFlags = PositionStateFlags.UserControlled,
            }, false);
            hub.Core.SendTo(a.Id, new VariableSyncUpdate
            {
                ObjectId = 5,
                Entries = [new VariableEntry { Vuid = 0x11223344, Kind = VariableKind.Int32, IntValue = 7 }, new VariableEntry { Vuid = 9, Kind = VariableKind.String8, StringValue = "ab" }],
            }, false);
            hub.Core.SendTo(a.Id, new EventUpdate { ObjectId = 5, EventId = 6, Data = 7 }, true);
            hub.Core.SendTo(a.Id, new FlightPlanUpdate
            {
                ObjectId = 5, IcaoType = "C172", Departure = "EGLL", Destination = "LFPG", Rules = "VFR", Route = "DCT", Remarks = "",
                Alternate = "", Speed = "N0100", Altitude = "A050", Callsign = "CS1", Registration = "R", IcaoAirline = "AL", FlightNumber = "1",
            }, false);
            hub.Core.SendTo(a.Id, new NotesBundle
            {
                Scope = CommsScope.Single,
                Users = [new NotesUser { Guid = SampleGuid, Nickname = "N", Callsign = "C", Notes = [new CommsNote { NoteId = 3, Age = 1.5f, Channel = 2, Text = "hi" }] }],
            }, true);
            hub.Core.SendTo(a.Id, new WeatherUpdate { Metar = "EGLL 1" }, false);
            hub.Core.SendTo(a.Id, new StatusUpdate
            {
                Guid = SampleGuid, AppVersion = "26.6", Users = 1, AtcCount = 2, AtcAirport = "EGLL", AtcLevel = 3, Planes = 4, Helicopters = 5,
                Boats = 6, Vehicles = 7, HubEnabled = true, Address = "a", Name = "n", About = "b", Voip = "v", NextEvent = "e", Airport = "p",
                ActivityCircle = 9, GlobalSession = true, PasswordRequired = true,
            }, false);
            hub.Core.SendTo(a.Id, new StatusRequestUpdate { HubEnabled = true, HubListRequested = true, Uuid = 0x01020304 }, false);
            hub.Core.SendTo(a.Id, new WeatherReply { Metar = "LFPG 2" }, true);

            List<string> sent = Jfp2From(mesh, hub, a);
            Assert.Equal(new List<string>
            {
                // flags (Guaranteed = 01), class, payload
                "00 01 050000000301030043533101004D01004C0400433137320200414C01005201003103004C315001004C", // Identity, ahead of the position
                "00 00 0500000000000000000004400000000000C04940000000000000D0BF0000000000408F400000803F0000004000004040000080400000A0400000C040000000000000000000000000000000000000000000000000002000000000004000000000E0400000004104",
                "00 02 0500000002443322110007000000090000000202006162",
                "01 03 050000000600000007000000",
                "00 04 05000000040043313732040045474C4C04004C465047030056465203004443540000000005004E3031303004004130353003004353310100520200414C010031",
                "01 05 33221100554477668899AABBCCDDEEFF01004E010043030000000000C03F020002006869",
                "00 06 060045474C4C2031",
                "00 07 33221100554477668899AABBCCDDEEFF040032362E3601000200040045474C4C0304000500060007000701006101006E01006201007601006501007009000000",
                "00 08 0304030201",
                "01 09 06004C4650472032",
            }, sent);
        }
    }
}
