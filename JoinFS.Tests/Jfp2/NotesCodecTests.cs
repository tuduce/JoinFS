using System;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    // Round-trip coverage for the Notes codec (docs/protocol-v2-implementation-plan.md Phase 5).
    // Scoped to the single live-note-push shape only - see NotesCodec.cs's header comment for why
    // the bulk catch-up dump exchange isn't ported.
    public class NotesCodecTests
    {
        static NoteUpdate Sample() => new()
        {
            Guid = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Nickname = "Pilot1",
            Callsign = "JFS123",
            NoteId = 99,
            Age = 12.5f,
            Channel = 3,
            Text = "Hello world",
        };

        [Fact]
        public void RoundTrips_AllFields()
        {
            var codec = new NotesV1Codec();
            NoteUpdate update = Sample();

            Span<byte> buffer = stackalloc byte[1024];
            int written = codec.Encode(update, buffer);
            NoteUpdate back = codec.Decode(buffer[..written]);

            Assert.Equal(update.Guid, back.Guid);
            Assert.Equal(update.Nickname, back.Nickname);
            Assert.Equal(update.Callsign, back.Callsign);
            Assert.Equal(update.NoteId, back.NoteId);
            Assert.Equal(update.Age, back.Age);
            Assert.Equal(update.Channel, back.Channel);
            Assert.Equal(update.Text, back.Text);
        }

        [Fact]
        public void EmptyGuid_RoundTrips()
        {
            var codec = new NotesV1Codec();
            NoteUpdate update = Sample();
            update.Guid = Guid.Empty;

            Span<byte> buffer = stackalloc byte[1024];
            int written = codec.Encode(update, buffer);
            NoteUpdate back = codec.Decode(buffer[..written]);

            Assert.Equal(Guid.Empty, back.Guid);
        }

        [Fact]
        public void EmptyText_RoundTrips()
        {
            var codec = new NotesV1Codec();
            NoteUpdate update = Sample();
            update.Text = "";

            Span<byte> buffer = stackalloc byte[1024];
            int written = codec.Encode(update, buffer);
            NoteUpdate back = codec.Decode(buffer[..written]);

            Assert.Equal("", back.Text);
        }

        [Fact]
        public void CodecRegistry_ResolvesNotesCodec()
        {
            CodecRegistry.Register(new NotesV1Codec());
            ICodec<NoteUpdate> codec = CodecRegistry.Resolve<NoteUpdate>(MessageClasses.Notes, 1);
            Assert.Equal(MessageClasses.Notes, codec.MessageClass);
        }
    }
}
