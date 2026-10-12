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
    /// Stage 2 writes track-open, identity and position only.
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

        readonly RecordingWriter writer;
        readonly Jfs2KindIds kinds;
        readonly Action<string> log;
        readonly Dictionary<uint, long> lastTime = [];
        readonly PositionV1Codec positionCodec = new();
        readonly IdentityV1Codec identityCodec = new();
        readonly byte[] positionBody = new byte[PositionV1Codec.Size];
        readonly byte[] identityBody = new byte[IdentityV1Codec.MaxSize];

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
