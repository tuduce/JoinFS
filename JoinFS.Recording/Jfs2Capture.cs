using System;
using System.Collections.Generic;
using JoinFS.Net;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Recording.Format;

namespace JoinFS.Recording
{
    /// <summary>
    /// Writes a recording as a JFS2 file while it is made (docs/recording/jfs2/recorder-v2-design.md section 5.5). It takes canonical
    /// messages and times in recording seconds, and knows nothing of Sim or Main, so it is testable without a simulator.
    /// Writes track-open, identity, position, object-position, variables and event.
    /// </summary>
    /// <remarks>
    /// Runs on the thread that produces the records (the sim thread), which encodes a chunk every couple of seconds; the
    /// writer's own thread deflates and writes it, and asks for the file to reach the disk every few seconds
    /// (<see cref="SyncInterval"/>), so the disk never blocks the sim thread. A failure (disk full, file removed) switches the
    /// capture off and is reported once through the log callback, at the latest when the next chunk ends; the recording itself
    /// carries on in memory. Not thread-safe.
    /// </remarks>
    public sealed class Jfs2Capture : IDisposable
    {
        /// <summary>A chunk ends once its records span this long (spec 9.2: about two seconds).</summary>
        const long ChunkSpanMicroseconds = 2_000_000;

        /// <summary>The most entries a variables record holds (spec 7.6).</summary>
        const int MaxVariablesPerRecord = 255;

        /// <summary>Every fifth chunk (about 10 s) is a keyframe (spec 9.2).</summary>
        const int ChunksPerKeyframe = 5;

        /// <summary>
        /// What a keyframe's snapshot needs of a track (spec 6.1): its track-open, its latest identity and position, and the merged
        /// variables, each with the time of the record it summarises (spec 6.2).
        /// </summary>
        sealed class TrackState
        {
            public readonly Dictionary<uint, int> Integers = [];
            public readonly Dictionary<uint, float> Floats = [];
            public readonly Dictionary<uint, string> Strings = [];
            /// <summary>Time of the latest variables record, which is when a snapshot's variables keep (spec 6.2).</summary>
            public long VariablesTime;
            public byte[] Open;
            public long OpenTime;
            public byte[] Identity;
            public long IdentityTime;
            /// <summary>The latest position or object-position body; null until the track has had one.</summary>
            public byte[] Position;
            public uint PositionKind;
            public long PositionTime;
        }

        readonly RecordingWriter writer;
        readonly Jfs2KindIds kinds;
        readonly Action<string> log;
        readonly Dictionary<uint, long> lastTime = [];
        readonly PositionV1Codec positionCodec = new();
        readonly IdentityV1Codec identityCodec = new();
        readonly byte[] positionBody = new byte[PositionV1Codec.Size];
        readonly byte[] identityBody = new byte[IdentityV1Codec.MaxSize];
        readonly VariableSyncV1Codec variablesCodec = new();
        readonly EventV1Codec eventCodec = new();
        readonly byte[] variablesBody = new byte[VariableSyncV1Codec.HeaderSize + MaxVariablesPerRecord * VariableSyncV1Codec.MaxEntrySize];
        readonly byte[] objectPositionBody = new byte[ObjectPositionV1Codec.Size];
        readonly byte[] eventBody = new byte[EventV1Codec.Size];
        /// <summary>What was written of each open track: the variables so only changes are written (spec 7.6), and the rest for snapshots.</summary>
        readonly Dictionary<uint, TrackState> states = [];
        /// <summary>The tracks in the order they opened, which is the order of a snapshot.</summary>
        readonly List<uint> trackOrder = [];
        /// <summary>Every variable written, for the <c>variable-names.txt</c> attachment (spec 3.5).</summary>
        readonly SortedSet<uint> vuids = [];

        /// <summary>No record may be earlier than the TMin of the chunk before (spec 5).</summary>
        long floor;
        int chunksSinceKeyframe;
        bool finished;

        Jfs2Capture(RecordingWriter writer, string path, Action<string> log)
        {
            this.writer = writer;
            Path = path;
            this.log = log;
            kinds = Jfs2KindIds.Bind(writer);
        }

        public string Path { get; }

        /// <summary>The capture stopped after an error and writes nothing more.</summary>
        public bool Failed { get; private set; }

        public int TrackCount { get; private set; }
        /// <summary>Tracks closed before the end, because their object left.</summary>
        public int ClosedCount { get; private set; }
        public int PositionCount { get; private set; }
        public int ObjectPositionCount { get; private set; }
        public int IdentityChangeCount { get; private set; }
        public int VariableRecordCount { get; private set; }
        public int EventCount { get; private set; }
        /// <summary>Chunks started as keyframes after the first (which always is one).</summary>
        public int KeyframeCount { get; private set; }
        /// <summary>
        /// Records written later than their time, so a track's times do not go back (spec 5). None when each object's records are on
        /// one time base (<see cref="TrackTimeBase"/>) and its clock does not step back.
        /// </summary>
        public int HeldCount { get; private set; }

        /// <summary>
        /// Every chunk is deflated (kept as is when that is not smaller): files shrink to 55 to 62% for under 1 ms a chunk
        /// (docs/recording/jfs2/README.md, stage 6). The scene writer writes through a capture, so Save writes the same.
        /// </summary>
        static readonly RecordingWriterOptions WriterOptions = new()
        {
            Compression = ChunkCompression.Deflate,
            // spec 9.1 and 9.2 (README limit 5): the disk never blocks the sim thread, and a crash or a power cut loses the last few
            // seconds at most
            WriterThread = true,
            SyncInterval = SyncInterval,
        };

        /// <summary>How often the file is asked to reach the disk while it is written: every few seconds (spec 9.1), a chunk ends every 2.</summary>
        public static TimeSpan SyncInterval => TimeSpan.FromSeconds(5);

        /// <summary>Creates the file. Throws if it cannot be created.</summary>
        /// <param name="app">For the header's <c>app</c> metadata, for example "JoinFS 26.10".</param>
        /// <param name="simulator">For the header's <c>sim</c> metadata; empty when unknown.</param>
        /// <param name="startUtc">When recording time 0 happened.</param>
        /// <param name="log">Told once if the capture fails.</param>
        /// <param name="more">More header metadata, for example an overdub's <c>base</c>.</param>
        /// <param name="options">How chunks are written; when null Deflate, on a writer thread, synced every <see cref="SyncInterval"/>.</param>
        public static Jfs2Capture Create(string path, string app, string simulator, DateTime startUtc, Action<string> log, IEnumerable<KeyValuePair<string, string>> more = null,
            RecordingWriterOptions options = null)
        {
            List<KeyValuePair<string, string>> metadata = [new("app", app)];
            if (simulator.Length > 0)
            {
                metadata.Add(new("sim", simulator));
            }
            if (more != null)
            {
                metadata.AddRange(more);
            }
            var header = new RecordingHeader
            {
                StartUtcMicroseconds = (startUtc.ToUniversalTime() - DateTime.UnixEpoch).Ticks / 10,
                Metadata = metadata,
            };
            var writer = RecordingWriter.Create(path, header, options ?? WriterOptions);
            try
            {
                return new Jfs2Capture(writer, path, log);
            }
            catch
            {
                writer.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Starts a track: its track-open and the identity the object has now (spec 5: both precede its first position).
        /// The identity's object id is replaced by the track id.
        /// </summary>
        public void OpenTrack(uint track, double seconds, TrackOwner owner, string nickname, string sourceApp, string simulator, IdentityUpdate identity)
        {
            if (Failed || finished)
            {
                return;
            }
            try
            {
                long time = TimeOf(track, seconds);
                byte[] open = new TrackOpenV1(owner, nickname, sourceApp, simulator).Encode();
                writer.Add(kinds.TrackOpen, track, time, open);
                lastTime[track] = time;
                identity = Sanitised(identity, track);
                int length = identityCodec.Encode(identity, identityBody);
                writer.Add(kinds.Identity, track, time, identityBody.AsSpan(0, length));
                states[track] = new TrackState { Open = open, OpenTime = time, Identity = identityBody.AsSpan(0, length).ToArray(), IdentityTime = time };
                trackOrder.Add(track);
                writer.SetTrackLabel(track, identity.Callsign, identity.Model);
                TrackCount++;
                EndChunkIfDue();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        /// <summary>
        /// Writes the identity of an open track again when it differs from the latest one written (a new callsign or model, spec 7.1).
        /// The comparison is on the encoded bytes, so a change the codec cuts off (a long ICAO string) writes nothing.
        /// </summary>
        public void Identity(uint track, double seconds, IdentityUpdate identity)
        {
            if (Failed || finished || !states.TryGetValue(track, out TrackState state))
            {
                return;
            }
            try
            {
                identity = Sanitised(identity, track);
                int length = identityCodec.Encode(identity, identityBody);
                if (identityBody.AsSpan(0, length).SequenceEqual(state.Identity))
                {
                    return;
                }
                long time = TimeOf(track, seconds);
                writer.Add(kinds.Identity, track, time, identityBody.AsSpan(0, length));
                state.Identity = identityBody.AsSpan(0, length).ToArray();
                state.IdentityTime = time;
                lastTime[track] = time;
                // the Index lists the latest callsign and model (spec 3.4)
                writer.SetTrackLabel(track, identity.Callsign, identity.Model);
                IdentityChangeCount++;
                EndChunkIfDue();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        /// <summary>
        /// Adds a position sample of an open track. The body's object id and time are set from the track and the time (spec 5:
        /// the record header is the authority, the body only has to be well-formed).
        /// </summary>
        public void Position(uint track, double seconds, ref PositionUpdate position)
        {
            if (Failed || finished || !states.TryGetValue(track, out TrackState state))
            {
                return;
            }
            try
            {
                long time = TimeOf(track, seconds);
                position.ObjectId = track;
                position.NetTime = time / 1e6;
                positionCodec.Encode(position, positionBody);
                writer.Add(kinds.Position, track, time, positionBody);
                KeepPosition(state, kinds.Position, time, positionBody);
                lastTime[track] = time;
                PositionCount++;
                EndChunkIfDue();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        /// <summary>Adds a position sample of an open track that is not an aircraft (spec 7.2). Time and object id are set as for <see cref="Position"/>.</summary>
        public void ObjectPosition(uint track, double seconds, ref ObjectPositionUpdate position)
        {
            if (Failed || finished || !states.TryGetValue(track, out TrackState state))
            {
                return;
            }
            try
            {
                long time = TimeOf(track, seconds);
                position.ObjectId = track;
                position.NetTime = time / 1e6;
                ObjectPositionV1Codec.Encode(position, objectPositionBody);
                writer.Add(kinds.ObjectPosition, track, time, objectPositionBody);
                KeepPosition(state, kinds.ObjectPosition, time, objectPositionBody);
                lastTime[track] = time;
                ObjectPositionCount++;
                EndChunkIfDue();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        /// <summary>
        /// Adds the variables of an open track. Each dictionary is the object's current set (or null); only the entries that
        /// differ from what this track last wrote are stored, and the first call of a track stores them all (spec 7.6: a
        /// <c>variables</c> record is an upsert, and a writer SHOULD write changes). Nothing is written when nothing changed.
        /// </summary>
        public void Variables(uint track, double seconds, Dictionary<uint, int> integers, Dictionary<uint, float> floats, Dictionary<uint, string> strings)
        {
            if (Failed || finished || !states.TryGetValue(track, out TrackState state))
            {
                return;
            }
            try
            {
                List<VariableEntry> changes = [];
                if (integers != null)
                {
                    foreach (var variable in integers)
                    {
                        if (!state.Integers.TryGetValue(variable.Key, out int old) || old != variable.Value)
                        {
                            state.Integers[variable.Key] = variable.Value;
                            changes.Add(new VariableEntry { Vuid = variable.Key, Kind = VariableKind.Int32, IntValue = variable.Value });
                        }
                    }
                }
                if (floats != null)
                {
                    foreach (var variable in floats)
                    {
                        // bit comparison: NaN equals itself, and -0 is not +0
                        if (!state.Floats.TryGetValue(variable.Key, out float old) || BitConverter.SingleToInt32Bits(old) != BitConverter.SingleToInt32Bits(variable.Value))
                        {
                            state.Floats[variable.Key] = variable.Value;
                            changes.Add(new VariableEntry { Vuid = variable.Key, Kind = VariableKind.Float32, FloatValue = variable.Value });
                        }
                    }
                }
                if (strings != null)
                {
                    foreach (var variable in strings)
                    {
                        string value = variable.Value ?? "";
                        if (!state.Strings.TryGetValue(variable.Key, out string old) || old != value)
                        {
                            state.Strings[variable.Key] = value;
                            changes.Add(new VariableEntry { Vuid = variable.Key, Kind = VariableKind.String8, StringValue = value });
                        }
                    }
                }
                if (changes.Count == 0)
                {
                    return;
                }
                foreach (VariableEntry change in changes)
                {
                    vuids.Add(change.Vuid);
                }

                long time = TimeOf(track, seconds);
                WriteVariableRecords(track, time, changes, snapshot: false);
                state.VariablesTime = time;
                lastTime[track] = time;
                EndChunkIfDue();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        /// <summary>Adds a simulator event raised on an open track.</summary>
        public void Event(uint track, double seconds, uint eventId, uint data)
        {
            if (Failed || finished || !lastTime.ContainsKey(track))
            {
                return;
            }
            try
            {
                long time = TimeOf(track, seconds);
                int length = eventCodec.Encode(new EventUpdate { ObjectId = track, EventId = eventId, Data = data }, eventBody);
                writer.Add(kinds.Event, track, time, eventBody.AsSpan(0, length));
                lastTime[track] = time;
                EventCount++;
                EndChunkIfDue();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        /// <summary>
        /// Ends a track: the object has left (spec 7.1). It is in no later snapshot, and records for it are not written any more.
        /// </summary>
        public void CloseTrack(uint track, double seconds)
        {
            if (Failed || finished || !states.Remove(track))
            {
                return;
            }
            try
            {
                trackOrder.Remove(track);
                long time = TimeOf(track, seconds);
                writer.Add(kinds.TrackClose, track, time, []);
                lastTime[track] = time;
                ClosedCount++;
                EndChunkIfDue();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        /// <summary>
        /// Ends the file: closes the open tracks, writes the Index and Footer. Safe to call twice or after a failure.
        /// </summary>
        /// <param name="variableName">The name of a variable, or null when it has none; with it the file gets the <c>variable-names.txt</c>
        /// attachment (spec 3.5) for the variables it holds.</param>
        public void Finish(Func<uint, string> variableName = null)
        {
            if (finished)
            {
                return;
            }
            finished = true;
            if (!Failed)
            {
                try
                {
                    if (variableName != null)
                    {
                        WriteVariableNames(variableName);
                    }
                    writer.Finish();
                }
                catch (Exception ex)
                {
                    Fail(ex);
                }
            }
            writer.Dispose();
        }

        public void Dispose() => Finish();

        /// <summary>The attachment that names the variables (spec 3.5).</summary>
        public const string VariableNamesAttachment = "variable-names.txt";

        /// <summary>
        /// Writes <c>variable-names.txt</c>: a line per variable that has a name, its vuid as eight lower-case hex digits, a tab, the name.
        /// </summary>
        void WriteVariableNames(Func<uint, string> variableName)
        {
            var text = new System.Text.StringBuilder();
            foreach (uint vuid in vuids)
            {
                string name = variableName(vuid);
                if (!string.IsNullOrEmpty(name))
                {
                    text.Append(vuid.ToString("x8")).Append('\t').Append(name.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
                }
            }
            if (text.Length > 0)
            {
                writer.AddAttachment(VariableNamesAttachment, "text/plain; charset=utf-8", System.Text.Encoding.UTF8.GetBytes(text.ToString()));
            }
        }

        /// <summary>Recording seconds as the format's microseconds, never before the track's last record or the previous chunk.</summary>
        long TimeOf(uint track, double seconds)
        {
            long time = (long)Math.Round(seconds * 1e6);
            long earliest = lastTime.TryGetValue(track, out long last) ? Math.Max(last, floor) : floor;
            if (time < earliest)
            {
                HeldCount++;
                return earliest;
            }
            return time;
        }

        void EndChunkIfDue()
        {
            if (writer.PendingTMin is { } tmin && writer.PendingTMax - tmin >= ChunkSpanMicroseconds)
            {
                floor = tmin;
                writer.EndChunk();
                if (++chunksSinceKeyframe >= ChunksPerKeyframe)
                {
                    StartKeyframe();
                }
            }
        }

        /// <summary>
        /// Starts a keyframe chunk and writes the snapshot of every open track (spec 6.1). It is the state after everything
        /// written so far, so a seek or a reader that lost a chunk can start here.
        /// </summary>
        void StartKeyframe()
        {
            chunksSinceKeyframe = 0;
            writer.StartKeyframeChunk();
            KeyframeCount++;
            foreach (uint track in trackOrder)
            {
                TrackState state = states[track];
                writer.AddSnapshot(kinds.TrackOpen, track, state.OpenTime, state.Open);
                writer.AddSnapshot(kinds.Identity, track, state.IdentityTime, state.Identity);
                if (state.Position != null)
                {
                    writer.AddSnapshot(state.PositionKind, track, state.PositionTime, state.Position);
                }
                WriteVariableRecords(track, state.VariablesTime, SceneVariablesFrame.Entries(state.Integers, state.Floats, state.Strings), snapshot: true);
            }
        }

        /// <summary>Writes entries as variables records of at most 255 entries each, all of the same time (spec 7.6).</summary>
        void WriteVariableRecords(uint track, long time, List<VariableEntry> entries, bool snapshot)
        {
            for (int start = 0; start < entries.Count; start += MaxVariablesPerRecord)
            {
                var update = new VariableSyncUpdate
                {
                    ObjectId = track,
                    Entries = entries.GetRange(start, Math.Min(MaxVariablesPerRecord, entries.Count - start)),
                };
                int length = variablesCodec.Encode(update, variablesBody);
                if (snapshot)
                {
                    writer.AddSnapshot(kinds.Variables, track, time, variablesBody.AsSpan(0, length));
                }
                else
                {
                    writer.Add(kinds.Variables, track, time, variablesBody.AsSpan(0, length));
                    VariableRecordCount++;
                }
            }
        }

        static void KeepPosition(TrackState state, uint kind, long time, byte[] body)
        {
            if (state.Position == null || state.Position.Length != body.Length)
            {
                state.Position = new byte[body.Length];
            }
            body.CopyTo(state.Position, 0);
            state.PositionKind = kind;
            state.PositionTime = time;
        }

        static IdentityUpdate Sanitised(IdentityUpdate identity, uint track)
        {
            identity.ObjectId = track;
            identity.Callsign ??= "";
            identity.Model ??= "";
            identity.Livery ??= "";
            identity.IcaoType ??= "";
            identity.IcaoAirline ??= "";
            identity.Registration ??= "";
            identity.FlightNumber ??= "";
            identity.ClassCode ??= "";
            identity.Wtc ??= "";
            return identity;
        }

        void Fail(Exception ex)
        {
            Failed = true;
            log("Recorder: the JFS2 recording '" + Path + "' stopped (" + ex.Message + "); the recording itself carries on.");
        }
    }
}
