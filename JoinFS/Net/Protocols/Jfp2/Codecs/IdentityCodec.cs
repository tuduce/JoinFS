using System;
using System.Buffers.Binary;
using System.Collections.Generic;

// docs/protocol-v2-implementation-plan.md Phase 3: a mechanical port of
// ProtocolV2Reference/Codecs.cs's IdentityV1Codec, with the identity fields sourced for real from
// JoinFS/Sim.cs's Obj/Aircraft (see Network.BuildIdentity) instead of the reference
// demo's hand-written sample values. Splitting these fields out of the hot Position message is the
// fix for the v26.4/v26.5 livery bug (docs/protocol-changes-v26.4-v26.5.md §1.2) and its recording-
// format mirror (docs/recording-protocol.md §7.1) - see docs/reference/jfp2-protocol.md §6.2.

using JoinFS.Net;

namespace JoinFS.Net.Jfp2.Codecs
{
    /// <summary>
    /// Length-prefixed UTF8 strings throughout (via the shared WireText helper) - every field here is
    /// sent rarely enough (join/change, not per-tick) that the extra 1-2 bytes per string is
    /// irrelevant; the "shave the last millisecond" goal applies to the Position hot path, not here.
    /// </summary>
    public sealed class IdentityV1Codec : ICodec<IdentityUpdate>
    {
        public byte MessageClass => MessageClasses.Identity;
        public byte SchemaVersion => 1;

        public int Encode(in IdentityUpdate v, Span<byte> dest)
        {
            var bytes = new List<byte>(160);
            Span<byte> id = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(id, v.ObjectId);
            bytes.AddRange(id.ToArray());
            byte flags = (byte)((v.IsAircraft ? 1 : 0) | (v.IsPlane ? 2 : 0) | (v.ClassCodeConfirmed ? 4 : 0));
            bytes.Add(flags);
            bytes.Add(v.TypeRole);
            WireText.WriteString(bytes, v.Callsign);
            WireText.WriteString(bytes, v.Model);
            WireText.WriteString(bytes, v.Livery);
            WireText.WriteString(bytes, v.IcaoType);
            WireText.WriteString(bytes, v.IcaoAirline);
            WireText.WriteString(bytes, v.Registration);
            WireText.WriteString(bytes, v.FlightNumber);
            WireText.WriteString(bytes, v.ClassCode);
            WireText.WriteString(bytes, v.Wtc);
            bytes.CopyTo(dest);
            return bytes.Count;
        }

        public IdentityUpdate Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            var v = new IdentityUpdate();
            v.ObjectId = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(i, 4)); i += 4;
            byte flags = src[i++];
            v.IsAircraft = (flags & 1) != 0;
            v.IsPlane = (flags & 2) != 0;
            v.ClassCodeConfirmed = (flags & 4) != 0;
            v.TypeRole = src[i++];
            v.Callsign = WireText.ReadString(src, ref i);
            v.Model = WireText.ReadString(src, ref i);
            v.Livery = WireText.ReadString(src, ref i);
            v.IcaoType = WireText.ReadString(src, ref i);
            v.IcaoAirline = WireText.ReadString(src, ref i);
            v.Registration = WireText.ReadString(src, ref i);
            v.FlightNumber = WireText.ReadString(src, ref i);
            v.ClassCode = WireText.ReadString(src, ref i);
            v.Wtc = WireText.ReadString(src, ref i);
            return v;
        }
    }
}
