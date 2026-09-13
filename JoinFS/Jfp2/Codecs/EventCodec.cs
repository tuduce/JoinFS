using System;
using System.Buffers.Binary;

// docs/protocol-v2-implementation-plan.md Phase 5: a mechanical port of the legacy SimEvent message
// (docs/network-protocol.md §8.2: "a discrete SimConnect/X-Plane event forwarded to a specific
// object, e.g. gear up/down, lights toggle"). Fixed 3-field shape, no version gating in the legacy
// message either, so nothing to widen or simplify here beyond ObjectId's uint (matching every other
// Phase 3/4 codec - Obj.netId is never ushort).

namespace JoinFS.Jfp2.Codecs
{
    public struct EventUpdate
    {
        /// <summary>Obj.netId, or uint.MaxValue for the shared-cockpit sentinel (matching every other
        /// per-object JFP2 message).</summary>
        public uint ObjectId;
        public uint EventId;
        public uint Data;
    }

    public sealed class EventV1Codec : ICodec<EventUpdate>
    {
        public byte MessageClass => MessageClasses.Event;
        public byte SchemaVersion => 1;
        public const int Size = 12;

        public int Encode(in EventUpdate v, Span<byte> dest)
        {
            int i = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(i, 4), v.ObjectId); i += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(i, 4), v.EventId); i += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(i, 4), v.Data); i += 4;
            return i; // == Size
        }

        public EventUpdate Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            var v = new EventUpdate();
            v.ObjectId = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(i, 4)); i += 4;
            v.EventId = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(i, 4)); i += 4;
            v.Data = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(i, 4)); i += 4;
            return v;
        }
    }
}
