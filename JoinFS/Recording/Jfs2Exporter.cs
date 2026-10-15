using System;
using System.Collections.Generic;
using System.Linq;
using JoinFS.Net;
using JoinFS.Net.Jfp2.Codecs;

namespace JoinFS.Recording
{
    /// <summary>
    /// Writes recorded objects (<see cref="Recorder.Obj"/>) as a JFS2 file, so a recording that was edited in memory can be saved in the new
    /// format. It maps the objects onto tracks of canonical messages and hands them to <see cref="Jfs2SceneWriter"/>, which the importer of
    /// old files (<see cref="LegacyJfsImporter"/>) uses too. Knows nothing of Main, so it is testable without a simulator.
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
            List<SceneTrack> tracks = Tracks(objects, out int skipped);
            Jfs2SceneWriter.Result result = Jfs2SceneWriter.Write(path, tracks, app, startUtc, log);
            return new Result(result.Tracks, result.Frames, skipped);
        }

        /// <summary>Feeds the objects into an open capture, frames of all objects in time order (a chunk may not go back in time, spec 5).</summary>
        public static Result Write(Jfs2Capture capture, IReadOnlyList<Recorder.Obj> objects, string app)
        {
            List<SceneTrack> tracks = Tracks(objects, out int skipped);
            Jfs2SceneWriter.Result result = Jfs2SceneWriter.Write(capture, tracks, app);
            return new Result(result.Tracks, result.Frames, skipped);
        }

        /// <summary>The objects as tracks of canonical messages; frames that fit no message of their object are counted, not kept.</summary>
        static List<SceneTrack> Tracks(IReadOnlyList<Recorder.Obj> objects, out int skipped)
        {
            skipped = 0;
            List<SceneTrack> tracks = [];
            foreach (Recorder.Obj obj in objects)
            {
                if (!obj.frames.Any(IsPosition))
                {
                    continue;
                }
                var track = new SceneTrack
                {
                    Id = (uint)tracks.Count + 1,
                    Nickname = (obj as Recorder.Aircraft)?.nickname ?? "",
                    Owner = Recorder.ToTrackOwner(obj.owner),
                    SourceApp = obj.sourceApp ?? "",
                    Simulator = obj.simulator ?? "",
                    Identity = Identity(obj, (uint)tracks.Count + 1),
                };
                foreach (Recorder.Frame frame in obj.frames)
                {
                    SceneFrame mapped = Map(obj, frame);
                    if (mapped == null)
                    {
                        skipped++;
                    }
                    else
                    {
                        track.Frames.Add(mapped);
                    }
                }
                tracks.Add(track);
            }
            return tracks;
        }

        static SceneFrame Map(Recorder.Obj obj, Recorder.Frame frame)
        {
            double time = frame.time;
            switch (frame)
            {
                case Recorder.AircraftPositionFrame position when obj is Recorder.Aircraft:
                {
                    Sim.AircraftPosition data = position.data;
                    return new ScenePositionFrame(time, SimMessageMapper.ToPositionUpdate(0, time, ref data, data.ground != 0 ? PositionStateFlags.OnGround : PositionStateFlags.None));
                }
                case Recorder.ObjectPositionFrame position when obj is not Recorder.Aircraft:
                {
                    Sim.ObjectPositionVelocity data = position.data;
                    return new SceneObjectPositionFrame(time, SimMessageMapper.ToObjectPositionUpdate(0, time, ref data, data.ground != 0 ? PositionStateFlags.OnGround : PositionStateFlags.None));
                }
                case Recorder.SimEventFrame sim:
                    return new SceneEventFrame(time, sim.eventId, sim.data);
                case Recorder.IntegerVariablesFrame integers:
                    return new SceneVariablesFrame(time, new VariableSyncUpdate { Entries = SimMessageMapper.ToVariableEntries(integers.variables, null, null) });
                case Recorder.FloatVariablesFrame floats:
                    return new SceneVariablesFrame(time, new VariableSyncUpdate { Entries = SimMessageMapper.ToVariableEntries(null, floats.variables, null) });
                case Recorder.String8VariablesFrame strings:
                    return new SceneVariablesFrame(time, new VariableSyncUpdate { Entries = SimMessageMapper.ToVariableEntries(null, null, strings.variables) });
                default:
                    return null;
            }
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
