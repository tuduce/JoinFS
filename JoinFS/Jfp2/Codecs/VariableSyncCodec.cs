using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

// docs/protocol-v2-implementation-plan.md Phase 3: a mechanical port of
// ProtocolV2Reference/Codecs.cs's VariableSyncV1Codec. Replaces the legacy protocol's three separate
// Integer/Float/String8 "variables" messages (docs/network-protocol.md §8.6-8.8) with one self-
// describing message - each entry carries its own VariableKind tag instead of the frame/message type
// implying the value type. See docs/protocol-v2-design.md §6.3/§6.5.
//
// Entry count is capped at one byte (255) per message, same as the reference. Network.cs's send side
// (SendJfp2VariableSync) chunks into multiple VariableSync messages when a single object's combined
// integer+float+string8 set exceeds that - see the constants there.

namespace JoinFS.Jfp2.Codecs
{
    public enum VariableKind : byte
    {
        Int32 = 0,
        Float32 = 1,
        String8 = 2,
    }

    public struct VariableEntry
    {
        /// <summary>
        /// The SAME vuid the current implementation already computes and sends today
        /// (VariableMgr.CreateVuid -> LocalNode.HashString(name)) - not a new negotiated identifier.
        /// Every peer derives the same vuid from the same variable name independently, so no
        /// negotiation of this field is needed (docs/protocol-v2-design.md §6.5).
        /// </summary>
        public uint Vuid;
        public VariableKind Kind;
        public int IntValue;
        public float FloatValue;
        public string StringValue; // used only when Kind == String8, max 8 chars as in the legacy protocol
    }

    /// <summary>One object's batch of simulator variable updates.</summary>
    public struct VariableSyncUpdate
    {
        /// <summary>Obj.netId - widened from the reference's `ushort ObjectId` to `uint`, matching
        /// IdentityUpdate.ObjectId; see that struct's comment for why.</summary>
        public uint ObjectId;
        public List<VariableEntry> Entries;
    }

    public sealed class VariableSyncV1Codec : ICodec<VariableSyncUpdate>
    {
        public byte MessageClass => MessageClasses.VariableSync;
        public byte SchemaVersion => 1;

        /// <summary>Worst-case bytes per entry (String8's fixed 8-byte value is the largest payload).</summary>
        public const int MaxBytesPerEntry = 4 + 1 + 8;

        /// <summary>Fixed header size: ObjectId (4) + entry count (1).</summary>
        public const int HeaderSize = 5;

        public int Encode(in VariableSyncUpdate v, Span<byte> dest)
        {
            int i = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(i, 4), v.ObjectId); i += 4;
            dest[i++] = (byte)v.Entries.Count;
            foreach (var e in v.Entries)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(i, 4), e.Vuid); i += 4;
                dest[i++] = (byte)e.Kind;
                switch (e.Kind)
                {
                    case VariableKind.Int32:
                        BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(i, 4), e.IntValue); i += 4;
                        break;
                    case VariableKind.Float32:
                        BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), e.FloatValue); i += 4;
                        break;
                    case VariableKind.String8:
                        byte[] strBytes = Encoding.ASCII.GetBytes((e.StringValue ?? string.Empty).PadRight(8)[..8]);
                        strBytes.AsSpan().CopyTo(dest.Slice(i, 8)); i += 8;
                        break;
                }
            }
            return i;
        }

        public VariableSyncUpdate Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            var v = new VariableSyncUpdate { Entries = new List<VariableEntry>() };
            v.ObjectId = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(i, 4)); i += 4;
            byte count = src[i++];
            for (int n = 0; n < count; n++)
            {
                var e = new VariableEntry();
                e.Vuid = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(i, 4)); i += 4;
                e.Kind = (VariableKind)src[i++];
                switch (e.Kind)
                {
                    case VariableKind.Int32:
                        e.IntValue = BinaryPrimitives.ReadInt32LittleEndian(src.Slice(i, 4)); i += 4;
                        break;
                    case VariableKind.Float32:
                        e.FloatValue = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4)); i += 4;
                        break;
                    case VariableKind.String8:
                        e.StringValue = Encoding.ASCII.GetString(src.Slice(i, 8)).TrimEnd(); i += 8;
                        break;
                }
                v.Entries.Add(e);
            }
            return v;
        }
    }
}
