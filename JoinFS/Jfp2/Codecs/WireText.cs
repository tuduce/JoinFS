using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace JoinFS.Jfp2.Codecs
{
    /// <summary>
    /// Shared length-prefixed UTF8 string helper for codecs whose fields are sent rarely enough
    /// (join/change/low-frequency messages, not the per-tick Position hot path) that the extra 1-2
    /// bytes per string is irrelevant - same rationale docs/protocol-v2-design.md §6.2 gives for
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

        public static string ReadString(ReadOnlySpan<byte> src, ref int i)
        {
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            string s = len == 0 ? string.Empty : Encoding.UTF8.GetString(src.Slice(i, len)); i += len;
            return s;
        }
    }
}
