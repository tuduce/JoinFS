using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace JoinFS
{
    public class Quaternion
    {
        double x, y, z, w;

        /// <summary>
        /// Constructor
        /// </summary>
        public Quaternion(double x, double y, double z, double w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        /// <summary>
        /// Clone
        /// </summary>
        public Quaternion Clone() => new Quaternion(x, y, z, w);

        /// <summary>
        /// Normalise
        /// </summary>
        public void Normalize()
        {
            double s = 1.0 / Math.Sqrt(x * x + y * y + z * z + w * w);
            x *= s;
            y *= s;
            z *= s;
            w *= s;
        }

        /// <summary>
        /// Multiply quaternions
        /// </summary>
        public static Quaternion operator *(Quaternion q0, Quaternion q1)
        {
            return new Quaternion
                (
                    q0.w * q1.x + q0.x * q1.w + q0.y * q1.z - q0.z * q1.y,
                    q0.w * q1.y - q0.x * q1.z + q0.y * q1.w + q0.z * q1.x,
                    q0.w * q1.z + q0.x * q1.y - q0.y * q1.x + q0.z * q1.w,
                    q0.w * q1.w - q0.x * q1.x - q0.y * q1.y - q0.z * q1.z
                );
        }

        /// <summary>
        /// Add quaternions
        /// </summary>
        public static Quaternion operator +(Quaternion q0, Quaternion q1) => new Quaternion(q0.x + q1.x, q0.y + q1.y, q0.z + q1.z, q0.w + q1.w);

        /// <summary>
        /// Scale quaternion
        /// </summary>
        public static Quaternion operator *(Quaternion q, double scalar) => new Quaternion(q.x * scalar, q.y * scalar, q.z * scalar, q.w * scalar);

        /// <summary>
        /// Convert quaternion to euler angles
        /// </summary>
        public Vector ToEuler()
        {
            double test = x * y + z * w;
            // singularity at north pole
            if (test > 0.499f)
            {
                return new Vector(Math.PI / 2, 2.0 * Math.Atan2(x, w), 0);
            }
            // singularity at south pole
            else if (test < -0.499)
            {
                return new Vector(-Math.PI / 2, -2 * Math.Atan2(x, w), 0);
            }
            double xx = x * x;
            double yy = y * y;
            double zz = z * z;
            return new Vector(Math.Asin(2 * test), Math.Atan2(2 * y * w - 2 * x * z, 1 - 2 * yy - 2 * zz), Math.Atan2(2 * x * w - 2 * y * z, 1 - 2 * xx - 2 * zz));
        }

        /// <summary>
        /// Convert euler angles to quaternion
        /// </summary>
        public static Quaternion FromEuler(Vector euler)
        {
            double cy = Math.Cos(euler.y / 2);
            double sy = Math.Sin(euler.y / 2);
            double cx = Math.Cos(euler.x / 2);
            double sx = Math.Sin(euler.x / 2);
            double cz = Math.Cos(euler.z / 2);
            double sz = Math.Sin(euler.z / 2);
            double cycx = cy * cx;
            double sysx = sy * sx;
            return new Quaternion(cycx * sz + sysx * cz, sy * cx * cz + cy * sx * sz, cy * sx * cz - sy * cx * sz, cycx * cz - sysx * sz);
        }

        /// <summary>
        /// Interpolate between quaternions
        /// </summary>
        public static Quaternion Slerp(Quaternion qa, Quaternion qb, double t)
        {
            // Calculate angle between them.
            double cosHalfTheta = qa.w * qb.w + qa.x * qb.x + qa.y * qb.y + qa.z * qb.z;
            // if qa=qb or qa=-qb then theta = 0 and we can return qa
            if (Math.Abs(cosHalfTheta) >= 1.0)
            {
                return qa.Clone();
            }
            // Calculate temporary values.
            double halfTheta = Math.Acos(cosHalfTheta);
            double sinHalfTheta = Math.Sqrt(1.0 - cosHalfTheta * cosHalfTheta);
            // if theta = 180 degrees then result is not fully defined
            // we could rotate around any axis normal to qa or qb
            if (Math.Abs(sinHalfTheta) < 0.001)
            { // fabs is floating point absolute
                return new Quaternion(qa.x * 0.5 + qb.x * 0.5, qa.y * 0.5 + qb.y * 0.5, qa.z * 0.5 + qb.z * 0.5, qa.w * 0.5 + qb.w * 0.5);
            }
            double ratioA = Math.Sin((1 - t) * halfTheta) / sinHalfTheta;
            double ratioB = Math.Sin(t * halfTheta) / sinHalfTheta;
            //calculate Quaternion.
            return new Quaternion(qa.x * ratioA + qb.x * ratioB, qa.y * ratioA + qb.y * ratioB, qa.z * ratioA + qb.z * ratioB, qa.w * ratioA + qb.w * ratioB);
        }
    }
}
