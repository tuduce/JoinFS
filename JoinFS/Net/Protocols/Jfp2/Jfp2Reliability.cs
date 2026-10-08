using System;
using System.Collections.Generic;

namespace JoinFS.Net.Jfp2
{
    /// <summary>
    /// JFP2 guaranteed delivery (docs/jfp2/protocol.md §7): a guaranteed message is resent
    /// every 2 s until acknowledged or 180 s pass. Each attempt goes through the target's current next
    /// hop, so a route change or a session that drops and is verified again does not strand it; while
    /// JFP2 has no route to the target the message waits. Receivers ack every copy and deliver once,
    /// remembering ids for 30 s.
    ///
    /// A guaranteed id is unique per (origin, final target) within those 30 s; one message to several
    /// targets may share one (docs/jfp2/protocol.md §7.2). So everything here is keyed by
    /// both: pending segments by (origin, target, id, index), since a relay re-sending on an origin's
    /// behalf keeps the origin's id; received ids by (origin, target, id), since a relay that
    /// translates sees one origin's id for several targets. A node that is the final target is the
    /// target of its own keys.
    ///
    /// Messages are single-segment: multi-segment delivery is specified but not implemented. Pending
    /// entries and acks already carry the segment index, and <see cref="Send"/> and
    /// <see cref="Reassemble"/> are the two places segmentation plugs in.
    /// </summary>
    sealed class Jfp2Reliability
    {
        /// <summary>
        /// Payload bytes per guaranteed segment once segmentation exists: the payload ceiling that
        /// keeps a datagram within 1,200 bytes with every header (docs/jfp2/protocol.md
        /// §3.6). The field limits keep every v1 payload within it, so today a larger one is a codec bug.
        /// </summary>
        public const int GuaranteedSegmentSize = Envelope.MaxPayloadSize;

        const double RetryInterval = 2.0;
        const double ExpireTime = 180.0;
        const double DedupWindow = 30.0;

        sealed class Pending
        {
            public byte MessageClass;
            /// <summary>The schema version the payload is encoded in; only a hop that agreed it can carry it.</summary>
            public byte Version;
            public byte[] Payload;
            public byte Count;
            public double NextRetry;
            public double Expire;
        }

        /// <summary>
        /// Send one guaranteed segment toward <paramref name="target"/> through its current next hop.
        /// False when JFP2 has no hop to it right now that agreed <paramref name="version"/>.
        /// </summary>
        public delegate bool Transmit(NodeId target, NodeId origin, byte messageClass, byte version, ReadOnlySpan<byte> payload,
            ushort guaranteedId, byte guaranteedIndex, byte guaranteedCount);

        readonly IProtocolHost host;
        readonly Transmit transmit;
        /// <summary>Keyed by who the message is from (this node, or the origin a relay re-sends it for), its final target, its id and the segment.</summary>
        readonly Dictionary<(NodeId Origin, NodeId Target, ushort Id, byte Index), Pending> pending = [];
        readonly Dictionary<(NodeId Origin, NodeId Target, ushort Id), double> recentlySeen = [];
        readonly List<(NodeId, NodeId, ushort, byte)> scratchPendingKeys = [];
        readonly List<(NodeId, NodeId, ushort)> scratchSeenKeys = [];
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

        /// <summary>
        /// Send <paramref name="payload"/> (encoded in <paramref name="version"/>) guaranteed to
        /// <paramref name="target"/>, and keep it until acknowledged. A relay re-sending on
        /// <paramref name="origin"/>'s behalf passes the id the origin gave it, <paramref name="originId"/>:
        /// the target deduplicates by the origin's ids, so one from this node's counter could collide
        /// with one of the origin's own. 0: this node's next id.
        /// </summary>
        public void Send(NodeId target, NodeId origin, byte messageClass, byte version, ReadOnlySpan<byte> payload, ushort originId = 0)
        {
            if (payload.Length > GuaranteedSegmentSize)
            {
                host.Log(NetLogLevel.Network, "JFP2: guaranteed class " + messageClass + " payload of " + payload.Length + " bytes is over the "
                    + GuaranteedSegmentSize + "-byte ceiling (a codec exceeds its field limits) - sent as one datagram");
            }
            double now = Now;
            var key = (origin, target, originId != 0 ? originId : NextId(), (byte)0);
            var message = new Pending
            {
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

        void TransmitNow((NodeId Origin, NodeId Target, ushort Id, byte Index) key, Pending p) =>
            transmit(key.Target, key.Origin, p.MessageClass, p.Version, p.Payload, key.Id, key.Index, p.Count);

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
        /// Clear the pending segment a GuaranteedDone confirms; true if one matched. The ack names both
        /// ends exactly: <paramref name="acker"/> is the final target that acknowledges, and
        /// <paramref name="ackedOrigin"/> the origin of the message (this node, or at a relay the
        /// origin it re-sent for). A plain ack comes from a neighbor that was the final target of our
        /// own message; a Forwarded one carries both in its Origin and Target. So an ack never clears
        /// another origin's message, or another target's copy that has the same id.
        /// </summary>
        public bool Acknowledge(NodeId acker, NodeId ackedOrigin, ushort guaranteedId, byte index) =>
            pending.Remove((ackedOrigin, acker, guaranteedId, index));

        /// <summary>Record a received guaranteed id from <paramref name="origin"/> for <paramref name="target"/> (this node, or the one a relay translates for); true if it was already delivered.</summary>
        public bool IsDuplicate(NodeId origin, NodeId target, ushort guaranteedId)
        {
            var key = (origin, target, guaranteedId);
            bool duplicate = recentlySeen.ContainsKey(key);
            recentlySeen[key] = Now;
            return duplicate;
        }

        /// <summary>
        /// Turn a received guaranteed segment into its complete message; false while (or because) it
        /// is not complete. Single-segment messages pass straight through.
        /// </summary>
        public bool Reassemble(NodeId origin, NodeId target, ushort guaranteedId, byte index, byte count, ReadOnlySpan<byte> segment, out ReadOnlySpan<byte> message)
        {
            if (count <= 1)
            {
                message = segment;
                return true;
            }
            // Implementing this needs a per-(origin, target, id) segment buffer here, and IsDuplicate must
            // then run on completion instead of per datagram: keyed by id alone, it drops segments 2..N.
            // Until then the caller drops the segment before acknowledging it, so its sender does not
            // take a message that was never delivered as delivered.
            host.Log(NetLogLevel.Network, "JFP2: dropped segment " + index + "/" + count + " of guaranteed id " + guaranteedId + " from " + origin + " for " + target + " (segmentation not implemented)");
            message = default;
            return false;
        }

        /// <summary>A peer left: its messages have nowhere to go. Messages that only passed through it re-route.</summary>
        public void RemovePeer(NodeId peer)
        {
            pending.RemoveWhere((k, _) => k.Target == peer);
            recentlySeen.RemoveWhere((k, _) => k.Origin == peer);
        }

        public void Clear()
        {
            pending.Clear();
            recentlySeen.Clear();
        }
    }
}
