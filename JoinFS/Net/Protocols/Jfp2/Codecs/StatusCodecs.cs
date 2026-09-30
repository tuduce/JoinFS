using System;
using System.Buffers.Binary;
using System.Collections.Generic;

// docs/protocol-v2-implementation-plan.md Phase 2: the first real application-partition codec pair,
// a mechanical port of the legacy StatusRequest/Status messages (docs/network-protocol.md §8.6) onto
// the JFP2 envelope, per docs/reference/jfp2-protocol.md §6.4. Field order and meaning match the legacy
// wire shape exactly; the one deliberate difference is that every field is always present on the wire
// here (no "only if AtcCount>0" / "only if HubEnabled" conditional writes) - JFP2's codecs are meant
// to replace conditional/EOF-sensed shapes with a single, version-explicit layout (design doc §1.3,
// §7.5), and Status is low-frequency enough that a handful of empty strings costs nothing worth
// measuring. JoinFS/Network.cs's HandleStatus/HandleStatusRequest apply the exact same downstream
// logic to a decoded value here as to a legacy-decoded one - see the comment there.

using JoinFS.Net;

namespace JoinFS.Net.Jfp2.Codecs
{
    public sealed class StatusRequestV1Codec : ICodec<StatusRequestUpdate>
    {
        public byte MessageClass => MessageClasses.StatusRequest;
        public byte SchemaVersion => 1;
        public const int Size = 5;

        public int Encode(in StatusRequestUpdate v, Span<byte> dest)
        {
            int i = 0;
            byte flags = (byte)((v.HubEnabled ? 1 : 0) | (v.HubListRequested ? 2 : 0));
            dest[i++] = flags;
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(i, 4), v.Uuid); i += 4;
            return i; // == Size
        }

        public StatusRequestUpdate Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            var v = new StatusRequestUpdate();
            byte flags = src[i++];
            v.HubEnabled = (flags & 1) != 0;
            v.HubListRequested = (flags & 2) != 0;
            v.Uuid = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(i, 4)); i += 4;
            return v;
        }
    }

    public sealed class StatusV1Codec : ICodec<StatusUpdate>
    {
        public byte MessageClass => MessageClasses.Status;
        public byte SchemaVersion => 1;

        public int Encode(in StatusUpdate v, Span<byte> dest)
        {
            var bytes = new List<byte>(160);
            bytes.AddRange(v.Guid.ToByteArray()); // 16 bytes
            WireText.WriteString(bytes, v.AppVersion);

            Span<byte> u16 = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(u16, v.Users); bytes.AddRange(u16.ToArray());
            BinaryPrimitives.WriteUInt16LittleEndian(u16, v.AtcCount); bytes.AddRange(u16.ToArray());
            WireText.WriteString(bytes, v.AtcAirport);
            bytes.Add((byte)v.AtcLevel);
            BinaryPrimitives.WriteUInt16LittleEndian(u16, v.Planes); bytes.AddRange(u16.ToArray());
            BinaryPrimitives.WriteUInt16LittleEndian(u16, v.Helicopters); bytes.AddRange(u16.ToArray());
            BinaryPrimitives.WriteUInt16LittleEndian(u16, v.Boats); bytes.AddRange(u16.ToArray());
            BinaryPrimitives.WriteUInt16LittleEndian(u16, v.Vehicles); bytes.AddRange(u16.ToArray());

            byte hubFlags = (byte)((v.HubEnabled ? 1 : 0) | (v.GlobalSession ? 2 : 0) | (v.PasswordRequired ? 4 : 0));
            bytes.Add(hubFlags);
            WireText.WriteString(bytes, v.Address);
            WireText.WriteString(bytes, v.Name);
            WireText.WriteString(bytes, v.About);
            WireText.WriteString(bytes, v.Voip);
            WireText.WriteString(bytes, v.NextEvent);
            WireText.WriteString(bytes, v.Airport);
            Span<byte> i32 = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(i32, v.ActivityCircle); bytes.AddRange(i32.ToArray());

            bytes.CopyTo(dest);
            return bytes.Count;
        }

        public StatusUpdate Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            var v = new StatusUpdate();
            v.Guid = new Guid(src.Slice(i, 16).ToArray()); i += 16;
            v.AppVersion = WireText.ReadString(src, ref i);

            v.Users = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            v.AtcCount = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            v.AtcAirport = WireText.ReadString(src, ref i);
            v.AtcLevel = src[i++];
            v.Planes = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            v.Helicopters = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            v.Boats = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            v.Vehicles = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;

            byte hubFlags = src[i++];
            v.HubEnabled = (hubFlags & 1) != 0;
            v.GlobalSession = (hubFlags & 2) != 0;
            v.PasswordRequired = (hubFlags & 4) != 0;
            v.Address = WireText.ReadString(src, ref i);
            v.Name = WireText.ReadString(src, ref i);
            v.About = WireText.ReadString(src, ref i);
            v.Voip = WireText.ReadString(src, ref i);
            v.NextEvent = WireText.ReadString(src, ref i);
            v.Airport = WireText.ReadString(src, ref i);
            v.ActivityCircle = BinaryPrimitives.ReadInt32LittleEndian(src.Slice(i, 4)); i += 4;

            return v;
        }
    }
}
