using System;
using System.Collections.Generic;
using System.IO;
using JoinFS.Properties;

namespace JoinFS
{
    /// <summary>
    /// The legacy wire's binary codec for the aircraft position XPlane exchanges with the native
    /// plugin (docs/network/network-plugin-architecture.md §6 calls this "Sim.Write/Read"; it used to live
    /// on Sim itself). It also wrote the frames of old .jfs recordings, which the application no
    /// longer writes; LegacyJfsImporter reads them. This message format is frozen - the same
    /// rule as LegacyPlugin's codecs (see CLAUDE.md's networking rules): never add fields here, only
    /// to JFP2.
    /// </summary>
    public static class JfsFrames
    {

        /// <summary>
        /// Exception for reading data
        /// </summary>
        public class ReadException(string message) : Exception(message)
        {
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
        /// Converts a control axis value to/from its wire representation. Deliberately separate from
        /// Sim's own ConvertToAxis/ConvertFromAxis (used for live control events, not serialization)
        /// and from PositionCodec's identical pair (the JFP2 wire) - each frozen wire format owns its
        /// own byte-level encoding rather than sharing an implementation across formats that must be
        /// able to evolve independently.
        /// </summary>
        static short ConvertToAxis(float input) { return (short)(input * 16384.0); }
        static float ConvertFromAxis(short input) { return (float)(int)input * (1.0f / 16384.0f); }

        /// <summary>
        /// Write aircraft position/velocity to a stream
        /// </summary>
        /// <param name="writer">Binary writer</param>
        /// <param name="simPositionVelocity">Position and Velocity</param>
        public static void Write(BinaryWriter writer, ref Sim.AircraftPosition aircraftPosition)
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
        public static void ReadAircraftPosition1(short version, BinaryReader reader, ref Sim.AircraftPosition aircraftPosition)
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
        static readonly Dictionary<short, ReadVersion<Sim.AircraftPosition>> aircraftPositionVersions = new()
        {
            { 10022, ReadAircraftPosition1 },
        };

        /// <summary>
        /// Generic read handler
        /// </summary>
        /// <param name="versions">List of versions</param>
        /// <param name="version">Version to read</param>
        /// <param name="reader">Reader</param>
        public static void Read(short version, BinaryReader reader, ref Sim.AircraftPosition aircraftPosition)
        {
            Read(version, aircraftPositionVersions, reader, ref aircraftPosition);
        }

    }
}
