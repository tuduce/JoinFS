using System;
using System.Collections.Generic;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    // Round-trip coverage for the VariableSync codec (docs/protocol-v2-implementation-plan.md Phase 3).
    public class VariableSyncCodecTests
    {
        [Fact]
        public void MixedKindEntries_RoundTrip()
        {
            var codec = new VariableSyncV1Codec();
            var sync = new VariableSyncUpdate
            {
                ObjectId = 99,
                Entries = new List<VariableEntry>
                {
                    new VariableEntry { Vuid = 0x1A2B3C4Du, Kind = VariableKind.Int32, IntValue = -7 },
                    new VariableEntry { Vuid = 0x2B3C4D5Eu, Kind = VariableKind.Float32, FloatValue = 0.75f },
                    new VariableEntry { Vuid = 0x3C4D5E6Fu, Kind = VariableKind.String8, StringValue = "N12345" },
                }
            };

            byte[] buffer = new byte[256];
            int written = codec.Encode(sync, buffer);
            VariableSyncUpdate back = codec.Decode(buffer.AsSpan(0, written));

            Assert.Equal(sync.ObjectId, back.ObjectId);
            Assert.Equal(3, back.Entries.Count);
            Assert.Equal(-7, back.Entries[0].IntValue);
            Assert.Equal(0.75f, back.Entries[1].FloatValue);
            Assert.Equal("N12345", back.Entries[2].StringValue);
        }

        // docs/protocol-v2-implementation-review.md Finding 5 (fixed 2026-09-14): String8 used to be a
        // fixed 8-byte ASCII field that silently truncated/mangled anything longer or non-ASCII. It's
        // now length-prefixed UTF8 like every other string field, matching the legacy String8Variables
        // message's own wire encoding (a plain BinaryWriter.Write(string), never capped at 8 bytes).

        [Fact]
        public void String8Value_LongerThan8Chars_RoundTripsExactly()
        {
            var codec = new VariableSyncV1Codec();
            const string value = "TOOLONGVALUEFORTHEOLD8BYTEFIELD";
            var sync = new VariableSyncUpdate
            {
                ObjectId = 1,
                Entries = new List<VariableEntry> { new VariableEntry { Vuid = 1, Kind = VariableKind.String8, StringValue = value } }
            };

            byte[] buffer = new byte[256];
            int written = codec.Encode(sync, buffer);
            VariableSyncUpdate back = codec.Decode(buffer.AsSpan(0, written));

            Assert.Equal(value, back.Entries[0].StringValue);
        }

        [Fact]
        public void String8Value_ShortString_RoundTripsExactly()
        {
            var codec = new VariableSyncV1Codec();
            var sync = new VariableSyncUpdate
            {
                ObjectId = 1,
                Entries = new List<VariableEntry> { new VariableEntry { Vuid = 1, Kind = VariableKind.String8, StringValue = "AB" } }
            };

            byte[] buffer = new byte[256];
            int written = codec.Encode(sync, buffer);
            VariableSyncUpdate back = codec.Decode(buffer.AsSpan(0, written));

            // No more fixed-width padding to trim - "AB" comes back as exactly "AB", not "AB      ".
            Assert.Equal("AB", back.Entries[0].StringValue);
        }

        [Fact]
        public void String8Value_NonAscii_RoundTripsExactly()
        {
            // The old ASCII-only fixed-width encoding would have mangled this; UTF8 doesn't.
            var codec = new VariableSyncV1Codec();
            const string value = "Zürich–München";
            var sync = new VariableSyncUpdate
            {
                ObjectId = 1,
                Entries = new List<VariableEntry> { new VariableEntry { Vuid = 1, Kind = VariableKind.String8, StringValue = value } }
            };

            byte[] buffer = new byte[256];
            int written = codec.Encode(sync, buffer);
            VariableSyncUpdate back = codec.Decode(buffer.AsSpan(0, written));

            Assert.Equal(value, back.Entries[0].StringValue);
        }

        [Fact]
        public void EntrySize_SizesABufferThatFitsExactly()
        {
            // Network.SendJfp2VariableSync sizes its send buffer with EntrySize instead of a fixed
            // guess (see the same finding's fix in Network.cs) - confirm it's neither too small
            // (Encode would throw) nor wastefully large (Decode should consume exactly `written`).
            var codec = new VariableSyncV1Codec();
            var entry = new VariableEntry { Vuid = 1, Kind = VariableKind.String8, StringValue = new string('X', 500) };
            var sync = new VariableSyncUpdate { ObjectId = 1, Entries = new List<VariableEntry> { entry } };

            int bufferSize = VariableSyncV1Codec.HeaderSize + VariableSyncV1Codec.EntrySize(entry);
            byte[] buffer = new byte[bufferSize];
            int written = codec.Encode(sync, buffer);

            Assert.Equal(bufferSize, written);
            VariableSyncUpdate back = codec.Decode(buffer.AsSpan(0, written));
            Assert.Equal(entry.StringValue, back.Entries[0].StringValue);
        }

        [Fact]
        public void EmptyEntries_RoundTrips()
        {
            var codec = new VariableSyncV1Codec();
            var sync = new VariableSyncUpdate { ObjectId = 5, Entries = new List<VariableEntry>() };

            byte[] buffer = new byte[16];
            int written = codec.Encode(sync, buffer);
            VariableSyncUpdate back = codec.Decode(buffer.AsSpan(0, written));

            Assert.Equal(5u, back.ObjectId);
            Assert.Empty(back.Entries);
        }

        [Fact]
        public void ObjectId_UsesFullUintRange()
        {
            var codec = new VariableSyncV1Codec();
            var sync = new VariableSyncUpdate { ObjectId = 0xFFFFFFF0u, Entries = new List<VariableEntry>() };

            byte[] buffer = new byte[16];
            int written = codec.Encode(sync, buffer);
            VariableSyncUpdate back = codec.Decode(buffer.AsSpan(0, written));

            Assert.Equal(0xFFFFFFF0u, back.ObjectId);
        }

        [Fact]
        public void MaxEntriesPerMessage_255_RoundTrips()
        {
            // VariableSyncV1Codec's entry count is a single byte - confirm the boundary (255) actually
            // works, since Network.SendJfp2VariableSync's chunking (200/message) depends on staying
            // under this.
            var codec = new VariableSyncV1Codec();
            var entries = new List<VariableEntry>();
            for (int n = 0; n < 255; n++)
            {
                entries.Add(new VariableEntry { Vuid = (uint)n, Kind = VariableKind.Int32, IntValue = n });
            }
            var sync = new VariableSyncUpdate { ObjectId = 1, Entries = entries };

            byte[] buffer = new byte[VariableSyncV1Codec.HeaderSize + 255 * VariableSyncV1Codec.MaxBytesPerEntry];
            int written = codec.Encode(sync, buffer);
            VariableSyncUpdate back = codec.Decode(buffer.AsSpan(0, written));

            Assert.Equal(255, back.Entries.Count);
            Assert.Equal(254, back.Entries[254].IntValue);
        }

        [Fact]
        public void CodecRegistry_ResolvesVariableSyncCodec()
        {
            CodecRegistry.Register(new VariableSyncV1Codec());
            ICodec<VariableSyncUpdate> codec = CodecRegistry.Resolve<VariableSyncUpdate>(MessageClasses.VariableSync, 1);
            Assert.Equal(MessageClasses.VariableSync, codec.MessageClass);
        }
    }
}
