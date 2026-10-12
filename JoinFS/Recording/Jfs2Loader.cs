using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JoinFS.Net;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Recording.Format;

namespace JoinFS.Recording
{
    /// <summary>
    /// Reads a JFS2 file into the objects the recorder plays (<see cref="Recorder.Obj"/>), so that opening, playing, jumping,
    /// trimming and the player UI work on it unchanged. This is the stage 2 tracer bullet: it loads the whole file, which stage 3
    /// replaces with a player that reads chunk by chunk. Stage 2 plays track-open, identity and position; everything else in the
    /// file is skipped and counted (spec 8.2, 10.2). Knows nothing of Main, so it is testable without a simulator.
    /// </summary>
    public static class Jfs2Loader
    {
        /// <param name="Objects">The recorded objects, in the order their tracks opened. Objects without a position are left out.</param>
        /// <param name="Notes">What was skipped or damaged, for the log; empty when the file was read completely.</param>
        public sealed record Result(List<Recorder.Obj> Objects, IReadOnlyList<string> Notes);

        sealed class Track
        {
            public string Nickname = "";
            public IdentityUpdate Identity;
            public bool HasIdentity;
            public readonly List<Recorder.Frame> Frames = [];
            public int PositionCount;
        }

        /// <summary>The kinds this stage plays; everything else is skipped by name and version.</summary>
        static readonly (string Name, byte Version)[] Played =
        [
            (Jfs2Kinds.TrackOpen, Jfs2Kinds.TrackOpenVersion),
            (Jfs2Kinds.TrackClose, Jfs2Kinds.TrackCloseVersion),
            (Jfs2Kinds.Identity, Jfs2Kinds.IdentityVersion),
            (Jfs2Kinds.Position, Jfs2Kinds.PositionVersion),
            (Jfs2Kinds.Variables, Jfs2Kinds.VariablesVersion),
            (Jfs2Kinds.Event, Jfs2Kinds.EventVersion),
        ];

        /// <summary>Kinds without which a recording shows nothing (spec 8.2).</summary>
        static readonly string[] Essential = [Jfs2Kinds.TrackOpen, Jfs2Kinds.Identity, Jfs2Kinds.Position, "object-position"];

        /// <exception cref="InvalidOperationException">The file is not a JFS2 file this build can play; the message says why.</exception>
        public static Result Load(Stream stream)
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

        static Result Read(RecordingReader reader)
        {
            List<string> notes = [];
            Dictionary<uint, Track> tracks = [];
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
                if (lost && !chunk.Info.IsKeyframe)
                {
                    continue;
                }
                lost = false;

                // A linear read has seen what a snapshot summarises. (After a lost chunk it would restore state; stage 3.)
                foreach (RecordEntry entry in chunk.Records.Skip(chunk.SnapshotCount))
                {
                    KindBinding kind = reader.GetKind(entry.Kind);
                    if (kind == null || !IsPlayed(kind))
                    {
                        string key = kind == null ? "kind " + entry.Kind : kind.Name + " v" + kind.Version;
                        skipped[key] = skipped.GetValueOrDefault(key) + 1;
                        continue;
                    }

                    try
                    {
                        switch (kind.Name)
                        {
                            case Jfs2Kinds.TrackOpen:
                                if (!tracks.ContainsKey(entry.Track))
                                {
                                    tracks[entry.Track] = new Track { Nickname = TrackOpenV1.Decode(entry.Body.Span).Nickname };
                                    order.Add(entry.Track);
                                }
                                break;

                            case Jfs2Kinds.Identity:
                                if (tracks.TryGetValue(entry.Track, out Track identityTrack))
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
                                if (tracks.TryGetValue(entry.Track, out Track positionTrack))
                                {
                                    PositionUpdate position = positionCodec.Decode(entry.Body.Span);
                                    Sim.AircraftPosition data = SimMessageMapper.ToAircraftPosition(position);
                                    positionTrack.Frames.Add(new Recorder.AircraftPositionFrame(entry.Time / 1e6, ref data));
                                    positionTrack.PositionCount++;
                                }
                                break;

                            case Jfs2Kinds.Variables:
                                if (tracks.TryGetValue(entry.Track, out Track variablesTrack))
                                {
                                    AddVariableFrames(variablesTrack.Frames, entry.Time / 1e6, variablesCodec.Decode(entry.Body.Span));
                                }
                                break;

                            case Jfs2Kinds.Event:
                                if (tracks.TryGetValue(entry.Track, out Track eventTrack))
                                {
                                    EventUpdate update = eventCodec.Decode(entry.Body.Span);
                                    eventTrack.Frames.Add(new Recorder.SimEventFrame(entry.Time / 1e6, update.EventId, update.Data));
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
            if (identityChanges > 0)
            {
                notes.Add(identityChanges + " identity changes were ignored; each aircraft plays with the identity it opened with.");
            }
            foreach (var count in skipped.OrderBy(pair => pair.Key))
            {
                notes.Add(count.Value + " '" + count.Key + "' records were skipped; this build does not play them yet.");
            }

            List<Recorder.Obj> objects = [];
            int notAircraft = 0;
            foreach (uint id in order)
            {
                Track track = tracks[id];
                if (track.PositionCount == 0 || !track.HasIdentity)
                {
                    continue;
                }
                if (track.Identity.IsAircraft)
                {
                    objects.Add(Build(track));
                }
                else
                {
                    notAircraft++;
                }
            }
            if (notAircraft > 0)
            {
                notes.Add(notAircraft + " objects that are not aircraft were skipped; this build does not play them yet.");
            }

            if (objects.Count == 0)
            {
                bool newer = skipped.Keys.Any(key => Essential.Any(name => key.StartsWith(name + " v", StringComparison.Ordinal)));
                throw new InvalidOperationException(newer
                    ? "This recording needs a newer version of JoinFS to play: " + string.Join(" ", notes)
                    : "This recording holds nothing that can be played. " + string.Join(" ", notes));
            }
            return new Result(objects, notes);
        }

        /// <summary>
        /// One variables record becomes up to three of the old frames, one per value kind. A frame holds only what the record
        /// changed; the old player merges frames into the object's variable set, which is how an upsert reads (spec 7.6).
        /// </summary>
        static void AddVariableFrames(List<Recorder.Frame> frames, double time, VariableSyncUpdate update)
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
            if (integers.Count > 0) frames.Add(new Recorder.IntegerVariablesFrame(time, integers));
            if (floats.Count > 0) frames.Add(new Recorder.FloatVariablesFrame(time, floats));
            if (strings.Count > 0) frames.Add(new Recorder.String8VariablesFrame(time, strings));
        }

        static bool IsPlayed(KindBinding kind) => Played.Any(played => played.Name == kind.Name && played.Version == kind.Version);

        static Recorder.Obj Build(Track track)
        {
            IdentityUpdate id = track.Identity;
            var obj = new Recorder.Aircraft { plane = id.IsPlane, callsign = id.Callsign, nickname = track.Nickname };
            obj.model = id.Model;
            obj.livery = id.Livery;
            obj.icaoType = id.IcaoType;
            obj.icaoAirline = id.IcaoAirline;
            obj.typerole = id.TypeRole;
            obj.frames.AddRange(track.Frames);
            return obj;
        }
    }
}
