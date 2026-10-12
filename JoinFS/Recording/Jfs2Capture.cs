using System;
using System.Collections.Generic;
using JoinFS.Net;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Recording.Format;

namespace JoinFS.Recording
{
    /// <summary>
    /// Writes a recording as a JFS2 file while it is made (docs/jfs2/recorder-v2-design.md section 5.5). It takes canonical
    /// messages and times in recording seconds, and knows nothing of Sim or Main, so it is testable without a simulator.
    /// Writes track-open, identity, position, object-position, variables and event.
    /// </summary>
    /// <remarks>
    /// Runs on the thread that produces the records (the sim thread) and writes a chunk, about 8 KB, every couple of
    /// seconds. A failure (disk full, file removed) switches the capture off and is reported once through the log
    /// callback; the recording itself carries on, so a fault here never costs the user the old recorder's result.
    /// Not thread-safe.
    /// </remarks>
    public sealed class Jfs2Capture : IDisposable
    {
        /// <summary>A chunk ends once its records span this long (spec 9.2: about two seconds).</summary>
        const long ChunkSpanMicroseconds = 2_000_000;

        /// <summary>The most entries a variables record holds (spec 7.6).</summary>
        const int MaxVariablesPerRecord = 255;

        sealed class VariableState
        {
            public readonly Dictionary<uint, int> Integers = [];
            public readonly Dictionary<uint, float> Floats = [];
            public readonly Dictionary<uint, string> Strings = [];
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
        /// <summary>What the last variables record of each track left, so only changes are written (spec 7.6).</summary>
        readonly Dictionary<uint, VariableState> variableStates = [];

        /// <summary>No record may be earlier than the TMin of the chunk before (spec 5).</summary>
        long floor;
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
        public int PositionCount { get; private set; }
        public int ObjectPositionCount { get; private set; }
        public int VariableRecordCount { get; private set; }
        public int EventCount { get; private set; }

        /// <summary>Creates the file. Throws if it cannot be created.</summary>
        /// <param name="app">For the header's <c>app</c> metadata, for example "JoinFS 26.10".</param>
        /// <param name="simulator">For the header's <c>sim</c> metadata; empty when unknown.</param>
        /// <param name="startUtc">When recording time 0 happened.</param>
        /// <param name="log">Told once if the capture fails.</param>
        public static Jfs2Capture Create(string path, string app, string simulator, DateTime startUtc, Action<string> log)
        {
            List<KeyValuePair<string, string>> metadata = [new("app", app)];
            if (simulator.Length > 0)
            {
                metadata.Add(new("sim", simulator));
            }
            var header = new RecordingHeader
            {
                StartUtcMicroseconds = (startUtc.ToUniversalTime() - DateTime.UnixEpoch).Ticks / 10,
                Metadata = metadata,
            };
            var writer = RecordingWriter.Create(path, header);
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
                writer.Add(kinds.TrackOpen, track, time, new TrackOpenV1(owner, nickname, sourceApp, simulator).Encode());
                lastTime[track] = time;
                variableStates[track] = new VariableState();
                identity = Sanitised(identity, track);
                int length = identityCodec.Encode(identity, identityBody);
                writer.Add(kinds.Identity, track, time, identityBody.AsSpan(0, length));
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
        /// Adds a position sample of an open track. The body's object id and time are set from the track and the time (spec 5:
        /// the record header is the authority, the body only has to be well-formed).
        /// </summary>
        public void Position(uint track, double seconds, ref PositionUpdate position)
        {
            if (Failed || finished || !lastTime.ContainsKey(track))
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
            if (Failed || finished || !lastTime.ContainsKey(track))
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
            if (Failed || finished || !variableStates.TryGetValue(track, out VariableState state))
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

                long time = TimeOf(track, seconds);
                // a larger update is split into records of the same time (spec 7.6)
                for (int start = 0; start < changes.Count; start += MaxVariablesPerRecord)
                {
                    var update = new VariableSyncUpdate
                    {
                        ObjectId = track,
                        Entries = changes.GetRange(start, Math.Min(MaxVariablesPerRecord, changes.Count - start)),
                    };
                    int length = variablesCodec.Encode(update, variablesBody);
                    writer.Add(kinds.Variables, track, time, variablesBody.AsSpan(0, length));
                    VariableRecordCount++;
                }
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

        /// <summary>Ends the file: closes the open tracks, writes the Index and Footer. Safe to call twice or after a failure.</summary>
        public void Finish()
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

        /// <summary>Recording seconds as the format's microseconds, never before the track's last record or the previous chunk.</summary>
        long TimeOf(uint track, double seconds)
        {
            long time = (long)Math.Round(seconds * 1e6);
            if (lastTime.TryGetValue(track, out long last) && time < last)
            {
                time = last;
            }
            return Math.Max(time, floor);
        }

        void EndChunkIfDue()
        {
            if (writer.PendingTMin is { } tmin && writer.PendingTMax - tmin >= ChunkSpanMicroseconds)
            {
                floor = tmin;
                writer.EndChunk();
            }
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
