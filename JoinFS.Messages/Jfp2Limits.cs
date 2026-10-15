namespace JoinFS.Net.Jfp2
{
    /// <summary>
    /// The one size limit the message codecs share with the JFP2 envelope, kept here so the codecs need
    /// not reference the envelope (docs/jfp2/protocol.md section 4.5).
    /// </summary>
    public static class Jfp2Limits
    {
        /// <summary>The largest payload of any v1 message, so that it fits the 1,200-byte datagram ceiling with every header (28 bytes) and room for 24 bytes of later per-hop security.</summary>
        public const int MaxPayloadSize = 1100;
    }
}
