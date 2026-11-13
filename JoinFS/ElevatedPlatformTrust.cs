using System;

namespace JoinFS
{
    /// <summary>
    /// Decides whether a remote aircraft near the ground is on an elevated platform (helipad, ship
    /// deck, rooftop), so that its raw altitude is trusted instead of being blended toward the local
    /// terrain - see helicopters-on-elevated-platforms feature.
    /// </summary>
    public static class ElevatedPlatformTrust
    {
        /// <summary>Below this height above the sender's ground (metres), the aircraft counts as near the ground even when airborne</summary>
        public const double NearGroundHeight = 50.0;

        /// <summary>
        /// Height of the sender's gear above the sender's own ground probe, in metres: its CG height
        /// less its STATIC CG TO GROUND. About zero on ordinary ground, the platform's height on a
        /// platform. The CG height alone when the clearance is not known (a peer that does not send it).
        /// </summary>
        public static double GearHeight(double senderCgHeight, double senderCgToGround) =>
            double.IsNaN(senderCgToGround) ? senderCgHeight : senderCgHeight - senderCgToGround;

        /// <summary>
        /// Whether to trust the sender's altitude as-is.
        /// </summary>
        /// <param name="previous">The decision made on the previous update</param>
        /// <param name="senderOnGround">The sender reports the aircraft on the ground</param>
        /// <param name="altitude">The sender's CG altitude, metres</param>
        /// <param name="senderElevation">The sender's ground altitude under the aircraft, metres</param>
        /// <param name="senderCgToGround">The sender's STATIC CG TO GROUND, metres; NaN when not known</param>
        /// <param name="localElevation">Our ground altitude under the aircraft, metres</param>
        /// <param name="thresholdCm">Mismatch, and gear height, from which a platform is recognised, cm</param>
        /// <param name="mismatchCm">How far the two installs' ground altitudes differ, cm</param>
        /// <param name="gearHeight">The sender's gear height above its ground, metres (see <see cref="GearHeight"/>)</param>
        public static bool Decide(bool previous, bool senderOnGround, double altitude, double senderElevation, double senderCgToGround,
            double localElevation, double thresholdCm, out double mismatchCm, out double gearHeight)
        {
            double senderHeight = altitude - senderElevation;
            double localHeight = altitude - localElevation;
            mismatchCm = Math.Abs(localHeight - senderHeight) * 100.0;
            gearHeight = GearHeight(senderHeight, senderCgToGround);

            bool nearGround = senderOnGround || senderHeight < NearGroundHeight;
            // the sender's own reading must show the gear well above its terrain. Measured at the gear,
            // not the CG: any aircraft's CG sits more than the threshold above the ground, so a CG test
            // passed for every aircraft on an ordinary runway, and a plain terrain difference between
            // the two installs then switched the elevation correction off - the aircraft sank into the
            // local ground, the sim pushed it out and levelled it, and a taildragger lost its
            // tail-down pitch on one part of a runway only
            double gearHeightCm = gearHeight * 100.0;
            // release at half the threshold, so a value near the threshold does not flip every update
            double releaseThreshold = thresholdCm * 0.5;

            if (nearGround && gearHeightCm >= thresholdCm && mismatchCm >= thresholdCm)
            {
                return true;
            }
            if (nearGround == false || gearHeightCm < releaseThreshold || mismatchCm < releaseThreshold)
            {
                return false;
            }
            // still near the ground with values inside the hysteresis band - keep the previous decision
            return previous;
        }
    }
}
