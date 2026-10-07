using System;
using JoinFS.Net.Jfp2.Codecs;

namespace JoinFS.Net.Jfp2
{
    /// <summary>Hands a decoded message of a JFP2 class to the core in canonical form.</summary>
    public delegate void Delivery<T>(IProtocolHost host, in MessageMeta meta, in T message);

    /// <summary>
    /// Everything about one JFP2 message class, stated once: the canonical kind it carries, its
    /// permanent number (<see cref="MessageClasses"/>), its partition, whether it is sent guaranteed,
    /// a codec per schema version it speaks, and how a decoded message reaches the core. A
    /// <see cref="Jfp2Profile"/> is a set of these, and the offers in Hello, routing, encoding and
    /// decoding all read them (docs/reference/jfp2-protocol.md §6.6).
    /// </summary>
    public abstract class ClassDescriptor
    {
        private protected ClassDescriptor(MessageKind kind, byte messageClass, bool guaranteed, bool sentAsIs, (byte Min, byte Max) versions)
        {
            Kind = kind;
            MessageClass = messageClass;
            Guaranteed = guaranteed;
            SentAsIs = sentAsIs;
            MinVersion = versions.Min;
            MaxVersion = versions.Max;
        }

        /// <summary>The canonical kind this class carries.</summary>
        public MessageKind Kind { get; }

        /// <summary>The class number on the wire, a <see cref="MessageClasses"/> constant.</summary>
        public byte MessageClass { get; }

        /// <summary>
        /// The class is in the internal partition (EnvelopeFlags.Internal) rather than the application
        /// one. None is yet: the plugin itself handles the internal classes it speaks (Hello, HelloAck,
        /// GuaranteedDone), and the mesh classes come with the mesh over JFP2.
        /// </summary>
        public bool Internal => false;

        /// <summary>Every message of the class is sent with guaranteed delivery.</summary>
        public bool Guaranteed { get; }

        /// <summary>
        /// Sent as it is: one message of the class per canonical message, to each target in the version
        /// agreed with its hop (a <c>Plain</c> class). Otherwise the plugin has its own sender for the
        /// kind (a <c>SentByPlugin</c> class).
        /// </summary>
        public bool SentAsIs { get; }

        /// <summary>The lowest schema version this class speaks; it has a codec for each up to <see cref="MaxVersion"/>.</summary>
        public byte MinVersion { get; }

        public byte MaxVersion { get; }

        /// <summary>What this class offers in Hello/HelloAck.</summary>
        public SchemaOffer Offer => new(Internal, MessageClass, MinVersion, MaxVersion);

        /// <summary>Decode a payload of the agreed <paramref name="version"/> and hand it to the core.</summary>
        internal abstract void Deliver(IProtocolHost host, in MessageMeta meta, byte version, ReadOnlySpan<byte> payload);

        /// <summary>A plain application class: its canonical message is what goes on the wire, sent and delivered as it is.</summary>
        public static ClassDescriptor<T> Plain<T>(byte messageClass, bool guaranteed, params ICodec<T>[] codecs) where T : struct, IMessage =>
            new(T.Kind, messageClass, guaranteed, sentAsIs: true, DeliverAsIs<T>(), codecs);

        /// <summary>A plain application class whose decoded message is completed on delivery (FlightPlan: the owner is the sender).</summary>
        public static ClassDescriptor<T> Plain<T>(byte messageClass, bool guaranteed, Delivery<T> deliver, params ICodec<T>[] codecs) where T : struct, IMessage =>
            new(T.Kind, messageClass, guaranteed, sentAsIs: true, deliver, codecs);

        /// <summary>
        /// An application class the plugin sends its own way, delivered as it is: Identity ahead of an
        /// object's positions, Position after it, VariableSync in datagram-sized chunks. Jfp2Plugin
        /// refuses a profile with such a class for a kind it has no sender for.
        /// </summary>
        public static ClassDescriptor<T> SentByPlugin<T>(byte messageClass, bool guaranteed, params ICodec<T>[] codecs) where T : struct, IMessage =>
            new(T.Kind, messageClass, guaranteed, sentAsIs: false, DeliverAsIs<T>(), codecs);

        /// <summary>
        /// An application class the plugin sends its own way whose wire message is not the canonical
        /// one (Notes: one note per message, delivered as a bundle).
        /// </summary>
        public static ClassDescriptor<T> SentByPlugin<T>(MessageKind kind, byte messageClass, bool guaranteed, Delivery<T> deliver, params ICodec<T>[] codecs) =>
            new(kind, messageClass, guaranteed, sentAsIs: false, deliver, codecs);

        /// <summary>Delivery of a decoded message that is already the canonical one.</summary>
        static Delivery<T> DeliverAsIs<T>() where T : struct, IMessage =>
            static (IProtocolHost host, in MessageMeta meta, in T message) => host.Deliver(meta, message);
    }

    /// <summary>A message class whose codecs read and write <typeparamref name="T"/>.</summary>
    public sealed class ClassDescriptor<T> : ClassDescriptor
    {
        /// <summary>Indexed by schema version: the hot path is an array read.</summary>
        readonly ICodec<T>[] codecs;
        readonly Delivery<T> deliver;

        /// <summary>Made by the factories of <see cref="ClassDescriptor"/>. <paramref name="codecs"/> are its schema versions, one each, without gaps.</summary>
        internal ClassDescriptor(MessageKind kind, byte messageClass, bool guaranteed, bool sentAsIs, Delivery<T> deliver, params ICodec<T>[] codecs)
            : base(kind, messageClass, guaranteed, sentAsIs, Versions(messageClass, codecs))
        {
            this.deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
            this.codecs = new ICodec<T>[MaxVersion + 1];
            foreach (ICodec<T> codec in codecs)
            {
                this.codecs[codec.SchemaVersion] = codec;
            }
        }

        /// <summary>The range the codecs cover; they must all be of this class, one per version, without gaps.</summary>
        static (byte Min, byte Max) Versions(byte messageClass, ICodec<T>[] codecs)
        {
            if (codecs == null || codecs.Length == 0)
            {
                throw new ArgumentException("JFP2 class " + messageClass + " needs at least one codec");
            }
            int min = byte.MaxValue, max = 0;
            var seen = new bool[256];
            foreach (ICodec<T> codec in codecs)
            {
                if (codec.MessageClass != messageClass || codec.SchemaVersion == 0 || seen[codec.SchemaVersion])
                {
                    throw new ArgumentException("JFP2 class " + messageClass + ": codec " + codec.GetType().Name + " (class " + codec.MessageClass
                        + ", version " + codec.SchemaVersion + ") is of another class, version 0 or a second codec for its version");
                }
                seen[codec.SchemaVersion] = true;
                min = Math.Min(min, codec.SchemaVersion);
                max = Math.Max(max, codec.SchemaVersion);
            }
            if (max - min + 1 != codecs.Length)
            {
                throw new ArgumentException("JFP2 class " + messageClass + ": versions " + min + ".." + max + " have a gap");
            }
            return ((byte)min, (byte)max);
        }

        /// <summary>The codec of an agreed version (always within <see cref="ClassDescriptor.MinVersion"/>..<see cref="ClassDescriptor.MaxVersion"/>).</summary>
        public ICodec<T> Codec(byte version) => codecs[version];

        /// <summary>The same class speaking other schema versions, as an older or a newer build does.</summary>
        public ClassDescriptor<T> WithCodecs(params ICodec<T>[] versions) => new(Kind, MessageClass, Guaranteed, SentAsIs, deliver, versions);

        internal override void Deliver(IProtocolHost host, in MessageMeta meta, byte version, ReadOnlySpan<byte> payload)
        {
            T message = codecs[version].Decode(payload);
            deliver(host, meta, message);
        }
    }
}
