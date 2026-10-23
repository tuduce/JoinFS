using System;
using System.Buffers.Binary;
using System.Collections.Generic;

// docs/network/jfp2/protocol.md §9.4 and §9.8: the kinds legacy alone carried until stage 10 of
// jfp2-standalone (ObjectPosition, RemoveObject, PeerInfo, CommsRequest), and the notes history,
// the only class that is larger than one datagram.

namespace JoinFS.Net.Jfp2.Codecs
{
    public sealed class ObjectPositionUpdateV1Codec : ICodec<ObjectPositionUpdate>
    {
        public byte MessageClass => MessageClasses.ObjectPosition;
        public byte SchemaVersion => 1;
        public const int Size = 89;

        public int Encode(in ObjectPositionUpdate v, Span<byte> dest)
        {
            int i = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(i, 4), v.ObjectId); i += 4;
            BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(i, 8), v.NetTime); i += 8;
            BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(i, 8), v.Latitude); i += 8;
            BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(i, 8), v.Longitude); i += 8;
            BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(i, 8), v.Altitude); i += 8;
            foreach (float f in (ReadOnlySpan<float>)[v.Pitch, v.Bank, v.Heading, v.VelocityX, v.VelocityY, v.VelocityZ,
                v.AngularVelocityX, v.AngularVelocityY, v.AngularVelocityZ, v.AccelerationX, v.AccelerationY, v.AccelerationZ, v.Height])
            {
                BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i, 4), f); i += 4;
            }
            dest[i++] = (byte)v.StateFlags;
            return i; // == Size
        }

        public ObjectPositionUpdate Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            var v = new ObjectPositionUpdate();
            v.ObjectId = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(i, 4)); i += 4;
            v.NetTime = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i, 8)); i += 8;
            v.Latitude = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i, 8)); i += 8;
            v.Longitude = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i, 8)); i += 8;
            v.Altitude = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i, 8)); i += 8;
            v.Pitch = ReadSingle(src, ref i); v.Bank = ReadSingle(src, ref i); v.Heading = ReadSingle(src, ref i);
            v.VelocityX = ReadSingle(src, ref i); v.VelocityY = ReadSingle(src, ref i); v.VelocityZ = ReadSingle(src, ref i);
            v.AngularVelocityX = ReadSingle(src, ref i); v.AngularVelocityY = ReadSingle(src, ref i); v.AngularVelocityZ = ReadSingle(src, ref i);
            v.AccelerationX = ReadSingle(src, ref i); v.AccelerationY = ReadSingle(src, ref i); v.AccelerationZ = ReadSingle(src, ref i);
            v.Height = ReadSingle(src, ref i);
            v.StateFlags = (PositionStateFlags)src[i++];
            return v;
        }

        static float ReadSingle(ReadOnlySpan<byte> src, ref int i)
        {
            float f = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i, 4));
            i += 4;
            return f;
        }
    }

    public sealed class RemoveObjectV1Codec : ICodec<RemoveObject>
    {
        public byte MessageClass => MessageClasses.RemoveObject;
        public byte SchemaVersion => 1;
        public const int Size = 4;

        public int Encode(in RemoveObject v, Span<byte> dest)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(dest, v.ObjectId);
            return Size;
        }

        public RemoveObject Decode(ReadOnlySpan<byte> src) => new() { ObjectId = BinaryPrimitives.ReadUInt32LittleEndian(src) };
    }

    public sealed class CommsRequestV1Codec : ICodec<CommsRequest>
    {
        public byte MessageClass => MessageClasses.CommsRequest;
        public byte SchemaVersion => 1;
        public const int Size = 1;

        public int Encode(in CommsRequest v, Span<byte> dest)
        {
            dest[0] = (byte)v.Scope;
            return Size;
        }

        public CommsRequest Decode(ReadOnlySpan<byte> src) => new() { Scope = (CommsScope)src[0] };
    }

    public sealed class PeerInfoV1Codec : ICodec<PeerInfo>
    {
        public byte MessageClass => MessageClasses.PeerInfo;
        public byte SchemaVersion => 1;

        // Byte limits of the string fields (UTF-8, without the length prefix; protocol.md §9.7)
        public const int NicknameLimit = 32;
        public const int AtcAirportLimit = 8;
        public const int VersionLimit = 32;
        public const int SimulatorLimit = 32;

        /// <summary>The largest payload: 22 fixed bytes, then four strings at their limits with their prefixes.</summary>
        public const int MaxSize = 22 + 4 * WireText.PrefixSize + NicknameLimit + AtcAirportLimit + VersionLimit + SimulatorLimit;

        public int Encode(in PeerInfo v, Span<byte> dest)
        {
            int i = 0;
            dest[i++] = (byte)v.Share;
            dest[i++] = (byte)((v.Hub ? 1 : 0) | (v.Atc ? 2 : 0) | (v.SimulatorConnected ? 4 : 0));
            v.Guid.ToByteArray().CopyTo(dest.Slice(i, 16)); i += 16;
            dest[i++] = v.AtcLevel;
            BinaryPrimitives.WriteInt16LittleEndian(dest.Slice(i, 2), v.AtcFrequency); i += 2;
            dest[i++] = v.ActivityCircle;
            i += WireText.WriteString(dest[i..], v.Nickname, NicknameLimit);
            i += WireText.WriteString(dest[i..], v.AtcAirport, AtcAirportLimit);
            i += WireText.WriteString(dest[i..], v.Version, VersionLimit);
            i += WireText.WriteString(dest[i..], v.Simulator, SimulatorLimit);
            return i;
        }

        public PeerInfo Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            var v = new PeerInfo();
            v.Share = (ShareCockpitFlags)src[i++];
            byte flags = src[i++];
            v.Hub = (flags & 1) != 0;
            v.Atc = (flags & 2) != 0;
            v.SimulatorConnected = (flags & 4) != 0;
            v.Guid = new Guid(src.Slice(i, 16).ToArray()); i += 16;
            v.AtcLevel = src[i++];
            v.AtcFrequency = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(i, 2)); i += 2;
            v.ActivityCircle = src[i++];
            v.Nickname = WireText.ReadString(src, ref i);
            v.AtcAirport = WireText.ReadString(src, ref i);
            v.Version = WireText.ReadString(src, ref i);
            v.Simulator = WireText.ReadString(src, ref i);
            return v;
        }
    }

    /// <summary>
    /// The notes of several users as one message (§9.8): the answer to a <see cref="CommsRequest"/>,
    /// and so larger than a datagram. One that would pass <see cref="MaxSize"/> is cut: whole notes
    /// of the user it reaches, then whole users, from the end.
    /// </summary>
    public sealed class NotesHistoryV1Codec : ICodec<NotesBundle>
    {
        public byte MessageClass => MessageClasses.NotesHistory;
        public byte SchemaVersion => 1;

        /// <summary>255 segments of <see cref="Jfp2Limits.MaxPayloadSize"/> bytes: what the guaranteed extension can number (§7.5).</summary>
        public const int MaxSize = 255 * Jfp2Limits.MaxPayloadSize;

        /// <summary>NoteId u32, Age f32, Channel u16, then the text.</summary>
        const int NoteFixedSize = 10;

        public int Encode(in NotesBundle v, Span<byte> dest)
        {
            var bytes = new List<byte>(1024) { (byte)v.Scope, 0, 0 };
            int users = 0;
            foreach (NotesUser user in v.Users ?? [])
            {
                int userStart = bytes.Count;
                if (users == ushort.MaxValue || userStart + 16 + 2 * WireText.PrefixSize + NotesV1Codec.NicknameLimit + NotesV1Codec.CallsignLimit + 2 > MaxSize)
                {
                    break;
                }
                bytes.AddRange(user.Guid.ToByteArray());
                WireText.WriteString(bytes, user.Nickname, NotesV1Codec.NicknameLimit);
                WireText.WriteString(bytes, user.Callsign, NotesV1Codec.CallsignLimit);
                int countAt = bytes.Count;
                bytes.AddRange(new byte[2]);
                int notes = 0;
                foreach (CommsNote note in user.Notes ?? [])
                {
                    if (notes == ushort.MaxValue || bytes.Count + NoteFixedSize + WireText.MeasureString(note.Text, NotesV1Codec.TextLimit) > MaxSize)
                    {
                        break;
                    }
                    Span<byte> fixedFields = stackalloc byte[NoteFixedSize];
                    BinaryPrimitives.WriteUInt32LittleEndian(fixedFields[..4], note.NoteId);
                    BinaryPrimitives.WriteSingleLittleEndian(fixedFields.Slice(4, 4), note.Age);
                    BinaryPrimitives.WriteUInt16LittleEndian(fixedFields.Slice(8, 2), note.Channel);
                    bytes.AddRange(fixedFields.ToArray());
                    WireText.WriteString(bytes, note.Text, NotesV1Codec.TextLimit);
                    notes++;
                }
                bytes[countAt] = (byte)notes;
                bytes[countAt + 1] = (byte)(notes >> 8);
                users++;
            }
            bytes[1] = (byte)users;
            bytes[2] = (byte)(users >> 8);
            bytes.CopyTo(dest);
            return bytes.Count;
        }

        public NotesBundle Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            var v = new NotesBundle { Scope = (CommsScope)src[i++], Users = [] };
            int users = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            for (int u = 0; u < users; u++)
            {
                var user = new NotesUser { Guid = new Guid(src.Slice(i, 16).ToArray()), Notes = [] };
                i += 16;
                user.Nickname = WireText.ReadString(src, ref i);
                user.Callsign = WireText.ReadString(src, ref i);
                int notes = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
                for (int n = 0; n < notes; n++)
                {
                    var note = new CommsNote
                    {
                        NoteId = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(i, 4)),
                        Age = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i + 4, 4)),
                        Channel = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i + 8, 2)),
                    };
                    i += NoteFixedSize;
                    note.Text = WireText.ReadString(src, ref i);
                    user.Notes.Add(note);
                }
                v.Users.Add(user);
            }
            return v;
        }
    }
}
