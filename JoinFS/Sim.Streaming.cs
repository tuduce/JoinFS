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
        /// Method for reading specific data versions
        /// </summary>
        /// <param name="reader"></param>
        public delegate void ReadVersion(short version, BinaryReader reader);

        /// <summary>
        /// Exception for reading data
        /// </summary>
        public class ReadException(string message) : Exception(message)
        {
        }

        /// <summary>
        /// Generic read handler
        /// </summary>
        /// <param name="versions">List of versions</param>
        /// <param name="version">Version to read</param>
        /// <param name="reader">Reader</param>
        public static void Read(short version, Dictionary<short, ReadVersion> versions, BinaryReader reader)
        {
            // get keys
            List<short> keys = [.. versions.Keys];
            // sort keys
            keys.Sort();
            // go backwards through the versions
            for (int index = keys.Count - 1; index >= 0; index--)
            {
                // check version
                if (version >= keys[index])
                {
                    // read version
                    versions[keys[index]](version, reader);
                    break;
                }
            }
        }

        /// <summary>
        /// Method for reading specific data versions
        /// </summary>
        /// <param name="reader"></param>
        public delegate void ReadVersion<T>(short version, BinaryReader reader, ref T t);

        /// <summary>
        /// Generic read handler
        /// </summary>
        /// <param name="versions">List of versions</param>
        /// <param name="version">Version to read</param>
        /// <param name="reader">Reader</param>
        public static void Read<T>(short version, Dictionary<short, ReadVersion<T>> versions, BinaryReader reader, ref T t)
        {
            // get keys
            List<short> keys = [.. versions.Keys];
            // sort keys
            keys.Sort();
            // go backwards through the versions
            for (int index = keys.Count - 1; index >= 0; index--)
            {
                // check version
                if (version >= keys[index])
                {
                    // read version
                    versions[keys[index]](version, reader, ref t);
                    break;
                }
            }
        }

        /// <summary>
        /// Write position/velocity to a stream
        /// </summary>
        /// <param name="writer">Binary writer</param>
        /// <param name="simPositionVelocity">Position and Velocity</param>
        public static void Write(BinaryWriter writer, ref ObjectPositionVelocity positionVelocity)
        {
            // add position
            writer.Write(positionVelocity.latitude);
            writer.Write(positionVelocity.longitude);
            writer.Write(positionVelocity.altitude);
            writer.Write(positionVelocity.pitch);
            writer.Write(positionVelocity.bank);
            writer.Write(positionVelocity.heading);
            // add velocity
            writer.Write(positionVelocity.velocityX);
            writer.Write(positionVelocity.velocityY);
            writer.Write(positionVelocity.velocityZ);
            writer.Write(positionVelocity.angularVelocityX);
            writer.Write(positionVelocity.angularVelocityY);
            writer.Write(positionVelocity.angularVelocityZ);
            writer.Write(positionVelocity.accelerationX);
            writer.Write(positionVelocity.accelerationY);
            writer.Write(positionVelocity.accelerationZ);
            writer.Write(positionVelocity.height);
            // ground flags
            byte flags = 0;
            if (positionVelocity.ground != 0) flags |= 0x01;
            if (Settings.Default.ElevationCorrection) flags |= 0x02;
            writer.Write(flags);
        }

        /// <summary>
        /// Read position and velocity from a stream
        /// </summary>
        /// <param name="reader">Binary reader</param>
        public static void ReadPositionVelocity1(short version, BinaryReader reader, ref ObjectPositionVelocity positionVelocity)
        {
            // update position
            positionVelocity.latitude = reader.ReadDouble();
            positionVelocity.longitude = reader.ReadDouble();
            positionVelocity.altitude = reader.ReadDouble();
            positionVelocity.pitch = reader.ReadSingle();
            positionVelocity.bank = reader.ReadSingle();
            positionVelocity.heading = reader.ReadSingle();
            // update velocity
            positionVelocity.velocityX = reader.ReadSingle();
            positionVelocity.velocityY = reader.ReadSingle();
            positionVelocity.velocityZ = reader.ReadSingle();
            positionVelocity.angularVelocityX = reader.ReadSingle();
            positionVelocity.angularVelocityY = reader.ReadSingle();
            positionVelocity.angularVelocityZ = reader.ReadSingle();
            positionVelocity.accelerationX = reader.ReadSingle();
            positionVelocity.accelerationY = reader.ReadSingle();
            positionVelocity.accelerationZ = reader.ReadSingle();
            // update ground state
            positionVelocity.height = version >= 10023 ? reader.ReadSingle() : 0.0f;
            byte flags = version >= 10023 ? reader.ReadByte() : (byte)0;
            positionVelocity.ground = (flags & 0x01) != 0 ? 1 : 0;
        }

        /// <summary>
        /// Version table for reading position and velocity
        /// </summary>
        static readonly Dictionary<short, ReadVersion<ObjectPositionVelocity>> positionVelocityVersions = new()
        {
            { 10022, ReadPositionVelocity1 },
        };

        /// <summary>
        /// Generic read handler
        /// </summary>
        /// <param name="versions">List of versions</param>
        /// <param name="version">Version to read</param>
        /// <param name="reader">Reader</param>
        public static void Read(short version, BinaryReader reader, ref ObjectPositionVelocity positionVelocity)
        {
            Read<ObjectPositionVelocity>(version, positionVelocityVersions, reader, ref positionVelocity);
        }

        /// <summary>
        /// Write aircraft position/velocity to a stream
        /// </summary>
        /// <param name="writer">Binary writer</param>
        /// <param name="simPositionVelocity">Position and Velocity</param>
        public static void Write(BinaryWriter writer, ref AircraftPosition aircraftPosition)
        {
            // add position
            writer.Write(aircraftPosition.latitude);
            writer.Write(aircraftPosition.longitude);
            writer.Write(aircraftPosition.altitude);
            writer.Write(aircraftPosition.pitch);
            writer.Write(aircraftPosition.bank);
            writer.Write(aircraftPosition.heading);
            // add velocity
            writer.Write(aircraftPosition.velocityX);
            writer.Write(aircraftPosition.velocityY);
            writer.Write(aircraftPosition.velocityZ);
            writer.Write(aircraftPosition.angularVelocityX);
            writer.Write(aircraftPosition.angularVelocityY);
            writer.Write(aircraftPosition.angularVelocityZ);
            writer.Write(aircraftPosition.accelerationX);
            writer.Write(aircraftPosition.accelerationY);
            writer.Write(aircraftPosition.accelerationZ);
            // add control positions
            writer.Write(ConvertToAxis(aircraftPosition.rudder));
            writer.Write(ConvertToAxis(aircraftPosition.elevator));
            writer.Write(ConvertToAxis(aircraftPosition.aileron));
            writer.Write(ConvertToAxis(aircraftPosition.brakeLeft));
            writer.Write(ConvertToAxis(aircraftPosition.brakeRight));
            // add ground state
            writer.Write(aircraftPosition.elevation);
            // ground flags
            byte flags = 0;
            if (aircraftPosition.ground != 0) flags |= 0x01;
            if (Settings.Default.ElevationCorrection) flags |= 0x02;
            writer.Write(flags);
            // "STATIC CG TO GROUND", feet - the sender's own real ground clearance, used by the receiver to
            // ground a substitute model using its own clearance instead of the sender's (see
            // helicopters-on-elevated-platforms feature / ground-jitter-on-model-mismatch fix). Always
            // written; older readers (version < 21008) simply don't read it, matching the elevation/flags
            // fields' existing pattern above.

            // This message format is frozen. Anything written here breaks the ability of older clients to read the message.
            // If you need to add new fields, use the JFP2 protocol.
            // writer.Write(aircraftPosition.staticCgToGround);
        }

        /// <summary>
        /// Read aircraft position and velocity from a stream
        /// </summary>
        /// <param name="reader">Binary reader</param>
        public static void ReadAircraftPosition1(short version, BinaryReader reader, ref AircraftPosition aircraftPosition)
        {
            // update position
            aircraftPosition.latitude = reader.ReadDouble();
            aircraftPosition.longitude = reader.ReadDouble();
            aircraftPosition.altitude = reader.ReadDouble();
            aircraftPosition.pitch = reader.ReadSingle();
            aircraftPosition.bank = reader.ReadSingle();
            aircraftPosition.heading = reader.ReadSingle();
            // update velocity
            aircraftPosition.velocityX = reader.ReadSingle();
            aircraftPosition.velocityY = reader.ReadSingle();
            aircraftPosition.velocityZ = reader.ReadSingle();
            aircraftPosition.angularVelocityX = reader.ReadSingle();
            aircraftPosition.angularVelocityY = reader.ReadSingle();
            aircraftPosition.angularVelocityZ = reader.ReadSingle();
            aircraftPosition.accelerationX = reader.ReadSingle();
            aircraftPosition.accelerationY = reader.ReadSingle();
            aircraftPosition.accelerationZ = reader.ReadSingle();
            // update controls
            aircraftPosition.rudder = ConvertFromAxis(reader.ReadInt16());
            aircraftPosition.elevator = ConvertFromAxis(reader.ReadInt16());
            aircraftPosition.aileron = ConvertFromAxis(reader.ReadInt16());
            aircraftPosition.brakeLeft = ConvertFromAxis(reader.ReadInt16());
            aircraftPosition.brakeRight = ConvertFromAxis(reader.ReadInt16());
            // update ground state
            aircraftPosition.elevation = version >= 10023 ? reader.ReadSingle() : 0.0f;
            byte flags = version >= 10023 ? reader.ReadByte() : (byte)0;
            aircraftPosition.ground = (flags & 0x01) != 0 ? 1 : 0;
            // "STATIC CG TO GROUND" - see Write() above. NaN (not 0.0f) for an older peer that didn't send
            // it, so downstream code can tell "no data" apart from a real zero clearance and fall back to
            // uncorrected placement instead of attempting a wrong correction.

            // The legacy message format is frozen. Anything read here breaks the ability of older clients to read the message.
            // If you need to add new fields, use the JFP2 protocol.
            // aircraftPosition.staticCgToGround = version >= 21008 ? reader.ReadSingle() : float.NaN;
        }

        /// <summary>
        /// Version table for reading position and velocity
        /// </summary>
        static readonly Dictionary<short, ReadVersion<AircraftPosition>> aircraftPositionVersions = new()
        {
            { 10022, ReadAircraftPosition1 },
        };

        /// <summary>
        /// Generic read handler
        /// </summary>
        /// <param name="versions">List of versions</param>
        /// <param name="version">Version to read</param>
        /// <param name="reader">Reader</param>
        public static void Read(short version, BinaryReader reader, ref AircraftPosition aircraftPosition)
        {
            Read<AircraftPosition>(version, aircraftPositionVersions, reader, ref aircraftPosition);
        }

        /// <summary>
        /// Write integer variables to a stream
        /// </summary>
        public static void Write(BinaryWriter writer, Dictionary<uint, int> variables)
        {
            // write count
            writer.Write((ushort)variables.Count);
            // for each variable
            foreach (var variable in variables)
            {
                // add variable ID
                writer.Write(variable.Key);
                // add value
                writer.Write(variable.Value);
            }
        }

        /// <summary>
        /// Read integer variables from a stream
        /// </summary>
        public static void Read(short version, BinaryReader reader, Dictionary<uint, int> variables)
        {
            // read count
            ushort count = reader.ReadUInt16();
            // for each variable
            for (int i = 0; i < count; i++)
            {
                // read variable ID
                uint vuid = reader.ReadUInt32();
                // read integer
                int value = reader.ReadInt32();
                // add variable
                variables[vuid] = value;
            }
        }

        /// <summary>
        /// Write float variables to a stream
        /// </summary>
        public static void Write(BinaryWriter writer, Dictionary<uint, float> variables)
        {
            // write count
            writer.Write((ushort)variables.Count);
            // for each variable
            foreach (var variable in variables)
            {
                // add variable ID
                writer.Write(variable.Key);
                // add value
                writer.Write(variable.Value);
            }
        }

        /// <summary>
        /// Read float variables from a stream
        /// </summary>
        public static void Read(short version, BinaryReader reader, Dictionary<uint, float> variables)
        {
            // read count
            ushort count = reader.ReadUInt16();
            // for each variable
            for (int i = 0; i < count; i++)
            {
                // read variable ID
                uint vuid = reader.ReadUInt32();
                // read float
                float value = reader.ReadSingle();
                // add variable
                variables[vuid] = value;
            }
        }

        /// <summary>
        /// Write string8 variables to a stream
        /// </summary>
        public static void Write(BinaryWriter writer, Dictionary<uint, string> variables)
        {
            // write count
            writer.Write((ushort)variables.Count);
            // for each variable in the set
            foreach (var variable in variables)
            {
                // add variable ID
                writer.Write(variable.Key);
                // add value
                writer.Write(variable.Value);
            }
        }

        /// <summary>
        /// Read string8 variables from a stream
        /// </summary>
        public static void Read(short version, BinaryReader reader, Dictionary<uint, string> variables)
        {
            // read count
            ushort count = reader.ReadUInt16();
            // for each variable
            for (int i = 0; i < count; i++)
            {
                // read variable ID
                uint vuid = reader.ReadUInt32();
                // read string
                string value = reader.ReadString();
                // add variable
                variables[vuid] = value;
            }
        }
    }
}
