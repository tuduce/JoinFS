using System;

namespace JoinFS.Estimation
{
    /// <summary>
    /// The original estimator: dead reckoning from the newest sample with its velocity and
    /// acceleration (<see cref="Sim.Pos.Extrapolate"/>, <see cref="Sim.Vel.Extrapolate"/>).
    /// Kept unchanged as the baseline to compare others against, including its known errors
    /// (findings F3/F4 in docs/position-estimation-plan.md).
    /// </summary>
    public sealed class ClassicEstimator : IStateEstimator
    {
        /// <summary>Longest extrapolation either way, in seconds</summary>
        public const double MaxAge = 2.0;

        public void OnSample(in KinematicState sample, double netTime)
        {
        }

        public KinematicState Predict(in KinematicState sample, double age)
        {
            // limit extrapolation to two seconds
            age = Math.Min(MaxAge, Math.Max(-MaxAge, age));
            return new KinematicState(sample.Position.Extrapolate(sample.Velocity, age), sample.Velocity.Extrapolate(age));
        }
    }
}
