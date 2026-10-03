namespace JoinFS.Estimation
{
    /// <summary>
    /// Predicts a remote object's state from its newest sample (docs/position-estimation-plan.md
    /// §4). One instance per object, created through <see cref="EstimationRegistry"/>. Used on the
    /// sim thread only.
    /// </summary>
    public interface IStateEstimator
    {
        /// <summary>A new sample was accepted - for estimators that keep a history</summary>
        /// <param name="sample">The sample (read-only)</param>
        /// <param name="netTime">Its time on the sender's clock</param>
        void OnSample(in KinematicState sample, double netTime);

        /// <summary>The object's state <paramref name="age"/> seconds after <paramref name="sample"/></summary>
        KinematicState Predict(in KinematicState sample, double age);
    }
}
