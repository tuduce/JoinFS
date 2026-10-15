using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JoinFS.Net;
using JoinFS.Recording.Format;

namespace JoinFS.Recording
{
    /// <summary>
    /// Writes tracks of canonical messages (<see cref="SceneTrack"/>) as a JFS2 file: what the importer of old recordings and the
    /// exporter of the recorder's in-memory objects both end in. Knows nothing of Sim or Main, so it is testable without a simulator.
    /// </summary>
    public static class Jfs2SceneWriter
    {
        /// <param name="Tracks">Tracks written; tracks without a position are left out, as the reader does.</param>
        /// <param name="Frames">Frames written.</param>
        public sealed record Result(int Tracks, int Frames);

        /// <summary>
        /// Writes the file. Throws if it cannot be created or written; a half-written file is not left behind.
        /// </summary>
        /// <param name="app">For the header's <c>app</c> metadata, and the source application of a track that does not name one, for example
        /// "JoinFS 26.10 (converted)". The header's <c>sim</c> is the simulator all tracks name, and left out when they differ.</param>
        /// <param name="startUtc">When recording time 0 happened, if known.</param>
        public static Result Write(string path, IReadOnlyList<SceneTrack> tracks, string app, DateTime startUtc, Action<string> log)
        {
            List<string> simulators = [.. tracks.Select(track => track.Simulator ?? "").Distinct()];
            Jfs2Capture capture = Jfs2Capture.Create(path, app, simulators.Count == 1 ? simulators[0] : "", startUtc, log);
            try
            {
                Result result = Write(capture, tracks, app);
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
                try { File.Delete(path); } catch (IOException) { }
                throw;
            }
        }

        /// <summary>
        /// Feeds the tracks into an open capture. Frames of all tracks go in time order (a chunk may not go back in time, spec 5); equal
        /// times keep track order, then frame order, so the result is deterministic. Tracks are numbered from 1 in the order of the list
        /// and opened at their first frame with their own owner, source application and simulator; a time before 0 is held at 0 by the capture.
        /// </summary>
        static Result Write(Jfs2Capture capture, IReadOnlyList<SceneTrack> scene, string app)
        {
            List<SceneTrack> tracks = [];
            foreach (SceneTrack track in scene)
            {
                if (track.Frames.Exists(IsPosition))
                {
                    tracks.Add(track);
                }
            }

            List<(double Time, int Track, int Index)> order = [];
            for (int t = 0; t < tracks.Count; t++)
            {
                List<SceneFrame> frames = tracks[t].Frames;
                for (int i = 0; i < frames.Count; i++)
                {
                    order.Add((frames[i].Time, t, i));
                }
            }
            order.Sort((a, b) =>
            {
                int byTime = a.Time.CompareTo(b.Time);
                if (byTime != 0) return byTime;
                int byTrack = a.Track.CompareTo(b.Track);
                return byTrack != 0 ? byTrack : a.Index.CompareTo(b.Index);
            });

            bool[] opened = new bool[tracks.Count];
            int written = 0;
            foreach (var (time, t, index) in order)
            {
                SceneTrack track = tracks[t];
                uint id = (uint)t + 1;
                if (!opened[t])
                {
                    opened[t] = true;
                    string sourceApp = string.IsNullOrEmpty(track.SourceApp) ? app : track.SourceApp;
                    capture.OpenTrack(id, time, track.Owner, track.Nickname ?? "", sourceApp, track.Simulator ?? "", track.Identity);
                }

                switch (track.Frames[index])
                {
                    case ScenePositionFrame position:
                    {
                        PositionUpdate update = position.Position;
                        capture.Position(id, time, ref update);
                        break;
                    }
                    case SceneObjectPositionFrame position:
                    {
                        ObjectPositionUpdate update = position.Position;
                        capture.ObjectPosition(id, time, ref update);
                        break;
                    }
                    case SceneEventFrame sim:
                        capture.Event(id, time, sim.EventId, sim.Data);
                        break;
                    case SceneVariablesFrame variables:
                    {
                        var (integers, floats, strings) = variables.ByKind();
                        capture.Variables(id, time, integers, floats, strings);
                        break;
                    }
                    default:
                        continue;
                }
                written++;
            }
            return new Result(tracks.Count, written);
        }

        static bool IsPosition(SceneFrame frame) => frame is ScenePositionFrame or SceneObjectPositionFrame;
    }
}
