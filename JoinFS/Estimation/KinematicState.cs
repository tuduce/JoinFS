namespace JoinFS.Estimation
{
    /// <summary>
    /// Where an object is and how it moves: a received sample, or a prediction made from one.
    /// The position and velocity are shared with the object they came from, so treat them as
    /// read-only (clone before changing).
    /// </summary>
    public readonly struct KinematicState(Sim.Pos position, Sim.Vel velocity)
    {
        public Sim.Pos Position { get; } = position;
        public Sim.Vel Velocity { get; } = velocity;
    }

    /// <summary>
    /// What the network knows about the link to an object's owner, read when a prediction is made.
    /// Objects that do not come over the network (recorder playback) have no link.
    /// </summary>
    public readonly struct PeerTiming(bool hasLink, float rtt)
    {
        /// <summary>The object's owner is a peer</summary>
        public bool HasLink { get; } = hasLink;
        /// <summary>Round-trip time to the owner, in seconds - end-to-end, through a relay if there is one</summary>
        public float Rtt { get; } = rtt;

        public static PeerTiming None => new(false, 0.0f);
    }
}
