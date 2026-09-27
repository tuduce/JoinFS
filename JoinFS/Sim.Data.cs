using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using JoinFS.Properties;
using JoinFS.Net;




#if SIMCONNECT
#if P3D
//using LockheedMartin.Prepar3D.SimConnect;
using Microsoft.FlightSimulator.SimConnect;
#else
using Microsoft.FlightSimulator.SimConnect;
#endif
#endif

namespace JoinFS
{
    public partial class Sim
    {
        /// <summary>
        /// Object info in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ObjectGetInfo
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public String category;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public String callsign;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public String type;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public String model;
            public int isUser;
            public int engineType;
            public int numEngines;
#if FS2024
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public String livery;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public String liveryFolder;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public String airline;
#endif
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public String flightNumber;
        };

        /// <summary>
        /// Object position velocity variables in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ObjectPositionVelocity
        {
            public double latitude;
            public double longitude;
            public double altitude;
            public float pitch;
            public float bank;
            public float heading;
            public float velocityX;
            public float velocityY;
            public float velocityZ;
            public float angularVelocityX;
            public float angularVelocityY;
            public float angularVelocityZ;
            public float accelerationX;
            public float accelerationY;
            public float accelerationZ;
            public float height;
            public int ground;
        };

        /// <summary>
        /// Object position in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ObjectPosition(Sim.Pos pos)
        {
            public double latitude = pos.geo.z;
            public double longitude = pos.geo.x;
            public double altitude = pos.geo.y;
            public float pitch = (float)pos.angles.x;
            public float bank = (float)pos.angles.z;
            public float heading = (float)pos.angles.y;
            public float height = (float)pos.elevation;
            public int ground = pos.ground;
        };

        /// <summary>
        /// Object position in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ObjectPositionUpdate
        {
            public double latitude;
            public double longitude;
            public double altitude;
            public float pitch;
            public float bank;
            public float heading;
            /// <summary>Forwarded to SimConnect's "SIM ON GROUND" whenever the sender reports the object on-ground, for any aircraft type - independent of the elevated-platform elevation-trust decision. This lets the local sim's own gear/ground-contact physics handle placement instead of fighting an externally-driven altitude every tick. See helicopters-on-elevated-platforms feature.</summary>
            public int ground;

            public ObjectPositionUpdate(ref ObjectPosition position)
            {
                latitude = position.latitude;
                longitude = position.longitude;
                altitude = position.altitude;
                pitch = position.pitch;
                bank = position.bank;
                heading = position.heading;
            }

            public ObjectPositionUpdate(Pos pos)
            {
                latitude = pos.geo.z;
                longitude = pos.geo.x;
                altitude = pos.geo.y;
                pitch = (float)pos.angles.x;
                bank = (float)pos.angles.z;
                heading = (float)pos.angles.y;
            }
        };

        /// <summary>
        /// Object velocity in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ObjectVelocity(Vector linear, Vector angular, Vector acc)
        {
            public float velocityX = (float)linear.x;
            public float velocityY = (float)linear.y;
            public float velocityZ = (float)linear.z;
            public float angularVelocityX = (float)angular.x;
            public float angularVelocityY = (float)angular.y;
            public float angularVelocityZ = (float)angular.z;
            public float accelerationX = (float)acc.x;
            public float accelerationY = (float)acc.y;
            public float accelerationZ = (float)acc.z;
        };

        /// <summary>
        /// Object orientation in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ObjectEuler(Vector angles)
        {
            public float pitch = (float)angles.x;
            public float heading = (float)angles.y;
            public float bank = (float)angles.z;
        };

        /// <summary>
        /// Aircraft position variables in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct AircraftPosition
        {
            public double latitude;
            public double longitude;
            public double altitude;
            public float pitch;
            public float bank;
            public float heading;
            public float velocityX;
            public float velocityY;
            public float velocityZ;
            public float angularVelocityX;
            public float angularVelocityY;
            public float angularVelocityZ;
            public float accelerationX;
            public float accelerationY;
            public float accelerationZ;
            public float rudder;
            public float elevator;
            public float aileron;
            public float brakeLeft;
            public float brakeRight;
            public float elevation;
            /// <summary>"PLANE ALT ABOVE GROUND", feet - diagnostic only, see helicopters-on-elevated-platforms feature. Not yet used in any trust/correction decision.</summary>
            public float radarAltitude;
            public int ground;
            /// <summary>"STATIC CG TO GROUND", feet - this object's own real static ground clearance, read live via SimConnect (no file access needed, unlike aircraft.cfg contact-point data). Used to ground a substitute model using its own geometry instead of the sender's - see the ground-jitter-on-model-mismatch fix.</summary>
            public float staticCgToGround;
        };

        /// <summary>
        /// Aircraft ID in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct AircraftSetId
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public String callsign;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public String airline;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public String number;
        };

        /// <summary>
        /// Object velocity
        /// </summary>
        public class Vel
        {
            public Vector linear;
            public Vector angular;
            public Vector acc;

            /// <summary>
            /// Constructor
            /// </summary>
            public Vel(Vector linear, Vector angular, Vector acc)
            {
                this.linear = linear;
                this.angular = angular;
                this.acc = acc;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Vel()
            {
                this.linear = new Vector();
                this.angular = new Vector();
                this.acc = new Vector();
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Vel(ref ObjectPositionVelocity position)
            {
                this.linear = new Vector(position.velocityX, position.velocityY, position.velocityZ);
                this.angular = new Vector(position.angularVelocityX, position.angularVelocityY, position.angularVelocityZ);
                this.acc = new Vector(position.accelerationX, position.accelerationY, position.accelerationZ);
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Vel(ref AircraftPosition position)
            {
                this.linear = new Vector(position.velocityX, position.velocityY, position.velocityZ);
                this.angular = new Vector(position.angularVelocityX, position.angularVelocityY, position.angularVelocityZ);
                this.acc = new Vector(position.accelerationX, position.accelerationY, position.accelerationZ);
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Vel(ref ObjectVelocity velocity)
            {
                this.linear = new Vector(velocity.velocityX, velocity.velocityY, velocity.velocityZ);
                this.angular = new Vector(velocity.angularVelocityX, velocity.angularVelocityY, velocity.angularVelocityZ);
                this.acc = new Vector(velocity.accelerationX, velocity.accelerationY, velocity.accelerationZ);
            }

            /// <summary>
            /// Clone
            /// </summary>
            public Vel Clone() => new(linear.Clone(), angular.Clone(), acc.Clone());

            /// <summary>
            /// Extrapolate a velocity in time
            /// </summary>
            public Vel Extrapolate(double time)
            {
                return new Vel(new Vector(linear.x + acc.x * time, linear.y + acc.y * time, linear.z + acc.z * time), angular.Clone(), acc.Clone());
            }
        }

        /// <summary>
        /// Object position
        /// </summary>
        public class Pos
        {
            public Vector geo;
            public Vector angles;
            public double elevation;
            /// <summary>"PLANE ALT ABOVE GROUND" in meters, diagnostic only - see helicopters-on-elevated-platforms feature. NaN when not sourced from an AircraftPosition read (distinct from a real 0.0 reading).</summary>
            public double radarHeight;
            public int ground;
            /// <summary>"STATIC CG TO GROUND" in meters - this object's own real static ground clearance. NaN when not sourced from an AircraftPosition read (a live SimConnect poll not yet arrived, or an older peer that didn't broadcast it - see the ground-jitter-on-model-mismatch fix).</summary>
            public double staticCgToGround;

            /// <summary>
            /// Constructor
            /// </summary>
            public Pos()
            {
                this.geo = new Vector();
                this.angles = new Vector();
                this.elevation = 0.0;
                this.radarHeight = double.NaN;
                this.ground = 0;
                this.staticCgToGround = double.NaN;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Pos(Vector geo, Vector angles, double elevation, int ground, double radarHeight = double.NaN)
            {
                this.geo = geo;
                this.angles = angles;
                this.elevation = elevation;
                this.radarHeight = radarHeight;
                this.ground = ground;
                this.staticCgToGround = double.NaN;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Pos(ref ObjectPosition position)
            {
                this.geo = new Vector(position.longitude, position.altitude, position.latitude);
                this.angles = new Vector(position.pitch, position.heading, position.bank);
                this.elevation = position.height;
                this.radarHeight = double.NaN;
                this.ground = position.ground;
                this.staticCgToGround = double.NaN;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Pos(ref AircraftPosition position)
            {
                this.geo = new Vector(position.longitude, position.altitude, position.latitude);
                this.angles = new Vector(position.pitch, position.heading, position.bank);
                this.elevation = position.elevation;
                this.radarHeight = position.radarAltitude * 0.3048;
                this.ground = position.ground;
                this.staticCgToGround = position.staticCgToGround * 0.3048;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Pos(ref ObjectPositionVelocity position)
            {
                this.geo = new Vector(position.longitude, position.altitude, position.latitude);
                this.angles = new Vector(position.pitch, position.heading, position.bank);
                this.elevation = position.height;
                this.radarHeight = double.NaN;
                this.ground = position.ground;
                this.staticCgToGround = double.NaN;
            }

            /// <summary>
            /// Clone
            /// </summary>
            public Pos Clone() => new(geo.Clone(), angles.Clone(), elevation, ground, radarHeight);

            /// <summary>Full copy, including staticCgToGround (for snapshot copies)</summary>
            public Pos CloneAll()
            {
                Pos copy = Clone();
                copy.staticCgToGround = staticCgToGround;
                return copy;
            }

            /// <summary>
            /// Extrapolate a position using velocity and time
            /// </summary>
            /// <param name="velocity"></param>
            /// <param name="time"></param>
            /// <returns></returns>
            public Pos Extrapolate(Vel velocity, double time)
            {
                // get rate of change of geodesic position
                double xRate = Vector.GeodesicDistance(geo.x, geo.z, geo.x + Vector.GEODESIC_EPSILON, geo.z);
                double zRate = Vector.GeodesicDistance(geo.x, geo.z, geo.x, geo.z + Vector.GEODESIC_EPSILON);
                // extrapolate position and velocity
                Vector scalar = new(1.0 / xRate * Vector.GEODESIC_EPSILON, 1.0, 1.0 / zRate * Vector.GEODESIC_EPSILON);
                return new Pos(geo + (velocity.linear * time + velocity.acc * (time * time)) * scalar, angles + velocity.angular * time, elevation, ground, radarHeight);
            }
        }

        /// <summary>
        /// Integer value
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct IntegerStruct
        {
            public int value;
        };

        /// <summary>
        /// Float value
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct FloatStruct
        {
            public float value;
        };

        /// <summary>
        /// String value
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct String8Struct
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
            public string value;
        };

        static short ConvertToAxis(float input) { return (short)(input * 16384.0); }
        static float ConvertFromAxis(short input) { return (float)(int)input * (1.0f / 16384.0f); }
    }
}
