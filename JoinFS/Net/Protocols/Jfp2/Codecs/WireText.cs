using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace JoinFS.Net.Jfp2.Codecs
{
    /// <summary>
    /// Shared length-prefixed UTF8 string helper for codecs whose fields are sent rarely enough
    /// (join/change/low-frequency messages, not the per-tick Position hot path) that the extra 1-2
    /// bytes per string is irrelevant - same rationale docs/reference/jfp2-protocol.md §6.2 gives for
    /// `IdentityV1Codec`'s string encoding, factored out here so every codec that needs it (Status
    /// now, Event/FlightPlan/Notes/Weather in later phases) shares one implementation instead of
    /// duplicating it per codec.
    /// </summary>
    static class WireText
    {
        public static void WriteString(List<byte> dest, string s)
        {
            byte[] bytes = string.IsNullOrEmpty(s) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(s);
            Span<byte> len = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(len, (ushort)bytes.Length);
            dest.AddRange(len.ToArray());
            dest.AddRange(bytes);
        }

        /// <summary>
        /// Span-based counterpart to the List&lt;byte&gt; overload above, for codecs that write directly
        /// into a caller-provided buffer instead of building one up (e.g. VariableSyncV1Codec, which
        /// stays off the heap for its fixed-size entries - see docs/protocol-v2-implementation-
        /// review.md Finding 5 for why its String8 entries need this rather than the fixed 8-byte
        /// field they used to be). Returns the number of bytes written (2 + the UTF8 byte count).
        /// </summary>
        public static int WriteString(Span<byte> dest, string s)
        {
            byte[] bytes = string.IsNullOrEmpty(s) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(s);
            BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(0, 2), (ushort)bytes.Length);
            bytes.AsSpan().CopyTo(dest.Slice(2));
            return 2 + bytes.Length;
        }

        public static string ReadString(ReadOnlySpan<byte> src, ref int i)
        {
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            string s = len == 0 ? string.Empty : Encoding.UTF8.GetString(src.Slice(i, len)); i += len;
            return s;
        }

        /// <summary>Bytes WriteString would need for `s` - 2-byte length prefix plus its UTF8 byte
        /// count. Lets a caller size a destination buffer exactly instead of guessing a worst case.
        /// </summary>
        public static int MeasureString(string s) =>
            2 + (string.IsNullOrEmpty(s) ? 0 : Encoding.UTF8.GetByteCount(s));
    }
}
