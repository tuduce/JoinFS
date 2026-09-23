using System;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    // Round-trip coverage for the Position codec (docs/protocol-v2-implementation-plan.md Phase 4).
    public class PositionCodecTests
    {
        static PositionUpdate Sample() => new()
        {
            ObjectId = 42,
            NetTime = 123456.789,
            Latitude = 47.4502371,
            Longitude = -122.3088344,
            Altitude = 433.2,
            Pitch = 0.02f,
            Bank = -0.15f,
            Heading = 3.14f,
            VelocityX = 68.2f,
            VelocityY = 0.1f,
            VelocityZ = -2.4f,
            AngularVelocityX = 0f,
            AngularVelocityY = 0.01f,
            AngularVelocityZ = 0f,
            AccelerationX = 0f,
            AccelerationY = 0f,
            AccelerationZ = 0.02f,
            Rudder = 0.1f,
            Elevator = -0.3f,
            Aileron = 0.5f,
            BrakeLeft = 1.0f,
            BrakeRight = -1.0f,
            Elevation = 12.5f,
            StaticCgToGround = 3.4f,
            StateFlags = PositionStateFlags.OnGround | PositionStateFlags.UserControlled,
        };

        [Fact]
        public void RoundTrips_AllFields()
        {
            var codec = new PositionV1Codec();
            PositionUpdate update = Sample();

            Span<byte> buffer = stackalloc byte[PositionV1Codec.Size];
            int written = codec.Encode(update, buffer);
            PositionUpdate back = codec.Decode(buffer[..written]);

            Assert.Equal(PositionV1Codec.Size, written);
            Assert.Equal(update.ObjectId, back.ObjectId);
            Assert.Equal(update.NetTime, back.NetTime);
            Assert.Equal(update.Latitude, back.Latitude);
            Assert.Equal(update.Longitude, back.Longitude);
            Assert.Equal(update.Altitude, back.Altitude);
            Assert.Equal(update.Pitch, back.Pitch);
            Assert.Equal(update.Bank, back.Bank);
            Assert.Equal(update.Heading, back.Heading);
            Assert.Equal(update.VelocityX, back.VelocityX);
            Assert.Equal(update.VelocityY, back.VelocityY);
            Assert.Equal(update.VelocityZ, back.VelocityZ);
            Assert.Equal(update.AngularVelocityX, back.AngularVelocityX);
            Assert.Equal(update.AngularVelocityY, back.AngularVelocityY);
            Assert.Equal(update.AngularVelocityZ, back.AngularVelocityZ);
            Assert.Equal(update.AccelerationX, back.AccelerationX);
            Assert.Equal(update.AccelerationY, back.AccelerationY);
            Assert.Equal(update.AccelerationZ, back.AccelerationZ);
            Assert.Equal(update.Elevation, back.Elevation);
            Assert.Equal(update.StaticCgToGround, back.StaticCgToGround);
            Assert.Equal(update.StateFlags, back.StateFlags);
        }

        [Theory]
        [InlineData(-1.0f)]
        [InlineData(-0.5f)]
        [InlineData(0.0f)]
        [InlineData(0.5f)]
        [InlineData(1.0f)]
        public void ControlAxes_FixedPointRoundTrip_WithinQuantizationTolerance(float value)
        {
            // Rudder/Elevator/Aileron/BrakeLeft/BrakeRight are wire-encoded as fixed-point int16
            // (matching legacy's Sim.ConvertToAxis/ConvertFromAxis, scale 16384) - not exact for every
            // float, but should round-trip within one quantization step.
            var codec = new PositionV1Codec();
            PositionUpdate update = Sample();
            update.Rudder = value;
            update.Elevator = value;
            update.Aileron = value;
            update.BrakeLeft = value;
            update.BrakeRight = value;

            Span<byte> buffer = stackalloc byte[PositionV1Codec.Size];
            int written = codec.Encode(update, buffer);
            PositionUpdate back = codec.Decode(buffer[..written]);

            const float tolerance = 1.0f / 16384.0f;
            Assert.InRange(back.Rudder, value - tolerance, value + tolerance);
            Assert.InRange(back.Elevator, value - tolerance, value + tolerance);
            Assert.InRange(back.Aileron, value - tolerance, value + tolerance);
            Assert.InRange(back.BrakeLeft, value - tolerance, value + tolerance);
            Assert.InRange(back.BrakeRight, value - tolerance, value + tolerance);
        }

        [Fact]
        public void SharedCockpitSentinel_ObjectIdMaxValue_RoundTrips()
        {
            var codec = new PositionV1Codec();
            PositionUpdate update = Sample();
            update.ObjectId = uint.MaxValue;

            Span<byte> buffer = stackalloc byte[PositionV1Codec.Size];
            int written = codec.Encode(update, buffer);
            PositionUpdate back = codec.Decode(buffer[..written]);

            Assert.Equal(uint.MaxValue, back.ObjectId);
        }

        [Theory]
        [InlineData(PositionStateFlags.None)]
        [InlineData(PositionStateFlags.OnGround)]
        [InlineData(PositionStateFlags.ElevationCorrection)]
        [InlineData(PositionStateFlags.UserControlled)]
        [InlineData(PositionStateFlags.Paused)]
        [InlineData(PositionStateFlags.OnGround | PositionStateFlags.ElevationCorrection | PositionStateFlags.UserControlled | PositionStateFlags.Paused)]
        public void StateFlags_AllCombinations_RoundTrip(PositionStateFlags flags)
        {
            var codec = new PositionV1Codec();
            PositionUpdate update = Sample();
            update.StateFlags = flags;

            Span<byte> buffer = stackalloc byte[PositionV1Codec.Size];
            int written = codec.Encode(update, buffer);
            PositionUpdate back = codec.Decode(buffer[..written]);

            Assert.Equal(flags, back.StateFlags);
        }

        [Fact]
        public void CodecRegistry_ResolvesPositionCodec()
        {
            CodecRegistry.Register(new PositionV1Codec());
            ICodec<PositionUpdate> codec = CodecRegistry.Resolve<PositionUpdate>(MessageClasses.Position, 1);
            Assert.Equal(MessageClasses.Position, codec.MessageClass);
        }

        [Fact]
        public void FixedSize_MatchesEncodedLength()
        {
            var codec = new PositionV1Codec();
            byte[] buffer = new byte[256];
            int written = codec.Encode(Sample(), buffer);
            Assert.Equal(PositionV1Codec.Size, written);
        }
    }
}
