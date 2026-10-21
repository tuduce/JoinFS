using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JoinFS.Net;

namespace JoinFS.Recording
{
    /// <summary>
    /// Reads a legacy <c>.jfs</c> recording into canonical messages (docs/recording/jfs2/recorder-v2-design.md section 8). This is the old reader,
    /// frozen and isolated: the only code that knows the old format. It has no reference to Sim or Main and no build symbol.
    /// </summary>
    /// <remarks>
    /// The old format is ambiguous in a way only an offline pass can resolve (docs/recording/jfs-format.md 7.1): an FS2024 build wrote
    /// three trailing strings per object (livery, ICAO type, ICAO airline) and every other build two (type, airline), at the same version.
    /// The importer parses the file both ways and accepts the layout that passes validation (known frame types, plausible values, ending
    /// exactly at the end of the file). When both pass it takes the FS2024 layout and says so; when neither does it fails with the reason
    /// for each. Unlike the in-app reader it reads every frame type on plain objects too (jfs-format.md 7.2).
    /// </remarks>
    public static class LegacyJfsImporter
    {
        /// <summary>The oldest version the format's reader ever supported.</summary>
        public const short OldestVersion = 10022;

        /// <summary>How the trailing strings of an object were written.</summary>
        public enum TailLayout
        {
            /// <summary>Livery from version 21004, then ICAO type and airline from 21005 (an FS2024 build).</summary>
            Fs2024,
            /// <summary>ICAO type and airline from version 21004 (any other build).</summary>
            Other,
        }

        /// <param name="Scene">The recording; its <see cref="Jfs2Scene.Warnings"/> says what the user should know.</param>
        /// <param name="Version">The file version.</param>
        /// <param name="Layout">The layout that was accepted.</param>
        public sealed record Result(Jfs2Scene Scene, short Version, TailLayout Layout);

        enum FrameKind : byte
        {
            ObjectPosition = 0,
            AircraftPosition = 1,
            SimEvent = 10,
            IntegerVariables = 11,
            FloatVariables = 12,
            String8Variables = 13,
        }

        const float AxisScale = 1.0f / 16384.0f;

        /// <summary>
        /// Reads the whole recording. Objects come in file order (aircraft, then plain objects) and their frames in time order.
        /// </summary>
        /// <param name="endedUtc">When the file was written, which is when the recording ended; the old format does not say when it started,
        /// so the start is that minus the recording's length. Leave it out to start at the Unix epoch.</param>
        /// <exception cref="InvalidDataException">The file is not a recording this importer can read; the message says why.</exception>
        public static Result Read(Stream stream, DateTime? endedUtc = null)
        {
            byte[] bytes;
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                bytes = copy.ToArray();
            }
            if (bytes.Length < 2)
            {
                throw new InvalidDataException("This is not a JoinFS recording: the file is empty.");
            }

            short version = BitConverter.ToInt16(bytes, 0);
            if (version < OldestVersion)
            {
                throw new InvalidDataException("This recording is from version " + version + ", which is older than JoinFS reads (" + OldestVersion + ").");
            }

            List<SceneTrack> accepted = null;
            TailLayout layout = TailLayout.Fs2024;
            List<string> failures = [];
            bool both = false;
            foreach (TailLayout candidate in new[] { TailLayout.Fs2024, TailLayout.Other })
            {
                // before 21004 neither layout has trailing strings, so there is only one way to read the file
                if (candidate == TailLayout.Other && version < 21004)
                {
                    break;
                }
                try
                {
                    List<SceneTrack> tracks = Parse(bytes, version, candidate);
                    if (accepted == null)
                    {
                        accepted = tracks;
                        layout = candidate;
                    }
                    else
                    {
                        both = true;
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException or ArgumentException or FormatException)
                {
                    failures.Add(candidate + " layout: " + ex.Message);
                }
            }

            if (accepted == null)
            {
                throw new InvalidDataException("This recording cannot be read (" + string.Join("; ", failures) + ").");
            }

            var scene = new Jfs2Scene();
            scene.Tracks.AddRange(accepted);
            if (both)
            {
                scene.Warnings.Add("This recording reads correctly with either layout of the trailing strings (an FS2024 build or another one wrote it); the FS2024 layout was used. If the livery, ICAO type and airline look wrong, the file was written by another build.");
            }
            scene.Notes.AddRange(scene.Warnings);

            double length = accepted.Count == 0 ? 0 : accepted.Max(t => t.Frames.Count == 0 ? 0 : t.Frames[^1].Time);
            scene.Initialise(endedUtc.HasValue ? endedUtc.Value.AddSeconds(-length) : DateTime.UnixEpoch, []);
            return new Result(scene, version, layout);
        }

        static List<SceneTrack> Parse(byte[] bytes, short version, TailLayout layout)
        {
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);
            reader.ReadInt16(); // the version

            List<SceneTrack> tracks = [];
            uint nextId = 1;

            int aircraftCount = ReadCount(reader, "aircraft");
            for (int i = 0; i < aircraftCount; i++)
            {
                bool plane = reader.ReadBoolean();
                string callsign = reader.ReadString();
                string nickname = reader.ReadString();
                tracks.Add(ReadObject(reader, version, layout, nextId++, isAircraft: true, plane, callsign, nickname));
            }

            if (stream.Position < stream.Length)
            {
                int objectCount = ReadCount(reader, "object");
                for (int i = 0; i < objectCount; i++)
                {
                    tracks.Add(ReadObject(reader, version, layout, nextId++, isAircraft: false, false, "", ""));
                }
            }

            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException((stream.Length - stream.Position) + " bytes are left after the last object");
            }
            return tracks;
        }

        static int ReadCount(BinaryReader reader, string what)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > reader.BaseStream.Length - reader.BaseStream.Position)
            {
                throw new InvalidDataException("the " + what + " count " + count + " cannot be right");
            }
            return count;
        }

        static SceneTrack ReadObject(BinaryReader reader, short version, TailLayout layout, uint id, bool isAircraft, bool plane, string callsign, string nickname)
        {
            string model = reader.ReadString();
            byte typeRole = reader.ReadByte();

            int frameCount = ReadCount(reader, "frame");
            List<SceneFrame> frames = new(frameCount);
            for (int i = 0; i < frameCount; i++)
            {
                frames.Add(ReadFrame(reader, version));
            }

            string livery = "", icaoType = "", icaoAirline = "";
            if (layout == TailLayout.Fs2024)
            {
                if (version >= 21004) livery = reader.ReadString();
                if (version >= 21005) icaoType = reader.ReadString();
                if (version >= 21005) icaoAirline = reader.ReadString();
            }
            else if (version >= 21004)
            {
                icaoType = reader.ReadString();
                icaoAirline = reader.ReadString();
            }

            // the old player sorted the frames of an object when it stopped, so files hold some out of order
            List<SceneFrame> ordered = [.. frames.OrderBy(frame => frame.Time)];
            var track = new SceneTrack
            {
                Id = id,
                Nickname = nickname,
                Identity = new IdentityUpdate
                {
                    ObjectId = id,
                    IsAircraft = isAircraft,
                    IsPlane = isAircraft && plane,
                    Callsign = callsign,
                    Model = model,
                    Livery = livery,
                    IcaoType = icaoType,
                    IcaoAirline = icaoAirline,
                    Registration = "",
                    FlightNumber = "",
                    ClassCode = "",
                    Wtc = "",
                    TypeRole = typeRole,
                },
            };
            track.Frames.AddRange(ordered);
            return track;
        }

        static SceneFrame ReadFrame(BinaryReader reader, short version)
        {
            var kind = (FrameKind)reader.ReadByte();
            double time = reader.ReadDouble();
            if (double.IsNaN(time) || double.IsInfinity(time) || Math.Abs(time) > 1e7)
            {
                throw new InvalidDataException("a frame time of " + time + " cannot be right");
            }

            switch (kind)
            {
                case FrameKind.AircraftPosition:
                    return new ScenePositionFrame(time, ReadAircraftPosition(reader, version));
                case FrameKind.ObjectPosition:
                    return new SceneObjectPositionFrame(time, ReadObjectPosition(reader, version));
                case FrameKind.SimEvent:
                    return new SceneEventFrame(time, reader.ReadUInt32(), reader.ReadUInt32());
                case FrameKind.IntegerVariables:
                {
                    ushort count = reader.ReadUInt16();
                    List<VariableEntry> entries = new(count);
                    for (int i = 0; i < count; i++)
                    {
                        entries.Add(new VariableEntry { Vuid = reader.ReadUInt32(), Kind = VariableKind.Int32, IntValue = reader.ReadInt32() });
                    }
                    return new SceneVariablesFrame(time, new VariableSyncUpdate { Entries = entries });
                }
                case FrameKind.FloatVariables:
                {
                    ushort count = reader.ReadUInt16();
                    List<VariableEntry> entries = new(count);
                    for (int i = 0; i < count; i++)
                    {
                        entries.Add(new VariableEntry { Vuid = reader.ReadUInt32(), Kind = VariableKind.Float32, FloatValue = reader.ReadSingle() });
                    }
                    return new SceneVariablesFrame(time, new VariableSyncUpdate { Entries = entries });
                }
                case FrameKind.String8Variables:
                {
                    ushort count = reader.ReadUInt16();
                    List<VariableEntry> entries = new(count);
                    for (int i = 0; i < count; i++)
                    {
                        entries.Add(new VariableEntry { Vuid = reader.ReadUInt32(), Kind = VariableKind.String8, StringValue = reader.ReadString() });
                    }
                    return new SceneVariablesFrame(time, new VariableSyncUpdate { Entries = entries });
                }
                default:
                    // the eight reserved types (aircraft state, engines, fuel, ...) have no length and no known layout
                    throw new InvalidDataException("frame type " + (byte)kind + " has no known layout");
            }
        }

        static PositionUpdate ReadAircraftPosition(BinaryReader reader, short version)
        {
            var p = new PositionUpdate
            {
                Latitude = reader.ReadDouble(),
                Longitude = reader.ReadDouble(),
                Altitude = reader.ReadDouble(),
                Pitch = reader.ReadSingle(),
                Bank = reader.ReadSingle(),
                Heading = reader.ReadSingle(),
                VelocityX = reader.ReadSingle(),
                VelocityY = reader.ReadSingle(),
                VelocityZ = reader.ReadSingle(),
                AngularVelocityX = reader.ReadSingle(),
                AngularVelocityY = reader.ReadSingle(),
                AngularVelocityZ = reader.ReadSingle(),
                AccelerationX = reader.ReadSingle(),
                AccelerationY = reader.ReadSingle(),
                AccelerationZ = reader.ReadSingle(),
                Rudder = reader.ReadInt16() * AxisScale,
                Elevator = reader.ReadInt16() * AxisScale,
                Aileron = reader.ReadInt16() * AxisScale,
                BrakeLeft = reader.ReadInt16() * AxisScale,
                BrakeRight = reader.ReadInt16() * AxisScale,
                // "STATIC CG TO GROUND" was never written (the legacy format is frozen); NaN is "no data"
                StaticCgToGround = float.NaN,
            };
            CheckPosition(p.Latitude, p.Longitude, p.Altitude);
            if (version >= 10023)
            {
                p.Elevation = reader.ReadSingle();
                p.StateFlags = Flags(reader.ReadByte());
            }
            return p;
        }

        static ObjectPositionUpdate ReadObjectPosition(BinaryReader reader, short version)
        {
            var p = new ObjectPositionUpdate
            {
                Latitude = reader.ReadDouble(),
                Longitude = reader.ReadDouble(),
                Altitude = reader.ReadDouble(),
                Pitch = reader.ReadSingle(),
                Bank = reader.ReadSingle(),
                Heading = reader.ReadSingle(),
                VelocityX = reader.ReadSingle(),
                VelocityY = reader.ReadSingle(),
                VelocityZ = reader.ReadSingle(),
                AngularVelocityX = reader.ReadSingle(),
                AngularVelocityY = reader.ReadSingle(),
                AngularVelocityZ = reader.ReadSingle(),
                AccelerationX = reader.ReadSingle(),
                AccelerationY = reader.ReadSingle(),
                AccelerationZ = reader.ReadSingle(),
            };
            CheckPosition(p.Latitude, p.Longitude, p.Altitude);
            if (version >= 10023)
            {
                p.Height = reader.ReadSingle();
                p.StateFlags = Flags(reader.ReadByte());
            }
            return p;
        }

        static PositionStateFlags Flags(byte flags)
        {
            PositionStateFlags state = PositionStateFlags.None;
            if ((flags & 0x01) != 0) state |= PositionStateFlags.OnGround;
            if ((flags & 0x02) != 0) state |= PositionStateFlags.ElevationCorrection;
            return state;
        }

        /// <summary>A wrongly aligned read finds nonsense here almost at once; the angles are radians.</summary>
        static void CheckPosition(double latitude, double longitude, double altitude)
        {
            if (!(Math.Abs(latitude) <= 2 * Math.PI) || !(Math.Abs(longitude) <= 2 * Math.PI) || !(Math.Abs(altitude) <= 1e9))
            {
                throw new InvalidDataException("a position of " + latitude + ", " + longitude + ", " + altitude + " cannot be right");
            }
        }
    }
}
