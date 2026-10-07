using System;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    /// <summary>A class descriptor states its facts once, and a profile refuses the mistakes that would make two of them disagree.</summary>
    public class ProfileTests
    {
        sealed class EventCodec(byte messageClass, byte version) : ICodec<EventUpdate>
        {
            public byte MessageClass => messageClass;
            public byte SchemaVersion => version;
            public int Encode(in EventUpdate value, Span<byte> dest) => 0;
            public EventUpdate Decode(ReadOnlySpan<byte> src) => default;
        }

        [Fact]
        public void Descriptor_TakesItsVersionRangeFromItsCodecs()
        {
            ClassDescriptor<EventUpdate> c = ClassDescriptor.Plain(MessageClasses.Event, guaranteed: true,
                new EventCodec(MessageClasses.Event, 3), new EventCodec(MessageClasses.Event, 2));
            Assert.Equal(MessageKind.Event, c.Kind);
            Assert.True(c.SentAsIs);
            Assert.Equal((2, 3), (c.MinVersion, c.MaxVersion));
            Assert.Equal(3, c.Codec(3).SchemaVersion);
            Assert.Equal((false, MessageClasses.Event, (byte)2, (byte)3), (c.Offer.Internal, c.Offer.MessageClass, c.Offer.MinVersion, c.Offer.MaxVersion));
        }

        [Fact]
        public void Descriptor_RefusesCodecsOfAnotherClassOrWithGaps()
        {
            Assert.Throws<ArgumentException>(() => ClassDescriptor.Plain(MessageClasses.Event, true, new EventCodec(MessageClasses.Status, 1)));
            Assert.Throws<ArgumentException>(() => ClassDescriptor.Plain(MessageClasses.Event, true, new EventCodec(MessageClasses.Event, 1), new EventCodec(MessageClasses.Event, 3)));
            Assert.Throws<ArgumentException>(() => ClassDescriptor.Plain(MessageClasses.Event, true, new EventCodec(MessageClasses.Event, 1), new EventCodec(MessageClasses.Event, 1)));
            Assert.Throws<ArgumentException>(() => ClassDescriptor.Plain<EventUpdate>(MessageClasses.Event, true));
        }

        [Fact]
        public void Profile_RefusesTwoClassesWithOneNumberOrOneKind()
        {
            ClassDescriptor<EventUpdate> events = ClassDescriptor.Plain(MessageClasses.Event, true, new EventCodec(MessageClasses.Event, 1));
            Assert.Throws<ArgumentException>(() => Jfp2Profile.Default.Without(MessageClasses.Status).With(
                ClassDescriptor.Plain(MessageClasses.Status, true, new EventCodec(MessageClasses.Status, 1)))); // Event's kind again
            Assert.Throws<ArgumentException>(() => new Jfp2Profile(events, events));
        }

        [Fact]
        public void Plugin_RefusesAClassToSendItselfForAKindItHasNoSenderFor()
        {
            // advertised by CanCarry yet never sent, nor sent over legacy: refused rather than dropped
            Assert.Throws<ArgumentException>(() => new Jfp2Plugin(profile: Jfp2Profile.Default.With(
                ClassDescriptor.SentByPlugin(MessageClasses.Event, true, new EventV1Codec()))));
            // Notes, but not in the wire type the plugin's sender writes
            Assert.Throws<ArgumentException>(() => new Jfp2Plugin(profile: Jfp2Profile.Default.With(
                ClassDescriptor.SentByPlugin(MessageKind.Notes, MessageClasses.Notes, true,
                    (IProtocolHost host, in MessageMeta meta, in EventUpdate message) => { }, new EventCodec(MessageClasses.Notes, 1)))));
            // the other way round: Position sent as is would skip identity-before-position
            Assert.Throws<ArgumentException>(() => new Jfp2Plugin(profile: Jfp2Profile.Default.With(
                ClassDescriptor.Plain(MessageClasses.Position, false, new PositionV1Codec()))));
            _ = new Jfp2Plugin(profile: Jfp2Profile.Default);
        }

        [Fact]
        public void Profile_ListsCannotBeChangedThroughTheirArrays()
        {
            Assert.False(Jfp2Profile.Default.Offers is SchemaOffer[]);
            Assert.False(Jfp2Profile.Default.Classes is ClassDescriptor[]);
        }

        [Fact]
        public void DefaultProfile_LooksUpByClassAndByKind()
        {
            Assert.Equal(10, Jfp2Profile.Default.Classes.Count);
            foreach (ClassDescriptor c in Jfp2Profile.Default.Classes)
            {
                Assert.Same(c, Jfp2Profile.Default.ForClass(c.MessageClass));
                Assert.Same(c, Jfp2Profile.Default.ForKind(c.Kind));
                Assert.False(c.Internal);
            }
            Assert.Null(Jfp2Profile.Default.ForKind(MessageKind.RemoveObject));
            Assert.Null(Jfp2Profile.Default.ForKind<EventUpdate>(MessageKind.Position)); // another codec type
        }
    }
}
