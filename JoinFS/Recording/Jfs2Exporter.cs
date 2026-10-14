using System;
using System.Collections.Generic;
using System.Linq;
using JoinFS.Net;
using JoinFS.Net.Jfp2.Codecs;

namespace JoinFS.Recording
{
    /// <summary>
    /// Writes recorded objects (<see cref="Recorder.Obj"/>) as a JFS2 file: the importer of docs/jfs2/recorder-v2-design.md section 8.
    /// The old <c>.jfs</c> reader (<see cref="Recorder.Parse"/>) produces the objects, so every file the current release plays can be
    /// converted, and a recording that was edited in memory can be saved in the new format. Knows nothing of Main, so it is testable
    /// without a simulator.
    /// </summary>
    public static class Jfs2Exporter
    {
        /// <param name="Tracks">Objects written; objects without a position frame are left out, as the loader does.</param>
        /// <param name="Skipped">Frames that did not fit their object (a position of the wrong kind) and were dropped.</param>
        public sealed record Result(int Tracks, int Frames, int Skipped);

        /// <summary>
        /// Writes the file. Throws if it cannot be created or written; a half-written file is not left behind.
        /// </summary>
        /// <param name="app">For the header's <c>app</c> metadata and each track's source application, for example "JoinFS 26.10 (converted)".</param>
        /// <param name="startUtc">When recording time 0 happened, if known; the old format does not say.</param>
        public static Result Write(string path, IReadOnlyList<Recorder.Obj> objects, string app, DateTime startUtc, Action<string> log)
        {
            Jfs2Capture capture = Jfs2Capture.Create(path, app, "", startUtc, log);
            try
            {
                Result result = Write(capture, objects, app);
                capture.Finish();
                if (capture.Failed)
                {
                    throw new InvalidOperationException("The JFS2 file could not be written; see the log.");
                }
                return result;
            }
            catch
            {
                capture.Dispose();
                try { System.IO.File.Delete(path); } catch (System.IO.IOException) { }
                throw;
            }
        }

        /// <summary>Feeds the objects into an open capture, frames of all objects in time order (a chunk may not go back in time, spec 5).</summary>
        public static Result Write(Jfs2Capture capture, IReadOnlyList<Recorder.Obj> objects, string app)
        {
            // every object with a position gets a track, numbered from 1 in the order of the list
            List<(Recorder.Obj Obj, uint Track)> tracks = [];
            foreach (Recorder.Obj obj in objects)
            {
                if (obj.frames.Any(IsPosition))
                {
                    tracks.Add((obj, (uint)tracks.Count + 1));
                }
            }

            // all frames in time order; equal times keep object order, then frame order, so the result is deterministic
            List<(double Time, int Object, int Index)> order = [];
            for (int o = 0; o < tracks.Count; o++)
            {
                List<Recorder.Frame> frames = tracks[o].Obj.frames;
                for (int i = 0; i < frames.Count; i++)
                {
                    order.Add((frames[i].time, o, i));
                }
            }
            order.Sort((a, b) =>
            {
                int byTime = a.Time.CompareTo(b.Time);
                if (byTime != 0) return byTime;
                int byObject = a.Object.CompareTo(b.Object);
                return byObject != 0 ? byObject : a.Index.CompareTo(b.Index);
            });

            bool[] opened = new bool[tracks.Count];
            int written = 0, skipped = 0;
            foreach (var (time, o, index) in order)
            {
                (Recorder.Obj obj, uint track) = tracks[o];
                if (!opened[o])
                {
                    opened[o] = true;
                    capture.OpenTrack(track, time, TrackOwner.Recorded, (obj as Recorder.Aircraft)?.nickname ?? "", app, "", Identity(obj, track));
                }

                Recorder.Frame frame = obj.frames[index];
                switch (frame)
                {
                    case Recorder.AircraftPositionFrame position when obj is Recorder.Aircraft:
                    {
                        Sim.AircraftPosition data = position.data;
                        PositionUpdate update = SimMessageMapper.ToPositionUpdate(track, time, ref data, data.ground != 0 ? PositionStateFlags.OnGround : PositionStateFlags.None);
                        capture.Position(track, time, ref update);
                        break;
                    }
                    case Recorder.ObjectPositionFrame position when obj is not Recorder.Aircraft:
                    {
                        Sim.ObjectPositionVelocity data = position.data;
                        ObjectPositionUpdate update = SimMessageMapper.ToObjectPositionUpdate(track, time, ref data, data.ground != 0 ? PositionStateFlags.OnGround : PositionStateFlags.None);
                        capture.ObjectPosition(track, time, ref update);
                        break;
                    }
                    case Recorder.SimEventFrame sim:
                        capture.Event(track, time, sim.eventId, sim.data);
                        break;
                    case Recorder.IntegerVariablesFrame integers:
                        capture.Variables(track, time, integers.variables, null, null);
                        break;
                    case Recorder.FloatVariablesFrame floats:
                        capture.Variables(track, time, null, floats.variables, null);
                        break;
                    case Recorder.String8VariablesFrame strings:
                        capture.Variables(track, time, null, null, strings.variables);
                        break;
                    default:
                        skipped++;
                        continue;
                }
                written++;
            }
            return new Result(tracks.Count, written, skipped);
        }

        static bool IsPosition(Recorder.Frame frame) => frame.type == Recorder.FrameType.AircraftPosition || frame.type == Recorder.FrameType.ObjectPosition;

        /// <summary>The old format kept only these; the fields it lacks stay empty (design section 8).</summary>
        static IdentityUpdate Identity(Recorder.Obj obj, uint track)
        {
            var aircraft = obj as Recorder.Aircraft;
            return new IdentityUpdate
            {
                ObjectId = track,
                IsAircraft = aircraft != null,
                IsPlane = aircraft?.plane ?? false,
                Callsign = aircraft?.callsign ?? "",
                Model = obj.model,
                Livery = obj.livery,
                IcaoType = obj.icaoType,
                IcaoAirline = obj.icaoAirline,
                TypeRole = (byte)obj.typerole,
            };
        }
    }
}
