using System;
using System.Collections.Generic;

namespace JoinFS.Net.Jfp2
{
    /// <summary>
    /// JFP2 guaranteed delivery (docs/reference/jfp2-protocol.md §4.4): a guaranteed message is resent
    /// every 2 s until acknowledged or 180 s pass. Each attempt goes through the target's current next
    /// hop, so a route change or a session that drops and is verified again does not strand it; while
    /// JFP2 has no route to the target the message waits. Receivers ack every copy and deliver once,
    /// remembering ids for 30 s.
    ///
    /// Messages are single-segment: multi-segment delivery is specified but not implemented. Pending
    /// entries and acks already carry the segment index, and <see cref="Send"/> and
    /// <see cref="Reassemble"/> are the two places segmentation plugs in.
    /// </summary>
    sealed class Jfp2Reliability
    {
        /// <summary>
        /// Payload bytes per guaranteed segment once segmentation exists. Legacy uses 1000
        /// (LegacyWire.MaxGuaranteedData) to stay under a safe UDP MTU.
        /// </summary>
        public const int GuaranteedSegmentSize = 1000;

        const double RetryInterval = 2.0;
        const double ExpireTime = 180.0;
        const double DedupWindow = 30.0;

        sealed class Pending
        {
            /// <summary>Who the message is from: this node, or the author a relay re-sends it for.</summary>
            public NodeId Origin;
            public byte MessageClass;
            /// <summary>The schema version the payload is encoded in; only a hop that agreed it can carry it.</summary>
            public byte Version;
            public byte[] Payload;
            public byte Count;
            /// <summary>The neighbor of the latest transmission (default until one went out).</summary>
            public NodeId Hop;
            public double NextRetry;
            public double Expire;
        }

        /// <summary>
        /// Send one guaranteed segment toward <paramref name="target"/> through its current next hop.
        /// False when JFP2 has no hop to it right now that agreed <paramref name="version"/>.
        /// </summary>
        public delegate bool Transmit(NodeId target, NodeId origin, byte messageClass, byte version, ReadOnlySpan<byte> payload,
            ushort guaranteedId, byte guaranteedIndex, byte guaranteedCount, out NodeId hop);

        readonly IProtocolHost host;
        readonly Transmit transmit;
        readonly Dictionary<(NodeId Target, ushort Id, byte Index), Pending> pending = [];
        readonly Dictionary<(NodeId Sender, ushort Id), double> recentlySeen = [];
        readonly List<(NodeId, ushort, byte)> scratchPendingKeys = [];
        readonly List<(NodeId, ushort)> scratchSeenKeys = [];
        ushort nextGuaranteedId;
        double nextDedupSweep;

        /// <param name="firstGuaranteedId">
        /// Where this node's ids start. Receivers suppress an id they saw from us within the last 30 s,
        /// so a node that restarts must not count from the same place again (legacy seeds from the
        /// clock the same way).
        /// </param>
        public Jfp2Reliability(IProtocolHost host, Transmit transmit, ushort firstGuaranteedId)
        {
            this.host = host;
            this.transmit = transmit;
            nextGuaranteedId = firstGuaranteedId == 0 ? (ushort)1 : firstGuaranteedId;
        }

        double Now => host.Clock.Now;

        public int PendingCount => pending.Count;

        ushort NextId()
        {
            ushort id = nextGuaranteedId++;
            if (nextGuaranteedId == 0) nextGuaranteedId = 1;
            return id;
        }

        /// <summary>Send <paramref name="payload"/> (encoded in <paramref name="version"/>) guaranteed to <paramref name="target"/>, and keep it until acknowledged.</summary>
        public void Send(NodeId target, NodeId origin, byte messageClass, byte version, ReadOnlySpan<byte> payload)
        {
            if (payload.Length > GuaranteedSegmentSize)
            {
                host.Log(NetLogLevel.Network, "JFP2: guaranteed class " + messageClass + " payload of " + payload.Length + " bytes sent as one datagram (segmentation not implemented)");
            }
            double now = Now;
            var key = (target, NextId(), (byte)0);
            var message = new Pending
            {
                Origin = origin,
                MessageClass = messageClass,
                Version = version,
                Payload = payload.ToArray(),
                Count = 1,
                NextRetry = now + RetryInterval,
                Expire = now + ExpireTime,
            };
            pending[key] = message;
            TransmitNow(key, message);
        }

        void TransmitNow((NodeId Target, ushort Id, byte Index) key, Pending p)
        {
            if (transmit(key.Target, p.Origin, p.MessageClass, p.Version, p.Payload, key.Id, key.Index, p.Count, out NodeId hop))
            {
                p.Hop = hop;
            }
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
                    if (now > p.Expire)
                    {
                        host.Log(NetLogLevel.Event, "JFP2: giving up on guaranteed message class " + p.MessageClass + " to " + kv.Key.Target + " after " + ExpireTime + " s");
                        scratchPendingKeys.Add(kv.Key);
                        continue;
                    }
                    p.NextRetry = now + RetryInterval;
                    TransmitNow(kv.Key, p);
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
        /// Clear the pending segment a GuaranteedDone confirms; true if one matched.
        /// <list type="bullet">
        /// <item>A Forwarded ack is addressed end to end, to <paramref name="ackFor"/>: it confirms only a
        /// segment sent for that origin. At a relay this keeps an ack of someone else's message, whose id
        /// that origin assigned, from clearing the relay's own message with the same id.</item>
        /// <item>A plain ack (<paramref name="ackFor"/> null) comes from the neighbor we handed the segment
        /// to. When we sent through it toward another target the ack names the neighbor, not the target,
        /// so it falls back to (id, index, hop).</item>
        /// </list>
        /// </summary>
        public bool Acknowledge(NodeId acker, NodeId hop, NodeId? ackFor, ushort guaranteedId, byte index)
        {
            var key = (acker, guaranteedId, index);
            if (pending.TryGetValue(key, out Pending p) && (ackFor == null || p.Origin == ackFor.Value))
            {
                pending.Remove(key);
                return true;
            }
            if (ackFor != null)
            {
                return false;
            }
            scratchPendingKeys.Clear();
            foreach (var kv in pending)
            {
                if (kv.Key.Id == guaranteedId && kv.Key.Index == index && kv.Value.Hop == hop) scratchPendingKeys.Add(kv.Key);
            }
            foreach (var k in scratchPendingKeys) pending.Remove(k);
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

        /// <summary>A peer left: its messages have nowhere to go. Messages that only passed through it re-route.</summary>
        public void RemovePeer(NodeId peer)
        {
            pending.RemoveWhere((k, _) => k.Target == peer);
            recentlySeen.RemoveWhere((k, _) => k.Sender == peer);
        }

        public void Clear()
        {
            pending.Clear();
            recentlySeen.Clear();
        }
    }
}
