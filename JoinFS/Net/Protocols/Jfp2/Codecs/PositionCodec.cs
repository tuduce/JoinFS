using System;
using System.Buffers.Binary;

// docs/protocol-v2-implementation-plan.md Phase 4: PositionV1Codec, a real (not reference-demo)
// implementation of the highest-frequency JFP2 message class - see docs/reference/jfp2-protocol.md §6.1
// for the size-reduction rationale. Scoped to AIRCRAFT position only (mirroring the legacy
// AircraftPosition message, network-protocol.md §8.2) - generic (non-Aircraft) Obj position stays on
// the legacy ObjectPosition path for this phase; see the implementation plan for why.
//
// Every identity-ish field the legacy AircraftPosition message carries on every tick (livery, ICAO
// type/airline, registration, class code/WTC, callsign, model, typerole) has already moved to the
// Identity message class (Phase 3) and is deliberately NOT repeated here - that split is the entire
// point of docs/reference/jfp2-protocol.md §6.2. PositionUpdate carries only what changes every frame:
// motion state plus the small set of per-tick flags (on ground, paused, ...) legacy also resends
// every tick rather than treating as identity.

using JoinFS.Net;

namespace JoinFS.Net.Jfp2.Codecs
{
    public sealed class PositionV1Codec : ICodec<PositionUpdate>
    {
        public byte MessageClass => MessageClasses.Position;
        public byte SchemaVersion => 1;
        public const int Size = 103;

        static short ConvertToAxis(float input) => (short)(input * 16384.0);
        static float ConvertFromAxis(short input) => (float)(int)input * (1.0f / 16384.0f);

        public int Encode(in PositionUpdate v, Span<byte> dest)
        {
            int i = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(i, 4), v.ObjectId); i += 4;
            BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(i, 8), v.NetTime); i += 8;
            BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(i, 8), v.Latitude); i += 8;
            BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(i, 8), v.Longitude); i += 8;
            BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(i, 8), v.Altitude); i += 8;
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), v.Pitch); i += 4;
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), v.Bank); i += 4;
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), v.Heading); i += 4;
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), v.VelocityX); i += 4;
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), v.VelocityY); i += 4;
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), v.VelocityZ); i += 4;
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), v.AngularVelocityX); i += 4;
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), v.AngularVelocityY); i += 4;
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), v.AngularVelocityZ); i += 4;
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), v.AccelerationX); i += 4;
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), v.AccelerationY); i += 4;
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), v.AccelerationZ); i += 4;
            BinaryPrimitives.WriteInt16LittleEndian(dest.Slice(i, 2), ConvertToAxis(v.Rudder)); i += 2;
            BinaryPrimitives.WriteInt16LittleEndian(dest.Slice(i, 2), ConvertToAxis(v.Elevator)); i += 2;
            BinaryPrimitives.WriteInt16LittleEndian(dest.Slice(i, 2), ConvertToAxis(v.Aileron)); i += 2;
            BinaryPrimitives.WriteInt16LittleEndian(dest.Slice(i, 2), ConvertToAxis(v.BrakeLeft)); i += 2;
            BinaryPrimitives.WriteInt16LittleEndian(dest.Slice(i, 2), ConvertToAxis(v.BrakeRight)); i += 2;
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), v.Elevation); i += 4;
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), v.StaticCgToGround); i += 4;
            dest[i++] = (byte)v.StateFlags;
            return i; // == Size
        }

        public PositionUpdate Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            var v = new PositionUpdate();
            v.ObjectId = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(i, 4)); i += 4;
            v.NetTime = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i, 8)); i += 8;
            v.Latitude = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i, 8)); i += 8;
            v.Longitude = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i, 8)); i += 8;
            v.Altitude = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i, 8)); i += 8;
            v.Pitch = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.Bank = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.Heading = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.VelocityX = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.VelocityY = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.VelocityZ = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.AngularVelocityX = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.AngularVelocityY = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.AngularVelocityZ = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.AccelerationX = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.AccelerationY = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.AccelerationZ = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.Rudder = ConvertFromAxis(BinaryPrimitives.ReadInt16LittleEndian(src.Slice(i, 2))); i += 2;
            v.Elevator = ConvertFromAxis(BinaryPrimitives.ReadInt16LittleEndian(src.Slice(i, 2))); i += 2;
            v.Aileron = ConvertFromAxis(BinaryPrimitives.ReadInt16LittleEndian(src.Slice(i, 2))); i += 2;
            v.BrakeLeft = ConvertFromAxis(BinaryPrimitives.ReadInt16LittleEndian(src.Slice(i, 2))); i += 2;
            v.BrakeRight = ConvertFromAxis(BinaryPrimitives.ReadInt16LittleEndian(src.Slice(i, 2))); i += 2;
            v.Elevation = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.StaticCgToGround = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
            v.StateFlags = (PositionStateFlags)src[i++];
            return v;
        }
    }
}
