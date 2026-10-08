using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Text.Unicode;

namespace JoinFS.Net.Jfp2.Codecs
{
    /// <summary>
    /// Shared length-prefixed UTF8 string helper for codecs whose fields are sent rarely enough
    /// (join/change/low-frequency messages, not the per-tick Position hot path) that the extra 1-2
    /// bytes per string is irrelevant - same rationale docs/jfp2/protocol.md §9.2 gives for
    /// `IdentityV1Codec`'s string encoding, factored out here so every codec that needs it shares one
    /// implementation instead of duplicating it per codec.
    ///
    /// Every write takes the field's byte limit (docs/jfp2/protocol.md §9.7): a constant of the
    /// codec, next to the field it bounds. A longer text is cut at a UTF-8 character boundary, never
    /// inside a character, so what goes out is still valid UTF-8. The limits keep every message within
    /// the 1,200-byte datagram ceiling (§4.5), and since a limit can be no more than <see cref="MaxLimit"/>
    /// the u16 length prefix cannot wrap. Reading checks no limit: a message is valid at any size that
    /// fits its datagram.
    /// </summary>
    static class WireText
    {
        /// <summary>Bytes of the length prefix (u16 LE) before every string.</summary>
        public const int PrefixSize = 2;

        /// <summary>The largest limit a field may have: the whole payload of a datagram (design §4.5).</summary>
        public const int MaxLimit = Envelope.MaxPayloadSize;

        /// <summary>Limits up to this many bytes are encoded on the stack; larger ones (none today) on the heap.</summary>
        const int StackLimit = 1024;

        /// <summary>
        /// Writes <paramref name="s"/> as a u16 length and its UTF-8 bytes, cut to at most
        /// <paramref name="limit"/> bytes at a character boundary. Allocation-free. Returns the number
        /// of bytes written (2 + the UTF-8 byte count). Throws when <paramref name="dest"/> cannot hold
        /// the text as it would be sent.
        /// </summary>
        public static int WriteString(Span<byte> dest, string s, int limit)
        {
            if ((uint)limit > MaxLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(limit), limit, "a JFP2 string field's limit is 0 to " + MaxLimit + " bytes");
            }
            Span<byte> text = dest[PrefixSize..];
            bool room = text.Length >= limit;
            int length = Encode(s, room ? text[..limit] : text, out bool cut);
            if (cut && !room)
            {
                throw new ArgumentException("destination too small for a JFP2 string", nameof(dest));
            }
            BinaryPrimitives.WriteUInt16LittleEndian(dest, (ushort)length);
            return PrefixSize + length;
        }

        /// <summary>The <see cref="List{T}"/> counterpart of the span overload, for codecs that build a message up.</summary>
        public static void WriteString(List<byte> dest, string s, int limit)
        {
            Span<byte> buffer = limit <= StackLimit ? stackalloc byte[PrefixSize + StackLimit] : new byte[PrefixSize + limit];
            int length = WriteString(buffer, s, limit);
            dest.AddRange(buffer[..length]);
        }

        public static string ReadString(ReadOnlySpan<byte> src, ref int i)
        {
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            string s = len == 0 ? string.Empty : Encoding.UTF8.GetString(src.Slice(i, len)); i += len;
            return s;
        }

        /// <summary>Bytes WriteString would write for `s` under `limit`: the 2-byte length prefix plus
        /// its UTF-8 byte count, cut as WriteString cuts it. Lets a caller size a destination exactly.
        /// </summary>
        public static int MeasureString(string s, int limit)
        {
            if (string.IsNullOrEmpty(s))
            {
                return PrefixSize;
            }
            int count = Encoding.UTF8.GetByteCount(s);
            if (count <= limit)
            {
                return PrefixSize + count;
            }
            Span<byte> buffer = limit <= StackLimit ? stackalloc byte[StackLimit] : new byte[limit];
            return PrefixSize + Encode(s, buffer[..limit], out _);
        }

        /// <summary>
        /// <paramref name="s"/> as UTF-8 into <paramref name="text"/>, as many whole characters as fit;
        /// <paramref name="cut"/> says some did not. Invalid UTF-16 (a lone surrogate) becomes U+FFFD,
        /// as <see cref="Encoding.UTF8"/> does.
        /// </summary>
        static int Encode(string s, Span<byte> text, out bool cut)
        {
            if (string.IsNullOrEmpty(s))
            {
                cut = false;
                return 0;
            }
            OperationStatus status = Utf8.FromUtf16(s, text, out _, out int written, replaceInvalidSequences: true, isFinalBlock: true);
            cut = status == OperationStatus.DestinationTooSmall;
            return written;
        }
    }
}
