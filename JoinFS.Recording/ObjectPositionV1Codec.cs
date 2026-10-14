using System;
using System.Buffers.Binary;
using JoinFS.Net;

// The body of an object-position record, version 1 (docs/jfs2/format-spec.md section 7.2): 89 bytes. It is private to the
// recorder (spec Appendix C item 6): JFP2 has no message class for it, so the codec lives here and not in JoinFS.Messages.
// No build symbols (spec 10.8).

namespace JoinFS.Recording
{
    public static class ObjectPositionV1Codec
    {
        public const int Size = 89;

        /// <returns>The number of bytes written, <see cref="Size"/>.</returns>
        public static int Encode(in ObjectPositionUpdate v, Span<byte> dest)
        {
            if (dest.Length < Size)
            {
                throw new ArgumentException("An object-position body needs " + Size + " bytes.", nameof(dest));
            }
            int i = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(i, 4), v.ObjectId); i += 4;
            BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(i, 8), v.NetTime); i += 8;
            BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(i, 8), v.Latitude); i += 8;
            BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(i, 8), v.Longitude); i += 8;
            BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(i, 8), v.Altitude); i += 8;
            WriteFloat(dest, ref i, v.Pitch);
            WriteFloat(dest, ref i, v.Bank);
            WriteFloat(dest, ref i, v.Heading);
            WriteFloat(dest, ref i, v.VelocityX);
            WriteFloat(dest, ref i, v.VelocityY);
            WriteFloat(dest, ref i, v.VelocityZ);
            WriteFloat(dest, ref i, v.AngularVelocityX);
            WriteFloat(dest, ref i, v.AngularVelocityY);
            WriteFloat(dest, ref i, v.AngularVelocityZ);
            WriteFloat(dest, ref i, v.AccelerationX);
            WriteFloat(dest, ref i, v.AccelerationY);
            WriteFloat(dest, ref i, v.AccelerationZ);
            WriteFloat(dest, ref i, v.Height);
            dest[i++] = (byte)v.StateFlags;
            return i;
        }

        /// <summary>Reads exactly the 89 bytes of the body; bytes after them are ignored.</summary>
        /// <exception cref="FormatException">The body is shorter than <see cref="Size"/>.</exception>
        public static ObjectPositionUpdate Decode(ReadOnlySpan<byte> src)
        {
            if (src.Length < Size)
            {
                throw new FormatException("An object-position body is " + Size + " bytes; this one has " + src.Length + ".");
            }
            int i = 0;
            var v = new ObjectPositionUpdate();
            v.ObjectId = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(i, 4)); i += 4;
            v.NetTime = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i, 8)); i += 8;
            v.Latitude = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i, 8)); i += 8;
            v.Longitude = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i, 8)); i += 8;
            v.Altitude = BinaryPrimitives.ReadDoubleLittleEndian(src.Slice(i, 8)); i += 8;
            v.Pitch = ReadFloat(src, ref i);
            v.Bank = ReadFloat(src, ref i);
            v.Heading = ReadFloat(src, ref i);
            v.VelocityX = ReadFloat(src, ref i);
            v.VelocityY = ReadFloat(src, ref i);
            v.VelocityZ = ReadFloat(src, ref i);
            v.AngularVelocityX = ReadFloat(src, ref i);
            v.AngularVelocityY = ReadFloat(src, ref i);
            v.AngularVelocityZ = ReadFloat(src, ref i);
            v.AccelerationX = ReadFloat(src, ref i);
            v.AccelerationY = ReadFloat(src, ref i);
            v.AccelerationZ = ReadFloat(src, ref i);
            v.Height = ReadFloat(src, ref i);
            v.StateFlags = (PositionStateFlags)src[i];
            return v;
        }

        static void WriteFloat(Span<byte> dest, ref int at, float value)
        {
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(at, 4), value);
            at += 4;
        }

        static float ReadFloat(ReadOnlySpan<byte> src, ref int at)
        {
            float value = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(at, 4));
            at += 4;
            return value;
        }
    }
}
