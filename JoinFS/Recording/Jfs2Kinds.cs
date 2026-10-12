using System;
using System.Collections.Generic;
using System.Text;
using JoinFS.Recording.Format;

// The record kinds of docs/jfs2/format-spec.md section 7 that the engine writes and reads so far.
// The container library sees records as opaque (kind, track, time, bytes); this is where a kind gets its meaning.
// No build symbols here (spec 10.8): a conditional compilation symbol must never change a byte of the file.

namespace JoinFS.Recording
{
    /// <summary>Names and versions of the record kinds (spec 7). A shipped (name, version) is never edited.</summary>
    public static class Jfs2Kinds
    {
        public const string TrackOpen = Jfs2.TrackOpenKind;
        public const string TrackClose = Jfs2.TrackCloseKind;
        public const string Identity = "identity";
        public const string Position = "position";
        public const string Variables = "variables";
        public const string Event = "event";

        public const byte TrackOpenVersion = 1;
        public const byte TrackCloseVersion = 1;
        public const byte IdentityVersion = 1;
        public const byte PositionVersion = 1;
        public const byte VariablesVersion = 1;
        public const byte EventVersion = 1;
    }

    /// <summary>The kind ids a writer bound for the kinds this engine writes.</summary>
    public readonly record struct Jfs2KindIds(uint TrackOpen, uint TrackClose, uint Identity, uint Position, uint Variables, uint Event)
    {
        public static Jfs2KindIds Bind(RecordingWriter writer) => new(
            writer.BindKind(Jfs2Kinds.TrackOpen, Jfs2Kinds.TrackOpenVersion),
            writer.BindKind(Jfs2Kinds.TrackClose, Jfs2Kinds.TrackCloseVersion),
            writer.BindKind(Jfs2Kinds.Identity, Jfs2Kinds.IdentityVersion),
            writer.BindKind(Jfs2Kinds.Position, Jfs2Kinds.PositionVersion),
            writer.BindKind(Jfs2Kinds.Variables, Jfs2Kinds.VariablesVersion),
            writer.BindKind(Jfs2Kinds.Event, Jfs2Kinds.EventVersion));
    }

    /// <summary>Who owned the object when it was recorded (spec 7.1).</summary>
    public enum TrackOwner : byte
    {
        /// <summary>This application's own object.</summary>
        Local = 0,
        /// <summary>A remote pilot's.</summary>
        Network = 1,
        /// <summary>Played from a recording earlier.</summary>
        Recorded = 2,
    }

    /// <summary>The body of a <c>track-open</c> record, version 1 (spec 7.1). Strings are varint-length UTF-8 (spec 1.2).</summary>
    public readonly record struct TrackOpenV1(TrackOwner Owner, string Nickname, string SourceApp, string Simulator)
    {
        public byte[] Encode()
        {
            var bytes = new List<byte>(32) { (byte)Owner };
            WriteString(bytes, Nickname);
            WriteString(bytes, SourceApp);
            WriteString(bytes, Simulator);
            return bytes.ToArray();
        }

        /// <summary>Trailing bytes after the last field are ignored (spec 7.1).</summary>
        /// <exception cref="FormatException">The body is too short for its strings.</exception>
        public static TrackOpenV1 Decode(ReadOnlySpan<byte> body)
        {
            if (body.Length < 1)
            {
                throw new FormatException("A track-open body needs its owner byte.");
            }
            int at = 1;
            string nickname = ReadString(body, ref at);
            string sourceApp = ReadString(body, ref at);
            string simulator = ReadString(body, ref at);
            return new TrackOpenV1((TrackOwner)body[0], nickname, sourceApp, simulator);
        }

        static void WriteString(List<byte> bytes, string text)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(text ?? "");
            ulong length = (ulong)utf8.Length;
            while (length >= 0x80)
            {
                bytes.Add((byte)(length | 0x80));
                length >>= 7;
            }
            bytes.Add((byte)length);
            bytes.AddRange(utf8);
        }

        static string ReadString(ReadOnlySpan<byte> body, ref int at)
        {
            ulong length = 0;
            for (int shift = 0; ; shift += 7)
            {
                if (at >= body.Length || shift > 63)
                {
                    throw new FormatException("A track-open string has a damaged length.");
                }
                byte b = body[at++];
                length |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    break;
                }
            }
            if (length > (ulong)(body.Length - at))
            {
                throw new FormatException("A track-open string runs past the end of the body.");
            }
            string text = Encoding.UTF8.GetString(body.Slice(at, (int)length));
            at += (int)length;
            return text;
        }
    }
}
