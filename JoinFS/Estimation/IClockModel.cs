namespace JoinFS.Estimation
{
    /// <summary>
    /// Follows the sender's clock for one remote object, so that the age of its newest sample is
    /// known at any local time (docs/position-estimation-plan.md §4). Used on the sim thread only.
    /// </summary>
    public interface IClockModel
    {
        /// <summary>A sample has arrived since the last reset</summary>
        bool Started { get; }

        /// <summary>
        /// A new sample arrived, stamped <paramref name="netTime"/> on the sender's clock
        /// </summary>
        /// <param name="netTime">The sample's time on the sender's clock</param>
        /// <param name="receivedAt">Local time it arrived; 0 when it did not come off the network, meaning now</param>
        /// <param name="now">Local time now</param>
        void OnSample(double netTime, double receivedAt, double now);

        /// <summary>
        /// How long ago, in seconds, the newest sample was taken, at local time <paramref name="now"/>.
        /// Called once per frame (it may filter the link timing it is given).
        /// </summary>
        double SampleAge(double now, in PeerTiming peer);

        /// <summary>Forget the sender's clock: the next sample starts over</summary>
        void Reset();

        /// <summary>Take over another object's sender-clock tracking (entering its cockpit)</summary>
        void CopyFrom(IClockModel other);
    }
}
