using System.Collections.Concurrent;
using System.Collections.Generic;

// Ported from ProtocolV2Reference/Codecs.cs (docs/protocol-v2-design.md §6, §9.2) as part of
// docs/protocol-v2-implementation-plan.md Phase 2.

namespace JoinFS.Jfp2.Codecs
{
    /// <summary>
    /// A single (MessageClass, SchemaVersion) encoder/decoder pair. Every codec is independent and
    /// stateless: encoding never depends on what version a *different* message class negotiated, so
    /// classes can evolve on entirely separate timelines (see docs/protocol-v2-design.md §5). This is
    /// the structural fix for the legacy protocol's single global DataVersion, which forced every
    /// message on the wire to be re-validated whenever ANY one message's shape changed
    /// (docs/protocol-changes-v26.4-v26.5.md §1.1).
    /// </summary>
    public interface ICodec<T>
    {
        byte MessageClass { get; }
        byte SchemaVersion { get; }

        /// <summary>Encodes into <paramref name="dest"/> and returns the number of bytes written.
        /// Callers size <paramref name="dest"/> from the codec's known fixed Size (hot codecs) or a
        /// conservative upper bound (variable-length codecs) - never by growing a buffer mid-encode,
        /// which is what keeps this off the heap on the hot path.</summary>
        int Encode(in T value, System.Span<byte> dest);

        T Decode(System.ReadOnlySpan<byte> src);
    }

    /// <summary>
    /// Looks up the right codec instance for a (class, version) pair resolved once at handshake time
    /// (see Negotiation.PeerSession.AgreedAppVersion/AgreedInternalVersion). Registration happens once
    /// at startup (see Network.cs's constructor); lookup on the hot send/receive path is a single
    /// dictionary read keyed by a value tuple, no boxing beyond the stored codec reference itself.
    /// ConcurrentDictionary rather than plain Dictionary: this is process-wide static state, and more
    /// than one Network instance can exist in one process (e.g. a two-instance loopback test, or any
    /// future multi-session host) each registering the same classes from its own constructor - a plain
    /// Dictionary is not safe under concurrent writes and corrupted internal state here surfaces as a
    /// spurious "codec never registered" KeyNotFoundException on an unrelated class.
    /// </summary>
    public static class CodecRegistry
    {
        static readonly ConcurrentDictionary<(byte Class, byte Version), object> codecs = new();

        public static void Register<T>(ICodec<T> codec) =>
            codecs[(codec.MessageClass, codec.SchemaVersion)] = codec;

        public static ICodec<T> Resolve<T>(byte messageClass, byte schemaVersion)
        {
            if (codecs.TryGetValue((messageClass, schemaVersion), out object codec))
                return (ICodec<T>)codec;
            throw new KeyNotFoundException(
                $"No codec registered for class {messageClass} version {schemaVersion}. " +
                "This should never happen for a version that survived Negotiator.Resolve - " +
                "it means a codec was negotiated but never registered.");
        }
    }
}
