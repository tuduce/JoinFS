namespace JoinFS.Estimation
{
    /// <summary>
    /// Decides how to move an injected object in the simulator toward its predicted state
    /// (docs/position-estimation-plan.md §4). It only computes a <see cref="SteeringCommand"/>;
    /// the simulator side applies it, so the law can be tested without a simulator.
    /// </summary>
    public interface ISteeringLaw
    {
        /// <summary>The sender is paused: hold the object at its sample</summary>
        SteeringCommand Hold(in KinematicState sample);

        /// <summary>
        /// Steer the object toward <paramref name="target"/>
        /// </summary>
        /// <param name="target">Predicted state for now</param>
        /// <param name="sample">The newest sample the prediction was made from</param>
        /// <param name="measured">The object's position as last reported by the simulator</param>
        /// <param name="measuredAge">How long ago, in seconds, <paramref name="measured"/> was reported</param>
        SteeringCommand Steer(in KinematicState target, in KinematicState sample, Sim.Pos measured, double measuredAge);
    }

    /// <summary>
    /// What to set on an injected object this frame. Applied in this order: with
    /// <see cref="ResetTo"/>, position, velocity, attitude; without it, attitude, velocity.
    /// </summary>
    public readonly struct SteeringCommand
    {
        /// <summary>Place the object here (a reset); null when only steering</summary>
        public Sim.Pos ResetTo { get; init; }
        /// <summary>Velocity to set</summary>
        public Sim.ObjectVelocity Velocity { get; init; }
        /// <summary>Attitude to set (pitch, heading, bank); null to leave it to the velocity</summary>
        public Vector Attitude { get; init; }
    }
}
