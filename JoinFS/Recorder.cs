using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
#if !CONSOLE
using System.Windows.Forms;
#endif
using JoinFS.Properties;
using JoinFS.Net;
using JoinFS.Recording;
using JoinFS.Recording.Format;


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
                this.variables = [];
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
                // new variables
                variables = [];
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
                this.variables = [];
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
                // new variables
                variables = [];
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
                this.variables = [];
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
                // new variables
                variables = [];
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
            /// <summary>
            /// Aircraft variation
            /// </summary>
            public string livery = "";
            /// <summary>
            /// ICAO type designator (e.g. A20N)
            /// </summary>
            public string icaoType = "";
            /// <summary>
            /// ICAO airline code (e.g. DLH)
            /// </summary>
            public string icaoAirline = "";
            /// <summary>
            /// Aircraft type role
            /// </summary>
            public int typerole = Substitution.TypeRole_SingleProp;
            /// <summary>
            /// List of frames
            /// </summary>
            public List<Frame> frames = [];

            /// <summary>Copy with its own frame list (the frames themselves are shared)</summary>
            public Obj CopyForSave()
            {
                Obj copy = (Obj)MemberwiseClone();
                copy.frames = new(frames);
                return copy;
            }
            public int frameIndex = 0;
            /// <summary>
            /// Object owner
            /// </summary>
            public Sim.Obj.Owner owner = Sim.Obj.Owner.Me;
            /// <summary>
            /// The application that recorded the object and the simulator it was seen in (a JFS2 track-open); empty when unknown
            /// </summary>
            public string sourceApp = "";
            public string simulator = "";
            /// <summary>
            /// Object is currently being played
            /// </summary>
            public bool playing = false;
            /// <summary>
            /// The object is left out of playback (<see cref="Recorder.Skip"/>). Its frames stay, so it is still saved with the recording.
            /// A recording that is loaded again has no skipped objects.
            /// </summary>
            public bool skipped = false;
            /// <summary>
            /// The recording times of the object's records while it is recorded: positions at their sample time, the rest with them
            /// </summary>
            public readonly TrackTimeBase timeBase = new();
            public Vector playbackAngles = new();
            public bool playbackAnglesValid = false;

            /// <summary>
            /// Next ID
            /// </summary>
            static uint nextId = 10000;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="model">Object model</param>
            public Obj(string model, string livery, string icaoType, string icaoAirline, int typerole, Sim.Obj.Owner owner)
            {
                // ID
                this.id = nextId++;
                this.model = model;
                this.icaoType = icaoType;
                this.icaoAirline = icaoAirline;
                this.typerole = typerole;
                this.owner = owner;
                this.livery = livery;

            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Obj()
            {
                // ID
                this.id = nextId++;
                this.owner = Sim.Obj.Owner.Recorder;

            }

            /// <summary>
            /// Start playing this object
            /// </summary>
            public void Play()
            {
                // a skipped object stays out of playback
                if (skipped)
                {
                    return;
                }

                // start at beginning
                frameIndex = 0;
                playbackAnglesValid = false;
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
                playbackAnglesValid = false;
                // sort frames
                frames.Sort(delegate(Frame f1, Frame f2) { return f1.time.CompareTo(f2.time); });
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
            public Aircraft(bool plane, string callsign, string nickname, string model, string livery, string icaoType, string icaoAirline, int typerole, Sim.Obj.Owner owner) : base(model, livery, icaoType, icaoAirline, typerole, owner)
            {
                this.plane = plane;
                this.callsign = callsign;
                this.nickname = nickname;
                this.livery = livery;

            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Aircraft()
            {
            }

        }

        /// <summary>
        /// List of recorded objects
        /// </summary>
        List<Obj> objList = [];

        /// <summary>
        /// Is the recorder currently recording. The recorder runs on the sim thread; other threads
        /// may read the flags and <see cref="EndTimeView"/>/<see cref="Exists"/>, and send
        /// commands through Main.InvokeOnSim / Main.SimCommand.
        /// </summary>
        public volatile bool recording;

        /// <summary>
        /// Is the recorder currently playing
        /// </summary>
        public volatile bool playing;

        /// <summary>
        /// End time and object ids as of the last pass, for other threads
        /// </summary>
        double endTimeView;
        volatile uint[] idsView = [];

        /// <summary>
        /// <see cref="EndTime"/> as of the sim thread's last pass (any thread)
        /// </summary>
        public double EndTimeView => System.Threading.Volatile.Read(ref endTimeView);

        /// <summary>
        /// An aircraft of the recording, as other threads see it
        /// </summary>
        public sealed record AircraftView(uint Id, string Callsign, string Model, bool Skipped);

        volatile AircraftView[] aircraftView = [];

        /// <summary>
        /// The aircraft of the recording as of the sim thread's last pass (any thread)
        /// </summary>
        public AircraftView[] AircraftSnapshot => aircraftView;

        /// <summary>
        /// Publish what other threads read (sim thread)
        /// </summary>
        public void PublishStatus()
        {
            System.Threading.Volatile.Write(ref endTimeView, EndTime);
            idsView = objList.ConvertAll(o => o.id).ToArray();

            List<AircraftView> aircraft = [];
            foreach (var obj in objList)
            {
                if (obj is Aircraft recorded)
                {
                    aircraft.Add(new AircraftView(recorded.id, recorded.callsign, recorded.model, recorded.skipped));
                }
            }
            aircraftView = aircraft.ToArray();
        }

        /// <summary>
        /// Is the recorder recording or playing
        /// </summary>
        public bool Active { get { return recording || playing; } }

        /// <summary>
        /// Is the recorder empty
        /// </summary>
        public bool Empty { get { return (objList.Count == 0); } }

        /// <summary>
        /// What each recorder action is allowed to do in a given state. The Recorder buttons and the
        /// global hotkeys both ask these, so a hotkey can't fire what the button would have disabled
        /// (Button.Enabled goes stale while the Recorder window is hidden).
        /// </summary>
        public static bool CanRecord(bool recording, bool playing) => !(recording || playing);
        public static bool CanOverdub(bool recording, bool empty) => !recording && !empty;
        public static bool CanPlay(bool recording, bool empty) => !recording && !empty;
        public static bool CanStop(bool recording, bool playing) => recording || playing;

        public bool CanRecord() => CanRecord(recording, playing);
        public bool CanOverdub() => CanOverdub(recording, Empty);
        public bool CanPlay() => CanPlay(recording, Empty);
        public bool CanStop() => CanStop(recording, playing);

        /// <summary>
        /// The Record and Overdub hotkeys pressed during a recording stop it first (Record then
        /// auto-saves it) and start a new one, since a hotkey user (VR) can't reach Stop. The buttons
        /// stay disabled meanwhile.
        /// </summary>
        public static bool HotkeyRestartsRecording(bool recording) => recording;

        /// <summary>
        /// An overdub needs a track to overdub: on an empty recorder it is a plain record, which also
        /// sets the start time that frames are stamped against
        /// </summary>
        public static bool ResolveOverdub(bool requested, bool empty) => requested && !empty;

        /// <summary>
        /// File name for an auto-saved recording: date, time and the first aircraft's callsign
        /// </summary>
        public static string BuildAutoSaveFileName(DateTime now, string firstCallsign)
        {
            string callsign = string.IsNullOrWhiteSpace(firstCallsign) ? "recording" : firstCallsign.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                callsign = callsign.Replace(invalid, '_');
            }
            return now.ToString("yyyy-MM-dd_HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "_" + callsign + Jfs2.Extension;
        }

        /// <summary>
        /// Callsign of the first aircraft in a recording, empty if there is none
        /// </summary>
        public static string FirstCallsign(List<Obj> objects)
        {
            foreach (var obj in objects)
            {
                if (obj is Aircraft aircraft)
                {
                    return aircraft.callsign ?? "";
                }
            }
            return "";
        }

        /// <summary>
        /// A path in the folder for the file name that doesn't exist yet (-2, -3, ... appended)
        /// </summary>
        public static string UniquePath(string folder, string fileName)
        {
            string path = Path.Combine(folder, fileName);
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            for (int counter = 2; File.Exists(path); counter++)
            {
                path = Path.Combine(folder, stem + "-" + counter + extension);
            }
            return path;
        }

        /// <summary>
        /// Write a copy of a recording (from <see cref="CopyForSave"/>) into the folder under an
        /// automatic name, never overwriting a file. Returns the path; throws if it can't be written
        /// (nothing is left behind) - any thread
        /// </summary>
        public string AutoSave(string folder, List<Obj> objects, DateTime now)
        {
            string path = UniquePath(folder, BuildAutoSaveFileName(now, FirstCallsign(objects)));
            SaveJfs2(path, objects, now, false);
            return path;
        }

        /// <summary>
        /// Save a recording (from <see cref="CopyForSave"/>) as a JFS2 file. It is written beside the target and moved into place,
        /// so a failure leaves nothing behind and does not destroy a file that is being replaced. Throws if it can't be written - any thread
        /// </summary>
        /// <param name="now">When the recording ended (local time); the file's start time is that minus the recording's length</param>
        /// <param name="overwrite">Replace an existing file (the user confirmed it in the dialog) instead of failing</param>
        public void SaveJfs2(string path, List<Obj> objects, DateTime now, bool overwrite)
        {
            double length = objects.Count == 0 ? 0 : objects.Max(obj => obj.frames.Count == 0 ? 0 : obj.frames[^1].time);
            string temp = path + ".tmp";
            try
            {
                Jfs2Exporter.Write(temp, objects, AppLabel, now.ToUniversalTime().AddSeconds(-length), _ => { });
                File.Move(temp, path, overwrite);
            }
            catch
            {
                TryDeleteQuietly(temp);
                throw;
            }
        }

        /// <summary>
        /// Delete a file, ignoring failures (nothing sensible left to do about them) - any thread
        /// </summary>
        static void TryDeleteQuietly(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// Time that playing or recording started
        /// </summary>
        double startTime;

        /// <summary>
        /// Frame type counts recorded during current recording session
        /// </summary>
        readonly Dictionary<FrameType, int> recordFrameCounts = [];
        int recordSimErrorCount = 0;
        string recordFirstSimError = "";

        void IncrementRecordFrameCount(FrameType type)
        {
            if (recordFrameCounts.ContainsKey(type))
            {
                recordFrameCounts[type]++;
            }
            else
            {
                recordFrameCounts[type] = 1;
            }
        }

        void LogRecordFrameCounts()
        {
            if (recordFrameCounts.Count == 0)
            {
                main.MonitorEvent("Recorder: no frames were captured in this recording session.");
                return;
            }

            StringBuilder summary = new();
            foreach (var count in recordFrameCounts)
            {
                if (summary.Length > 0)
                {
                    summary.Append(", ");
                }
                summary.Append(count.Key);
                summary.Append('=');
                summary.Append(count.Value);
            }

            main.MonitorEvent("Recorder: captured frames -> " + summary + ".");
        }

        public void NotifySimulatorError(string error)
        {
            if (recording)
            {
                recordSimErrorCount++;
                if (recordFirstSimError.Length == 0)
                {
                    recordFirstSimError = error;
                }
            }
        }

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
        /// <param name="objTime">When the position was sampled, on the object's clock (the sender's network time for a network object)</param>
        /// <param name="positionVelocity">Position and Velocity</param>
        public void Record(Obj obj, double objTime, ref Sim.ObjectPositionVelocity positionVelocity)
        {
            // check object
            if (obj != null && obj.playing == false)
            {
                // add position velocity frame
                double frameTime = obj.timeBase.Position(Time, objTime);
                obj.frames.Add(new ObjectPositionFrame(frameTime, ref positionVelocity));
                IncrementRecordFrameCount(FrameType.ObjectPosition);

                // and to the JFS2 file (flags: the old format keeps only on-ground)
                if (jfs2 != null)
                {
                    ObjectPositionUpdate update = SimMessageMapper.ToObjectPositionUpdate(obj.id, frameTime, ref positionVelocity, positionVelocity.ground != 0 ? PositionStateFlags.OnGround : PositionStateFlags.None);
                    jfs2.ObjectPosition(obj.id, frameTime, ref update);
                }
            }
        }

        /// <summary>
        /// Record aircraft position
        /// </summary>
        /// <param name="objTime">When the position was sampled, on the object's clock (the sender's network time for a network object)</param>
        /// <param name="positionVelocity">Position and Velocity</param>
        public void Record(Obj obj, double objTime, ref Sim.AircraftPosition aircraftPosition)
        {
            // check object
            if (obj != null && obj.playing == false)
            {
                // add position velocity frame
                double frameTime = obj.timeBase.Position(Time, objTime);
                obj.frames.Add(new AircraftPositionFrame(frameTime, ref aircraftPosition));
                IncrementRecordFrameCount(FrameType.AircraftPosition);

                // and to the JFS2 file (flags: the old format keeps only on-ground)
                if (jfs2 != null)
                {
                    PositionUpdate update = SimMessageMapper.ToPositionUpdate(obj.id, frameTime, ref aircraftPosition, aircraftPosition.ground != 0 ? PositionStateFlags.OnGround : PositionStateFlags.None);
                    jfs2.Position(obj.id, frameTime, ref update);
                }
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
                // add event, on the time base of the object's positions
                double time = obj.timeBase.Other(Time);
                obj.frames.Add(new SimEventFrame(time, eventId, data));
                IncrementRecordFrameCount(FrameType.SimEvent);
                jfs2?.Event(obj.id, time, eventId, data);
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
                // add variables, on the time base of the object's positions
                double time = obj.timeBase.Other(Time);
                obj.frames.Add(new IntegerVariablesFrame(time, variables));
                IncrementRecordFrameCount(FrameType.IntegerVariables);
                jfs2?.Variables(obj.id, time, variables, null, null);
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
                // add variables, on the time base of the object's positions
                double time = obj.timeBase.Other(Time);
                obj.frames.Add(new FloatVariablesFrame(time, variables));
                IncrementRecordFrameCount(FrameType.FloatVariables);
                jfs2?.Variables(obj.id, time, null, variables, null);
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
                // add variables, on the time base of the object's positions
                double time = obj.timeBase.Other(Time);
                obj.frames.Add(new String8VariablesFrame(time, variables));
                IncrementRecordFrameCount(FrameType.String8Variables);
                jfs2?.Variables(obj.id, time, null, null, variables);
            }
        }

        /// <summary>
        /// Start recording a sim object: create its recorded object, link it and add it to the recording
        /// </summary>
        void AddRecordedObject(Sim.Obj simObject)
        {
            // check for aircraft
            if (simObject is Sim.Aircraft simAircraft)
            {
                // create recorded aircraft
                simObject.recorderObj = new Aircraft(simAircraft is Sim.Plane, simAircraft.flightPlan.callsign, main.network.Peers.GetNodeName(simAircraft.ownerNuid), simAircraft.ownerModel, simAircraft.ownerLivery, simAircraft.ownerIcaoType, simAircraft.ownerIcaoAirline, simAircraft.typerole, simAircraft.owner);
            }
            else
            {
                // create recorded object
                simObject.recorderObj = new Obj(simObject.ownerModel, simObject.ownerLivery, simObject.ownerIcaoType, simObject.ownerIcaoAirline, simObject.typerole, simObject.owner);
            }
            simObject.recorderObj.sourceApp = AppLabel;
            simObject.recorderObj.simulator = SimulatorLabel();
            OpenJfs2Track(simObject.recorderObj, main.network.Peers.GetNodeName(simObject.ownerNuid), SimMessageMapper.BuildIdentity(simObject, simObject.recorderObj.id));
            // add to list
            objList.Add(simObject.recorderObj);
        }

        /// <summary>
        /// The live capture into the working file (docs/jfs2), or null: a journal of the recording that survives a crash and is offered
        /// for recovery. The in-memory objects stay the recording that is played, edited and saved (Save exports them).
        /// </summary>
        Jfs2Capture jfs2;

        /// <summary>
        /// Start the JFS2 file for a new recording. A failure is logged and leaves the recording as it was.
        /// </summary>
        void StartJfs2Capture()
        {
            jfs2 = null;
            DiscardWorkingFile();
            if (!main.settingsJfs2Capture)
            {
                return;
            }
            try
            {
                string folder = WorkingFolder(main.documentsPath);
                Directory.CreateDirectory(folder);
                string path = UniquePath(folder, BuildAutoSaveFileName(DateTime.Now, ""));
                jfs2 = Jfs2Capture.Create(path, AppLabel, SimulatorLabel(), DateTime.UtcNow, main.MonitorEvent);
                workingFile = path;
                main.MonitorEvent("Recorder: writing the working file '" + path + "'.");
            }
            catch (Exception ex)
            {
                main.MonitorEvent("Recorder: cannot write a JFS2 recording (" + ex.Message + "); recording without it.");
            }
        }

        /// <summary>
        /// Where the files of recordings in progress are kept: the live capture writes there from the first second, so a crash
        /// loses at most the last seconds, and the file goes when the recording is saved or thrown away. What is left there at
        /// the next start is a recording that was never saved.
        /// </summary>
        public const string WorkingFolderName = "Working";

        public static string WorkingFolder(string documentsPath) => Path.Combine(documentsPath, WorkingFolderName);

        /// <summary>
        /// The working file of the recording the recorder holds, or null (the recording was opened from a file, or has been saved)
        /// </summary>
        string workingFile;

        /// <summary>
        /// The recording has been saved (or its owner let it go), so its working file is not needed any more - sim thread
        /// </summary>
        public void DiscardWorkingFile()
        {
            string path = workingFile;
            workingFile = null;
            if (path != null)
            {
                TryDeleteQuietly(path);
            }
        }

        /// <summary>
        /// The working files left behind by recordings that were never saved (a crash, or a window closed without saving),
        /// oldest first. The one of the recording being held is not among them, nor is one another JoinFS is still writing - any thread
        /// </summary>
        public List<string> FindUnsavedWorkingFiles(string documentsPath)
        {
            string folder = WorkingFolder(documentsPath);
            if (!Directory.Exists(folder))
            {
                return [];
            }
            string current = workingFile;
            return Directory.EnumerateFiles(folder, "*" + Jfs2.Extension)
                .Where(path => !string.Equals(path, current, StringComparison.OrdinalIgnoreCase) && !InUse(path))
                .OrderBy(path => File.GetLastWriteTimeUtc(path))
                .ToList();
        }

        /// <summary>
        /// A file that cannot be opened exclusively is being written by someone, for example another JoinFS on this machine
        /// </summary>
        static bool InUse(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        /// <summary>
        /// The simulator being recorded in, or empty when none is connected
        /// </summary>
        string SimulatorLabel() => main.sim != null && main.sim.Connected ? main.sim.GetSimulatorName() : "";

        /// <summary>
        /// This application, as a recording names it (header metadata, track-open)
        /// </summary>
        static string AppLabel => "JoinFS " + Main.Version;

        /// <summary>
        /// The owner a JFS2 track-open states for an object (spec 7.1): our own objects and the simulator's are local
        /// </summary>
        public static TrackOwner ToTrackOwner(Sim.Obj.Owner owner) => owner switch
        {
            Sim.Obj.Owner.Network => TrackOwner.Network,
            Sim.Obj.Owner.Recorder => TrackOwner.Recorded,
            _ => TrackOwner.Local,
        };

        /// <summary>
        /// The owner of a recorded object read from a JFS2 track-open, so saving it again states the same owner
        /// </summary>
        public static Sim.Obj.Owner FromTrackOwner(TrackOwner owner) => owner switch
        {
            TrackOwner.Network => Sim.Obj.Owner.Network,
            TrackOwner.Local => Sim.Obj.Owner.Me,
            _ => Sim.Obj.Owner.Recorder,
        };

        /// <summary>
        /// Open the JFS2 track of a recorded object
        /// </summary>
        void OpenJfs2Track(Obj obj, string nickname, IdentityUpdate identity)
        {
            jfs2?.OpenTrack(obj.id, Time, ToTrackOwner(obj.owner), nickname, obj.sourceApp, obj.simulator, identity);
        }

        /// <summary>
        /// Write the identity of a recorded object again when it has changed (callsign, model) since the last one written
        /// </summary>
        public void RecordIdentity(Sim.Obj simObject)
        {
            if (jfs2 != null && simObject.recorderObj is Obj recorded && recorded.playing == false)
            {
                jfs2.Identity(recorded.id, recorded.timeBase.Other(Time), SimMessageMapper.BuildIdentity(simObject, recorded.id));
            }
        }

        /// <summary>
        /// Finish the JFS2 file: close the open tracks and write its index
        /// </summary>
        void FinishJfs2Capture()
        {
            if (jfs2 != null)
            {
                Jfs2Capture finished = jfs2;
                jfs2 = null;
                finished.Finish();
                if (finished.PositionCount + finished.ObjectPositionCount == 0)
                {
                    // nothing to play, nothing to recover
                    DiscardWorkingFile();
                }
                else if (!finished.Failed)
                {
                    main.MonitorEvent("Recorder: wrote the JFS2 recording '" + finished.Path + "' (" + finished.TrackCount + " objects, " + finished.PositionCount + " aircraft positions, " + finished.ObjectPositionCount + " object positions, " + finished.VariableRecordCount + " variable records, " + finished.EventCount + " events, " + finished.IdentityChangeCount + " identity changes, " + finished.HeldCount + " records held back).");
                }
            }
        }

        /// <summary>
        /// Start recording
        /// </summary>
        public void StartRecord(bool overdub)
        {
            // overdubbing nothing is recording
            overdub = ResolveOverdub(overdub, Empty);

            // should not be recording
            if (main.sim != null && recording == false && (playing == false || overdub))
            {
                // check for overdub
                if (overdub == false)
                {
                    // clear aircraft
                    objList.Clear();
                    recordFrameCounts.Clear();
                    recordSimErrorCount = 0;
                    recordFirstSimError = "";
                }

                // check for overdub
                if (overdub == false)
                {
                    // set start time
                    startTime = main.ElapsedTime;
                    // the working file journals a new recording; an overdub adds to a recording that is not in it
                    StartJfs2Capture();
                }

                // for each object in the sim
                foreach (var simObject in main.sim.objectList)
                {
                    // check if object is flagged to record
                    if (simObject.record)
                    {
                        AddRecordedObject(simObject);
                    }
                }

                // summary
                int aircraftCount = 0;
                foreach (var obj in objList)
                {
                    if (obj is Aircraft)
                    {
                        aircraftCount++;
                    }
                }
                main.MonitorEvent("Recorder: start recording " + objList.Count + " objects (" + aircraftCount + " aircraft). SimConnected=" + main.sim.Connected + ", NetworkConnected=" + main.network.Connected + ".");

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
                main.sim ?. RemoveObject(new NodeId(), obj.id);
            }

            // check state
            if (main.sim != null && recording)
            {
                FinishJfs2Capture();
                LogRecordFrameCounts();

                int positionCount = recordFrameCounts.GetValueOrDefault(FrameType.ObjectPosition) + recordFrameCounts.GetValueOrDefault(FrameType.AircraftPosition);
                int variableCount = recordFrameCounts.GetValueOrDefault(FrameType.IntegerVariables) + recordFrameCounts.GetValueOrDefault(FrameType.FloatVariables) + recordFrameCounts.GetValueOrDefault(FrameType.String8Variables);
                if (positionCount == 0 && variableCount > 0)
                {
                    main.MonitorEvent("Recorder: warning - captured variable frames but no position frames. SimConnected=" + main.sim.Connected + ", NetworkConnected=" + main.network.Connected + ".");
                }

                if (recordSimErrorCount > 0)
                {
                    main.MonitorEvent("Recorder: warning - " + recordSimErrorCount + " simulator error(s) occurred during recording. First error: " + recordFirstSimError);
                }

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
            // any thread: the ids as of the sim thread's last pass
            return Array.IndexOf(idsView, id) >= 0;
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
                    main.sim.RemoveObject(new NodeId(), obj.id);

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
        /// Leave an object out of playback, now and until the recording is loaded again (sim thread). It is not taken out of the
        /// recording: it is still saved with it.
        /// </summary>
        public void Skip(uint id)
        {
            // get object
            Obj obj = objList.Find(o => o.id == id);
            if (obj != null)
            {
                obj.skipped = true;
                // stop it, and take it out of the sim, if it is being played
                obj.StopPlaying();
                main.sim ?. RemoveObject(new NodeId(), obj.id);
                PublishStatus();
            }
        }

        /// <summary>
        /// Is the recorder currently paused
        /// </summary>
        public volatile bool paused;

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
                        main.sim ?. PauseObject(new NodeId(), obj.id, false);
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
                        main.sim ?. PauseObject(new NodeId(), obj.id, true);
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
                            // recordings don't currently capture classCode/wtc (a Phase 3 network-only
                            // addition) - pass empty/unconfirmed so replay falls back to local re-derivation
                            // from icaoType, same as before this feature existed
                            main.sim ?. UpdateAircraft(new NodeId(), obj.id, false, aircraft.plane, aircraft.callsign, "", aircraft.nickname, aircraft.model, aircraft.livery, aircraft.icaoType, aircraft.icaoAirline, "", "", "", false, aircraft.typerole, recentFrame.time, ref (recentFrame as AircraftPositionFrame).data);
                        }
                        else
                        {
                            // update position
                            main.sim?.UpdateObject(new NodeId(), obj.id, obj.model, obj.livery, obj.icaoType, obj.icaoAirline, "", "", false, obj.typerole, recentFrame.time, ref (recentFrame as ObjectPositionFrame).data);
                        }
                        // reset object
                        main.sim ?. ResetObject(new NodeId(), obj.id);
                    }
                }
            }
        }

        static bool IsPositionFrame(Frame frame) => frame.type == FrameType.ObjectPosition || frame.type == FrameType.AircraftPosition;

        static float Lerp(float a, float b, double t) => (float)(a + (b - a) * t);
        static double Lerp(double a, double b, double t) => a + (b - a) * t;
        static double Blend(Frame from, Frame to, double time)
        {
            double dt = to.time - from.time;
            return dt > 0.000001 ? Math.Clamp((time - from.time) / dt, 0.0, 1.0) : 0.0;
        }

        static Vector SlerpEuler(float pitch0, float heading0, float bank0, float pitch1, float heading1, float bank1, double t)
        {
            Quaternion from = Quaternion.FromEuler(new Vector(pitch0, heading0, bank0));
            Quaternion to = Quaternion.FromEuler(new Vector(pitch1, heading1, bank1));
            Quaternion blended = Quaternion.Slerp(from, to, t);
            blended.Normalize();
            return blended.ToEuler();
        }

        Vector InterpolateAngles(Obj obj, float pitch0, float heading0, float bank0, float pitch1, float heading1, float bank1, double t)
        {
            bool nearGimbal = Math.Abs(Math.Abs(pitch0) - Math.PI * 0.5) < 0.08 || Math.Abs(Math.Abs(pitch1) - Math.PI * 0.5) < 0.08;
            bool wrappedAxis = Math.Abs(pitch1 - pitch0) > Math.PI || Math.Abs(heading1 - heading0) > Math.PI || Math.Abs(bank1 - bank0) > Math.PI;

            Vector angles = (nearGimbal || wrappedAxis)
                ? new Vector
                (
                    pitch0 + Vector.AngleDelta(pitch0, pitch1) * t,
                    heading0 + Vector.AngleDelta(heading0, heading1) * t,
                    bank0 + Vector.AngleDelta(bank0, bank1) * t
                )
                : SlerpEuler(pitch0, heading0, bank0, pitch1, heading1, bank1, t);

            if (obj.playbackAnglesValid)
            {
                angles.x = obj.playbackAngles.x + Vector.AngleDelta(obj.playbackAngles.x, angles.x);
                angles.y = obj.playbackAngles.y + Vector.AngleDelta(obj.playbackAngles.y, angles.y);
                angles.z = obj.playbackAngles.z + Vector.AngleDelta(obj.playbackAngles.z, angles.z);
            }

            obj.playbackAngles = angles;
            obj.playbackAnglesValid = true;

            return angles;
        }

        static Sim.ObjectPositionVelocity Interpolate(ObjectPositionFrame from, ObjectPositionFrame to, double t, Vector angles)
        {
            Sim.ObjectPositionVelocity a = from.data;
            Sim.ObjectPositionVelocity b = to.data;

            return new Sim.ObjectPositionVelocity
            {
                latitude = Lerp(a.latitude, b.latitude, t),
                longitude = Lerp(a.longitude, b.longitude, t),
                altitude = Lerp(a.altitude, b.altitude, t),
                pitch = (float)angles.x,
                heading = (float)angles.y,
                bank = (float)angles.z,
                velocityX = Lerp(a.velocityX, b.velocityX, t),
                velocityY = Lerp(a.velocityY, b.velocityY, t),
                velocityZ = Lerp(a.velocityZ, b.velocityZ, t),
                angularVelocityX = Lerp(a.angularVelocityX, b.angularVelocityX, t),
                angularVelocityY = Lerp(a.angularVelocityY, b.angularVelocityY, t),
                angularVelocityZ = Lerp(a.angularVelocityZ, b.angularVelocityZ, t),
                accelerationX = Lerp(a.accelerationX, b.accelerationX, t),
                accelerationY = Lerp(a.accelerationY, b.accelerationY, t),
                accelerationZ = Lerp(a.accelerationZ, b.accelerationZ, t),
                height = Lerp(a.height, b.height, t),
                ground = t < 0.5 ? a.ground : b.ground
            };
        }

        static Sim.AircraftPosition Interpolate(AircraftPositionFrame from, AircraftPositionFrame to, double t, Vector angles)
        {
            Sim.AircraftPosition a = from.data;
            Sim.AircraftPosition b = to.data;

            return new Sim.AircraftPosition
            {
                latitude = Lerp(a.latitude, b.latitude, t),
                longitude = Lerp(a.longitude, b.longitude, t),
                altitude = Lerp(a.altitude, b.altitude, t),
                pitch = (float)angles.x,
                heading = (float)angles.y,
                bank = (float)angles.z,
                velocityX = Lerp(a.velocityX, b.velocityX, t),
                velocityY = Lerp(a.velocityY, b.velocityY, t),
                velocityZ = Lerp(a.velocityZ, b.velocityZ, t),
                angularVelocityX = Lerp(a.angularVelocityX, b.angularVelocityX, t),
                angularVelocityY = Lerp(a.angularVelocityY, b.angularVelocityY, t),
                angularVelocityZ = Lerp(a.angularVelocityZ, b.angularVelocityZ, t),
                accelerationX = Lerp(a.accelerationX, b.accelerationX, t),
                accelerationY = Lerp(a.accelerationY, b.accelerationY, t),
                accelerationZ = Lerp(a.accelerationZ, b.accelerationZ, t),
                rudder = Lerp(a.rudder, b.rudder, t),
                elevator = Lerp(a.elevator, b.elevator, t),
                aileron = Lerp(a.aileron, b.aileron, t),
                brakeLeft = Lerp(a.brakeLeft, b.brakeLeft, t),
                brakeRight = Lerp(a.brakeRight, b.brakeRight, t),
                elevation = Lerp(a.elevation, b.elevation, t),
                ground = t < 0.5 ? a.ground : b.ground,
                // carried through so the ground-clearance correction in UpdateAircraft (see
                // ground-jitter-on-model-mismatch fix) still has the sender's real clearance during
                // playback - the object initializer above left this at its default (0.0f), which the
                // correction can't tell apart from a real "sits flush on the ground" reading, so it
                // silently added the substitute's own full clearance on top of every interpolated
                // altitude - exactly the "hovers meters above the ground" symptom. NaN propagates
                // through Lerp when either endpoint frame predates this field, which correctly falls
                // back to no correction rather than inventing one from partial data.
                staticCgToGround = Lerp(a.staticCgToGround, b.staticCgToGround, t)
            };
        }

        void UpdateInterpolatedPosition(Obj obj, double time)
        {
            Frame previous = null;
            Frame next = null;

            for (int i = Math.Min(obj.frameIndex - 1, obj.frames.Count - 1); i >= 0; i--)
            {
                if (IsPositionFrame(obj.frames[i]))
                {
                    previous = obj.frames[i];
                    break;
                }
            }

            for (int i = Math.Max(obj.frameIndex, 0); i < obj.frames.Count; i++)
            {
                if (IsPositionFrame(obj.frames[i]))
                {
                    next = obj.frames[i];
                    break;
                }
            }

            if (previous == null && next == null)
            {
                return;
            }

            previous ??= next;
            next ??= previous;

            if (obj is Aircraft aircraft)
            {
                AircraftPositionFrame from = previous as AircraftPositionFrame;
                AircraftPositionFrame to = next as AircraftPositionFrame;
                if (from != null && to != null)
                {
                    double t = Blend(from, to, time);
                    Vector angles = InterpolateAngles(obj, from.data.pitch, from.data.heading, from.data.bank, to.data.pitch, to.data.heading, to.data.bank, t);
                    Sim.AircraftPosition data = Interpolate(from, to, t, angles);
                    main.sim?.UpdateAircraft(new NodeId(), obj.id, false, aircraft.plane, aircraft.callsign, "", aircraft.nickname, aircraft.model, aircraft.livery, aircraft.icaoType, aircraft.icaoAirline, "", "", "", false, aircraft.typerole, time, ref data);
                }
            }
            else
            {
                ObjectPositionFrame from = previous as ObjectPositionFrame;
                ObjectPositionFrame to = next as ObjectPositionFrame;
                if (from != null && to != null)
                {
                    double t = Blend(from, to, time);
                    Vector angles = InterpolateAngles(obj, from.data.pitch, from.data.heading, from.data.bank, to.data.pitch, to.data.heading, to.data.bank, t);
                    Sim.ObjectPositionVelocity data = Interpolate(from, to, t, angles);
                    main.sim?.UpdateObject(new NodeId(), obj.id, obj.model, obj.livery, obj.icaoType, obj.icaoAirline, "", "", false, obj.typerole, time, ref data);
                }
            }
        }

        /// <summary>
        /// Do work
        /// </summary>
        /// <summary>
        /// How often playback advances while playing (seconds)
        /// </summary>
        const double PlaybackInterval = 0.005;

        /// <summary>
        /// When DoWork next has timed work (playback); the sim thread sleeps until then
        /// </summary>
        public double NextDue(double now)
        {
            return playing ? now + PlaybackInterval : double.MaxValue;
        }

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
                            main.sim ?. TouchObject(new NodeId(), obj.id);
                        }
                        else
                        {
                            // process all due frames at current playback time
                            while (obj.frameIndex < obj.frames.Count)
                            {
                                // get frame
                                Frame frame = obj.frames[obj.frameIndex];
                                // wait for frame time
                                if (Time <= frame.time)
                                {
                                    break;
                                }

                                // check frame type
                                switch (frame.type)
                                {
                                    case FrameType.ObjectPosition:
                                    case FrameType.AircraftPosition:
                                        break;
                                    case FrameType.SimEvent:
                                        main.sim ?. UpdateAircraft(new NodeId(), obj.id, (frame as SimEventFrame).eventId, (frame as SimEventFrame).data, true);
                                        break;
                                    case FrameType.IntegerVariables:
                                        main.sim ?. UpdateAircraft(new NodeId(), obj.id, (frame as IntegerVariablesFrame).variables);
                                        break;
                                    case FrameType.FloatVariables:
                                        main.sim ?. UpdateAircraft(new NodeId(), obj.id, (frame as FloatVariablesFrame).variables);
                                        break;
                                    case FrameType.String8Variables:
                                        main.sim ?. UpdateAircraft(new NodeId(), obj.id, (frame as String8VariablesFrame).variables);
                                        break;
                                }
                                // next frame
                                obj.frameIndex++;
                            }

                            // continuously update interpolated position
                            UpdateInterpolatedPosition(obj, Time);

                            // end object playback when all frames are consumed
                            if (obj.frameIndex >= obj.frames.Count)
                            {
                                // stop aircraft
                                obj.StopPlaying();
                                // remove recorded object from sim
                                main.sim ?. RemoveObject(new NodeId(), obj.id);
                            }
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
                        AddRecordedObject(simObject);
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

            // publish what other threads read
            PublishStatus();
        }

        /// <summary>
        /// Copies of the objects with their own frame lists, so a recording can be written on
        /// another thread while this one goes on (sim thread)
        /// </summary>
        public List<Obj> CopyForSave()
        {
            return objList.ConvertAll(o => o.CopyForSave());
        }

#region Reader

        /// <summary>
        /// Parse a recording into objects without touching the recorder, so a file can be read on
        /// any thread and then applied on the sim thread with <see cref="Load"/>. Null when the
        /// recording can't be used. Throws with a message for the user when the file is damaged.
        /// </summary>
        public List<Obj> Parse(BinaryReader reader, List<string> warnings = null)
        {
            // get version
            short version = reader.ReadInt16();
            // log version
            main?.MonitorEvent("Recorder: reading recording stream (version " + version + ").");
            // a JFS2 file starts with FA 00, which reads as version 250 (docs/jfs2/format-spec.md section 2)
            if (version == Jfs2.LegacyVersionSignature && reader.BaseStream.CanSeek)
            {
                reader.BaseStream.Position -= sizeof(short);
                return ParseJfs2(reader.BaseStream, warnings);
            }
            // check version
            if (version < 10022)
            {
                // warning
                main?.ShowMessage(Resources.Strings.OldRecording);
                return null;
            }
            // an old recording is read by the importer (docs/jfs2/recorder-v2-design.md section 8), which parses the file both ways
            // the trailing strings were written and takes the one that fits, then plays like any other recording
            return ParseLegacy(version, reader.BaseStream, warnings);
        }

        /// <summary>
        /// Read an old .jfs recording whose version has just been read from the stream
        /// </summary>
        List<Obj> ParseLegacy(short version, Stream rest, List<string> warnings)
        {
            // the importer wants the whole file, version included
            var whole = new MemoryStream();
            whole.Write(BitConverter.GetBytes(version));
            rest.CopyTo(whole);
            whole.Position = 0;

            LegacyJfsImporter.Result imported = LegacyJfsImporter.Read(whole);
            foreach (string note in imported.Scene.Notes)
            {
                main?.MonitorEvent("Recorder: " + note);
            }
            warnings?.AddRange(imported.Scene.Warnings);

            List<Obj> objects = Jfs2Loader.Objects(imported.Scene);
            main?.MonitorEvent("Recorder: loaded " + objects.Count(o => o is Aircraft) + " aircraft, " + objects.Count(o => o is not Aircraft) + " objects, " + objects.Sum(o => o.frames.Count) + " frames (trailing strings: " + imported.Layout + ").");
            return objects;
        }

        /// <summary>
        /// Read a JFS2 recording into objects, logging what was skipped. Throws, with a message for the user, if it can't be played.
        /// </summary>
        List<Obj> ParseJfs2(Stream stream, List<string> warnings)
        {
            Jfs2Loader.Result result = Jfs2Loader.Load(stream);
            foreach (string note in result.Notes)
            {
                main?.MonitorEvent("Recorder: " + note);
            }
            warnings?.AddRange(result.Warnings);
            main?.MonitorEvent("Recorder: read a JFS2 recording with " + result.Objects.Count + " aircraft.");
            return result.Objects;
        }

        /// <summary>
        /// Replace the recording with parsed objects, or append them after its end (sim thread)
        /// </summary>
        public void Load(List<Obj> objects, bool append, string recoveredFrom = null)
        {
            if (objects == null)
            {
                return;
            }
            if (append)
            {
                double appendTime = EndTime;
                main.MonitorEvent("Recorder: appending recording from " + appendTime.ToString("0.00") + "s.");
                // for each frame
                foreach (var obj in objects)
                {
                    foreach (var frame in obj.frames)
                    {
                        // adjust time
                        frame.time += appendTime;
                    }
                }
            }
            else
            {
                // clear all aircraft; the recording that is replaced is saved or given up, so its working file goes
                objList.Clear();
                DiscardWorkingFile();
            }
            objList.AddRange(objects);
            // a recording recovered from its working file stays unsaved until it is saved, which then removes that file
            if (!append && recoveredFrom != null)
            {
                workingFile = recoveredFrom;
            }
            PublishStatus();
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
