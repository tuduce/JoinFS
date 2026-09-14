using System;
using System.Buffers.Binary;
using System.Collections.Generic;

// docs/protocol-v2-implementation-plan.md Phase 3: a mechanical port of
// ProtocolV2Reference/Codecs.cs's VariableSyncV1Codec. Replaces the legacy protocol's three separate
// Integer/Float/String8 "variables" messages (docs/network-protocol.md §8.6-8.8) with one self-
// describing message - each entry carries its own VariableKind tag instead of the frame/message type
// implying the value type. See docs/protocol-v2-design.md §6.3/§6.5.
//
// Entry count is capped at one byte (255) per message, same as the reference. Network.cs's send side
// (SendJfp2VariableSync) chunks into multiple VariableSync messages when a single object's combined
// integer+float+string8 set exceeds that - see the constants there.
//
// String8 wire encoding (docs/protocol-v2-implementation-review.md Finding 5, fixed 2026-09-14):
// originally encoded as a fixed 8-byte ASCII field, on the assumption that "String8" meant the value
// itself is capped at 8 characters. It doesn't: "String8" names the category of SimConnect variable
// this carries (one declared with SIMCONNECT_DATATYPE.STRING8 - see JoinFS/SimConnectInterface.cs's
// AddToDataDefinition calls for that datatype), not a wire-width limit, and the legacy protocol's own
// String8Variables message this replaces writes the value with a plain BinaryWriter.Write(string) -
// a normal length-prefixed string, no 8-byte cap at all (JoinFS/Network.cs's
// SendString8VariablesMessage/Sim.Read). The old fixed-width encoding was therefore a JFP2-introduced
// regression, not a faithful port: any value over 8 bytes (or containing non-ASCII) got silently
// corrupted where legacy carried it losslessly. Now uses the same length-prefixed UTF8 encoding
// (WireText) every other string field in this codebase already uses.

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
        /// <summary>Used only when Kind == String8. No length cap beyond WireText's 2-byte (65535
        /// byte) length prefix - see this file's own header comment for why an 8-char cap would be
        /// wrong here.</summary>
        public string StringValue;
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

        /// <summary>Worst-case bytes per entry for the two fixed-size kinds (Int32/Float32). String8
        /// is variable-length - use EntrySize for an exact per-entry size when a String8 entry may be
        /// present, e.g. when sizing a send buffer (see Network.SendJfp2VariableSync).</summary>
        public const int MaxBytesPerEntry = 4 + 1 + 4;

        /// <summary>Fixed header size: ObjectId (4) + entry count (1).</summary>
        public const int HeaderSize = 5;

        /// <summary>Exact wire size of one entry: Vuid (4) + Kind (1) + the value, whose size depends
        /// on Kind (4 for Int32/Float32, 2 + UTF8 byte count for String8). Lets a caller size a send
        /// buffer precisely instead of assuming every entry is as large as the old fixed-width String8
        /// encoding used to guarantee (docs/protocol-v2-implementation-review.md Finding 5).</summary>
        public static int EntrySize(in VariableEntry e) => 4 + 1 + e.Kind switch
        {
            VariableKind.Int32 => 4,
            VariableKind.Float32 => 4,
            VariableKind.String8 => WireText.MeasureString(e.StringValue),
            _ => throw new ArgumentOutOfRangeException(nameof(e), e.Kind, "unknown VariableKind"),
        };

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
                        i += WireText.WriteString(dest.Slice(i), e.StringValue);
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
                        e.StringValue = WireText.ReadString(src, ref i);
                        break;
                }
                v.Entries.Add(e);
            }
            return v;
        }
    }
}
