using System;
using System.Collections.Generic;
using System.Net;

namespace JoinFS.Net.Legacy
{
    /// <summary>
    /// The legacy guaranteed-delivery layer: messages flagged Guaranteed are split into segments of
    /// at most <see cref="LegacyWire.MaxGuaranteedData"/> payload bytes (each with a copy of the
    /// header, index/count patched), sent, and resent every 2 s until every segment is acknowledged
    /// with a GuaranteedDone or 180 s pass. Receivers ack every segment, reassemble multi-segment
    /// messages, and suppress duplicates for 240 s.
    ///
    /// Same wire behaviour as LocalNode's, with its bugs fixed on our side: the first transmission
    /// is immediate (not on the next tick), resends always go to the recipient's current route
    /// (Finding 8), acks are matched by (id, recipient) (Finding 9), and a peer's pending messages
    /// are dropped when it leaves.
    /// </summary>
    sealed class LegacyReliability
    {
        const double ResendInterval = 2;
        const double OutExpireTime = 180;
        const double InExpireTime = 240;

        sealed class Outgoing
        {
            public ushort Id;
            /// <summary>Header sender: us, or the original sender of a message translated on its behalf.</summary>
            public NodeId Sender;
            public NodeId Recipient;
            public IPEndPoint EndPoint;
            public readonly List<byte[]> Segments = [];
            public bool[] Acked;
            public double ResendTime;
            public double ExpireTime;

            public bool Complete => Array.TrueForAll(Acked, a => a);
        }

        sealed class Incoming
        {
            public IPEndPoint NodeEndPoint;
            public ushort Id;
            public byte[][] Segments;
            public double ExpireTime;

            public bool Done => Segments == null;
        }

        readonly IProtocolHost host;
        readonly List<Outgoing> outgoing = [];
        readonly List<Incoming> incoming = [];
        ushort nextId;

        public LegacyReliability(IProtocolHost host, ushort firstId)
        {
            this.host = host;
            nextId = firstId;
        }

        public int OutgoingCount => outgoing.Count;
        public int IncomingCount => incoming.Count;

        public ushort NextId() => nextId++;

        /// <summary>
        /// Queue <paramref name="datagram"/> (header + payload, recipient already patched) for
        /// guaranteed delivery and transmit it.
        /// </summary>
        public void Send(ushort id, NodeId sender, NodeId recipient, IPEndPoint endPoint, ReadOnlySpan<byte> datagram)
        {
            double now = host.Clock.Now;
            var message = new Outgoing
            {
                Id = id,
                Sender = sender,
                Recipient = recipient,
                EndPoint = endPoint,
                ResendTime = now + ResendInterval,
                ExpireTime = now + OutExpireTime,
            };
            ReadOnlySpan<byte> header = datagram[..LegacyWire.DataOffset];
            for (int offset = LegacyWire.DataOffset; offset < datagram.Length; offset += LegacyWire.MaxGuaranteedData)
            {
                int length = Math.Min(datagram.Length - offset, LegacyWire.MaxGuaranteedData);
                byte[] segment = new byte[LegacyWire.DataOffset + length];
                header.CopyTo(segment);
                datagram.Slice(offset, length).CopyTo(segment.AsSpan(LegacyWire.DataOffset));
                message.Segments.Add(segment);
            }
            if (message.Segments.Count > 1)
            {
                for (int index = 0; index < message.Segments.Count; index++)
                {
                    message.Segments[index][LegacyWire.GuaranteedIndexOffset] = (byte)index;
                    message.Segments[index][LegacyWire.GuaranteedCountOffset] = (byte)message.Segments.Count;
                }
            }
            message.Acked = new bool[message.Segments.Count];
            outgoing.Add(message);
            Transmit(message);
        }

        IPEndPoint CurrentEndPoint(Outgoing message) =>
            message.Recipient.Valid() && host.Peers.TryGet(message.Recipient, out Peer peer) ? peer.RouteEndPoint : message.EndPoint;

        void Transmit(Outgoing message)
        {
            IPEndPoint endPoint = CurrentEndPoint(message);
            for (int i = 0; i < message.Segments.Count; i++)
            {
                if (!message.Acked[i])
                {
                    host.Transport.Send(endPoint, message.Segments[i]);
                }
            }
        }

        /// <summary>A GuaranteedDone arrived from <paramref name="sender"/> for one of our own messages.</summary>
        public void Acknowledge(NodeId sender, ushort id, byte index) =>
            Acknowledge(outgoing.FindIndex(m => m.Id == id && m.Sender == host.Identity.Id && (!m.Recipient.Valid() || m.Recipient == sender)), index);

        /// <summary>
        /// A GuaranteedDone from <paramref name="acker"/> addressed to <paramref name="originalSender"/>
        /// (not us): if it acknowledges a message we sent on that sender's behalf (a translation at
        /// this hub), consume it here instead of relaying it to a sender that never sent that id.
        /// </summary>
        public bool AcknowledgeProxied(NodeId originalSender, NodeId acker, ushort id, byte index)
        {
            int i = outgoing.FindIndex(m => m.Id == id && m.Sender == originalSender && m.Recipient == acker);
            Acknowledge(i, index);
            return i >= 0;
        }

        void Acknowledge(int i, byte index)
        {
            if (i >= 0 && index < outgoing[i].Acked.Length)
            {
                outgoing[i].Acked[index] = true;
                if (outgoing[i].Complete)
                {
                    outgoing.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// A guaranteed segment arrived (already acked by the caller). Returns false if the message
        /// was already processed or is still incomplete; otherwise true, with the complete message
        /// (header + reassembled payload) in <paramref name="message"/>.
        /// </summary>
        public bool Receive(IPEndPoint nodeEndPoint, ushort id, byte index, byte count, byte[] datagram, int length, out byte[] message, out int messageLength)
        {
            message = datagram;
            messageLength = length;
            Incoming entry = incoming.Find(g => g.NodeEndPoint.Equals(nodeEndPoint) && g.Id == id);
            if (entry == null)
            {
                entry = new Incoming
                {
                    NodeEndPoint = nodeEndPoint,
                    Id = id,
                    Segments = new byte[count][],
                    ExpireTime = host.Clock.Now + InExpireTime,
                };
                incoming.Add(entry);
            }
            if (entry.Done)
            {
                return false;
            }
            if (count == 1)
            {
                entry.Segments = null;
                return true;
            }
            if (index < entry.Segments.Length && entry.Segments[index] == null)
            {
                entry.Segments[index] = datagram.AsSpan(0, length).ToArray();
            }
            foreach (byte[] segment in entry.Segments)
            {
                if (segment == null) return false;
            }
            int total = LegacyWire.DataOffset;
            foreach (byte[] segment in entry.Segments) total += segment.Length - LegacyWire.DataOffset;
            message = new byte[total];
            entry.Segments[0].AsSpan(0, LegacyWire.DataOffset).CopyTo(message);
            int offset = LegacyWire.DataOffset;
            foreach (byte[] segment in entry.Segments)
            {
                segment.AsSpan(LegacyWire.DataOffset).CopyTo(message.AsSpan(offset));
                offset += segment.Length - LegacyWire.DataOffset;
            }
            messageLength = total;
            entry.Segments = null;
            return true;
        }

        public void Tick()
        {
            double now = host.Clock.Now;
            for (int i = outgoing.Count - 1; i >= 0; i--)
            {
                Outgoing message = outgoing[i];
                if (now >= message.ResendTime)
                {
                    Transmit(message);
                    message.ResendTime = now + ResendInterval;
                    if (now > message.ExpireTime)
                    {
                        outgoing.RemoveAt(i);
                    }
                }
            }
            incoming.RemoveAll(g => now > g.ExpireTime);
        }

        public void RemovePeer(Peer peer)
        {
            outgoing.RemoveAll(m => m.Recipient == peer.Id || (!m.Recipient.Valid() && m.EndPoint.Equals(peer.EndPoint)));
        }

        public void Clear()
        {
            outgoing.Clear();
            incoming.Clear();
        }
    }
}
