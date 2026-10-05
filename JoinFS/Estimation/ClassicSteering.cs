using System;

namespace JoinFS.Estimation
{
    /// <summary>
    /// The original steering: velocity feed-forward plus a proportional catch-up on the position
    /// and attitude error, with a reset when the object is too far off.
    /// </summary>
    /// <param name="setAttitudeEveryFrame">Set the attitude directly every frame (FS2020/FS2024)</param>
    /// <param name="groundAltitudeLimit">Largest altitude error before a reset while on the ground,
    /// in metres. FS2020/FS2024 keep the aircraft glued to the ground, so they need it small.</param>
    /// <param name="catchUpRate">Catch-up rate, per second of error. The classic 1.5 leaves a steady
    /// offset where the simulator pushes the object off (the sag in a banked turn); a stiffer one
    /// leaves less (docs/position-estimation-plan.md §6.6)</param>
    public sealed class ClassicSteering(bool setAttitudeEveryFrame, double groundAltitudeLimit, double catchUpRate = ClassicSteering.CatchUpRate) : ISteeringLaw
    {
        /// <summary>Longest extrapolation of the measured position either way, in seconds</summary>
        public const double MaxMeasuredAge = 2.0;
        /// <summary>Largest error before a reset, in metres</summary>
        public const double ResetDistance = 50.0;
        /// <summary>The original catch-up rate, per second of error</summary>
        public const double CatchUpRate = 1.5;

        public SteeringCommand Hold(in KinematicState sample)
        {
            // reset to the sample, zero velocity
            return new SteeringCommand
            {
                ResetTo = sample.Position,
                Velocity = new Sim.ObjectVelocity(),
                Attitude = setAttitudeEveryFrame ? sample.Position.angles : null,
            };
        }

        public SteeringCommand Steer(in KinematicState target, in KinematicState sample, Sim.Pos measured, double measuredAge)
        {
            // limit extrapolation to two seconds
            measuredAge = Math.Min(MaxMeasuredAge, Math.Max(-MaxMeasuredAge, measuredAge));
            // where the object is now: its last reported position, moved on at the sample's velocity
            Sim.Pos simPosition = measured.Extrapolate(sample.Velocity, measuredAge);
            Sim.Pos netPosition = target.Position;
            // changed below, so not the target's own
            Sim.Vel netVelocity = target.Velocity.Clone();

            // get V between network and sim positions
            double distance = Vector.GeodesicDistance(simPosition.geo.x, simPosition.geo.z, netPosition.geo.x, netPosition.geo.z);
            double bearing = Vector.GeodesicBearing(simPosition.geo.x, simPosition.geo.z, netPosition.geo.x, netPosition.geo.z);

            // largest difference in altitude before reset
            double altitudeDeltaLimit = simPosition.ground != 0 ? groundAltitudeLimit : ResetDistance;

            // check if object is beyond specific distance
            if (distance > ResetDistance || Math.Abs(simPosition.geo.y - netPosition.geo.y) > altitudeDeltaLimit)
            {
                // reset to target position (the velocity is passed in the world frame, as it always was)
                return new SteeringCommand
                {
                    ResetTo = netPosition,
                    Velocity = new Sim.ObjectVelocity(netVelocity.linear, netVelocity.angular, netVelocity.acc),
                    Attitude = setAttitudeEveryFrame ? netPosition.angles : null,
                };
            }

            // get world space relative position
            Vector deltaGeo = new(distance * Math.Sin(bearing), netPosition.geo.y - simPosition.geo.y, distance * Math.Cos(bearing));
            // get delta between current and network orientations
            Vector deltaAngles = Vector.AnglesDelta(simPosition.angles, netPosition.angles);

            // add delta to velocity to catch up
            netVelocity.linear += deltaGeo * catchUpRate;

            Vector attitude = null;
            // only catch up the orientation if no high angular turns are being made
            if (Math.Abs(simPosition.angles.x) < Math.PI * 0.25 && Math.Abs(simPosition.angles.z) < Math.PI * 0.5)
            {
                if (Math.Abs(netVelocity.angular.x) < 0.2 && Math.Abs(netVelocity.angular.y) < 0.2 && Math.Abs(netVelocity.angular.z) < 0.2)
                {
                    // add delta to angular velocity to catch up
                    netVelocity.angular += deltaAngles * catchUpRate;
                }
                if (setAttitudeEveryFrame)
                {
                    attitude = netPosition.angles;
                }
            }
            else
            {
                // set orientation
                attitude = netPosition.angles;
            }

            // velocity in the object's body frame
            return new SteeringCommand
            {
                Velocity = new Sim.ObjectVelocity(netVelocity.linear.InvRotate(simPosition.angles), netVelocity.angular * 0.3, netVelocity.acc.InvRotate(simPosition.angles)),
                Attitude = attitude,
            };
        }
    }
}
