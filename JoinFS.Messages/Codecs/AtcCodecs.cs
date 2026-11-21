using System;
using System.Buffers.Binary;

// docs/network/jfp2/protocol.md §9.9: the two classes of the ATC clients on the mesh.

namespace JoinFS.Net.Jfp2.Codecs
{
    public sealed class AtcStationV1Codec : ICodec<AtcStation>
    {
        public byte MessageClass => MessageClasses.AtcStation;
        public byte SchemaVersion => 1;

        // Byte limits of the string fields (UTF-8, without the length prefix; protocol.md §9.7)
        public const int CallsignLimit = 12;
        public const int NameLimit = 32;

        /// <summary>Slot, Flags, Facility, Rating, Visibility u16, Frequency u32, Latitude and Longitude f64.</summary>
        const int FixedSize = 26;

        /// <summary>The largest payload: the fixed fields, then two strings at their limits with their prefixes.</summary>
        public const int MaxSize = FixedSize + 2 * WireText.PrefixSize + CallsignLimit + NameLimit;

        const byte OnlineFlag = 1;

        public int Encode(in AtcStation v, Span<byte> dest)
        {
            int i = 0;
            dest[i++] = v.Slot;
            dest[i++] = v.Online ? OnlineFlag : (byte)0;
            dest[i++] = v.Facility;
            dest[i++] = v.Rating;
            BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(i, 2), v.Visibility); i += 2;
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(i, 4), v.Frequency); i += 4;
            BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(i, 8), v.Latitude); i += 8;
            BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(i, 8), v.Longitude); i += 8;
            i += WireText.WriteString(dest[i..], v.Callsign, CallsignLimit);
            i += WireText.WriteString(dest[i..], v.Name, NameLimit);
            return i;
        }

        public AtcStation Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            var v = new AtcStation();
            v.Slot = src[i++];
            v.Online = (src[i++] & OnlineFlag) != 0; // the other bits are not assigned: ignored
            v.Facility = src[i++];
            v.Rating = src[i++];
            v.Visibility = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            v.Frequency = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(i, 4)); i += 4;
            v.Latitude = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i, 8)); i += 8;
            v.Longitude = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i, 8)); i += 8;
            v.Callsign = WireText.ReadString(src, ref i);
            v.Name = WireText.ReadString(src, ref i);
            return v;
        }
    }

    public sealed class AtcRelayV1Codec : ICodec<AtcRelay>
    {
        public byte MessageClass => MessageClasses.AtcRelay;
        public byte SchemaVersion => 1;

        /// <summary>
        /// Bytes of the line (UTF-8, without the line ending and the length prefix). The codec cuts a longer one, which
        /// would be a broken FSD line, so the sender checks the size first and does not send such a line (plan §4.5).
        /// </summary>
        public const int LineLimit = 1000;

        public const int MaxSize = WireText.PrefixSize + LineLimit;

        public int Encode(in AtcRelay v, Span<byte> dest) => WireText.WriteString(dest, v.Line, LineLimit);

        public AtcRelay Decode(ReadOnlySpan<byte> src)
        {
            int i = 0;
            return new AtcRelay { Line = WireText.ReadString(src, ref i) };
        }
    }
}
