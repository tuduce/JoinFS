using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using JoinFS.Properties;
using JoinFS.UI.Localization;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// The Recorder tab on the real recorder, which belongs to the sim thread: its flags and published end time are read from any thread, and
    /// everything else is asked of the sim thread, as the old RecorderForm did. Files are read and written off the sim thread and handed
    /// over to it.
    /// </summary>
    class LiveRecorderSource : IRecorderSource
    {
        readonly Main main;

        // the file the recording came from or went to, until another recording is made
        string loadedName = "";

        public LiveRecorderSource(Main main)
        {
            this.main = main;
        }

        public RecorderStatus GetStatus()
        {
            Recorder recorder = main.recorder;

            // the time means something while the recorder works; paused it is where it stopped
            double time = recorder.Active ? recorder.Time : 0.0;
            return new RecorderStatus(recorder.recording, recorder.playing, recorder.paused, recorder.Empty, time, recorder.EndTimeView);
        }

        public IReadOnlyList<RecordedAircraft> GetLoadedRecording()
        {
            List<RecordedAircraft> aircraft = [];
            foreach (Recorder.AircraftView view in main.recorder.AircraftSnapshot)
            {
                aircraft.Add(new RecordedAircraft(view.Callsign, view.Model, view.Id.ToString(), view.Skipped));
            }
            return aircraft;
        }

        public string LoadedRecordingName => loadedName;

        public bool Loop
        {
            get => main.settingsLoop;
            set
            {
                main.settingsLoop = value;
                Settings.Default.Loop = value;
                Settings.Default.Save();
            }
        }

        public string RecordingFolder => Settings.Default.RecordingFolder;

        /// <summary>
        /// Ask the sim thread to do something to the recorder and wait for it
        /// </summary>
        void OnSim(Action<Recorder> action) => main.InvokeOnSim(sim =>
        {
            action(main.recorder);
            return true;
        });

        public void Record()
        {
            OnSim(recorder => recorder.StartRecord(false));
            loadedName = "";
        }

        public void TogglePlay() => OnSim(recorder =>
        {
            // playing, the button pauses it, or goes on
            if (recorder.playing)
            {
                recorder.Pause();
            }
            else
            {
                recorder.StartPlay();
            }
        });

        public void Overdub() => OnSim(recorder =>
        {
            recorder.StartPlay();
            recorder.StartRecord(true);
        });

        public void Stop() => OnSim(recorder => recorder.Stop());

        public void Seek(double seconds) => OnSim(recorder => recorder.Jump(seconds));

        public void TrimStart() => OnSim(recorder =>
        {
            recorder.TrimFromStart();
            recorder.PublishStatus();
        });

        public void TrimEnd() => OnSim(recorder =>
        {
            recorder.TrimToEnd();
            recorder.PublishStatus();
        });

        public void SkipAircraft(string aircraftId)
        {
            if (uint.TryParse(aircraftId, out uint id))
            {
                OnSim(recorder => recorder.Skip(id));
            }
        }

        public async Task OpenAsync(string path, bool append)
        {
            main.MonitorEvent("Recorder: " + (append ? "appending" : "opening") + " recording file '" + path + "'.");

            // parse off the sim thread, then apply and play on it
            List<Recorder.Obj> objects = await Task.Run(() =>
            {
                using FileStream stream = File.OpenRead(path);
                return main.recorder.Parse(new BinaryReader(stream));
            });
            if (objects == null)
            {
                // an old version: Parse has said so in the log
                throw new InvalidOperationException(Resources.Strings.OldRecording);
            }

            OnSim(recorder =>
            {
                recorder.Load(objects, append);
                recorder.StartPlay();
            });

            // save folder
            Settings.Default.RecordingFolder = Path.GetDirectoryName(path);
            if (!append)
            {
                loadedName = Path.GetFileName(path);
            }
        }

        public async Task SaveAsync(string path)
        {
            // copy on the sim thread (which owns the recording) before opening the file, which truncates it:
            // a failed copy must not leave an empty file marked as saved
            List<Recorder.Obj> objects = main.InvokeOnSim(sim => main.recorder.CopyForSave());
            if (objects == null)
            {
                main.MonitorEvent("ERROR - Recording not saved: the simulator thread did not respond. Please try again.");
                throw new InvalidOperationException(Loc.T("Recording not saved: the simulator thread did not respond. Please try again."));
            }

            await Task.Run(() =>
            {
                using FileStream stream = File.Create(path);
                main.recorder.Write(new BinaryWriter(stream), objects);
            });

            // save folder
            Settings.Default.RecordingFolder = Path.GetDirectoryName(path);
            loadedName = Path.GetFileName(path);
        }
    }
}
