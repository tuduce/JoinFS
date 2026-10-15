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
    public sealed record SceneVariablesFrame(double Time, VariableSyncUpdate Variables) : SceneFrame(Time)
    {
        /// <summary>
        /// The entries by value kind, one dictionary each, as the old frames, RecordingXRay and <see cref="Jfs2Capture.Variables"/> take
        /// them; null for a kind the record has none of.
        /// </summary>
        public (Dictionary<uint, int> Integers, Dictionary<uint, float> Floats, Dictionary<uint, string> Strings) ByKind()
        {
            Dictionary<uint, int> integers = null;
            Dictionary<uint, float> floats = null;
            Dictionary<uint, string> strings = null;
            foreach (VariableEntry entry in Variables.Entries ?? [])
            {
                switch (entry.Kind)
                {
                    case VariableKind.Int32: (integers ??= [])[entry.Vuid] = entry.IntValue; break;
                    case VariableKind.Float32: (floats ??= [])[entry.Vuid] = entry.FloatValue; break;
                    case VariableKind.String8: (strings ??= [])[entry.Vuid] = entry.StringValue ?? ""; break;
                }
            }
            return (integers, floats, strings);
        }

        /// <summary>The entries of the dictionaries by value kind, the other way round from <see cref="ByKind"/>; a null dictionary has none.</summary>
        public static List<VariableEntry> Entries(Dictionary<uint, int> integers, Dictionary<uint, float> floats, Dictionary<uint, string> strings)
        {
            List<VariableEntry> entries = [];
            foreach (var variable in integers ?? [])
            {
                entries.Add(new VariableEntry { Vuid = variable.Key, Kind = VariableKind.Int32, IntValue = variable.Value });
            }
            foreach (var variable in floats ?? [])
            {
                entries.Add(new VariableEntry { Vuid = variable.Key, Kind = VariableKind.Float32, FloatValue = variable.Value });
            }
            foreach (var variable in strings ?? [])
            {
                entries.Add(new VariableEntry { Vuid = variable.Key, Kind = VariableKind.String8, StringValue = variable.Value });
            }
            return entries;
        }
    }

    /// <summary>A simulator event (kind <c>event</c>).</summary>
    public sealed record SceneEventFrame(double Time, uint EventId, uint Data) : SceneFrame(Time);

    /// <summary>The object's identity changed (kind <c>identity</c>, after the one its track opened with): it plays as this from here on.</summary>
    public sealed record SceneIdentityFrame(double Time, IdentityUpdate Identity) : SceneFrame(Time);

    /// <summary>
    /// What the frames of a track before a time leave its object as (design 5.4): where a player that jumps there goes on, the latest
    /// position (null if none yet), the identity, and the variables merged (spec 7.6; null for a kind with none).
    /// </summary>
    /// <param name="Next">The index of the first frame at or after the time.</param>
    public sealed record SceneState(int Next, SceneFrame Position, IdentityUpdate Identity,
        Dictionary<uint, int> Integers, Dictionary<uint, float> Floats, Dictionary<uint, string> Strings);

    /// <summary>One recorded object: its track-open (spec 7.1), the identity it opened with and its frames in time order (identity changes among them).</summary>
    public sealed class SceneTrack
    {
        public uint Id { get; init; }
        public string Nickname { get; init; } = "";
        /// <summary>Who owned the object when it was recorded; <see cref="TrackOwner.Recorded"/> when the source does not say (an old .jfs file).</summary>
        public TrackOwner Owner { get; init; } = TrackOwner.Recorded;
        /// <summary>The application that recorded the object; empty when unknown.</summary>
        public string SourceApp { get; init; } = "";
        /// <summary>The simulator the object was seen in; empty when unknown.</summary>
        public string Simulator { get; init; } = "";
        /// <summary>The identity the track opens with; a trim at the start sets it to the one at the cut.</summary>
        public IdentityUpdate Identity { get; set; }
        public List<SceneFrame> Frames { get; } = [];

        /// <summary>A copy with its own frame list (the frames themselves are immutable and shared), for example to write it on another thread.</summary>
        public SceneTrack Copy()
        {
            var copy = new SceneTrack { Id = Id, Nickname = Nickname, Owner = Owner, SourceApp = SourceApp, Simulator = Simulator, Identity = Identity };
            copy.Frames.AddRange(Frames);
            return copy;
        }

        /// <summary>What the frames before <paramref name="time"/> leave the object as; events are not state and are left out.</summary>
        public SceneState StateAt(double time)
        {
            SceneFrame position = null;
            IdentityUpdate identity = Identity;
            Dictionary<uint, int> integers = null;
            Dictionary<uint, float> floats = null;
            Dictionary<uint, string> strings = null;
            int next = 0;
            for (; next < Frames.Count && Frames[next].Time < time; next++)
            {
                switch (Frames[next])
                {
                    case ScenePositionFrame or SceneObjectPositionFrame:
                        position = Frames[next];
                        break;
                    case SceneIdentityFrame change:
                        identity = change.Identity;
                        break;
                    case SceneVariablesFrame variables:
                    {
                        var (i, f, t) = variables.ByKind();
                        Merge(ref integers, i);
                        Merge(ref floats, f);
                        Merge(ref strings, t);
                        break;
                    }
                }
            }
            return new SceneState(next, position, identity, integers, floats, strings);
        }

        /// <summary>
        /// Whether a player shows the object at <paramref name="time"/>: once its first position has passed (as in <see cref="StateAt"/>)
        /// and until its last frame has. A track that opens later in the recording (an object that came later, the user's aircraft after a
        /// change of aircraft) is not there before it.
        /// </summary>
        public bool ShownAt(double time)
        {
            if (Frames.Count == 0 || time > Frames[^1].Time)
            {
                return false;
            }
            foreach (SceneFrame frame in Frames)
            {
                if (frame.Time >= time)
                {
                    return false;
                }
                if (frame is ScenePositionFrame or SceneObjectPositionFrame)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Moves every frame by some seconds, for example to append a recording after another.</summary>
        public void Shift(double seconds)
        {
            for (int i = 0; i < Frames.Count; i++)
            {
                Frames[i] = Frames[i] with { Time = Frames[i].Time + seconds };
            }
        }

        /// <summary>
        /// Cuts off the frames before <paramref name="time"/> and starts the rest at 0. What they left the object as comes first, as a
        /// keyframe would put it (README limit 12): the track opens with the identity at the cut, then its variables merged and its latest
        /// position at 0, unless a position is at the cut itself. Events before the cut go. False when nothing is at or after the cut: the
        /// object is gone by then, and the track is left as it was.
        /// </summary>
        public bool TrimStart(double time)
        {
            SceneState state = StateAt(time);
            if (state.Next >= Frames.Count)
            {
                return false;
            }

            List<SceneFrame> kept = Frames.GetRange(state.Next, Frames.Count - state.Next);
            Frames.Clear();
            Identity = state.Identity;
            if (state.Integers != null || state.Floats != null || state.Strings != null)
            {
                Frames.Add(new SceneVariablesFrame(time, new VariableSyncUpdate { Entries = SceneVariablesFrame.Entries(state.Integers, state.Floats, state.Strings) }));
            }
            if (state.Position != null && !kept.Exists(frame => frame.Time == time && IsPosition(frame)))
            {
                Frames.Add(state.Position with { Time = time });
            }
            Frames.AddRange(kept);
            Shift(-time);
            return true;
        }

        /// <summary>
        /// Cuts off the frames at and after <paramref name="time"/>. False when no position is left: the object only came later and has
        /// nothing to show (a writer leaves such a track out).
        /// </summary>
        public bool TrimEnd(double time)
        {
            int first = Frames.FindIndex(frame => frame.Time >= time);
            if (first >= 0)
            {
                Frames.RemoveRange(first, Frames.Count - first);
            }
            return Frames.Exists(IsPosition);
        }

        static bool IsPosition(SceneFrame frame) => frame is ScenePositionFrame or SceneObjectPositionFrame;

        static void Merge<T>(ref Dictionary<uint, T> into, Dictionary<uint, T> from)
        {
            if (from != null)
            {
                into ??= [];
                foreach (var pair in from)
                {
                    into[pair.Key] = pair.Value;
                }
            }
        }
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

        /// <summary>For the readers in this library (the JFS2 reader here and the importer of old recordings) to fill in what the header holds.</summary>
        internal void Initialise(DateTime startUtc, IReadOnlyList<KeyValuePair<string, string>> metadata)
        {
            StartUtc = startUtc;
            Metadata = metadata;
        }

        sealed class Building
        {
            public TrackOpenV1 Open;
            public IdentityUpdate Identity;
            public bool HasIdentity;
            /// <summary>The identity in effect after the frames read so far, so only a change becomes a frame.</summary>
            public IdentityUpdate Latest;
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
            // what the user should be told goes to the log too; the rest is for the log only
            void Warn(string text)
            {
                notes.Add(text);
                scene.Warnings.Add(text);
            }
            Dictionary<uint, Building> tracks = [];
            List<uint> order = [];
            Dictionary<string, int> skipped = [];
            int damaged = 0, identityChanges = 0;
            bool lost = false;
            // tracks with a record that could not be decoded: their state is wrong until a snapshot sets it again
            HashSet<uint> unreliable = [];

            var positionCodec = new PositionV1Codec();
            var identityCodec = new IdentityV1Codec();
            var variablesCodec = new VariableSyncV1Codec();
            var eventCodec = new EventV1Codec();

            if (!reader.IsFinished)
            {
                Warn("The recording was not finished (the program stopped while recording); it is played as far as it was written.");
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
                // A linear read has seen what a snapshot summarises, so the snapshot is used only to restore the state after a lost chunk,
                // and to heal a track whose track-open, identity or another record could not be read (spec 8.2).
                bool restore = lost;
                lost = false;
                HashSet<uint> heal = [];
                for (int i = 0; i < chunk.SnapshotCount; i++)
                {
                    uint track = chunk.Records[i].Track;
                    if (!tracks.TryGetValue(track, out Building known) || !known.HasIdentity || unreliable.Contains(track))
                    {
                        heal.Add(track);
                    }
                }
                for (int i = 0; i < chunk.SnapshotCount; i++)
                {
                    unreliable.Remove(chunk.Records[i].Track);
                }

                foreach (RecordEntry entry in chunk.Records)
                {
                    if (entry.IsSnapshot && !restore && !heal.Contains(entry.Track))
                    {
                        continue;
                    }
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
                                    tracks[entry.Track] = new Building { Open = TrackOpenV1.Decode(entry.Body.Span) };
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
                                        identityTrack.Latest = identity;
                                        identityTrack.HasIdentity = true;
                                    }
                                    else if (!identityTrack.Latest.SameAs(in identity))
                                    {
                                        // a snapshot's identity keeps the time of the change it summarises, which may lie before frames already read
                                        double time = entry.IsSnapshot ? Math.Max(seconds, identityTrack.LastTime) : seconds;
                                        identityTrack.Add(new SceneIdentityFrame(time, identity));
                                        identityTrack.Latest = identity;
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
                        unreliable.Add(entry.Track);
                    }
                }

                // the rest of the chunk was not read, so what it held is lost like a skipped chunk (spec 8.2)
                if (!chunk.IsComplete)
                {
                    lost = true;
                }
            }

            foreach (Jfs2Damage damage in reader.Damage)
            {
                Warn("Damaged recording: " + damage.Message + " (at byte " + damage.Offset + ").");
            }
            if (damaged > 0)
            {
                Warn(damaged + " records could not be read and were skipped.");
            }
            if (identityChanges > 0)
            {
                notes.Add(identityChanges + " identity changes (callsign, model) during tracks.");
            }
            foreach (var count in skipped.OrderBy(pair => pair.Key))
            {
                Warn(count.Value + " '" + count.Key + "' records were skipped; this build does not play them yet.");
            }

            foreach (uint id in order)
            {
                Building track = tracks[id];
                if (track.PositionCount == 0 || !track.HasIdentity)
                {
                    continue;
                }
                var built = new SceneTrack
                {
                    Id = id,
                    Nickname = track.Open.Nickname ?? "",
                    Owner = track.Open.Owner,
                    SourceApp = track.Open.SourceApp ?? "",
                    Simulator = track.Open.Simulator ?? "",
                    Identity = track.Identity,
                };
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
