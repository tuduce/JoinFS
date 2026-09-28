using System;
using System.Collections.Generic;
using System.Net;

namespace JoinFS.Net.Jfp2
{
    /// <summary>
    /// JFP2 guaranteed delivery (docs/reference/jfp2-protocol.md §4.4): every guaranteed datagram is
    /// resent every 2 s until acknowledged, up to 5 attempts; receivers ack every copy and deliver
    /// once, remembering ids for 30 s. Messages are single-segment: multi-segment delivery is
    /// specified but not implemented. Pending entries and acks already carry the segment index, and
    /// <see cref="Send"/> and <see cref="Reassemble"/> are the two places segmentation plugs in.
    /// </summary>
    sealed class Jfp2Reliability
    {
        /// <summary>
        /// Payload bytes per guaranteed segment once segmentation exists. Legacy uses 1000
        /// (LegacyWire.MaxGuaranteedData) to stay under a safe UDP MTU.
        /// </summary>
        public const int GuaranteedSegmentSize = 1000;

        const double RetryInterval = 2.0;
        const int MaxAttempts = 5;
        const double DedupWindow = 30.0;

        sealed class Pending
        {
            public IPEndPoint EndPoint;
            public EnvelopeFlags Flags;
            public byte MessageClass;
            public ushort SenderPeerId;
            public ushort RecipientPeerId;
            public RelayNuid Origin;
            public RelayNuid Target;
            /// <summary>The neighbor the datagram went to (differs from the key's peer when relayed).</summary>
            public NodeId Hop;
            public byte[] Payload;
            public byte Count;
            public int Attempts;
            public double NextRetry;
        }

        /// <summary>Frames and sends one datagram; Jfp2Plugin.SendDatagram.</summary>
        public delegate void SendSegment(IPEndPoint endPoint, EnvelopeFlags flags, byte messageClass, ushort senderPeerId, ushort recipientPeerId,
            ReadOnlySpan<byte> payload, ushort guaranteedId, byte guaranteedIndex, byte guaranteedCount, RelayNuid origin, RelayNuid target);

        readonly IProtocolHost host;
        readonly SendSegment send;
        readonly Dictionary<(NodeId Peer, ushort Id, byte Index), Pending> pending = [];
        readonly Dictionary<(NodeId Peer, ushort Id), double> recentlySeen = [];
        readonly List<(NodeId, ushort, byte)> scratchPendingKeys = [];
        readonly List<(NodeId, ushort)> scratchSeenKeys = [];
        ushort nextGuaranteedId = 1;
        double nextDedupSweep;

        public Jfp2Reliability(IProtocolHost host, SendSegment send)
        {
            this.host = host;
            this.send = send;
        }

        double Now => host.Clock.Now;

        ushort NextId()
        {
            ushort id = nextGuaranteedId++;
            if (nextGuaranteedId == 0) nextGuaranteedId = 1;
            return id;
        }

        /// <summary>Send <paramref name="payload"/> guaranteed to <paramref name="target"/> through <paramref name="hop"/>, and track it until acknowledged.</summary>
        public void Send(NodeId target, NodeId hop, IPEndPoint endPoint, EnvelopeFlags flags, byte messageClass,
            ushort senderPeerId, ushort recipientPeerId, ReadOnlySpan<byte> payload, RelayNuid origin, RelayNuid destination)
        {
            if (payload.Length > GuaranteedSegmentSize)
            {
                host.Log(NetLogLevel.Network, "JFP2: guaranteed class " + messageClass + " payload of " + payload.Length + " bytes sent as one datagram (segmentation not implemented)");
            }
            ushort id = NextId();
            const byte index = 0, count = 1;
            flags |= EnvelopeFlags.Guaranteed;
            send(endPoint, flags, messageClass, senderPeerId, recipientPeerId, payload, id, index, count, origin, destination);
            pending[(target, id, index)] = new Pending
            {
                EndPoint = endPoint,
                Flags = flags,
                MessageClass = messageClass,
                SenderPeerId = senderPeerId,
                RecipientPeerId = recipientPeerId,
                Origin = origin,
                Target = destination,
                Hop = hop,
                Payload = payload.ToArray(),
                Count = count,
                Attempts = 1,
                NextRetry = Now + RetryInterval,
            };
        }

        public void Tick()
        {
            double now = Now;
            if (pending.Count > 0)
            {
                scratchPendingKeys.Clear();
                foreach (var kv in pending)
                {
                    Pending p = kv.Value;
                    if (now <= p.NextRetry) continue;
                    if (p.Attempts >= MaxAttempts)
                    {
                        host.Log(NetLogLevel.Event, "JFP2: giving up on guaranteed message class " + p.MessageClass + " to " + kv.Key.Peer + " after " + p.Attempts + " attempts");
                        scratchPendingKeys.Add(kv.Key);
                        continue;
                    }
                    p.Attempts++;
                    p.NextRetry = now + RetryInterval;
                    send(p.EndPoint, p.Flags, p.MessageClass, p.SenderPeerId, p.RecipientPeerId, p.Payload, kv.Key.Id, kv.Key.Index, p.Count, p.Origin, p.Target);
                }
                foreach (var key in scratchPendingKeys) pending.Remove(key);
            }
            if (now >= nextDedupSweep && recentlySeen.Count > 0)
            {
                nextDedupSweep = now + DedupWindow;
                scratchSeenKeys.Clear();
                foreach (var kv in recentlySeen)
                {
                    if (now - kv.Value >= DedupWindow) scratchSeenKeys.Add(kv.Key);
                }
                foreach (var key in scratchSeenKeys) recentlySeen.Remove(key);
            }
        }

        /// <summary>
        /// Clear the pending segment a GuaranteedDone confirms; true if one matched. Matched by
        /// (acker, id, index) first. A plain (not Forwarded) ack from a neighbor we sent through toward
        /// another target names the neighbor, not the target, so it falls back to (id, index, hop).
        /// </summary>
        public bool Acknowledge(NodeId acker, NodeId hop, bool ackWasForwarded, ushort guaranteedId, byte index)
        {
            if (pending.Remove((acker, guaranteedId, index)))
            {
                return true;
            }
            if (ackWasForwarded)
            {
                return false;
            }
            scratchPendingKeys.Clear();
            foreach (var kv in pending)
            {
                if (kv.Key.Id == guaranteedId && kv.Key.Index == index && kv.Value.Hop == hop) scratchPendingKeys.Add(kv.Key);
            }
            foreach (var key in scratchPendingKeys) pending.Remove(key);
            return scratchPendingKeys.Count > 0;
        }

        /// <summary>Record a received guaranteed id; true if it was already delivered.</summary>
        public bool IsDuplicate(NodeId sender, ushort guaranteedId)
        {
            var key = (sender, guaranteedId);
            bool duplicate = recentlySeen.ContainsKey(key);
            recentlySeen[key] = Now;
            return duplicate;
        }

        /// <summary>
        /// Turn a received guaranteed segment into its complete message; false while (or because) it
        /// is not complete. Single-segment messages pass straight through.
        /// </summary>
        public bool Reassemble(NodeId sender, ushort guaranteedId, byte index, byte count, ReadOnlySpan<byte> segment, out ReadOnlySpan<byte> message)
        {
            if (count <= 1)
            {
                message = segment;
                return true;
            }
            // Implementing this needs a per-(sender, id) segment buffer here, and IsDuplicate must then
            // run on completion instead of per datagram: keyed by id alone, it drops segments 2..N.
            host.Log(NetLogLevel.Network, "JFP2: dropped segment " + index + "/" + count + " of guaranteed id " + guaranteedId + " from " + sender + " (segmentation not implemented)");
            message = default;
            return false;
        }

        public void RemovePeer(NodeId peer)
        {
            pending.RemoveWhere((k, p) => k.Peer == peer || p.Hop == peer);
            recentlySeen.RemoveWhere((k, _) => k.Peer == peer);
        }

        public void Clear()
        {
            pending.Clear();
            recentlySeen.Clear();
        }
    }
}
