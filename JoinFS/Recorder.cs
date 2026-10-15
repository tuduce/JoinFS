using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
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
        /// A recorded object: its track of canonical messages (<see cref="SceneTrack"/>, what a JFS2 file holds) and the state of
        /// playing or recording it. The recording is the tracks of all objects; the id is the object's in the simulator while it is
        /// played or recorded.
        /// </summary>
        public class Obj
        {
            /// <summary>
            /// ID for the recorded object
            /// </summary>
            public readonly uint id;
            /// <summary>
            /// What was recorded, in time order
            /// </summary>
            public readonly SceneTrack track;
            /// <summary>
            /// The next frame to play
            /// </summary>
            public int frameIndex = 0;
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
            /// The identity it plays with (the track's, then each change played), or the latest one recorded
            /// </summary>
            public IdentityUpdate identity;
            /// <summary>
            /// The recording times of the object's records while it is recorded: positions at their sample time, the rest with them
            /// </summary>
            public readonly TrackTimeBase timeBase = new();
            public Vector playbackAngles = new();
            public bool playbackAnglesValid = false;
            /// <summary>
            /// Recorded: its track has started, with its first position that counts (<see cref="Recorder.StartTrack"/>); until then it
            /// records nothing and is not in the recording
            /// </summary>
            public bool started = false;

            /// <summary>
            /// Next ID
            /// </summary>
            static uint nextId = 10000;

            public Obj(SceneTrack track)
            {
                id = nextId++;
                this.track = track;
                identity = track.Identity;
            }

            public bool IsAircraft => track.Identity.IsAircraft;

            /// <summary>
            /// Time of the last frame
            /// </summary>
            public double EndTime => track.Frames.Count == 0 ? 0.0 : track.Frames[^1].Time;

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
                identity = track.Identity;
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
        /// The recording in progress is an overdub
        /// </summary>
        bool overdubbing;

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
                if (obj.IsAircraft)
                {
                    aircraft.Add(new AircraftView(obj.id, obj.track.Identity.Callsign, obj.track.Identity.Model, obj.skipped));
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
        public static string FirstCallsign(IReadOnlyList<SceneTrack> tracks)
        {
            foreach (var track in tracks)
            {
                if (track.Identity.IsAircraft)
                {
                    return track.Identity.Callsign ?? "";
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
        public string AutoSave(string folder, IReadOnlyList<SceneTrack> tracks, DateTime now) => AutoSave(folder, new SaveCopy(tracks, null), now);

        /// <inheritdoc cref="AutoSave(string, IReadOnlyList{SceneTrack}, DateTime)"/>
        public string AutoSave(string folder, SaveCopy copy, DateTime now)
        {
            string path = UniquePath(folder, BuildAutoSaveFileName(now, FirstCallsign(copy.Tracks)));
            SaveJfs2(path, copy, now, false);
            return path;
        }

        /// <summary>
        /// What Save writes, taken on the sim thread (<see cref="CopyForSave"/>): copies of the tracks, and the finished working file when
        /// it holds exactly the recording (it was not edited since it was recorded), so Save moves that file instead of writing the tracks
        /// </summary>
        public sealed record SaveCopy(IReadOnlyList<SceneTrack> Tracks, string File);

        public void SaveJfs2(string path, IReadOnlyList<SceneTrack> tracks, DateTime now, bool overwrite) => SaveJfs2(path, new SaveCopy(tracks, null), now, overwrite);

        /// <summary>
        /// Save a recording (from <see cref="CopyForSave"/>) as a JFS2 file. A recording that was not edited is its working file, which is
        /// moved into place (design 7); one that was is written by the scene writer. Either way the file is put beside the target first
        /// (a move on the same drive is a rename), so a failure leaves nothing behind and does not destroy a file that is being replaced.
        /// Throws if it can't be written - any thread
        /// </summary>
        /// <param name="now">When the recording ended (local time); a written file's start time is that minus the recording's length</param>
        /// <param name="overwrite">Replace an existing file (the user confirmed it in the dialog) instead of failing</param>
        public void SaveJfs2(string path, SaveCopy copy, DateTime now, bool overwrite)
        {
            if (copy.File != null && SameDrive(copy.File, path))
            {
                File.Move(copy.File, path, overwrite);
                return;
            }
            string temp = path + ".tmp";
            try
            {
                if (copy.File != null)
                {
                    // the working file stays until the recording is known to be saved
                    File.Copy(copy.File, temp, true);
                }
                else
                {
                    IReadOnlyList<SceneTrack> tracks = copy.Tracks;
                    double length = tracks.Count == 0 ? 0 : tracks.Max(track => track.Frames.Count == 0 ? 0 : track.Frames[^1].Time);
                    Jfs2SceneWriter.Write(temp, tracks, AppLabel, now.ToUniversalTime().AddSeconds(-length), _ => { }, VariableNamer());
                }
                File.Move(temp, path, overwrite);
            }
            catch
            {
                TryDeleteQuietly(temp);
                throw;
            }
        }

        static bool SameDrive(string a, string b) =>
            string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The name of a variable for <c>variable-names.txt</c> (spec 3.5): the text its vuid was made from (the SimConnect name, with its
        /// mask if it has one, or the X-Plane dataref), or null when the variable is not known here. Null without variables - any thread
        /// </summary>
        Func<uint, string> VariableNamer()
        {
            Dictionary<uint, VariableMgr.Definition> definitions = main?.variableMgr?.definitions;
            if (definitions == null)
            {
                return null;
            }
            return vuid =>
            {
                if (!definitions.TryGetValue(vuid, out VariableMgr.Definition definition))
                {
                    return null;
                }
                if (!string.IsNullOrEmpty(definition.scName))
                {
                    return definition.mask == 0 ? definition.scName : definition.scName + definition.mask.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                return definition.drName;
            };
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
        /// What is counted of a recording session, for the log
        /// </summary>
        enum Captured
        {
            ObjectPosition,
            AircraftPosition,
            SimEvent,
            IntegerVariables,
            FloatVariables,
            String8Variables,
        }

        /// <summary>
        /// Frame counts recorded during current recording session
        /// </summary>
        readonly Dictionary<Captured, int> recordFrameCounts = [];
        int recordSimErrorCount = 0;
        string recordFirstSimError = "";

        void IncrementRecordFrameCount(Captured type)
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
                    latest = Math.Max(latest, obj.EndTime);
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
                StartTrack(obj);
                // flags: only on-ground, as the old format kept
                double frameTime = obj.timeBase.Position(Time, objTime);
                ObjectPositionUpdate update = SimMessageMapper.ToObjectPositionUpdate(obj.id, frameTime, ref positionVelocity, positionVelocity.ground != 0 ? PositionStateFlags.OnGround : PositionStateFlags.None);
                obj.track.Frames.Add(new SceneObjectPositionFrame(frameTime, update));
                IncrementRecordFrameCount(Captured.ObjectPosition);
                jfs2?.ObjectPosition(obj.id, frameTime, ref update);
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
            // an aircraft at exactly 0/0 is not anywhere yet: MSFS puts the user's aircraft there while it loads a flight (README limit 16)
            if (obj != null && obj.playing == false && !AtNullIsland(aircraftPosition))
            {
                StartTrack(obj);
                // flags: only on-ground, as the old format kept
                double frameTime = obj.timeBase.Position(Time, objTime);
                PositionUpdate update = SimMessageMapper.ToPositionUpdate(obj.id, frameTime, ref aircraftPosition, aircraftPosition.ground != 0 ? PositionStateFlags.OnGround : PositionStateFlags.None);
                obj.track.Frames.Add(new ScenePositionFrame(frameTime, update));
                IncrementRecordFrameCount(Captured.AircraftPosition);
                jfs2?.Position(obj.id, frameTime, ref update);
            }
        }

        /// <summary>
        /// Record sim event
        /// </summary>
        public void Record(Obj obj, uint eventId, uint data)
        {
            // check object
            if (obj != null && obj.playing == false && obj.started)
            {
                // add event, on the time base of the object's positions
                double time = obj.timeBase.Other(Time);
                obj.track.Frames.Add(new SceneEventFrame(time, eventId, data));
                IncrementRecordFrameCount(Captured.SimEvent);
                jfs2?.Event(obj.id, time, eventId, data);
            }
        }

        /// <summary>
        /// Record integer variables
        /// </summary>
        public void Record(Obj obj, Dictionary<uint, int> variables) => RecordVariables(obj, variables, null, null, Captured.IntegerVariables);

        /// <summary>
        /// Record float variables
        /// </summary>
        public void Record(Obj obj, Dictionary<uint, float> variables) => RecordVariables(obj, null, variables, null, Captured.FloatVariables);

        /// <summary>
        /// Record string8 variables
        /// </summary>
        public void Record(Obj obj, Dictionary<uint, string> variables) => RecordVariables(obj, null, null, variables, Captured.String8Variables);

        void RecordVariables(Obj obj, Dictionary<uint, int> integers, Dictionary<uint, float> floats, Dictionary<uint, string> strings, Captured type)
        {
            // check object
            if (obj != null && obj.playing == false && obj.started)
            {
                // add variables (copied: the dictionaries are the object's live set), on the time base of the object's positions
                double time = obj.timeBase.Other(Time);
                obj.track.Frames.Add(new SceneVariablesFrame(time, new VariableSyncUpdate { Entries = SimMessageMapper.ToVariableEntries(integers, floats, strings) }));
                IncrementRecordFrameCount(type);
                jfs2?.Variables(obj.id, time, integers, floats, strings);
            }
        }

        /// <summary>
        /// Start recording a sim object: create its recorded object, link it and add it to the recording
        /// </summary>
        void AddRecordedObject(Sim.Obj simObject)
        {
            string nickname = main.network.Peers.GetNodeName(simObject.ownerNuid);
            var track = new SceneTrack
            {
                Nickname = nickname,
                Owner = ToTrackOwner(simObject.owner),
                SourceApp = AppLabel,
                Simulator = SimulatorLabel(),
                Identity = RecordedIdentity(simObject),
            };
            // its track starts with its first position that counts
            simObject.recorderObj = new Obj(track);
        }

        /// <summary>
        /// Start the track of a recorded object, if it has not started: it opens in the file with the identity it has now, and joins the
        /// recording. An object whose positions never count (an aircraft that stays at 0/0) leaves no track.
        /// </summary>
        void StartTrack(Obj obj)
        {
            if (!obj.started)
            {
                obj.started = true;
                jfs2?.OpenTrack(obj.id, Time, obj.track.Owner, obj.track.Nickname, obj.track.SourceApp, obj.track.Simulator, obj.track.Identity);
                objList.Add(obj);
            }
        }

        /// <summary>
        /// How near 0/0 an aircraft counts as at 0/0 (radians, 0.001 degrees, about 110 m): MSFS 2024 put a loading flight's aircraft
        /// 0.00002 degrees east of it (field test 2026-10-15), and no airport is within hundreds of kilometres
        /// </summary>
        const double NullIslandRadius = 0.001 * Math.PI / 180.0;

        /// <summary>
        /// An aircraft position at 0/0, where no flight is: the simulator's placeholder while it loads one (README limit 16)
        /// </summary>
        public static bool AtNullIsland(in Sim.AircraftPosition position) =>
            Math.Abs(position.latitude) < NullIslandRadius && Math.Abs(position.longitude) < NullIslandRadius;

        /// <summary>
        /// What a recorded object is: what its owner says (its model, not the one it is shown with here), as the network sends it. The
        /// track stands for the object, so the identity's own id is left 0.
        /// </summary>
        static IdentityUpdate RecordedIdentity(Sim.Obj simObject)
        {
            IdentityUpdate identity = SimMessageMapper.BuildIdentity(simObject, 0);
            identity.Model = simObject.ownerModel ?? "";
            identity.IcaoType = simObject.ownerIcaoType ?? "";
            identity.IcaoAirline = simObject.ownerIcaoAirline ?? "";
            return identity;
        }

        /// <summary>
        /// The live capture into a working file (docs/jfs2), or null. A new recording is captured into its working file, which becomes
        /// the recording when it stops (<see cref="ReadBackWorkingFile"/>); an overdub into a working file of its own on the recording's
        /// time line, whose <see cref="BaseKey"/> names the file it adds to, so a recovery lays it over that file.
        /// </summary>
        Jfs2Capture jfs2;

        /// <summary>
        /// The header key of an overdub's working file that names the file it adds to (design 11, open question 3)
        /// </summary>
        public const string BaseKey = "base";

        /// <summary>
        /// Start the JFS2 file for a new recording, or for an overdub of the recording held. A failure is logged and leaves the
        /// recording as it was.
        /// </summary>
        void StartJfs2Capture(bool overdub)
        {
            jfs2 = null;
            if (!overdub)
            {
                DiscardWorkingFile();
            }
            if (!main.settingsJfs2Capture)
            {
                return;
            }
            try
            {
                string folder = WorkingFolder(main.documentsPath);
                Directory.CreateDirectory(folder);
                string name = BuildAutoSaveFileName(DateTime.Now, overdub ? "overdub" : "");
                string path = UniquePath(folder, name);
                // an overdub adds to the latest file of the recording: its own working file, the overdub before, or the file it was opened from
                string baseFile = overdub ? (workingFiles.Count > 0 ? workingFiles[^1] : recordingPath) : null;
                List<KeyValuePair<string, string>> more = baseFile == null ? [] : [new(BaseKey, baseFile)];
                jfs2 = Jfs2Capture.Create(path, AppLabel, SimulatorLabel(), DateTime.UtcNow.AddSeconds(-Time), main.MonitorEvent, more);
                // replaced, not changed: other threads read the list (FindUnsavedWorkingFiles)
                workingFiles = [.. workingFiles, path];
                main.MonitorEvent("Recorder: writing the working file '" + path + "'" + (baseFile == null ? "" : ", an overdub of '" + baseFile + "'") + ".");
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
        /// The working files of the recording the recorder holds, oldest first: its own and those of its overdubs. Empty when the
        /// recording was opened from a file or has been saved, and has not been overdubbed since.
        /// </summary>
        List<string> workingFiles = [];

        /// <summary>
        /// The finished working file that holds exactly the recording, or null once the recording is edited (trimmed, an object
        /// removed, a recording added, overdubbed) or was not recorded here. Save moves it into place instead of writing the tracks.
        /// </summary>
        string unedited;

        /// <summary>
        /// The file the recording was opened from or last saved to, which an overdub names as its base when there is no working file
        /// </summary>
        string recordingPath;

        /// <summary>
        /// The recording is being changed from what its working file holds, so Save must write the tracks
        /// </summary>
        void Edited()
        {
            unedited = null;
            readingBack = null;
        }

        /// <summary>
        /// The recording has been saved to the path, so its working files are not needed any more - sim thread
        /// </summary>
        public void Saved(string path)
        {
            DiscardWorkingFile();
            recordingPath = path;
        }

        /// <summary>
        /// The recording has been saved or its owner let it go, so its working files are not needed any more - sim thread
        /// </summary>
        public void DiscardWorkingFile()
        {
            List<string> paths = workingFiles;
            workingFiles = [];
            unedited = null;
            readingBack = null;
            foreach (string path in paths)
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
            List<string> current = workingFiles;
            return Directory.EnumerateFiles(folder, "*" + Jfs2.Extension)
                .Where(path => !current.Contains(path, StringComparer.OrdinalIgnoreCase) && !InUse(path))
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
        /// Record the identity of a recorded object again when it has changed (callsign, model) since the last one recorded
        /// </summary>
        public void RecordIdentity(Sim.Obj simObject)
        {
            if (simObject.recorderObj is Obj recorded && recorded.playing == false)
            {
                IdentityUpdate identity = RecordedIdentity(simObject);
                if (!recorded.started)
                {
                    // not started yet: its track opens with this
                    recorded.track.Identity = identity;
                    recorded.identity = identity;
                }
                else if (!identity.SameAs(in recorded.identity))
                {
                    double time = recorded.timeBase.Other(Time);
                    recorded.track.Frames.Add(new SceneIdentityFrame(time, identity));
                    recorded.identity = identity;
                    jfs2?.Identity(recorded.id, time, identity);
                }
            }
        }

        /// <summary>
        /// Finish the JFS2 file: close the open tracks and write its index and the names of its variables. Returns the file when it
        /// was written whole, else null; a file with nothing to play is deleted.
        /// </summary>
        string FinishJfs2Capture()
        {
            if (jfs2 == null)
            {
                return null;
            }
            Jfs2Capture finished = jfs2;
            jfs2 = null;
            finished.Finish(VariableNamer());
            if (finished.PositionCount + finished.ObjectPositionCount == 0)
            {
                // nothing to play, nothing to recover
                workingFiles = workingFiles.Where(path => path != finished.Path).ToList();
                TryDeleteQuietly(finished.Path);
                return null;
            }
            if (finished.Failed)
            {
                return null;
            }
            main.MonitorEvent("Recorder: wrote the JFS2 recording '" + finished.Path + "' (" + finished.TrackCount + " objects, " + finished.ClosedCount + " left before the end, " + finished.PositionCount + " aircraft positions, " + finished.ObjectPositionCount + " object positions, " + finished.VariableRecordCount + " variable records, " + finished.EventCount + " events, " + finished.IdentityChangeCount + " identity changes, " + finished.HeldCount + " records held back).");
            return finished.Path;
        }

        /// <summary>
        /// The working file being read back on a worker thread (<see cref="ReadBackWorkingFile"/>), or null. Anything that changes the
        /// recording meanwhile clears it, so a read that ends after that is dropped.
        /// </summary>
        string readingBack;

        /// <summary>
        /// Make the working file of a recording that has just stopped the recording: it is read on a worker thread, and its tracks
        /// replace the ones recorded in memory on the sim thread (<paramref name="post"/>), so what plays, is edited and is saved is what
        /// the file holds, and Save of an unedited recording moves the file. Until then, or if the file cannot be read, the tracks in
        /// memory stay and Save writes them - sim thread
        /// </summary>
        internal Task ReadBackWorkingFile(string path, Action<Action> post)
        {
            readingBack = path;
            return Task.Run(() =>
            {
                List<SceneTrack> tracks = ReadWorkingFile(path);
                if (tracks != null)
                {
                    post(() => SwapInWorkingFile(path, tracks));
                }
            });
        }

        /// <summary>
        /// Read a working file into tracks, or null if it cannot be read. Save may delete it meanwhile - any thread
        /// </summary>
        List<SceneTrack> ReadWorkingFile(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                return ParseJfs2(stream, null);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                main?.MonitorEvent("Recorder: cannot read back the working file '" + path + "' (" + ex.Message + "); the recording in memory is kept.");
                return null;
            }
        }

        /// <summary>
        /// The tracks read back from the working file replace the ones recorded in memory, unless the recording was changed, saved,
        /// recorded again or played since it stopped. Returns whether they did - sim thread
        /// </summary>
        internal bool SwapInWorkingFile(string path, List<SceneTrack> tracks)
        {
            if (readingBack != path || recording || playing)
            {
                main?.MonitorEvent("Recorder: the working file '" + path + "' was read back after the recording changed; the recording in memory is kept.");
                return false;
            }
            readingBack = null;
            objList = tracks.ConvertAll(track => new Obj(track));
            unedited = path;
            PublishStatus();
            return true;
        }

        /// <summary>
        /// A sim object has gone: a recorded one's track is closed (spec 7.1), so it is in no later keyframe - sim thread
        /// </summary>
        public void ObjectLeft(Sim.Obj simObject) => EndRecordedTrack(simObject);

        public enum Step { None, Start, End }

        /// <summary>
        /// What a pass of a recording does with a sim object: start a track for one flagged to record and not recorded, end the track
        /// of one recorded and no longer flagged (README limit 7)
        /// </summary>
        /// <param name="linked">The recorded object it is linked to, or null</param>
        public static Step RecordingStep(bool flagged, Obj linked) => (flagged, linked) switch
        {
            (true, null) => Step.Start,
            (false, { playing: false }) => Step.End,
            _ => Step.None,
        };

        /// <summary>
        /// A recorded object is recorded no more (it has gone, or is no longer flagged to record): its track is closed, and the
        /// object is unlinked, so it is a new track if it is recorded again (design 5.3) - sim thread
        /// </summary>
        void EndRecordedTrack(Sim.Obj simObject)
        {
            if (recording && simObject.recorderObj is Obj recorded && recorded.playing == false)
            {
                jfs2?.CloseTrack(recorded.id, recorded.timeBase.Other(Time));
                simObject.recorderObj = null;
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
                    recordingPath = null;
                }
                else
                {
                    // the overdub is merged into the recording when it is saved
                    Edited();
                }
                // a new recording is captured into its working file, an overdub into one of its own
                overdubbing = overdub;
                StartJfs2Capture(overdub);

                // for each object in the sim
                int objectCount = 0;
                int aircraftCount = 0;
                foreach (var simObject in main.sim.objectList)
                {
                    // check if object is flagged to record
                    if (simObject.record)
                    {
                        AddRecordedObject(simObject);
                        objectCount++;
                        if (simObject.recorderObj.IsAircraft)
                        {
                            aircraftCount++;
                        }
                    }
                }

                // summary
                main.MonitorEvent("Recorder: start recording " + objectCount + " objects (" + aircraftCount + " aircraft). SimConnected=" + main.sim.Connected + ", NetworkConnected=" + main.network.Connected + ".");

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
                string captured = FinishJfs2Capture();
                LogRecordFrameCounts();

                // a clock that stepped back (limit 6 in docs/jfs2/README.md) leaves frames out of order; the player wants them in order
                foreach (var obj in objList)
                {
                    SortByTime(obj.track.Frames);
                }

                int positionCount = recordFrameCounts.GetValueOrDefault(Captured.ObjectPosition) + recordFrameCounts.GetValueOrDefault(Captured.AircraftPosition);
                int variableCount = recordFrameCounts.GetValueOrDefault(Captured.IntegerVariables) + recordFrameCounts.GetValueOrDefault(Captured.FloatVariables) + recordFrameCounts.GetValueOrDefault(Captured.String8Variables);
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

                // a new recording is what its working file holds; an overdub stays merged in memory until Save
                if (captured != null && !overdubbing)
                {
                    ReadBackWorkingFile(captured, main.PostToSim);
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
                Edited();
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
        /// Jump to a point in the recording. Each object is put in the state the frames up to that time leave it in (design 5.4): its
        /// latest position and identity, and its variables merged.
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
                // one that has played to its end plays again from a jump back; not while recording, whose own objects play nothing
                if (playing && !recording && !obj.playing)
                {
                    obj.Play();
                }

                // check if object is playing
                if (obj.playing)
                {
                    // fold the frames before the time
                    SceneState state = obj.track.StateAt(time);
                    obj.frameIndex = state.Next;
                    obj.identity = state.Identity;

                    // not there at that time (its track starts later, or has ended): out of the sim, which a jump back may need
                    if (!obj.track.ShownAt(time))
                    {
                        obj.playbackAnglesValid = false;
                        main.sim ?. RemoveObject(new NodeId(), obj.id);
                    }
                    else if (state.Position != null)
                    {
                        // the position creates the object, then its state goes to it
                        obj.playbackAnglesValid = false;
                        UpdatePosition(obj, state.Position, state.Position, state.Position.Time);
                        ApplyVariables(obj, state.Integers, state.Floats, state.Strings);
                        // reset object
                        main.sim ?. ResetObject(new NodeId(), obj.id);
                        // one that was out of the sim when playing was paused is paused now
                        if (paused)
                        {
                            main.sim ?. PauseObject(new NodeId(), obj.id, true);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Put frames recorded out of time order in order, keeping the order of frames at the same time
        /// </summary>
        static void SortByTime(List<SceneFrame> frames)
        {
            for (int i = 1; i < frames.Count; i++)
            {
                if (frames[i].Time < frames[i - 1].Time)
                {
                    List<SceneFrame> sorted = [.. frames.OrderBy(frame => frame.Time)];
                    frames.Clear();
                    frames.AddRange(sorted);
                    return;
                }
            }
        }

        static bool IsPositionFrame(SceneFrame frame) => frame is ScenePositionFrame or SceneObjectPositionFrame;

        static float Lerp(float a, float b, double t) => (float)(a + (b - a) * t);
        static double Lerp(double a, double b, double t) => a + (b - a) * t;
        static double Blend(SceneFrame from, SceneFrame to, double time)
        {
            double dt = to.Time - from.Time;
            return dt > 0.000001 ? Math.Clamp((time - from.Time) / dt, 0.0, 1.0) : 0.0;
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

        static Sim.ObjectPositionVelocity Interpolate(in Sim.ObjectPositionVelocity a, in Sim.ObjectPositionVelocity b, double t, Vector angles)
        {
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

        static Sim.AircraftPosition Interpolate(in Sim.AircraftPosition a, in Sim.AircraftPosition b, double t, Vector angles)
        {
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
            List<SceneFrame> frames = obj.track.Frames;
            SceneFrame previous = null;
            SceneFrame next = null;

            for (int i = Math.Min(obj.frameIndex - 1, frames.Count - 1); i >= 0; i--)
            {
                if (IsPositionFrame(frames[i]))
                {
                    previous = frames[i];
                    break;
                }
            }

            for (int i = Math.Max(obj.frameIndex, 0); i < frames.Count; i++)
            {
                if (IsPositionFrame(frames[i]))
                {
                    next = frames[i];
                    break;
                }
            }

            if (previous == null && next == null)
            {
                return;
            }

            UpdatePosition(obj, previous ?? next, next ?? previous, time);
        }

        /// <summary>
        /// Show the object between two positions (the same one twice to show it there), as it is at that time
        /// </summary>
        void UpdatePosition(Obj obj, SceneFrame previous, SceneFrame next, double time)
        {
            IdentityUpdate id = obj.identity;
            double t = Blend(previous, next, time);
            switch ((previous, next))
            {
                case (ScenePositionFrame from, ScenePositionFrame to):
                {
                    Sim.AircraftPosition a = SimMessageMapper.ToAircraftPosition(from.Position);
                    Sim.AircraftPosition b = SimMessageMapper.ToAircraftPosition(to.Position);
                    Vector angles = InterpolateAngles(obj, a.pitch, a.heading, a.bank, b.pitch, b.heading, b.bank, t);
                    Sim.AircraftPosition data = Interpolate(a, b, t, angles);
                    main.sim?.UpdateAircraft(new NodeId(), obj.id, false, id.IsPlane, id.Callsign ?? "", id.Registration ?? "", obj.track.Nickname, id.Model ?? "", id.Livery ?? "", id.IcaoType ?? "", id.IcaoAirline ?? "", id.FlightNumber ?? "", id.ClassCode ?? "", id.Wtc ?? "", id.ClassCodeConfirmed, id.TypeRole, time, ref data);
                    break;
                }
                case (SceneObjectPositionFrame from, SceneObjectPositionFrame to):
                {
                    Sim.ObjectPositionVelocity a = SimMessageMapper.ToPositionVelocity(from.Position);
                    Sim.ObjectPositionVelocity b = SimMessageMapper.ToPositionVelocity(to.Position);
                    Vector angles = InterpolateAngles(obj, a.pitch, a.heading, a.bank, b.pitch, b.heading, b.bank, t);
                    Sim.ObjectPositionVelocity data = Interpolate(a, b, t, angles);
                    main.sim?.UpdateObject(new NodeId(), obj.id, id.Model ?? "", id.Livery ?? "", id.IcaoType ?? "", id.IcaoAirline ?? "", id.ClassCode ?? "", id.Wtc ?? "", id.ClassCodeConfirmed, id.TypeRole, time, ref data);
                    break;
                }
            }
        }

        /// <summary>
        /// Send variables to the object (null where there are none of a kind)
        /// </summary>
        void ApplyVariables(Obj obj, Dictionary<uint, int> integers, Dictionary<uint, float> floats, Dictionary<uint, string> strings)
        {
            if (integers != null) main.sim?.UpdateAircraft(new NodeId(), obj.id, integers);
            if (floats != null) main.sim?.UpdateAircraft(new NodeId(), obj.id, floats);
            if (strings != null) main.sim?.UpdateAircraft(new NodeId(), obj.id, strings);
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
                            List<SceneFrame> frames = obj.track.Frames;
                            while (obj.frameIndex < frames.Count)
                            {
                                // get frame
                                SceneFrame frame = frames[obj.frameIndex];
                                // wait for frame time
                                if (Time <= frame.Time)
                                {
                                    break;
                                }

                                // check frame type (positions are interpolated below)
                                switch (frame)
                                {
                                    case SceneEventFrame sim:
                                        main.sim ?. UpdateAircraft(new NodeId(), obj.id, sim.EventId, sim.Data, true);
                                        break;
                                    case SceneVariablesFrame variables:
                                    {
                                        var (integers, floats, strings) = variables.ByKind();
                                        ApplyVariables(obj, integers, floats, strings);
                                        break;
                                    }
                                    case SceneIdentityFrame change:
                                        // the next position shows it as this
                                        obj.identity = change.Identity;
                                        break;
                                }
                                // next frame
                                obj.frameIndex++;
                            }

                            // continuously update interpolated position, once the track has started: one that opens later in
                            // the recording is not shown at its first position before then
                            if (obj.track.ShownAt(Time))
                            {
                                UpdateInterpolatedPosition(obj, Time);
                            }

                            // end object playback when all frames are consumed
                            if (obj.frameIndex >= frames.Count)
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
                    switch (RecordingStep(simObject.record, simObject.recorderObj))
                    {
                        case Step.Start:
                            AddRecordedObject(simObject);
                            break;
                        case Step.End:
                            EndRecordedTrack(simObject);
                            break;
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
        /// Copies of the tracks with their own frame lists, so a recording can be written on
        /// another thread while this one goes on (sim thread)
        /// </summary>
        public SaveCopy CopyForSave()
        {
            return new SaveCopy(objList.ConvertAll(o => o.track.Copy()), recording ? null : unedited);
        }

#region Reader

        /// <summary>
        /// Parse a recording into tracks without touching the recorder, so a file can be read on
        /// any thread and then applied on the sim thread with <see cref="Load"/>. Null when the
        /// recording can't be used. Throws with a message for the user when the file is damaged.
        /// </summary>
        public List<SceneTrack> Parse(BinaryReader reader, List<string> warnings = null) => Parse(reader, warnings, 0);

        /// <summary>
        /// The most overdubs read under one another (<see cref="BaseKey"/>), against a chain that loops
        /// </summary>
        const int MaxOverdubDepth = 32;

        List<SceneTrack> Parse(BinaryReader reader, List<string> warnings, int depth)
        {
            // get version
            short version = reader.ReadInt16();
            // log version
            main?.MonitorEvent("Recorder: reading recording stream (version " + version + ").");
            // a JFS2 file starts with FA 00, which reads as version 250 (docs/jfs2/format-spec.md section 2)
            if (version == Jfs2.LegacyVersionSignature && reader.BaseStream.CanSeek)
            {
                reader.BaseStream.Position -= sizeof(short);
                return ParseJfs2(reader.BaseStream, warnings, depth);
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
        List<SceneTrack> ParseLegacy(short version, Stream rest, List<string> warnings)
        {
            // the importer wants the whole file, version included
            var whole = new MemoryStream();
            whole.Write(BitConverter.GetBytes(version));
            rest.CopyTo(whole);
            whole.Position = 0;

            LegacyJfsImporter.Result imported = LegacyJfsImporter.Read(whole);
            List<SceneTrack> tracks = Loaded(imported.Scene, warnings);
            main?.MonitorEvent("Recorder: loaded " + tracks.Count(t => t.Identity.IsAircraft) + " aircraft, " + tracks.Count(t => !t.Identity.IsAircraft) + " objects, " + tracks.Sum(t => t.Frames.Count) + " frames (trailing strings: " + imported.Layout + ").");
            return tracks;
        }

        /// <summary>
        /// Read a JFS2 recording into tracks, logging what was skipped. Throws, with a message for the user, if it can't be played.
        /// </summary>
        List<SceneTrack> ParseJfs2(Stream stream, List<string> warnings, int depth = 0)
        {
            Jfs2Scene scene = Jfs2Scene.Load(stream);
            List<SceneTrack> tracks = Loaded(scene, warnings);
            main?.MonitorEvent("Recorder: read a JFS2 recording with " + tracks.Count + " objects.");
            string baseFile = BaseOf(scene.Metadata);
            if (baseFile == null)
            {
                return tracks;
            }
            // an overdub's working file: the recording it adds to first, then the overdub's tracks at their own times
            List<SceneTrack> under = null;
            string problem = "it is gone";
            if (depth < MaxOverdubDepth && File.Exists(baseFile))
            {
                try
                {
                    using FileStream baseStream = File.OpenRead(baseFile);
                    under = Parse(new BinaryReader(baseStream), warnings, depth + 1);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
                {
                    problem = ex.Message;
                }
            }
            if (under == null)
            {
                string warning = "The recording this overdub adds to, '" + baseFile + "', cannot be read (" + problem + "); only the overdub is played.";
                main?.MonitorEvent("Recorder: " + warning);
                warnings?.Add(warning);
                return tracks;
            }
            under.AddRange(tracks);
            return under;
        }

        static string BaseOf(IReadOnlyList<KeyValuePair<string, string>> metadata) =>
            metadata.FirstOrDefault(pair => pair.Key == BaseKey).Value is { Length: > 0 } value ? value : null;

        /// <summary>
        /// A working file and, for an overdub's, the files under it (<see cref="BaseKey"/>) that are in the same folder, oldest first:
        /// the working files a recovery of it takes over. A file that cannot be read ends the chain - any thread
        /// </summary>
        public static List<string> WorkingFileChain(string path)
        {
            List<string> chain = [];
            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            for (string file = path; file != null && chain.Count < MaxOverdubDepth; )
            {
                if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(file)), folder, StringComparison.OrdinalIgnoreCase)
                    || chain.Contains(file, StringComparer.OrdinalIgnoreCase) || !File.Exists(file))
                {
                    break;
                }
                chain.Add(file);
                try
                {
                    using RecordingReader reader = RecordingReader.Open(file);
                    file = BaseOf(reader.Header.Metadata);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Jfs2FormatException)
                {
                    break;
                }
            }
            chain.Reverse();
            return chain;
        }

        /// <summary>
        /// The tracks of a scene, its notes logged and its warnings passed on
        /// </summary>
        List<SceneTrack> Loaded(Jfs2Scene scene, List<string> warnings)
        {
            foreach (string note in scene.Notes)
            {
                main?.MonitorEvent("Recorder: " + note);
            }
            warnings?.AddRange(scene.Warnings);
            return scene.Tracks;
        }

        /// <summary>
        /// Replace the recording with parsed tracks, or append them after its end (sim thread)
        /// </summary>
        /// <param name="recoveredFrom">The working file the recording is recovered from; it and the files under it (an overdub's) stay
        /// the recording's working files until it is saved</param>
        /// <param name="openedFrom">The file the recording was opened from, which an overdub of it names as its base</param>
        public void Load(List<SceneTrack> tracks, bool append, string recoveredFrom = null, string openedFrom = null)
        {
            if (tracks == null)
            {
                return;
            }
            if (append)
            {
                double appendTime = EndTime;
                main.MonitorEvent("Recorder: appending recording from " + appendTime.ToString("0.00") + "s.");
                foreach (var track in tracks)
                {
                    track.Shift(appendTime);
                }
            }
            else
            {
                // clear all aircraft; the recording that is replaced is saved or given up, so its working file goes
                objList.Clear();
                DiscardWorkingFile();
                recordingPath = openedFrom;
            }
            // what is loaded is written by the scene writer on Save: an opened file has been read (and maybe repaired), a recovered
            // one may be unfinished
            Edited();
            objList.AddRange(tracks.Select(track => new Obj(track)));
            // a recording recovered from its working files stays unsaved until it is saved, which then removes them
            if (!append && recoveredFrom != null)
            {
                workingFiles = WorkingFileChain(recoveredFrom);
            }
            PublishStatus();
        }

#endregion

#region Edit

        /// <summary>
        /// Cut the recording before the current time, which becomes its start (sim thread). Each object starts in the state the cut
        /// frames left it in (<see cref="SceneTrack.TrimStart"/>); an object gone by then is removed.
        /// </summary>
        public void TrimFromStart()
        {
            Edited();
            // get current time
            double now = Time;
            // for each object; one gone by now is taken out of the list
            foreach (var obj in objList.ToList())
            {
                if (obj.track.TrimStart(now))
                {
                    obj.frameIndex = 0;
                    obj.identity = obj.track.Identity;
                }
                else
                {
                    Remove(obj.id);
                }
            }

            // reset time
            startTime = main.ElapsedTime;
            pauseTime = 0.0;
        }


        /// <summary>
        /// Cut the recording from the current time on (sim thread). An object that only came later is removed.
        /// </summary>
        public void TrimToEnd()
        {
            Edited();
            // get current time
            double now = Time;
            // for each object; one with nothing left to show is taken out of the list
            foreach (var obj in objList.ToList())
            {
                if (obj.track.TrimEnd(now))
                {
                    obj.frameIndex = Math.Min(obj.frameIndex, obj.track.Frames.Count);
                }
                else
                {
                    Remove(obj.id);
                }
            }
        }

#endregion
    }
}
