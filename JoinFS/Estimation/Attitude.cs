using System;

namespace JoinFS.Estimation
{
    /// <summary>
    /// Attitude kinematics: how pitch, heading and bank change under body rotation rates (finding
    /// F4 in docs/position-estimation-plan.md). Body rates are not the rates of the angles: in a
    /// banked turn the yaw rate is only cos(bank) of the heading rate, and the pitch rate makes up
    /// the rest. Integrated through a quaternion, which is exact for constant body rates and has
    /// no trouble near ±90° pitch.
    ///
    /// JoinFS keeps attitude as (x pitch, y heading, z bank) and body rates as (x pitch, y yaw,
    /// z roll). The simulator's pitch and bank may count the other way from the aerospace
    /// convention, but each flips together with its rate, and the kinematics are the same either
    /// way, so the aerospace formulas apply as they are.
    /// </summary>
    public static class Attitude
    {
        /// <summary>
        /// The attitude <paramref name="time"/> seconds on from <paramref name="angles"/>, turning
        /// at constant body <paramref name="rates"/>. Heading and bank stay within half a turn of
        /// the angles given, so they do not jump at the 0/360 line.
        /// </summary>
        public static Vector Integrate(Vector angles, Vector rates, double time)
        {
            // to a quaternion, in the aerospace order: heading, then pitch, then bank
            double ch = Math.Cos(angles.y * 0.5), sh = Math.Sin(angles.y * 0.5);
            double cp = Math.Cos(angles.x * 0.5), sp = Math.Sin(angles.x * 0.5);
            double cb = Math.Cos(angles.z * 0.5), sb = Math.Sin(angles.z * 0.5);
            double w = cb * cp * ch + sb * sp * sh;
            double x = sb * cp * ch - cb * sp * sh;
            double y = cb * sp * ch + sb * cp * sh;
            double z = cb * cp * sh - sb * sp * ch;

            // rotation about the body axes over the time: roll, pitch, yaw
            double rx = rates.z * time, ry = rates.x * time, rz = rates.y * time;
            double angle = Math.Sqrt(rx * rx + ry * ry + rz * rz);
            if (angle > 0.0)
            {
                double s = Math.Sin(angle * 0.5) / angle;
                double dw = Math.Cos(angle * 0.5), dx = rx * s, dy = ry * s, dz = rz * s;
                // body-frame rotation: the attitude times the change
                (w, x, y, z) = (w * dw - x * dx - y * dy - z * dz,
                                w * dx + x * dw + y * dz - z * dy,
                                w * dy - x * dz + y * dw + z * dx,
                                w * dz + x * dy - y * dx + z * dw);
            }

            // back to angles
            double bank = Math.Atan2(2.0 * (w * x + y * z), 1.0 - 2.0 * (x * x + y * y));
            double pitch = Math.Asin(Math.Clamp(2.0 * (w * y - z * x), -1.0, 1.0));
            double heading = Math.Atan2(2.0 * (w * z + x * y), 1.0 - 2.0 * (y * y + z * z));
            return new Vector(pitch, angles.y + Vector.AngleDelta(angles.y, heading), angles.z + Vector.AngleDelta(angles.z, bank));
        }
    }
}
