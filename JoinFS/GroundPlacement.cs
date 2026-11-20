using System;

namespace JoinFS
{
    /// <summary>
    /// Height of a remote aircraft near the ground. On ordinary ground the height comes only from local
    /// values: the local ground under the shown model, the shown model's own height of its reference
    /// point above its wheels (CG height) and the manual Adjust Height value. The sender's altitude is
    /// not used there, so one adjustment per local model fits every sender, whichever sim it flies and
    /// whichever model it was substituted from. In the air the sender's altitude is used. A time-based
    /// ramp moves between the two: quickly after touchdown, slowly after lift-off, so neither shows a
    /// step. In every phase the model never goes below the local ground plus its CG height.
    /// </summary>
    public static class GroundPlacement
    {
        /// <summary>Seconds the ramp takes from airborne to ground height after touchdown</summary>
        public const double LandingRampSeconds = 2.0;

        /// <summary>Seconds the ramp takes from ground to airborne height after lift-off</summary>
        public const double ClimbOutRampSeconds = 10.0;

        /// <summary>A CG height at or below this (metres) is a reading that has not arrived yet, not a real value</summary>
        public const double MinimumCgHeight = 0.05;

        /// <summary>CG height used when nothing better is known (metres)</summary>
        public const double DefaultCgHeight = 2.5;

        /// <summary>How the height was decided, for the ground log</summary>
        public enum State
        {
            /// <summary>No local ground known: the sender's (mesh corrected) altitude as it is</summary>
            NoElevation,
            /// <summary>In the air: the sender's (mesh corrected) altitude</summary>
            Airborne,
            /// <summary>Between airborne and ground height</summary>
            Ramp,
            /// <summary>On the ground: local ground + local CG height + adjustment</summary>
            Ground,
            /// <summary>Lifted to the local ground + CG height</summary>
            Underflow,
            /// <summary>On an elevated platform: the sender's altitude is trusted as it is</summary>
            Platform,
        }

        /// <summary>Where the CG height came from, for the ground log</summary>
        public enum CgSource
        {
            /// <summary>Read from the shown model in the local sim</summary>
            Sim,
            /// <summary>Guessed from the aircraft class</summary>
            Guess,
        }

        /// <summary>The result of <see cref="Place"/></summary>
        public readonly record struct Result(double Altitude, double Weight, State State, CgSource CgSource, double CgHeight);

        /// <summary>
        /// Move the ground weight toward 1 (on the ground) or 0 (airborne), by time: the whole way takes
        /// <see cref="LandingRampSeconds"/> toward the ground and <see cref="ClimbOutRampSeconds"/> away
        /// from it, at any update rate. NaN (the first update) goes straight to the target.
        /// </summary>
        public static double Ramp(double weight, bool onGround, double dt)
        {
            double target = onGround ? 1.0 : 0.0;
            if (double.IsNaN(weight))
            {
                return target;
            }
            // a clock going back or a long gap: no step larger than the whole way
            dt = Math.Max(0.0, dt);
            return onGround
                ? Math.Min(target, weight + dt / LandingRampSeconds)
                : Math.Max(target, weight - dt / ClimbOutRampSeconds);
        }

        /// <summary>
        /// The CG height to use: the shown model's own when the sim reported one, else a guess by the ICAO
        /// aircraft description (e.g. "L2J": landplane, 2 engines, jet), as Swift guesses it
        /// </summary>
        public static double CgHeight(double localCgHeight, string classCode, out CgSource source)
        {
            if (double.IsNaN(localCgHeight) == false && localCgHeight > MinimumCgHeight)
            {
                source = CgSource.Sim;
                return localCgHeight;
            }
            source = CgSource.Guess;
            return GuessCgHeight(classCode);
        }

        /// <summary>
        /// A CG height by the ICAO aircraft description: 1.5 m single engine, 2.0 m twin or turboprop,
        /// 2.5 m twin jet, 4.0 m three or more engines, 6.0 m three or more jets; 2.5 m when unknown
        /// </summary>
        public static double GuessCgHeight(string classCode)
        {
            if (string.IsNullOrEmpty(classCode) || classCode.Length < 3)
            {
                return DefaultCgHeight;
            }
            char engines = classCode[1];
            char engineType = char.ToUpperInvariant(classCode[2]);
            bool jet = engineType == 'J';
            if (engines == '1')
            {
                return 1.5;
            }
            if (engines == '2')
            {
                return jet ? 2.5 : 2.0;
            }
            if (engines >= '3' && engines <= '8')
            {
                return jet ? 6.0 : 4.0;
            }
            return DefaultCgHeight;
        }

        /// <summary>
        /// The height to show a remote aircraft at
        /// </summary>
        /// <param name="airborneAltitude">The sender's altitude, already corrected for the mesh difference, metres</param>
        /// <param name="weight">The ground weight after <see cref="Ramp"/>: 1 on the ground, 0 airborne</param>
        /// <param name="localGround">The local ground under the shown model, metres; NaN when not known</param>
        /// <param name="cgHeight">The shown model's CG height above its wheels (<see cref="CgHeight"/>), metres</param>
        /// <param name="adjustment">The Adjust Height value for the shown model, metres; applied on the ground only, ramped with the weight</param>
        /// <param name="platform">The sender is on an elevated platform: its altitude is trusted as it is</param>
        public static Result Place(double airborneAltitude, double weight, double localGround, double cgHeight, CgSource cgSource,
            double adjustment, bool platform)
        {
            if (platform)
            {
                return new Result(airborneAltitude, weight, State.Platform, cgSource, cgHeight);
            }
            if (double.IsNaN(localGround))
            {
                // nothing local to place it on: the sender's altitude, with the adjustment on the ground
                return new Result(airborneAltitude + weight * adjustment, weight, State.NoElevation, cgSource, cgHeight);
            }
            double groundAltitude = localGround + cgHeight + adjustment;
            double altitude = weight * groundAltitude + (1.0 - weight) * airborneAltitude;
            State state = weight >= 1.0 ? State.Ground : weight <= 0.0 ? State.Airborne : State.Ramp;
            // never below the local ground: the adjustment counts as far as the weight applies it, so a
            // model that hovers by its own data can still be lowered on the ground
            double floor = localGround + cgHeight + weight * adjustment;
            if (altitude < floor)
            {
                altitude = floor;
                state = State.Underflow;
            }
            return new Result(altitude, weight, state, cgSource, cgHeight);
        }
    }
}
