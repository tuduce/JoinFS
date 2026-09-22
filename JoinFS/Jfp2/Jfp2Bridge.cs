namespace JoinFS.Jfp2
{
    /// <summary>
    /// Reserved for the hub-role decode/re-encode translation described in
    /// docs/protocol-v2-design.md §7.7 - not instantiated or called by anything yet.
    ///
    /// The fast path (both legs JFP2, same agreed schema version for a message class - the common
    /// case) turned out to need no decode/re-encode at all and is handled entirely by
    /// LocalNode.RelayForwardedJfp2Datagram as a byte-for-byte forward, the same way the legacy
    /// stack's own FLAG_FORWARD relay works - see EnvelopeFlags.Forwarded's doc comment in
    /// Envelope.cs. This class exists for the harder cases that genuinely need it, left for a later
    /// increment: Tier 2 (both legs JFP2 but different agreed schema versions for a class) and Tier 3
    /// (one leg JFP2, one legacy-only - the original §7.7 scope), each needing an actual decode into
    /// the version-agnostic struct (PositionUpdate, IdentityUpdate, etc.) on one side and a re-encode
    /// on the other.
    ///
    /// Per-object identity/variable caches for that future work belong here, not in
    /// Sim.objectList/VariableMgr.Set - those carry real simulator-object side effects (SimConnect AI
    /// injection when a live sim is present) that a hub's pure store-and-forward relay shouldn't
    /// trigger.
    /// </summary>
    public sealed class Jfp2Bridge
    {
    }
}
