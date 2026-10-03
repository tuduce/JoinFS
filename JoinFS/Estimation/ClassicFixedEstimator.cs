using System;

namespace JoinFS.Estimation
{
    /// <summary>
    /// <see cref="ClassicEstimator"/> with its two kinematic errors fixed
    /// (docs/position-estimation-plan.md §2): the attitude follows the body rates through
    /// <see cref="Attitude.Integrate"/> instead of adding them to the angles (F4), and the
    /// acceleration moves the position by ½·a·t², not a·t² (F3). Everything else is Classic's.
    /// </summary>
    public sealed class ClassicFixedEstimator : IStateEstimator
    {
        public const string Name = "ClassicFixed";

        public void OnSample(in KinematicState sample, double netTime)
        {
        }

        public KinematicState Predict(in KinematicState sample, double age)
        {
            // limit extrapolation to two seconds
            age = Math.Min(ClassicEstimator.MaxAge, Math.Max(-ClassicEstimator.MaxAge, age));
            Sim.Pos p = sample.Position;
            Sim.Vel v = sample.Velocity;
            Vector moved = v.linear * age + v.acc * (0.5 * age * age);
            Sim.Pos position = new(p.geo + moved * p.GeoPerMetre(), Attitude.Integrate(p.angles, v.angular, age), p.elevation, p.ground, p.radarHeight);
            return new KinematicState(position, v.Extrapolate(age));
        }
    }
}
