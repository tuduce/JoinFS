using System;
using System.Collections.Generic;
using System.IO;
using JoinFS.Net;
using JoinFS.Net.Jfp2.Codecs;

namespace JoinFS.Recording
{
    /// <summary>
    /// Reads a JFS2 file into the objects the recorder plays (<see cref="Recorder.Obj"/>), so that opening, playing, jumping,
    /// trimming and the player UI work on it unchanged. The file is read by <see cref="Jfs2Scene"/> (shared with RecordingXRay);
    /// this maps its canonical messages onto the old frames. It loads everything, which the stage 5 player replaces with one that reads chunk by
    /// chunk. Knows nothing of Main, so it is testable without a simulator.
    /// </summary>
    public static class Jfs2Loader
    {
        /// <param name="Objects">The recorded objects, in the order their tracks opened. Objects without a position are left out.</param>
        /// <param name="Notes">Everything the load has to say, for the log.</param>
        /// <param name="Warnings">The notes the user should see: parts of the file that were lost or are not played.</param>
        public sealed record Result(List<Recorder.Obj> Objects, IReadOnlyList<string> Notes, IReadOnlyList<string> Warnings);

        /// <exception cref="InvalidOperationException">The file is not a JFS2 file this build can play; the message says why.</exception>
        public static Result Load(Stream stream)
        {
            Jfs2Scene scene = Jfs2Scene.Load(stream);
            return new Result(Objects(scene), scene.Notes, scene.Warnings);
        }

        /// <summary>The objects of a scene, whether it was read from a JFS2 file or imported from an old recording.</summary>
        public static List<Recorder.Obj> Objects(Jfs2Scene scene)
        {
            List<Recorder.Obj> objects = [];
            foreach (SceneTrack track in scene.Tracks)
            {
                objects.Add(Build(track));
            }
            return objects;
        }

        static Recorder.Obj Build(SceneTrack track)
        {
            IdentityUpdate id = track.Identity;
            Recorder.Obj obj = id.IsAircraft
                ? new Recorder.Aircraft { plane = id.IsPlane, callsign = id.Callsign, nickname = track.Nickname }
                : new Recorder.Obj();
            obj.model = id.Model;
            obj.livery = id.Livery;
            obj.icaoType = id.IcaoType;
            obj.icaoAirline = id.IcaoAirline;
            obj.typerole = id.TypeRole;
            // what the track-open said, so saving the recording again says it too
            obj.owner = Recorder.FromTrackOwner(track.Owner);
            obj.sourceApp = track.SourceApp;
            obj.simulator = track.Simulator;
            foreach (SceneFrame frame in track.Frames)
            {
                switch (frame)
                {
                    case ScenePositionFrame position:
                    {
                        Sim.AircraftPosition data = SimMessageMapper.ToAircraftPosition(position.Position);
                        obj.frames.Add(new Recorder.AircraftPositionFrame(frame.Time, ref data));
                        break;
                    }
                    case SceneObjectPositionFrame position:
                    {
                        Sim.ObjectPositionVelocity data = SimMessageMapper.ToPositionVelocity(position.Position);
                        obj.frames.Add(new Recorder.ObjectPositionFrame(frame.Time, ref data));
                        break;
                    }
                    case SceneVariablesFrame variables:
                        AddVariableFrames(obj, frame.Time, variables.Variables);
                        break;
                    case SceneEventFrame sim:
                        obj.frames.Add(new Recorder.SimEventFrame(frame.Time, sim.EventId, sim.Data));
                        break;
                }
            }
            return obj;
        }

        /// <summary>
        /// One variables record becomes up to three of the old frames, one per value kind. A frame holds only what the record
        /// changed; the old player merges frames into the object's variable set, which is how an upsert reads (spec 7.6).
        /// </summary>
        static void AddVariableFrames(Recorder.Obj obj, double time, VariableSyncUpdate update)
        {
            Dictionary<uint, int> integers = [];
            Dictionary<uint, float> floats = [];
            Dictionary<uint, string> strings = [];
            foreach (VariableEntry entry in update.Entries)
            {
                switch (entry.Kind)
                {
                    case VariableKind.Int32: integers[entry.Vuid] = entry.IntValue; break;
                    case VariableKind.Float32: floats[entry.Vuid] = entry.FloatValue; break;
                    case VariableKind.String8: strings[entry.Vuid] = entry.StringValue ?? ""; break;
                }
            }
            if (integers.Count > 0) obj.frames.Add(new Recorder.IntegerVariablesFrame(time, integers));
            if (floats.Count > 0) obj.frames.Add(new Recorder.FloatVariablesFrame(time, floats));
            if (strings.Count > 0) obj.frames.Add(new Recorder.String8VariablesFrame(time, strings));
        }
    }
}
