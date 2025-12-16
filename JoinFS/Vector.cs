using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;


namespace JoinFS
{
    /// <summary>
    /// Vector
    /// </summary>
    public class Vector
    {
        public double x, y, z;

        /// <summary>
        /// Constructor
        /// </summary>
        public Vector()
        {
            x = y = z = 0.0;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public Vector(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        /// <summary>
        /// Clone
        /// </summary>
        public Vector Clone() => new(x, y, z);

        /// <summary>
        /// Add two vectors
        /// </summary>
        public static Vector operator +(Vector a, Vector b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        /// <summary>
        /// Subtract two vectors
        /// </summary>
        public static Vector operator -(Vector a, Vector b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        /// <summary>
        /// Multiply vector
        /// </summary>
        public static Vector operator *(Vector a, Vector b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
        /// <summary>
        /// Multiply scalar
        /// </summary>
        public static Vector operator *(Vector v, double d) => new(v.x * d, v.y * d, v.z * d);

        /// <summary>
        /// Rotate vector around pitch angle
        /// </summary>
        /// <param name="pitch">Angle of pitch</param>
        /// <returns>Rotated vector</returns>
        public Vector RotatePitch(double pitch) => new(x, y * Math.Cos(pitch) - z * Math.Sin(pitch), y * Math.Sin(pitch) + z * Math.Cos(pitch));

        /// <summary>
        /// Rotate vector around bank angle
        /// </summary>
        /// <param name="bank">Angle of bank</param>
        /// <returns>Rotated vector</returns>
        public Vector RotateBank(double bank) => new(x * Math.Cos(bank) - y * Math.Sin(bank), x * Math.Sin(bank) + y * Math.Cos(bank), z);

        /// <summary>
        /// Rotate vector around heading angle
        /// </summary>
        /// <param name="heading">Angle of heading</param>
        /// <returns>Rotate vector</returns>
        public Vector RotateHeading(double heading) => new(x * Math.Cos(heading) + z * Math.Sin(heading), y, -x * Math.Sin(heading) + z * Math.Cos(heading));

        /// <summary>
        /// Rotate the vector by euler angles
        /// </summary>
        public Vector Rotate(Vector angles)
        {
            double ch = Math.Cos(angles.y);
            double sh = Math.Sin(angles.y);
            double cp = Math.Cos(angles.x);
            double sp = Math.Sin(angles.x);
            double cb = Math.Cos(angles.z);
            double sb = Math.Sin(angles.z);
            Vector v = new(x * cb - y * sb, x * sb + y * cb, z);
            x = v.x;
            y = v.y * cp - v.z * sp;
            z = v.y * sp + v.z * cp;
            v.x = x * ch + z * sh;
            v.y = y;
            v.z = -x * sh + z * ch;
            return v;
        }

        /// <summary>
        /// Inverse rotate the vector by euler angles
        /// </summary>
        public Vector InvRotate(Vector angles)
        {
            double ch = Math.Cos(-angles.y);
            double sh = Math.Sin(-angles.y);
            double cp = Math.Cos(-angles.x);
            double sp = Math.Sin(-angles.x);
            double cb = Math.Cos(-angles.z);
            double sb = Math.Sin(-angles.z);
            Vector v = new(x * ch + z * sh, y, -x * sh + z * ch);
            x = v.x;
            y = v.y * cp - v.z * sp;
            z = v.y * sp + v.z * cp;
            v.x = x * cb - y * sb;
            v.y = x * sb + y * cb;
            v.z = z;
            return v;
        }

        public const double GEODESIC_EPSILON = 0.0000001;

        /// <summary>
        /// Distance between two points on a globe
        /// </summary>
        /// <param name="ln1"></param>
        /// <param name="lt1"></param>
        /// <param name="ln2"></param>
        /// <param name="lt2"></param>
        /// <returns></returns>
        public static double GeodesicDistance(double ln1, double lt1, double ln2, double lt2) => 2.0 * Math.Asin(Math.Sqrt(Math.Pow(Math.Sin((lt2 - lt1) / 2.0), 2.0) + Math.Cos(lt1) * Math.Cos(lt2) * Math.Pow(Math.Sin((ln2 - ln1) / 2.0), 2.0))) * 6371009.0;

        /// <summary>
        /// Bearing between two points on a globe
        /// </summary>
        /// <param name="ln1"></param>
        /// <param name="lt1"></param>
        /// <param name="ln2"></param>
        /// <param name="lt2"></param>
        /// <returns></returns>
        public static double GeodesicBearing(double ln1, double lt1, double ln2, double lt2) => (Math.Atan2(Math.Sin(ln2 - ln1) * Math.Cos(lt2), Math.Cos(lt1) * Math.Sin(lt2) - Math.Sin(lt1) * Math.Cos(lt2) * Math.Cos(ln2 - ln1)) + 2.0 * Math.PI) % (2.0 * Math.PI);

        /// <summary>
        /// Difference between two angles
        /// </summary>
        public static double AngleDelta(double a, double b)
        {
            // difference
            double delta = b - a;
            // move into range
            if (delta < -Math.PI) delta += Math.PI * 2.0f;
            else if (delta > Math.PI) delta -= Math.PI * 2.0f;
            // return result
            return delta;
        }

        /// <summary>
        /// Difference between two sets of angles
        /// </summary>
        public static Vector AnglesDelta(Vector a, Vector b) => new(AngleDelta(a.x, b.x), AngleDelta(a.y, b.y), AngleDelta(a.z, b.z));
    }
}
