using System;
using System.IO;
using Xunit;

namespace JoinFS.Tests
{
    /// <summary>
    /// Guards the AircraftPosition / ObjectPositionVelocity wire+file serialization in Sim.cs:
    ///  - round-trips at the current stream version,
    ///  - the X-Plane IPC blob stays byte-for-byte the frozen layout the native plugin reads
    ///    (JoinFS-XP/Link.h struct AircraftPositionMsg),
    ///  - the length prefix added at Sim.POSITION_BLOB_LENGTH_PREFIXED lets a reader skip an
    ///    unknown trailing field instead of desyncing.
    /// </summary>
    public class SimPositionSerializationTests
    {
        static JoinFS.Sim.AircraftPosition SampleAircraft() => new()
        {
            latitude = 0.83, longitude = -0.12, altitude = 1234.5,
            pitch = 0.01f, bank = -0.02f, heading = 1.5f,
            velocityX = 1f, velocityY = 2f, velocityZ = 3f,
            angularVelocityX = 0.1f, angularVelocityY = 0.2f, angularVelocityZ = 0.3f,
            accelerationX = 0.4f, accelerationY = 0.5f, accelerationZ = 0.6f,
            rudder = 0.1f, elevator = -0.2f, aileron = 0.3f, brakeLeft = 0.4f, brakeRight = 0.5f,
            elevation = 42.0f, ground = 1, staticCgToGround = 3.75f,
        };

        static JoinFS.Sim.ObjectPositionVelocity SampleObject() => new()
        {
            latitude = 0.5, longitude = 0.25, altitude = 500.0,
            pitch = 0.01f, bank = 0.02f, heading = 0.03f,
            velocityX = 1f, velocityY = 2f, velocityZ = 3f,
            angularVelocityX = 0.1f, angularVelocityY = 0.2f, angularVelocityZ = 0.3f,
            accelerationX = 0.4f, accelerationY = 0.5f, accelerationZ = 0.6f,
            height = 12.0f, ground = 1,
        };

        static byte[] WriteAircraft(short version, JoinFS.Sim.AircraftPosition p)
        {
            using MemoryStream ms = new();
            using BinaryWriter w = new(ms);
            JoinFS.Sim.Write(w, version, ref p);
            w.Flush();
            return ms.ToArray();
        }

        [Fact]
        public void AircraftPosition_RoundTrips_AtCurrentVersion()
        {
            var original = SampleAircraft();
            byte[] bytes = WriteAircraft(JoinFS.Sim.VERSION, original);

            using MemoryStream ms = new(bytes);
            using BinaryReader r = new(ms);
            JoinFS.Sim.AircraftPosition read = new();
            JoinFS.Sim.Read(JoinFS.Sim.VERSION, r, ref read);

            Assert.Equal(ms.Length, ms.Position); // whole blob consumed / repositioned
            Assert.Equal(original.latitude, read.latitude, 6);
            Assert.Equal(original.altitude, read.altitude, 3);
            Assert.Equal(original.elevation, read.elevation, 3);
            Assert.Equal(1, read.ground);
            Assert.Equal(original.staticCgToGround, read.staticCgToGround, 3);
        }

        [Fact]
        public void AircraftPosition_XPlaneBlob_IsFrozenPluginLayout()
        {
            // JoinFS-XP/Link.h struct AircraftPositionMsg payload written by Sim.Write
            // (index + netTime are written by XPlane.cs itself, not Sim):
            //   3 x double (lat/lon/alt)          = 24
            //   3 x float  (pitch/bank/heading)   = 12
            //   9 x float  (vel + angVel + accel) = 36
            //   5 x Int16  (control axes)         = 10
            //   1 x float  (elevation)            =  4
            //   1 x byte   (ground flags)         =  1
            //   NO staticCgToGround, NO length prefix
            const int expected = 24 + 12 + 36 + 10 + 4 + 1; // 87

            byte[] bytes = WriteAircraft(JoinFS.Sim.XPLANE_POSITION_BLOB_VERSION, SampleAircraft());

            Assert.Equal(expected, bytes.Length);
        }

        [Fact]
        public void AircraftPosition_XPlaneBlob_RoundTripsWithoutStaticCg()
        {
            var original = SampleAircraft();
            byte[] bytes = WriteAircraft(JoinFS.Sim.XPLANE_POSITION_BLOB_VERSION, original);

            using MemoryStream ms = new(bytes);
            using BinaryReader r = new(ms);
            JoinFS.Sim.AircraftPosition read = new();
            JoinFS.Sim.Read(JoinFS.Sim.XPLANE_POSITION_BLOB_VERSION, r, ref read);

            Assert.Equal(ms.Length, ms.Position);
            Assert.Equal(original.elevation, read.elevation, 3);
            Assert.True(float.IsNaN(read.staticCgToGround)); // not present on this path
        }

        [Fact]
        public void AircraftPosition_LengthPrefixed_SkipsUnknownTrailingField()
        {
            var original = SampleAircraft();

            using MemoryStream ms = new();
            using (BinaryWriter w = new(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                JoinFS.Sim.AircraftPosition p = original;
                JoinFS.Sim.Write(w, JoinFS.Sim.VERSION, ref p);
                w.Flush();

                // Simulate a newer sender that appended 4 extra bytes INSIDE the blob by
                // bumping the stored length prefix and writing the extra bytes before the
                // trailing marker.
                long end = ms.Position;
                ms.Position = 0;
                ushort len = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true).ReadUInt16();
                ms.Position = 0;
                w.Write((ushort)(len + 4));
                ms.Position = end;
                w.Write(0xDEADBEEFu);          // the "unknown" future field
                w.Write(0x12345678u);          // a trailing marker that MUST survive
                w.Flush();
            }

            ms.Position = 0;
            using BinaryReader r = new(ms);
            JoinFS.Sim.AircraftPosition read = new();
            JoinFS.Sim.Read(JoinFS.Sim.VERSION, r, ref read);

            Assert.Equal(original.altitude, read.altitude, 3);
            Assert.Equal(0x12345678u, r.ReadUInt32()); // reader landed exactly past the blob
        }

        [Fact]
        public void ObjectPositionVelocity_RoundTrips_AtCurrentVersion()
        {
            var original = SampleObject();

            using MemoryStream ms = new();
            using (BinaryWriter w = new(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                JoinFS.Sim.ObjectPositionVelocity p = original;
                JoinFS.Sim.Write(w, JoinFS.Sim.VERSION, ref p);
                w.Flush();
            }

            ms.Position = 0;
            using BinaryReader r = new(ms);
            JoinFS.Sim.ObjectPositionVelocity read = new();
            JoinFS.Sim.Read(JoinFS.Sim.VERSION, r, ref read);

            Assert.Equal(ms.Length, ms.Position);
            Assert.Equal(original.latitude, read.latitude, 6);
            Assert.Equal(original.height, read.height, 3);
            Assert.Equal(1, read.ground);
        }

        [Fact]
        public void ReadLengthPrefixed_BogusLength_Throws()
        {
            // a length prefix that can't possibly fit the rest of the buffer => not really the
            // length-prefixed format (version mismatch) or truncated => must fail, not read garbage
            byte[] buf = { 0xFF, 0xFF, 1, 2, 3, 4, 5, 6 };
            using MemoryStream ms = new(buf);
            using BinaryReader r = new(ms);
            JoinFS.Sim.AircraftPosition read = new();
            Assert.Throws<JoinFS.Sim.ReadException>(() => JoinFS.Sim.Read(JoinFS.Sim.VERSION, r, ref read));
        }

        [Fact]
        public void StalePeer_PrefixlessBody_NeverPassesAsValid()
        {
            // an old peer (no ushort prefix) whose Sim.VERSION nonetheless reads as 21009+
            byte[] oldBody = WriteAircraft(JoinFS.Sim.XPLANE_POSITION_BLOB_VERSION, SampleAircraft());
            using MemoryStream ms = new(oldBody);
            using BinaryReader r = new(ms);
            JoinFS.Sim.AircraftPosition read = new();

            bool threw = false;
            try { JoinFS.Sim.Read(JoinFS.Sim.VERSION, r, ref read); }
            catch (Exception) { threw = true; }

            // either the read faulted, or it produced values the plausibility gate rejects -
            // never a clean, believable position
            Assert.True(threw || !JoinFS.Sim.PlausibleAircraftPosition(in read));
        }

        [Fact]
        public void PlausibleAircraftPosition_AcceptsRealRejectsGarbage()
        {
            Assert.True(JoinFS.Sim.PlausibleAircraftPosition(SampleAircraft()));

            var nan = SampleAircraft(); nan.latitude = double.NaN;
            Assert.False(JoinFS.Sim.PlausibleAircraftPosition(in nan));

            var huge = SampleAircraft(); huge.longitude = 1e200;
            Assert.False(JoinFS.Sim.PlausibleAircraftPosition(in huge));

            var inf = SampleAircraft(); inf.heading = float.PositiveInfinity;
            Assert.False(JoinFS.Sim.PlausibleAircraftPosition(in inf));

            var offEarth = SampleAircraft(); offEarth.latitude = 5.0; // radians, |lat| must be <= 3.2
            Assert.False(JoinFS.Sim.PlausibleAircraftPosition(in offEarth));
        }
    }
}
