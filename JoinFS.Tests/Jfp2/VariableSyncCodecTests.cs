using System;
using System.Collections.Generic;
using JoinFS.Jfp2;
using JoinFS.Jfp2.Codecs;
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

        [Fact]
        public void String8Value_LongerThan8Chars_IsTruncated()
        {
            var codec = new VariableSyncV1Codec();
            var sync = new VariableSyncUpdate
            {
                ObjectId = 1,
                Entries = new List<VariableEntry> { new VariableEntry { Vuid = 1, Kind = VariableKind.String8, StringValue = "TOOLONGVALUE" } }
            };

            byte[] buffer = new byte[256];
            int written = codec.Encode(sync, buffer);
            VariableSyncUpdate back = codec.Decode(buffer.AsSpan(0, written));

            Assert.Equal("TOOLONGVALUE"[..8], back.Entries[0].StringValue);
        }

        [Fact]
        public void String8Value_ShorterThan8Chars_TrimsPadding()
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

            Assert.Equal("AB", back.Entries[0].StringValue);
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
