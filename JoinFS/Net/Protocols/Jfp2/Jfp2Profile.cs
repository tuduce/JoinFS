using System;
using System.Collections.Generic;
using JoinFS.Net.Jfp2.Codecs;

namespace JoinFS.Net.Jfp2
{
    /// <summary>
    /// The message classes a <see cref="Jfp2Plugin"/> speaks, with their schema versions and codecs:
    /// one <see cref="ClassDescriptor"/> each, in the order they are offered in Hello; and the
    /// capabilities it advertises. Immutable, and per plugin instance, so a test can run a node of an
    /// older or a newer build next to this one (<see cref="With"/>, <see cref="Without"/>,
    /// <see cref="WithCapabilities"/>); production uses <see cref="Default"/>.
    /// </summary>
    public sealed class Jfp2Profile
    {
        readonly ClassDescriptor[] classes;
        /// <summary>The internal classes offered besides the application ones (<see cref="MembershipClasses"/>); their bodies are read by the plugin itself.</summary>
        readonly SchemaOffer[] internalOffers;
        /// <summary>Keyed by application class number only, since every class is an application one; it
        /// must take the partition too once internal classes become descriptors (the mesh over JFP2).</summary>
        readonly ClassDescriptor[] byClass = new ClassDescriptor[256];
        readonly ClassDescriptor[] byKind = new ClassDescriptor[(int)MessageKind.Count];

        /// <summary>The classes, each with its own class number and kind; they are offered in this order. No capabilities.</summary>
        public Jfp2Profile(params ClassDescriptor[] classes) : this(0, classes, [])
        {
        }

        Jfp2Profile(ulong capabilities, ClassDescriptor[] classes, SchemaOffer[] internalOffers)
        {
            Capabilities = capabilities;
            this.classes = (ClassDescriptor[])classes.Clone();
            this.internalOffers = (SchemaOffer[])internalOffers.Clone();
            var offers = new SchemaOffer[classes.Length + internalOffers.Length];
            for (int i = 0; i < classes.Length; i++)
            {
                ClassDescriptor c = classes[i];
                if (byClass[c.MessageClass] != null || byKind[(int)c.Kind] != null)
                {
                    throw new ArgumentException("JFP2 profile: class " + c.MessageClass + " (" + c.Kind + ") repeats a class number or a kind");
                }
                byClass[c.MessageClass] = c;
                byKind[(int)c.Kind] = c;
                offers[i] = c.Offer;
            }
            internalOffers.CopyTo(offers, classes.Length);
            Classes = Array.AsReadOnly(this.classes);
            Offers = Array.AsReadOnly(offers);
        }

        /// <summary>The internal classes that carry membership: version 1 each, and 2 for MemberList and MemberAnnounce (§5.14) and JoinResult (§5.15); protocol.md §5.10, §5.11, §5.13.</summary>
        public static IReadOnlyList<SchemaOffer> MembershipClasses { get; } = Array.AsReadOnly(
        [
            new SchemaOffer(true, MessageClasses.Join, 1, 1),
            new SchemaOffer(true, MessageClasses.JoinResult, 1, 2),
            new SchemaOffer(true, MessageClasses.MemberList, 1, 2),
            new SchemaOffer(true, MessageClasses.MemberAnnounce, 1, 2),
            new SchemaOffer(true, MessageClasses.MemberLeave, 1, 1),
            new SchemaOffer(true, MessageClasses.RouteQuery, 1, 1),
            new SchemaOffer(true, MessageClasses.RouteAnswer, 1, 1),
        ]);

        /// <summary>
        /// What this build speaks, in the order of its offers in Hello. Adding a class or a schema
        /// version is a change here alone, plus the codec (docs/network/jfp2/implementation.md §5).
        /// </summary>
        public static Jfp2Profile Default { get; } = new Jfp2Profile(
            ClassDescriptor.Plain(MessageClasses.Status, guaranteed: false, new StatusV1Codec()),
            ClassDescriptor.Plain(MessageClasses.StatusRequest, guaranteed: false, new StatusRequestV1Codec()),
            ClassDescriptor.SentByPlugin(MessageClasses.Identity, guaranteed: false, new IdentityV1Codec()),
            ClassDescriptor.SentByPlugin(MessageClasses.VariableSync, guaranteed: false, new VariableSyncV1Codec()),
            ClassDescriptor.SentByPlugin(MessageClasses.Position, guaranteed: false, new PositionV1Codec()),
            ClassDescriptor.Plain(MessageClasses.Event, guaranteed: true, new EventV1Codec()),
            ClassDescriptor.Plain(MessageClasses.FlightPlan, guaranteed: false, DeliverFlightPlan, new FlightPlanV1Codec()),
            ClassDescriptor.SentByPlugin(MessageKind.Notes, MessageClasses.Notes, guaranteed: true, DeliverNote, new NotesV1Codec()),
            ClassDescriptor.Plain(MessageClasses.Weather, guaranteed: false, new WeatherUpdateV1Codec()),
            ClassDescriptor.Plain(MessageClasses.WeatherReply, guaranteed: true, new WeatherReplyV1Codec()),
            ClassDescriptor.Plain(MessageClasses.HubList, guaranteed: false, new HubListV1Codec()),
            ClassDescriptor.Plain(MessageClasses.UserListRequest, guaranteed: false, new UserListRequestV1Codec()),
            ClassDescriptor.Plain(MessageClasses.HubUser, guaranteed: false, new HubUserV1Codec()),
            ClassDescriptor.Plain(MessageClasses.UserPositionsRequest, guaranteed: false, new UserPositionsRequestV1Codec()),
            ClassDescriptor.Plain(MessageClasses.UserPositions, guaranteed: false, new UserPositionsV1Codec()),
            ClassDescriptor.Plain(MessageClasses.Online, guaranteed: false, new OnlineV1Codec()),
            ClassDescriptor.Plain(MessageClasses.UserLookup, guaranteed: false, new UserLookupV1Codec()),
            ClassDescriptor.Plain(MessageClasses.UserLookupAnswer, guaranteed: false, new UserLookupAnswerV1Codec()))
            .WithInternalOffers(MembershipClasses);

        /// <summary>The wire carries no owner: the aircraft is the sender's (or the relayed origin's).</summary>
        static void DeliverFlightPlan(IProtocolHost host, in MessageMeta meta, in FlightPlanUpdate plan)
        {
            FlightPlanUpdate owned = plan;
            owned.Owner = meta.Sender;
            host.Deliver(meta, owned);
        }

        static void DeliverNote(IProtocolHost host, in MessageMeta meta, in NoteUpdate note) =>
            host.Deliver(meta, new NotesBundle
            {
                Scope = CommsScope.Single,
                Users =
                [
                    new NotesUser
                    {
                        Guid = note.Guid, Nickname = note.Nickname, Callsign = note.Callsign,
                        Notes = [new CommsNote { NoteId = note.NoteId, Age = note.Age, Channel = note.Channel, Text = note.Text }],
                    },
                ],
            });

        public IReadOnlyList<ClassDescriptor> Classes { get; }

        /// <summary>The offers of Hello/HelloAck, derived from the classes.</summary>
        public IReadOnlyList<SchemaOffer> Offers { get; }

        /// <summary>
        /// The capability bits advertised in Hello/HelloAck (docs/network/jfp2/protocol.md §5.6). None
        /// is assigned yet, so this build advertises none; a capability comes with the design that
        /// needs it, and tests set bits to run builds that differ in them.
        /// </summary>
        public ulong Capabilities { get; }

        /// <summary>The application class with this number, or null (see <see cref="byClass"/>).</summary>
        public ClassDescriptor ForClass(byte messageClass) => byClass[messageClass];

        /// <summary>The class that carries this kind, or null when JFP2 does not.</summary>
        public ClassDescriptor ForKind(MessageKind kind) => byKind[(int)kind];

        /// <summary>The class that carries this kind with codecs of <typeparamref name="T"/>, or null.</summary>
        public ClassDescriptor<T> ForKind<T>(MessageKind kind) => byKind[(int)kind] as ClassDescriptor<T>;

        /// <summary>This profile with <paramref name="replacement"/> in place of the class of the same number (in its place), or added last.</summary>
        public Jfp2Profile With(ClassDescriptor replacement)
        {
            var list = new List<ClassDescriptor>(classes);
            int index = list.FindIndex(c => c.MessageClass == replacement.MessageClass);
            if (index >= 0) list[index] = replacement; else list.Add(replacement);
            return new Jfp2Profile(Capabilities, list.ToArray(), internalOffers);
        }

        /// <summary>This profile also offering internal classes (a build that carries the membership over JFP2).</summary>
        public Jfp2Profile WithInternalOffers(IEnumerable<SchemaOffer> offers) => new(Capabilities, classes, [.. internalOffers, .. offers]);

        /// <summary>This profile without the internal class, as a build that does not know it.</summary>
        public Jfp2Profile WithoutInternal(byte messageClass) => new(Capabilities, classes, Array.FindAll(internalOffers, o => o.MessageClass != messageClass));

        /// <summary>This profile without a class, as a build that does not know it.</summary>
        public Jfp2Profile Without(byte messageClass) => new(Capabilities, Array.FindAll(classes, c => c.MessageClass != messageClass), internalOffers);

        /// <summary>This profile advertising <paramref name="capabilities"/> instead, as a build that has them.</summary>
        public Jfp2Profile WithCapabilities(ulong capabilities) => new(capabilities, classes, internalOffers);
    }
}
