using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
#if !CONSOLE
using System.Windows.Forms;
#endif
using JoinFS.Properties;


namespace JoinFS
{
    /// <summary>
    /// A recording
    /// </summary>
    public class Recorder
    {
        /// <summary>
        /// Reference to the main form
        /// </summary>
        Main main;

        /// <summary>
        /// Constructor
        /// </summary>
        public Recorder(Main main)
        {
            // set main form
            this.main = main;

            // read versions
            readVersions = new Dictionary<short, Sim.ReadVersion>()
            {
                { 10022, Read1 },
            };
        }

        /// <summary>
        /// Type of a frame
        /// </summary>
        public enum FrameType
        {
            ObjectPosition,
            AircraftPosition,
            PlaneState,
            HelicopterState,
            AircraftState,
            PistonEngineState,
            TurbineEngineState,
            ObjectSmoke,
            AircraftFuel,
            AircraftPayload,
            SimEvent,
            IntegerVariables,
            FloatVariables,
            String8Variables,
        }

        /// <summary>
        /// Recorded frame
        /// </summary>
        public class Frame
        {
            /// <summary>
            /// Type of frame
            /// </summary>
            public FrameType type;
            /// <summary>
            /// Recording time
            /// </summary>
            public double time;

            /// <summary>
            /// Constructor
            /// </summary>
            public Frame()
            {
                // versions
                readVersions = new Dictionary<short,Sim.ReadVersion>()
                {
                    { 10022, Read1 },
                };
            }

            /// <summary>
            /// Write frame to stream
            /// </summary>
            /// <param name="writer">Stream</param>
            public virtual void Write(BinaryWriter writer)
            {
                // write type
                writer.Write((byte)type);
                // write time
                writer.Write(time);
            }

            /// <summary>
            /// Read frame from a stream VERSION 1
            /// </summary>
            /// <param name="reader">Stream</param>
            public virtual void Read1(short version, BinaryReader reader)
            {
                // read type
                type = (FrameType)reader.ReadByte();
                // read time
                time = reader.ReadDouble();
            }

            /// <summary>
            /// Version table for reading data
            /// </summary>
            protected Dictionary<short, Sim.ReadVersion> readVersions;

            /// <summary>
            /// Read data
            /// </summary>
            /// <param name="version">Version to read</param>
            /// <param name="reader">Reader</param>
            public void Read(short version, BinaryReader reader)
            {
                // read correct version
                Sim.Read(version, readVersions, reader);
            }
        }

        /// <summary>
        /// A single object position frame for a recorded aircraft
        /// </summary>
        public class ObjectPositionFrame : Frame
        {
            /// <summary>
            ///  Object position velocity
            /// </summary>
            public Sim.ObjectPositionVelocity data;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="time">Record time</param>
            /// <param name="netTime">Network time</param>
            /// <param name="positionVelocity">Position and Velocity</param>
            public ObjectPositionFrame(double time, ref Sim.ObjectPositionVelocity data)
            {
                // set data
                base.type = FrameType.ObjectPosition;
                base.time = time;
                this.data = data;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public ObjectPositionFrame()
            {
                // versions
                readVersions = new Dictionary<short, Sim.ReadVersion>()
                {
                    { 10022, Read1 },
                };
            }

            /// <summary>
            /// Write position and velocity to a stream
            /// </summary>
            public override void Write(BinaryWriter writer)
            {
                // write frame
                base.Write(writer);
                // write position velocity
                Sim.Write(writer, ref data);
            }

            /// <summary>
            /// Read position and velocity from a stream
            /// </summary>
            /// <param name="reader">Stream</param>
            public override void Read1(short version, BinaryReader reader)
            {
                // read position velocity
                Sim.Read(version, reader, ref data);
            }
        }

        /// <summary>
        /// A single aircraft position frame for a recorded aircraft
        /// </summary>
        public class AircraftPositionFrame : Frame
        {
            /// <summary>
            ///  Aircraft position
            /// </summary>
            public Sim.AircraftPosition data;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="time">Record time</param>
            /// <param name="netTime">Network time</param>
            /// <param name="positionVelocity">Position and Velocity</param>
            public AircraftPositionFrame(double time, ref Sim.AircraftPosition data)
            {
                // set data
                base.type = FrameType.AircraftPosition;
                base.time = time;
                this.data = data;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public AircraftPositionFrame()
            {
                // versions
                readVersions = new Dictionary<short, Sim.ReadVersion>()
                {
                    { 10022, Read1 },
                };
            }

            /// <summary>
            /// Write position and velocity to a stream
            /// </summary>
            public override void Write(BinaryWriter writer)
            {
                // write frame
                base.Write(writer);
                // write position velocity
                Sim.Write(writer, ref data);
            }

            /// <summary>
            /// Read position and velocity from a stream
            /// </summary>
            /// <param name="reader">Stream</param>
            public override void Read1(short version, BinaryReader reader)
            {
                // read position velocity
                Sim.Read(version, reader, ref data);
            }
        }

        /// <summary>
        /// A single event frame for a recorded object
        /// </summary>
        public class SimEventFrame : Frame
        {
            /// <summary>
            /// Aircraft data
            /// </summary>
            public uint eventId;
            public uint data;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="time">Record time</param>
            /// <param name="eventId">Event ID</param>
            /// <param name="data">Data</param>
            public SimEventFrame(double time, uint eventId, uint data)
            {
                // set data
                base.type = FrameType.SimEvent;
                base.time = time;
                this.eventId = eventId;
                this.data = data;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public SimEventFrame()
            {
                // versions
                readVersions = new Dictionary<short, Sim.ReadVersion>()
                {
                    { 10022, Read1 },
                };
            }

            /// <summary>
            /// Write recorded data to a stream
            /// </summary>
            /// <param name="writer">Stream</param>
            public override void Write(BinaryWriter writer)
            {
                // write frame
                base.Write(writer);
                // write event ID
                writer.Write(eventId);
                // write data
                writer.Write(data);
            }

            /// <summary>
            /// Read recorded data from a stream
            /// </summary>
            /// <param name="reader">Stream</param>
            public override void Read1(short version, BinaryReader reader)
            {
                // read event
                eventId = reader.ReadUInt32();
                // read data
                data = reader.ReadUInt32();
            }
        }

        /// <summary>
        /// A single variable frame for a recorded object
        /// </summary>
        public class IntegerVariablesFrame : Frame
        {
            /// <summary>
            /// Variables data
            /// </summary>
            public Dictionary<uint, int> variables;

            /// <summary>
            /// Constructor
            /// </summary>
            public IntegerVariablesFrame(double time, Dictionary<uint, int> variables)
            {
                // set data
                type = FrameType.IntegerVariables;
                base.time = time;
                // new variables
                this.variables = new Dictionary<uint, int>();
                // for each variable
                foreach (var variable in variables)
                {
                    // add variable
                    this.variables.Add(variable.Key, variable.Value);
                }
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public IntegerVariablesFrame()
            {
                // versions
                readVersions = new Dictionary<short, Sim.ReadVersion>()
                {
                    { 10022, Read1 },
                };
                // new variables
                variables = new Dictionary<uint, int>();
            }

            /// <summary>
            /// Write recorded data to a stream
            /// </summary>
            /// <param name="writer">Stream</param>
            public override void Write(BinaryWriter writer)
            {
                // write frame
                base.Write(writer);
                // write variables
                Sim.Write(writer, variables);
            }

            /// <summary>
            /// Read recorded data from a stream
            /// </summary>
            /// <param name="reader">Stream</param>
            public override void Read1(short version, BinaryReader reader)
            {
                // clear variables
                variables.Clear();
                // read variables
                Sim.Read(version, reader, variables);
            }
        }

        /// <summary>
        /// A single variable frame for a recorded object
        /// </summary>
        public class FloatVariablesFrame : Frame
        {
            /// <summary>
            /// Variables data
            /// </summary>
            public Dictionary<uint, float> variables;

            /// <summary>
            /// Constructor
            /// </summary>
            public FloatVariablesFrame(double time, Dictionary<uint, float> variables)
            {
                // set data
                type = FrameType.FloatVariables;
                base.time = time;
                // new variables
                this.variables = new Dictionary<uint, float>();
                // for each variable
                foreach (var variable in variables)
                {
                    // add variable
                    this.variables.Add(variable.Key, variable.Value);
                }
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public FloatVariablesFrame()
            {
                // versions
                readVersions = new Dictionary<short, Sim.ReadVersion>()
                {
                    { 10022, Read1 },
                };
                // new variables
                variables = new Dictionary<uint, float>();
            }

            /// <summary>
            /// Write recorded data to a stream
            /// </summary>
            /// <param name="writer">Stream</param>
            public override void Write(BinaryWriter writer)
            {
                // write frame
                base.Write(writer);
                // write variables
                Sim.Write(writer, variables);
            }

            /// <summary>
            /// Read recorded data from a stream
            /// </summary>
            /// <param name="reader">Stream</param>
            public override void Read1(short version, BinaryReader reader)
            {
                // clear variables
                variables.Clear();
                // read variables
                Sim.Read(version, reader, variables);
            }
        }

        /// <summary>
        /// A single variable frame for a recorded object
        /// </summary>
        public class String8VariablesFrame : Frame
        {
            /// <summary>
            /// Variables data
            /// </summary>
            public Dictionary<uint, string> variables;

            /// <summary>
            /// Constructor
            /// </summary>
            public String8VariablesFrame(double time, Dictionary<uint, string> variables)
            {
                // set data
                type = FrameType.String8Variables;
                base.time = time;
                // new variables
                this.variables = new Dictionary<uint, string>();
                // for each variable
                foreach (var variable in variables)
                {
                    // add variable
                    this.variables.Add(variable.Key, variable.Value);
                }
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public String8VariablesFrame()
            {
                // versions
                readVersions = new Dictionary<short, Sim.ReadVersion>()
                {
                    { 10022, Read1 },
                };
                // new variables
                variables = new Dictionary<uint, string>();
            }

            /// <summary>
            /// Write recorded data to a stream
            /// </summary>
            /// <param name="writer">Stream</param>
            public override void Write(BinaryWriter writer)
            {
                // write frame
                base.Write(writer);
                // write variables
                Sim.Write(writer, variables);
            }

            /// <summary>
            /// Read recorded data from a stream
            /// </summary>
            /// <param name="reader">Stream</param>
            public override void Read1(short version, BinaryReader reader)
            {
                // clear variables
                variables.Clear();
                // read variables
                Sim.Read(version, reader, variables);
            }
        }

        /// <summary>
        /// Recorded object
        /// </summary>
        public class Obj
        {
            /// <summary>
            /// ID for the recorded object
            /// </summary>
            public uint id = 0;
            /// <summary>
            /// Aircraft model
            /// </summary>
            public string model = "";
#if FS2024
            /// <summary>
            /// Aircraft variation
            /// </summary>
            public string livery = "";
#endif
            /// <summary>
            /// Aircraft type role
            /// </summary>
            public int typerole = Substitution.TypeRole_SingleProp;
            /// <summary>
            /// List of frames
            /// </summary>
            public List<Frame> frames = new List<Frame>();
            public int frameIndex = 0;
            /// <summary>
            /// Object owner
            /// </summary>
            public Sim.Obj.Owner owner = Sim.Obj.Owner.Me;
            /// <summary>
            /// Object is currently being played
            /// </summary>
            public bool playing = false;
            /// <summary>
            /// Offset between recorder time and object time
            /// </summary>
            public double timeOffset = 0.0;
            /// <summary>
            /// Is the time offset valid
            /// </summary>
            public bool timeOffsetValid = false;

            /// <summary>
            /// Next ID
            /// </summary>
            static uint nextId = 10000;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="model">Object model</param>
#if FS2024
            public Obj(string model, string livery, int typerole, Sim.Obj.Owner owner)
#else
            public Obj(string model, int typerole, Sim.Obj.Owner owner)
#endif
            {
                // ID
                this.id = nextId++;
                this.model = model;
                this.typerole = typerole;
                this.owner = owner;
#if FS2024
                this.livery = livery;
#endif

                // versions
                readVersions = new Dictionary<short, Sim.ReadVersion>()
                {
                    { 10022, Read1 },
                };
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Obj()
            {
                // ID
                this.id = nextId++;
                this.owner = Sim.Obj.Owner.Recorder;

                // versions
                readVersions = new Dictionary<short, Sim.ReadVersion>()
                {
                    { 10022, Read1 },
                };
            }

            /// <summary>
            /// Start playing this object
            /// </summary>
            public void Play()
            {
                // start at beginning
                frameIndex = 0;
                // start playing
                playing = true;
            }

            /// <summary>
            /// Stop this object playing
            /// </summary>
            public void StopPlaying()
            {
                // stop playing
                playing = false;
                // sort frames
                frames.Sort(delegate(Frame f1, Frame f2) { return f1.time.CompareTo(f2.time); });
            }

            public virtual void Write(BinaryWriter writer)
            {
                // write model
                writer.Write(model);
                // write typerole
                writer.Write((byte)typerole);

                // write frame count
                writer.Write(frames.Count);
                // for each position and velocity
                foreach (var frame in frames)
                {
                    // write position and velocity
                    frame.Write(writer);
                }
#if FS2024
                writer.Write(livery);
#endif
            }

            /// <summary>
            /// Read aircraft from a stream VERSION 1
            /// </summary>
            /// <param name="reader">Stream</param>
            public void Read1(short version, BinaryReader reader)
            {
                // counter
                int n, count;
                // read model
                model = reader.ReadString();
                // read typerole
                typerole = reader.ReadByte();
                // read frame count
                count = reader.ReadInt32();
                for (n = 0; n < count; n++)
                {
                    // read generic frame
                    Frame baseFrame = new Frame();
                    baseFrame.Read(version, reader);

                    // check frame type
                    Frame frame = null;
                    switch (baseFrame.type)
                    {
                        case FrameType.ObjectPosition:
                            // read position velocity frame
                            frame = new ObjectPositionFrame();
                            break;
                    }

                    // check for valid frame
                    if (frame != null)
                    {
                        // copy base frame
                        frame.type = baseFrame.type;
                        frame.time = baseFrame.time;
                        // read frame specific data
                        frame.Read(version, reader);
                        // add to list
                        frames.Add(frame);
                    }
                }
#if FS2024
                livery = (version >= 21004) ? reader.ReadString() : "";
#endif
            }

            /// <summary>
            /// Version table for reading data
            /// </summary>
            protected Dictionary<short, Sim.ReadVersion> readVersions;

            /// <summary>
            /// Read data
            /// </summary>
            /// <param name="version">Version to read</param>
            /// <param name="reader">Reader</param>
            public void Read(short version, BinaryReader reader)
            {
                // read correct version
                Sim.Read(version, readVersions, reader);
            }
        }

        /// <summary>
        /// Recorded aircraft
        /// </summary>
        public class Aircraft : Obj
        {
            /// <summary>
            /// Is this object a plane
            /// </summary>
            public bool plane = true;
            /// <summary>
            /// Aircraft callsign
            /// </summary>
            public string callsign = "";
            /// <summary>
            /// Aircraft nickname
            /// </summary>
            public string nickname = "";

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="plane">Aircraft is a plane</param>
            /// <param name="callsign">Aircraft callsign</param>
            /// <param name="model">Aircraft model</param>
#if FS2024
            public Aircraft(bool plane, string callsign, string nickname, string model, string livery, int typerole, Sim.Obj.Owner owner) : base(model, livery, typerole, owner)
#else
            public Aircraft(bool plane, string callsign, string nickname, string model, int typerole, Sim.Obj.Owner owner) : base(model, typerole, owner)
#endif
            {
                this.plane = plane;
                this.callsign = callsign;
                this.nickname = nickname;
#if FS2024
                this.livery = livery;
#endif

                // versions
                readVersions = new Dictionary<short, Sim.ReadVersion>()
                {
                    { 10022, Read1 },
                };
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Aircraft()
            {
                // versions
                readVersions = new Dictionary<short, Sim.ReadVersion>()
                {
                    { 10022, Read1 },
                };
            }

            public override void Write(BinaryWriter writer)
            {
                // write plane
                writer.Write(plane);
                // write callsign
                writer.Write(callsign);
                // write nickname
                writer.Write(nickname);
                // write object
                base.Write(writer);
            }

            /// <summary>
            /// Read aircraft from a stream VERSION 1
            /// </summary>
            /// <param name="reader">Stream</param>
            public new void Read1(short version, BinaryReader reader)
            {
                // counter
                int n, count;
                // read plane flag
                plane = reader.ReadBoolean();
                // read callsign
                callsign = reader.ReadString();
                // read nickname
                nickname = reader.ReadString();
                // read model
                model = reader.ReadString();
                // read typerole
                typerole = reader.ReadByte();
                // read frame count
                count = reader.ReadInt32();
                for (n = 0; n < count; n++)
                {
                    // read generic frame
                    Frame baseFrame = new Frame();
                    baseFrame.Read(version, reader);

                    // check frame type
                    Frame frame = null;
                    switch (baseFrame.type)
                    {
                        case FrameType.ObjectPosition:
                            frame = new ObjectPositionFrame();
                            break;
                        case FrameType.AircraftPosition:
                            frame = new AircraftPositionFrame();
                            break;
                        case FrameType.SimEvent:
                            frame = new SimEventFrame();
                            break;
                        case FrameType.IntegerVariables:
                            frame = new IntegerVariablesFrame();
                            break;
                        case FrameType.FloatVariables:
                            frame = new FloatVariablesFrame();
                            break;
                        case FrameType.String8Variables:
                            frame = new String8VariablesFrame();
                            break;
                    }

                    // check for valid frame
                    if (frame != null)
                    {
                        // copy base frame
                        frame.type = baseFrame.type;
                        frame.time = baseFrame.time;
                        // read frame specific data
                        frame.Read(version, reader);
                        // add to list
                        frames.Add(frame);
                    }
                }
#if FS2024
                livery = (version >= 21004) ? reader.ReadString() : "";
#endif
            }
        }

        /// <summary>
        /// List of recorded objects
        /// </summary>
        List<Obj> objList = new List<Obj>();

        /// <summary>
        /// Is the recorder currently recording
        /// </summary>
        public bool recording;

        /// <summary>
        /// Is the recorder currently playing
        /// </summary>
        public bool playing;

        /// <summary>
        /// Is the recorder recording or playing
        /// </summary>
        public bool Active { get { return recording || playing; } }

        /// <summary>
        /// Is the recorder empty
        /// </summary>
        public bool Empty { get { return (objList.Count == 0) ? true : false; } }

        /// <summary>
        /// Time that playing or recording started
        /// </summary>
        double startTime;

        /// <summary>
        /// Current time since start
        /// </summary>
        public double Time { get { return paused ? pauseTime : (Active ? main.ElapsedTime - startTime : 0.0); } }

        /// <summary>
        /// End time of the recording
        /// </summary>
        public double EndTime
        {
            get
            {
                // latest time
                double latest = 0.0;
                // for each object
                foreach (var obj in objList)
                {
                    // check for data
                    if (obj.frames.Count > 0)
                    {
                        // check if last frame time is later than latest
                        if (obj.frames[obj.frames.Count - 1].time > latest)
                        {
                            // update latest
                            latest = obj.frames[obj.frames.Count - 1].time;
                        }
                    }
                }

                // return latest time
                return latest;
            }
        }

        /// <summary>
        /// Record position velocity
        /// </summary>
        /// <param name="time">Record time</param>
        /// <param name="sampleTime">Time of position</param>
        /// <param name="positionVelocity">Position and Velocity</param>
        public void Record(Obj obj, double objTime, ref Sim.ObjectPositionVelocity positionVelocity)
        {
            // check object
            if (obj != null && obj.playing == false)
            {
                // check for invalid time offset
                if (obj.timeOffsetValid == false)
                {
                    // intialize time offset
                    obj.timeOffset = Time - objTime;
                    // time offset is now valid
                    obj.timeOffsetValid = true;
                }

                // add position velocity frame
                obj.frames.Add(new ObjectPositionFrame(objTime + obj.timeOffset, ref positionVelocity));
            }
        }

        /// <summary>
        /// Record aircraft position
        /// </summary>
        /// <param name="time">Record time</param>
        /// <param name="sampleTime">Time of position</param>
        /// <param name="positionVelocity">Position and Velocity</param>
        public void Record(Obj obj, double objTime, ref Sim.AircraftPosition aircraftPosition)
        {
            // check object
            if (obj != null && obj.playing == false)
            {
                // check for invalid time offset
                if (obj.timeOffsetValid == false)
                {
                    // intialize time offset
                    obj.timeOffset = Time - objTime;
                    // time offset is now valid
                    obj.timeOffsetValid = true;
                }

                // add position velocity frame
                obj.frames.Add(new AircraftPositionFrame(objTime + obj.timeOffset, ref aircraftPosition));
            }
        }

        /// <summary>
        /// Record sim event
        /// </summary>
        public void Record(Obj obj, uint eventId, uint data)
        {
            // check object
            if (obj != null && obj.playing == false)
            {
                // add event
                obj.frames.Add(new SimEventFrame(Time, eventId, data));
            }
        }

        /// <summary>
        /// Record integer variables
        /// </summary>
        public void Record(Obj obj, Dictionary<uint, int> variables)
        {
            // check object
            if (obj != null && obj.playing == false)
            {
                // add event
                obj.frames.Add(new IntegerVariablesFrame(Time, variables));
            }
        }

        /// <summary>
        /// Record float variables
        /// </summary>
        public void Record(Obj obj, Dictionary<uint, float> variables)
        {
            // check object
            if (obj != null && obj.playing == false)
            {
                // add event
                obj.frames.Add(new FloatVariablesFrame(Time, variables));
            }
        }

        /// <summary>
        /// Record string8 variables
        /// </summary>
        public void Record(Obj obj, Dictionary<uint, string> variables)
        {
            // check object
            if (obj != null && obj.playing == false)
            {
                // add event
                obj.frames.Add(new String8VariablesFrame(Time, variables));
            }
        }

        /// <summary>
        /// Start recording
        /// </summary>
        public void StartRecord(bool overdub)
        {
            // should not be recording
            if (main.sim != null && recording == false && (playing == false || overdub))
            {
                // check for overdub
                if (overdub == false)
                {
                    // clear aircraft
                    objList.Clear();
                }

                // for each object in the sim
                foreach (var simObject in main.sim.objectList)
                {
                    // check if object is flagged to record
                    if (simObject.record)
                    {
                        // check for aircraft
                        if (simObject is Sim.Aircraft)
                        {
                            // plane
                            Sim.Aircraft simAircraft = simObject as Sim.Aircraft;
                            // create recorded aircraft
#if FS2024
                            simObject.recorderObj = new Aircraft(simAircraft is Sim.Plane, simAircraft.flightPlan.callsign, main.network.GetNodeName(simAircraft.ownerNuid), simAircraft.ownerModel, simAircraft.ownerLivery, simAircraft.typerole, simAircraft.owner);
#else
                            simObject.recorderObj = new Aircraft(simAircraft is Sim.Plane, simAircraft.flightPlan.callsign, main.network.GetNodeName(simAircraft.ownerNuid), simAircraft.ownerModel, simAircraft.typerole, simAircraft.owner);
#endif
                        }
                        else
                        {
                            // create recorded object
#if FS2024
                            simObject.recorderObj = new Obj(simObject.ownerModel, simObject.ownerLivery, simObject.typerole, simObject.owner);
#else
                            simObject.recorderObj = new Obj(simObject.ownerModel, simObject.typerole, simObject.owner);
#endif
                        }
                        // add to list
                        objList.Add(simObject.recorderObj);
                    }
                }

                // check for overdub
                if (overdub == false)
                {
                    // set start time
                    startTime = main.ElapsedTime;
                }
                // now recording
                recording = true;
            }
        }

        /// <summary>
        /// Start playing
        /// </summary>
        public void StartPlay()
        {
            // should not be active
            if (Active == false && Empty == false)
            {
                // for each object
                foreach (var obj in objList)
                {
                    // play object
                    obj.Play();
                }
                // set start time
                startTime = main.ElapsedTime;
                // now playing
                playing = true;
            }
        }

        /// <summary>
        /// Stop the recorder
        /// </summary>
        public void Stop()
        {
            // for each object
            foreach (var obj in objList)
            {
                // stop object
                obj.StopPlaying();

                // remove recorded object from sim
                main.sim ?. RemoveObject(new LocalNode.Nuid(), obj.id);
            }

            // check state
            if (main.sim != null && recording)
            {
                // for each object in the sim
                foreach (var simObject in main.sim.objectList)
                {
                    // object is no longer being recorded
                    simObject.recorderObj = null;
                }
            }

            // no longer active
            recording = false;
            playing = false;
            paused = false;
        }


        /// <summary>
        /// Does object exist
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public bool Exists(uint id)
        {
            return objList.Exists(o => o.id == id);
        }

        /// <summary>
        /// Remove object from the recorder
        /// </summary>
        /// <param name="id"></param>
        public void Remove(uint id)
        {
            // get object
            Obj obj = objList.Find(o => o.id == id);

            // check for valid object
            if (obj != null)
            {
                // stop object
                obj.StopPlaying();

                // check for sim
                if (main.sim != null)
                {
                    // remove recorded object from sim
                    main.sim.RemoveObject(new LocalNode.Nuid(), obj.id);

                    // for each object in the sim
                    foreach (var simObject in main.sim.objectList)
                    {
                        // check for link
                        if (simObject.recorderObj == obj)
                        {
                            // object is no longer being recorded
                            simObject.recorderObj = null;
                        }
                    }
                }

                // remove from list
                objList.Remove(obj);
            }
        }

        /// <summary>
        /// Is the recorder currently paused
        /// </summary>
        public bool paused;

        /// <summary>
        /// Time at which paused occurred
        /// </summary>
        public double pauseTime;

        /// <summary>
        /// Toggle pause state
        /// </summary>
        public void Pause()
        {
            // check if paused
            if (paused)
            {
                // for each object
                foreach (var obj in objList)
                {
                    // check if object is playing
                    if (obj.playing)
                    {
                        // update pause state
                        main.sim ?. PauseObject(new LocalNode.Nuid(), obj.id, false);
                    }
                }

                // no longer paused
                paused = false;
                // jump to pause time
                Jump(pauseTime);
            }
            else if (playing)
            {
                // save pause time
                pauseTime = Time;
                // now paused
                paused = true;

                // for each object
                foreach (var obj in objList)
                {
                    // check if object is playing
                    if (obj.playing)
                    {
                        // update pause state
                        main.sim ?. PauseObject(new LocalNode.Nuid(), obj.id, true);
                    }
                }
            }
        }

        /// <summary>
        /// Jump to a point in the recording
        /// </summary>
        /// <param name="time">Time to jump to</param>
        public void Jump(double time)
        {
            // check if paused
            if (paused)
            {
                // update pause time
                pauseTime = time;
            }
            else
            {
                // update start time
                startTime = main.ElapsedTime - time;
            }

            // for each object
            foreach (var obj in objList)
            {
                // check if object is playing
                if (obj.playing)
                {
                    // recent position frame
                    Frame recentFrame = null;
                    // reset frame index
                    obj.frameIndex = 0;
                    // while not at right frame
                    while (obj.frameIndex < obj.frames.Count && time > obj.frames[obj.frameIndex].time)
                    {
                        // check for position frame
                        if (obj.frames[obj.frameIndex].type == FrameType.ObjectPosition || obj.frames[obj.frameIndex].type == FrameType.AircraftPosition)
                        {
                            // update recent frame
                            recentFrame = obj.frames[obj.frameIndex];
                        }
                        // next frame
                        obj.frameIndex++;
                    }

                    // check for recent position frame
                    if (recentFrame != null)
                    {
                        // check for aircraft
                        if (obj is Aircraft)
                        {
                            // aircraft
                            Aircraft aircraft = obj as Aircraft;
                            // update position
#if FS2024
                            main.sim ?. UpdateAircraft(new LocalNode.Nuid(), obj.id, false, aircraft.plane, aircraft.callsign, aircraft.nickname, aircraft.model, aircraft.livery, aircraft.typerole, recentFrame.time, ref (recentFrame as AircraftPositionFrame).data);
#else
                            main.sim ?. UpdateAircraft(new LocalNode.Nuid(), obj.id, false, aircraft.plane, aircraft.callsign, aircraft.nickname, aircraft.model, aircraft.typerole, recentFrame.time, ref (recentFrame as AircraftPositionFrame).data);
#endif
                        }
                        else
                        {
                            // update position
#if FS2024
                            main.sim?.UpdateObject(new LocalNode.Nuid(), obj.id, obj.model, obj.livery, obj.typerole, recentFrame.time, ref (recentFrame as ObjectPositionFrame).data);
#else
                            main.sim ?. UpdateObject(new LocalNode.Nuid(), obj.id, obj.model, obj.typerole, recentFrame.time, ref (recentFrame as ObjectPositionFrame).data);
#endif
                        }
                        // reset object
                        main.sim ?. ResetObject(new LocalNode.Nuid(), obj.id);
                    }
                }
            }
        }

        /// <summary>
        /// Do work
        /// </summary>
        public void DoWork()
        {
            // check if playing
            if (playing)
            {
                // for each object
                foreach (var obj in objList)
                {
                    // check if aircraft is playing
                    if (obj.playing)
                    {
                        // check if paused
                        if (paused)
                        {
                            // touch object
                            main.sim ?. TouchObject(new LocalNode.Nuid(), obj.id);
                        }
                        // check if position velocity index is valid
                        else if (obj.frameIndex < obj.frames.Count)
                        {
                            // get frame
                            Frame frame = obj.frames[obj.frameIndex];
                            // check if time has passed the current frame
                            if (Time > frame.time)
                            {
                                // check frame type
                                switch (frame.type)
                                {
                                    case FrameType.ObjectPosition:
                                        {
                                            // update position
#if FS2024
                                            main.sim ?. UpdateObject(new LocalNode.Nuid(), obj.id, obj.model, obj.livery, obj.typerole, frame.time, ref (frame as ObjectPositionFrame).data);
#else
                                            main.sim ?. UpdateObject(new LocalNode.Nuid(), obj.id, obj.model, obj.typerole, frame.time, ref (frame as ObjectPositionFrame).data);
#endif
                                        }
                                        break;
                                    case FrameType.AircraftPosition:
                                        {
                                            // aircraft
                                            Aircraft aircraft = obj as Aircraft;
                                            // position
#if FS2024
                                            main.sim ?. UpdateAircraft(new LocalNode.Nuid(), obj.id, false, aircraft.plane, aircraft.callsign, aircraft.nickname, aircraft.model, aircraft.livery, aircraft.typerole, frame.time, ref (frame as AircraftPositionFrame).data);
#else
                                            main.sim ?. UpdateAircraft(new LocalNode.Nuid(), obj.id, false, aircraft.plane, aircraft.callsign, aircraft.nickname, aircraft.model, aircraft.typerole, frame.time, ref (frame as AircraftPositionFrame).data);
#endif
                                        }
                                        break;
                                    case FrameType.SimEvent:
                                        main.sim ?. UpdateAircraft(new LocalNode.Nuid(), obj.id, (frame as SimEventFrame).eventId, (frame as SimEventFrame).data, true);
                                        break;
                                    case FrameType.IntegerVariables:
                                        main.sim ?. UpdateAircraft(new LocalNode.Nuid(), obj.id, (frame as IntegerVariablesFrame).variables);
                                        break;
                                    case FrameType.FloatVariables:
                                        main.sim ?. UpdateAircraft(new LocalNode.Nuid(), obj.id, (frame as FloatVariablesFrame).variables);
                                        break;
                                    case FrameType.String8Variables:
                                        main.sim ?. UpdateAircraft(new LocalNode.Nuid(), obj.id, (frame as String8VariablesFrame).variables);
                                        break;
                                }
                                // next frame
                                obj.frameIndex++;
                            }
                        }
                        else
                        {
                            // stop aircraft
                            obj.StopPlaying();
                            // remove recorded object from sim
                            main.sim ?. RemoveObject(new LocalNode.Nuid(), obj.id);
                        }
                    }
                }
            }

            // check if recording
            if (main.sim != null && recording)
            {
                // for each object in the sim
                foreach (var simObject in main.sim.objectList)
                {
                    // check if object is flagged to record but not being recorded
                    if (simObject.record && simObject.recorderObj == null)
                    {
                        // check for aircraft
                        if (simObject is Sim.Aircraft)
                        {
                            // plane
                            Sim.Aircraft simAircraft = simObject as Sim.Aircraft;
                            // create recorded aircraft
#if FS2024
                            simObject.recorderObj = new Aircraft(simAircraft is Sim.Plane, simAircraft.flightPlan.callsign, main.network.GetNodeName(simAircraft.ownerNuid), simAircraft.ownerModel, simAircraft.ownerLivery, simAircraft.typerole, simAircraft.owner);
#else
                            simObject.recorderObj = new Aircraft(simAircraft is Sim.Plane, simAircraft.flightPlan.callsign, main.network.GetNodeName(simAircraft.ownerNuid), simAircraft.ownerModel, simAircraft.typerole, simAircraft.owner);
#endif
                        }
                        else
                        {
                            // create recorded object
#if FS2024
                            simObject.recorderObj = new Obj(simObject.ownerModel, simObject.ownerLivery, simObject.typerole, simObject.owner);
#else
                            simObject.recorderObj = new Obj(simObject.ownerModel, simObject.typerole, simObject.owner);
#endif
                        }
                        // add to list
                        objList.Add(simObject.recorderObj);
                    }
                }
            }
            else // not recording
            {
                // check if passed the end
                if (Time > EndTime)
                {
                    // stop playing
                    Stop();
                    // check for loop
                    if (main.settingsLoop)
                    {
                        // start
                        StartPlay();
                    }
                }
            }
        }

        /// <summary>
        /// Write the recording to a stream
        /// </summary>
        /// <param name="writer"></param>
        public void Write(BinaryWriter writer)
        {
            // write header
            writer.Write(Sim.VERSION);
            // count aircraft
            int aircraftCount = 0;
            // for each object
            foreach (var obj in objList)
            {
                // check if object is aircraft
                if (obj is Aircraft)
                {
                    // increment aircraft count
                    aircraftCount++;
                }
            }
            // write aircraft count
            writer.Write(aircraftCount);
            // for each object
            foreach (var obj in objList)
            {
                // check for aircraft
                if (obj is Aircraft)
                {
                    // write object to stream
                    obj.Write(writer);
                }
            }
            // write object count
            writer.Write(objList.Count - aircraftCount);
            // for each object
            foreach (var obj in objList)
            {
                // check for object
                if ((obj is Aircraft) == false)
                {
                    // write object to stream
                    obj.Write(writer);
                }
            }
        }

#region Reader

        /// <summary>
        /// Append existing recording
        /// </summary>
        bool append = false;
        double appendTime = 0.0;

        /// <summary>
        /// Read recording from a stream VERSION 1
        /// </summary>
        /// <param name="reader"></param>
        public void Read1(short version, BinaryReader reader)
        {
            // check for append
            if (append == false)
            {
                // clear all aircraft
                objList.Clear();
            }

            // read aircraft count
            int count = reader.ReadInt32();
            // for each aircraft
            for (int n = 0; n < count; n++)
            {
                // create aircraft
                Aircraft aircraft = new Aircraft();
                // read aircraft
                aircraft.Read(version, reader);

                // check for append
                if (append)
                {
                    // for each frame
                    foreach (var frame in aircraft.frames)
                    {
                        // adjust time
                        frame.time += appendTime;
                    }
                }

                // add to list
                objList.Add(aircraft);
            }

            if (reader.PeekChar() != -1)
            {
                // read object count
                count = reader.ReadInt32();
                // for each object
                for (int n = 0; n < count; n++)
                {
                    // create object
                    Obj obj = new Obj();
                    // read obj
                    obj.Read(version, reader);


                    // check for append
                    if (append)
                    {
                        // for each frame
                        foreach (var frame in obj.frames)
                        {
                            // adjust time
                            frame.time += appendTime;
                        }
                    }

                    // add to list
                    objList.Add(obj);
                }
            }
        }

        /// <summary>
        /// Version table for reading data
        /// </summary>
        readonly Dictionary<short, Sim.ReadVersion> readVersions;

        /// <summary>
        /// Read data
        /// </summary>
        /// <param name="reader">Reader</param>
        public void Read(BinaryReader reader)
        {
            // get version
            short version = reader.ReadInt16();
            // check version
            if (version < 10022)
            {
                // warning
                main.ShowMessage(Resources.strings.OldRecording);
                return;
            }
            // read correct version
            Sim.Read(version, readVersions, reader);
        }

        /// <summary>
        /// Append data
        /// </summary>
        /// <param name="reader">Reader</param>
        public void Append(BinaryReader reader)
        {
            // enable append
            append = true;
            appendTime = EndTime;
            // read data
            Read(reader);
            // finish append
            append = false;
        }

#endregion

#region Edit

        /// <summary>
        /// Remove frames since start
        /// </summary>
        public void TrimFromStart()
        {
            // get current time
            double now = Time;
            // for each object
            foreach (var obj in objList)
            {
                // find new first frame
                int firstIndex = 0;
                for (int index = 0; index < obj.frames.Count; index++)
                {
                    // check if reached current time
                    if (obj.frames[index].time >= now)
                    {
                        // adjust time
                        obj.frames[index].time -= now;
                        // check if first index is set
                        if (firstIndex == 0)
                        {
                            // save first index
                            firstIndex = index;
                        }
                    }
                }

                // check for frames to be removed
                if (firstIndex > 0)
                {
                    // remove frames up to current time
                    obj.frames.RemoveRange(0, firstIndex);
                }
            }

            // reset time
            startTime = main.ElapsedTime;
            pauseTime = 0.0;
        }


        /// <summary>
        /// Remove frames up to end
        /// </summary>
        public void TrimToEnd()
        {
            // for each object
            foreach (var obj in objList)
            {
                // find new first frame
                int firstIndex = 0;
                for (; firstIndex < obj.frames.Count; firstIndex++)
                {
                    // check if reached current time
                    if (obj.frames[firstIndex].time >= Time)
                    {
                        break;
                    }
                }

                // check for frames to be removed
                if (firstIndex < obj.frames.Count)
                {
                    // remove frames up to the end
                    obj.frames.RemoveRange(firstIndex, obj.frames.Count - firstIndex);
                }
            }
        }

#endregion
    }
}
