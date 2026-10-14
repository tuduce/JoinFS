using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JoinFS.Net;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Recording.Format;

namespace JoinFS.Recording
{
    /// <summary>One thing that happened to a recorded object at a time, in canonical messages (recording seconds).</summary>
    public abstract record SceneFrame(double Time);

    /// <summary>An aircraft position (kind <c>position</c>).</summary>
    public sealed record ScenePositionFrame(double Time, PositionUpdate Position) : SceneFrame(Time);

    /// <summary>The position of an object that is not an aircraft (kind <c>object-position</c>).</summary>
    public sealed record SceneObjectPositionFrame(double Time, ObjectPositionUpdate Position) : SceneFrame(Time);

    /// <summary>Variables that changed (kind <c>variables</c>): an upsert, to be merged over what came before (spec 7.6).</summary>
    public sealed record SceneVariablesFrame(double Time, VariableSyncUpdate Variables) : SceneFrame(Time);

    /// <summary>A simulator event (kind <c>event</c>).</summary>
    public sealed record SceneEventFrame(double Time, uint EventId, uint Data) : SceneFrame(Time);

    /// <summary>One recorded object: the identity it opened with and its frames in time order.</summary>
    public sealed class SceneTrack
    {
        public uint Id { get; init; }
        public string Nickname { get; init; } = "";
        public IdentityUpdate Identity { get; init; }
        public List<SceneFrame> Frames { get; } = [];
    }

    /// <summary>
    /// A JFS2 file read into tracks of canonical messages, ready for a player or a viewer to map onto its own objects.
    /// Knows nothing of Sim, Main or any UI, so the app and RecordingXRay share it.
    /// </summary>
    public sealed class Jfs2Scene
    {
        /// <summary>The recorded objects, in the order their tracks opened. Tracks without a position are left out.</summary>
        public List<SceneTrack> Tracks { get; } = [];

        /// <summary>Everything the load has to say, for the log.</summary>
        public List<string> Notes { get; } = [];

        /// <summary>The notes the user should see: parts of the file that were lost or are not played.</summary>
        public List<string> Warnings { get; } = [];

        /// <summary>When recording time 0 happened.</summary>
        public DateTime StartUtc { get; private set; }

        /// <summary>The header's metadata (app, sim, ...).</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Metadata { get; private set; } = [];

        /// <summary>Identity records that differed from the one a track opened with; a viewer that shows one identity per object ignores them.</summary>
        public int IdentityChanges { get; private set; }

        /// <summary>For the readers in this library (the JFS2 reader here and the importer of old recordings) to fill in what the header holds.</summary>
        internal void Initialise(DateTime startUtc, IReadOnlyList<KeyValuePair<string, string>> metadata)
        {
            StartUtc = startUtc;
            Metadata = metadata;
        }

        sealed class Building
        {
            public string Nickname = "";
            public IdentityUpdate Identity;
            public bool HasIdentity;
            public readonly List<SceneFrame> Frames = [];
            public int PositionCount;
            /// <summary>Time of the latest frame, so a snapshot after a lost chunk adds nothing out of order.</summary>
            public double LastTime = double.NegativeInfinity;

            public void Add(SceneFrame frame)
            {
                Frames.Add(frame);
                LastTime = Math.Max(LastTime, frame.Time);
            }
        }

        /// <summary>The kinds this reader knows; everything else is skipped by name and version.</summary>
        static readonly (string Name, byte Version)[] Known =
        [
            (Jfs2Kinds.TrackOpen, Jfs2Kinds.TrackOpenVersion),
            (Jfs2Kinds.TrackClose, Jfs2Kinds.TrackCloseVersion),
            (Jfs2Kinds.Identity, Jfs2Kinds.IdentityVersion),
            (Jfs2Kinds.Position, Jfs2Kinds.PositionVersion),
            (Jfs2Kinds.ObjectPosition, Jfs2Kinds.ObjectPositionVersion),
            (Jfs2Kinds.Variables, Jfs2Kinds.VariablesVersion),
            (Jfs2Kinds.Event, Jfs2Kinds.EventVersion),
        ];

        /// <summary>Kinds without which a recording shows nothing (spec 8.2).</summary>
        static readonly string[] Essential = [Jfs2Kinds.TrackOpen, Jfs2Kinds.Identity, Jfs2Kinds.Position, Jfs2Kinds.ObjectPosition];

        /// <summary>
        /// Reads the whole file. A damaged chunk is skipped and reported; reading goes on from the next keyframe (spec 8.2).
        /// </summary>
        /// <exception cref="InvalidOperationException">The file is not a JFS2 file this build can read, or holds nothing to show; the message says why.</exception>
        public static Jfs2Scene Load(Stream stream)
        {
            RecordingReader reader;
            try
            {
                reader = RecordingReader.Open(stream, leaveOpen: true);
            }
            catch (Jfs2FormatException ex)
            {
                throw new InvalidOperationException("This recording cannot be opened: " + ex.Message, ex);
            }
            using (reader)
            {
                return Read(reader);
            }
        }

        static Jfs2Scene Read(RecordingReader reader)
        {
            var scene = new Jfs2Scene
            {
                StartUtc = DateTime.UnixEpoch.AddTicks(reader.Header.StartUtcMicroseconds * 10),
                Metadata = reader.Header.Metadata,
            };
            List<string> notes = scene.Notes;
            Dictionary<uint, Building> tracks = [];
            List<uint> order = [];
            Dictionary<string, int> skipped = [];
            int damaged = 0, identityChanges = 0;
            bool lost = false;

            var positionCodec = new PositionV1Codec();
            var identityCodec = new IdentityV1Codec();
            var variablesCodec = new VariableSyncV1Codec();
            var eventCodec = new EventV1Codec();

            if (!reader.IsFinished)
            {
                notes.Add("The recording was not finished (the program stopped while recording); it is played as far as it was written.");
            }

            for (int index = 0; index < reader.Chunks.Count; index++)
            {
                if (!reader.TryReadChunk(index, out RecordChunk chunk))
                {
                    // spec 8.2: the track state is unreliable until the next keyframe
                    lost = true;
                    continue;
                }
                lost |= chunk.Info.PrecededByGap;
                if (lost && !chunk.Info.IsKeyframe)
                {
                    continue;
                }
                // A linear read has seen what a snapshot summarises, so the snapshot is used only to restore the state after a lost chunk.
                bool restore = lost;
                lost = false;

                foreach (RecordEntry entry in restore ? chunk.Records : chunk.Records.Skip(chunk.SnapshotCount))
                {
                    KindBinding kind = reader.GetKind(entry.Kind);
                    if (kind == null || !IsKnown(kind))
                    {
                        string key = kind == null ? "kind " + entry.Kind : kind.Name + " v" + kind.Version;
                        skipped[key] = skipped.GetValueOrDefault(key) + 1;
                        continue;
                    }

                    try
                    {
                        double seconds = entry.Time / 1e6;
                        switch (kind.Name)
                        {
                            case Jfs2Kinds.TrackOpen:
                                if (!tracks.ContainsKey(entry.Track))
                                {
                                    tracks[entry.Track] = new Building { Nickname = TrackOpenV1.Decode(entry.Body.Span).Nickname };
                                    order.Add(entry.Track);
                                }
                                break;

                            case Jfs2Kinds.Identity:
                                if (tracks.TryGetValue(entry.Track, out Building identityTrack))
                                {
                                    IdentityUpdate identity = identityCodec.Decode(entry.Body.Span);
                                    if (!identityTrack.HasIdentity)
                                    {
                                        identityTrack.Identity = identity;
                                        identityTrack.HasIdentity = true;
                                    }
                                    else if (!identityTrack.Identity.SameAs(in identity))
                                    {
                                        identityChanges++;
                                    }
                                }
                                break;

                            case Jfs2Kinds.Position:
                                // a snapshot's position that the track already has (it is not newer than its last frame) is a duplicate
                                if (tracks.TryGetValue(entry.Track, out Building positionTrack) && !(entry.IsSnapshot && seconds <= positionTrack.LastTime))
                                {
                                    positionTrack.Add(new ScenePositionFrame(seconds, positionCodec.Decode(entry.Body.Span)));
                                    positionTrack.PositionCount++;
                                }
                                break;

                            case Jfs2Kinds.ObjectPosition:
                                if (tracks.TryGetValue(entry.Track, out Building objectTrack) && !(entry.IsSnapshot && seconds <= objectTrack.LastTime))
                                {
                                    objectTrack.Add(new SceneObjectPositionFrame(seconds, ObjectPositionV1Codec.Decode(entry.Body.Span)));
                                    objectTrack.PositionCount++;
                                }
                                break;

                            case Jfs2Kinds.Variables:
                                if (tracks.TryGetValue(entry.Track, out Building variablesTrack))
                                {
                                    // a snapshot's variables keep the time of what they summarise, which may lie before frames already read
                                    double time = entry.IsSnapshot ? Math.Max(seconds, variablesTrack.LastTime) : seconds;
                                    variablesTrack.Add(new SceneVariablesFrame(time, variablesCodec.Decode(entry.Body.Span)));
                                }
                                break;

                            case Jfs2Kinds.Event:
                                if (tracks.TryGetValue(entry.Track, out Building eventTrack))
                                {
                                    EventUpdate update = eventCodec.Decode(entry.Body.Span);
                                    eventTrack.Add(new SceneEventFrame(seconds, update.EventId, update.Data));
                                }
                                break;
                        }
                    }
                    catch (Exception ex) when (ex is FormatException or ArgumentException or IndexOutOfRangeException)
                    {
                        damaged++;
                    }
                }
            }

            foreach (Jfs2Damage damage in reader.Damage)
            {
                notes.Add("Damaged recording: " + damage.Message + " (at byte " + damage.Offset + ").");
            }
            if (damaged > 0)
            {
                notes.Add(damaged + " records could not be read and were skipped.");
            }
            int warningCount = notes.Count;
            if (identityChanges > 0)
            {
                notes.Add(identityChanges + " identity changes were ignored; each object plays with the identity it opened with.");
            }
            foreach (var count in skipped.OrderBy(pair => pair.Key))
            {
                notes.Add(count.Value + " '" + count.Key + "' records were skipped; this build does not play them yet.");
            }
            // the identity note is for the log only: it changes nothing the user can see go missing
            scene.Warnings.AddRange(notes.Take(warningCount));
            scene.Warnings.AddRange(notes.Skip(warningCount).Where(n => !n.Contains("identity changes")));
            scene.IdentityChanges = identityChanges;

            foreach (uint id in order)
            {
                Building track = tracks[id];
                if (track.PositionCount == 0 || !track.HasIdentity)
                {
                    continue;
                }
                var built = new SceneTrack { Id = id, Nickname = track.Nickname, Identity = track.Identity };
                built.Frames.AddRange(track.Frames);
                scene.Tracks.Add(built);
            }

            if (scene.Tracks.Count == 0)
            {
                bool newer = skipped.Keys.Any(key => Essential.Any(name => key.StartsWith(name + " v", StringComparison.Ordinal)));
                throw new InvalidOperationException(newer
                    ? "This recording needs a newer version of JoinFS to play: " + string.Join(" ", notes)
                    : "This recording holds nothing that can be played. " + string.Join(" ", notes));
            }
            return scene;
        }

        static bool IsKnown(KindBinding kind) => Known.Any(known => known.Name == kind.Name && known.Version == kind.Version);
    }
}
