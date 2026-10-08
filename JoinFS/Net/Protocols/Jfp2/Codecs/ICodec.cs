// Ported from ProtocolV2Reference/Codecs.cs (docs/jfp2/protocol.md §9) as part of
// docs/jfp2/history/protocol-v2-implementation-plan.md Phase 2.

namespace JoinFS.Net.Jfp2.Codecs
{
    /// <summary>
    /// A single (MessageClass, SchemaVersion) encoder/decoder pair. Every codec is independent and
    /// stateless: encoding never depends on what version a *different* message class negotiated, so
    /// classes can evolve on entirely separate timelines (see docs/jfp2/protocol.md §5). This is
    /// the structural fix for the legacy protocol's single global DataVersion, which forced every
    /// message on the wire to be re-validated whenever ANY one message's shape changed
    /// (73b203d^:docs/protocol-changes-v26.4-v26.5.md §1.1). A class's codecs belong to its
    /// <see cref="ClassDescriptor{T}"/>, which picks one by the version agreed with each peer.
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
}
