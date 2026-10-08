using System;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    // Round-trip coverage for the Notes codec (docs/jfp2/history/protocol-v2-implementation-plan.md Phase 5).
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
        public void DefaultProfile_CarriesNotesWithThisCodec()
        {
            ClassDescriptor<NoteUpdate> messageClass = Jfp2Profile.Default.ForKind<NoteUpdate>(MessageKind.Notes);
            Assert.Equal(MessageClasses.Notes, messageClass.MessageClass);
            Assert.IsType<NotesV1Codec>(messageClass.Codec(1));
        }

        /// <summary>
        /// Every string is cut to its limit at a character boundary (docs/jfp2/protocol.md §9.7), so
        /// with every field at its limit the message is the largest NoteUpdate payload, 864 bytes.
        /// </summary>
        [Fact]
        public void LongStrings_AreCutToTheirLimits()
        {
            var codec = new NotesV1Codec();
            static NoteUpdate With(Func<int, string> text) => new()
            {
                NoteId = 99,
                Channel = 3,
                Nickname = text(NotesV1Codec.NicknameLimit),
                Callsign = text(NotesV1Codec.CallsignLimit),
                Text = text(NotesV1Codec.TextLimit),
            };
            byte[] buffer = new byte[4096];

            Assert.Equal(864, NotesV1Codec.MaxSize);
            Assert.Equal(NotesV1Codec.MaxSize, codec.Encode(With(LongText.Ascii), buffer));

            NoteUpdate sent = With(LongText.Over);
            int written = codec.Encode(sent, buffer);
            NoteUpdate back = codec.Decode(buffer.AsSpan(0, written));

            LongText.AssertCut(sent.Nickname, NotesV1Codec.NicknameLimit, back.Nickname);
            LongText.AssertCut(sent.Callsign, NotesV1Codec.CallsignLimit, back.Callsign);
            LongText.AssertCut(sent.Text, NotesV1Codec.TextLimit, back.Text);
        }
    }
}
