namespace JoinFS.Net.Jfp2
{
    /// <summary>The capability bits this build assigns (docs/network/jfp2/protocol.md §5.6, §10.2); a bit is never reused.</summary>
    public static class Jfp2Capabilities
    {
        /// <summary>
        /// A guaranteed message may be sent as several segments (guaranteed count above 1, §7.5) to a
        /// neighbour that agreed this bit. Only a class marked segmented (<see cref="ClassDescriptor.Segmented"/>) is.
        /// </summary>
        public const ulong Segments = 1UL << 0;
    }
}
