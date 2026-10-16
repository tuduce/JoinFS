using System;
using System.Collections.Generic;
using System.Net;

// docs/jfp2/protocol.md §9.8: the body of the internal class PeerCandidates (edition 2, the IPv6 stage).

namespace JoinFS.Net.Jfp2.Codecs
{
    public sealed class PeerCandidatesV1Codec : ICodec<PeerCandidates>
    {
        public byte MessageClass => MessageClasses.PeerCandidates;
        public byte SchemaVersion => 1;

        /// <summary>Entries in one message; a longer list is sent as several (<see cref="Split"/>).</summary>
        public const int MaxEntries = 12;

        /// <summary>Candidates in one entry.</summary>
        public const int MaxCandidates = 4;

        /// <summary>The largest entry: name, candidate count, four IPv6 candidates with their length byte.</summary>
        const int MaxEntrySize = NodeName.WireSize + 1 + MaxCandidates * (1 + WireEndPoint.IPv6Size);

        /// <summary>The largest payload (1,069 bytes): the count and twelve of the largest entries.</summary>
        public const int MaxSize = 1 + MaxEntries * MaxEntrySize;

        public int Encode(in PeerCandidates v, Span<byte> dest)
        {
            int count = Math.Min(v.Entries?.Count ?? 0, MaxEntries);
            int i = 0;
            dest[i++] = (byte)count;
            for (int n = 0; n < count; n++)
            {
                PeerCandidateEntry entry = v.Entries[n];
                NodeName.FromLegacy(entry.Node).WriteTo(dest.Slice(i, NodeName.WireSize));
                i += NodeName.WireSize;
                int countAt = i++;
                int candidates = 0;
                foreach (IPEndPoint candidate in entry.Candidates ?? [])
                {
                    if (candidates == MaxCandidates) break;
                    dest[i++] = (byte)WireEndPoint.SizeOf(candidate);
                    i += WireEndPoint.WriteTo(candidate, dest[i..]);
                    candidates++;
                }
                dest[countAt] = (byte)candidates;
            }
            return i;
        }

        /// <summary>
        /// Lenient, as the rest of the extension-style data is: an entry whose name this build cannot resolve is
        /// skipped, so is a candidate of an unassigned family or too short for its family, and a payload that ends
        /// inside an entry yields the entries before it.
        /// </summary>
        public PeerCandidates Decode(ReadOnlySpan<byte> src)
        {
            var entries = new List<PeerCandidateEntry>();
            if (src.IsEmpty)
            {
                return new PeerCandidates { Entries = entries };
            }
            int count = Math.Min((int)src[0], MaxEntries);
            int i = 1;
            for (int n = 0; n < count; n++)
            {
                if (i + NodeName.WireSize + 1 > src.Length) break;
                NodeName name = NodeName.ReadFrom(src.Slice(i, NodeName.WireSize));
                i += NodeName.WireSize;
                int candidateCount = Math.Min((int)src[i++], MaxCandidates);
                var candidates = new List<IPEndPoint>(candidateCount);
                bool complete = true;
                for (int c = 0; c < candidateCount; c++)
                {
                    if (i >= src.Length) { complete = false; break; }
                    int length = src[i++];
                    if (i + length > src.Length) { complete = false; break; }
                    if (WireEndPoint.TryReadFrom(src.Slice(i, length), out IPEndPoint endPoint))
                    {
                        candidates.Add(endPoint);
                    }
                    i += length;
                }
                if (!complete) break;
                if (name.TryGetLegacy(out NodeId node) && node.Valid())
                {
                    entries.Add(new PeerCandidateEntry { Node = node, Candidates = candidates });
                }
            }
            return new PeerCandidates { Entries = entries };
        }

        /// <summary>
        /// The messages that carry a long list: the same message when it fits, else parts of at most
        /// <see cref="MaxEntries"/> entries, in order. No entries, no message.
        /// </summary>
        public static List<PeerCandidates> Split(in PeerCandidates message)
        {
            var parts = new List<PeerCandidates>();
            List<PeerCandidateEntry> entries = message.Entries;
            if (entries == null || entries.Count == 0)
            {
                return parts;
            }
            if (entries.Count <= MaxEntries)
            {
                parts.Add(message);
                return parts;
            }
            for (int start = 0; start < entries.Count; start += MaxEntries)
            {
                parts.Add(new PeerCandidates { Entries = entries.GetRange(start, Math.Min(MaxEntries, entries.Count - start)) });
            }
            return parts;
        }
    }
}
