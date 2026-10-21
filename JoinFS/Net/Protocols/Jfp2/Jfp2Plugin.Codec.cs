using System;
using System.Collections.Generic;
using System.Net;
using JoinFS.Net.Jfp2.Codecs;

namespace JoinFS.Net.Jfp2
{
    // Encode and decode: canonical messages to JFP2 datagrams and back (docs/network/jfp2/protocol.md §9).
    public sealed partial class Jfp2Plugin
    {
        // ================================================================== canonical → wire

        public void Send<T>(in MessageMeta meta, in T message, ReadOnlySpan<NodeId> recipients) where T : struct, IMessage
        {
            ClassDescriptor messageClass = profile.ForKind(T.Kind);
            if (messageClass == null)
            {
                return; // a kind JFP2 does not carry (CanCarry said so)
            }
            targets.Clear();
            sendOrigin = meta.Sender.Valid() ? meta.Sender : Local;
            // a relay re-sending a guaranteed message on its origin's behalf keeps the origin's id
            sendOriginId = sendOrigin != Local ? meta.OriginGuaranteedId : (ushort)0;
            if (meta.EndPoint != null)
            {
                NodeId known = meta.Recipient.Valid() ? meta.Recipient : host.Peers.FindByEndPoint(meta.EndPoint)?.Id ?? default;
                if (known.Valid()) targets.Add(known);
            }
            else
            {
                foreach (NodeId recipient in recipients) targets.Add(recipient);
            }
            if (messageClass.SentAsIs && messageClass is ClassDescriptor<T> plain)
            {
                encoder.SendAsIs(plain, message);
            }
            else
            {
                message.Dispatch(encoder, meta);
            }
        }

        /// <summary>
        /// Make sure <paramref name="peer"/> has this object's current identity before a position:
        /// sent the first time, whenever it changes, and every few seconds as a heartbeat (a peer that
        /// joins late or lost a datagram converges). False when no identity is known yet.
        /// </summary>
        bool EnsureIdentity(NodeId owner, uint objectId, Peer peer, PeerSession session)
        {
            if (objectId == uint.MaxValue)
            {
                return true; // shared cockpit: the recipient's own aircraft, no identity involved
            }
            if (!host.Objects.TryGetIdentity(owner, objectId, out IdentityUpdate identity))
            {
                return false;
            }
            ClassDescriptor<IdentityUpdate> identityClass = profile.ForKind<IdentityUpdate>(MessageKind.Identity);
            byte version = identityClass == null ? (byte)0 : session.AgreedAppVersion[identityClass.MessageClass];
            if (version == 0)
            {
                return true;
            }
            var key = (owner, objectId, peer.Id);
            double now = Now;
            if (identitySent.TryGetValue(key, out IdentitySent sent) && sent.Last.SameAs(identity) && now - sent.Time < IdentityHeartbeatInterval)
            {
                return true;
            }
            int length = identityClass.Codec(version).Encode(identity, payloadBuffer);
            SendApplication(peer, session, identityClass, payloadBuffer.AsSpan(0, length));
            identitySent[key] = new IdentitySent { Last = identity, Time = now };
            return true;
        }

        /// <summary>
        /// Canonical to JFP2. A class sent as it is goes through <see cref="SendAsIs"/>; each kind the
        /// plugin sends its own way has an overload here. Each loops the targets because versions are per peer.
        /// </summary>
        sealed class Encoder(Jfp2Plugin p) : IMessageHandler
        {
            public void SendAsIs<T>(ClassDescriptor<T> messageClass, in T message)
            {
                foreach (NodeId target in p.targets)
                {
                    byte version = p.AgreedVersion(target, messageClass, out Peer peer, out PeerSession session);
                    if (version == 0) continue;
                    int length = messageClass.Codec(version).Encode(message, p.payloadBuffer);
                    p.SendApplication(peer, session, messageClass, p.payloadBuffer.AsSpan(0, length));
                }
            }

            public void Handle(in MessageMeta meta, in PositionUpdate m)
            {
                if (p.profile.ForKind<PositionUpdate>(MessageKind.Position) is not { } messageClass) return;
                foreach (NodeId target in p.targets)
                {
                    byte version = p.AgreedVersion(target, messageClass, out Peer peer, out PeerSession session);
                    if (version == 0 || !p.EnsureIdentity(meta.Sender, m.ObjectId, peer, session)) continue;
                    int length = messageClass.Codec(version).Encode(m, p.payloadBuffer);
                    p.SendApplication(peer, session, messageClass, p.payloadBuffer.AsSpan(0, length));
                }
            }

            public void Handle(in MessageMeta meta, in ObjectPositionUpdate m)
            {
                if (p.profile.ForKind<ObjectPositionUpdate>(MessageKind.ObjectPosition) is not { } messageClass) return;
                foreach (NodeId target in p.targets)
                {
                    byte version = p.AgreedVersion(target, messageClass, out Peer peer, out PeerSession session);
                    if (version == 0 || !p.EnsureIdentity(meta.Sender, m.ObjectId, peer, session)) continue;
                    int length = messageClass.Codec(version).Encode(m, p.payloadBuffer);
                    p.SendApplication(peer, session, messageClass, p.payloadBuffer.AsSpan(0, length));
                }
            }

            public void Handle(in MessageMeta meta, in VariableSyncUpdate m)
            {
                if (m.Entries == null || m.Entries.Count == 0) return;
                if (p.profile.ForKind<VariableSyncUpdate>(MessageKind.VariableSync) is not { } messageClass) return;
                List<VariableSyncUpdate> chunks = null;
                foreach (NodeId target in p.targets)
                {
                    byte version = p.AgreedVersion(target, messageClass, out Peer peer, out PeerSession session);
                    if (version == 0) continue;
                    chunks ??= Chunk(m);
                    ICodec<VariableSyncUpdate> codec = messageClass.Codec(version);
                    foreach (VariableSyncUpdate chunk in chunks)
                    {
                        int length = codec.Encode(chunk, p.payloadBuffer);
                        p.SendApplication(peer, session, messageClass, p.payloadBuffer.AsSpan(0, length));
                    }
                }
            }

            /// <summary>
            /// Split into messages whose payload fits <see cref="VariableSyncMaxPayload"/>: at least one
            /// entry each, at most 255 (the count is one byte). Sizes are v1's, the only VariableSync schema.
            /// </summary>
            static List<VariableSyncUpdate> Chunk(in VariableSyncUpdate m)
            {
                var chunks = new List<VariableSyncUpdate>(1);
                int start = 0;
                while (start < m.Entries.Count)
                {
                    int end = start;
                    int size = VariableSyncV1Codec.HeaderSize;
                    while (end < m.Entries.Count && end - start < byte.MaxValue)
                    {
                        int entry = VariableSyncV1Codec.EntrySize(m.Entries[end]);
                        if (end > start && size + entry > VariableSyncMaxPayload) break;
                        size += entry;
                        end++;
                    }
                    chunks.Add(start == 0 && end == m.Entries.Count
                        ? m
                        : new VariableSyncUpdate { ObjectId = m.ObjectId, Entries = m.Entries.GetRange(start, end - start) });
                    start = end;
                }
                return chunks;
            }

            public void Handle(in MessageMeta meta, in NotesBundle m)
            {
                // a note posted goes as one Notes message. A history (the answer to a CommsRequest) goes whole as one
                // NotesHistory message, in segments, to a direct neighbour that agreed them, and as its notes, one each, to the rest
                if (p.profile.ForKind<NoteUpdate>(MessageKind.Notes) is not { } messageClass) return;
                if (m.Users.Count != 1 || m.Users[0].Notes.Count != 1)
                {
                    p.sendOriginId = 0; // an origin's id belongs to one message, not to each of several
                    p.SendHistory(m);
                }
                foreach (NotesUser user in m.Users)
                {
                    foreach (CommsNote note in user.Notes)
                    {
                        var single = new NoteUpdate
                        {
                            Guid = user.Guid, Nickname = user.Nickname, Callsign = user.Callsign,
                            NoteId = note.NoteId, Age = note.Age, Channel = note.Channel, Text = note.Text,
                        };
                        SendAsIs(messageClass, single);
                    }
                }
            }
        }

        /// <summary>
        /// The history as one message to each target that is a direct neighbour having agreed segments and the
        /// class; those are taken from <see cref="targets"/>, so that the notes go one by one to the others only.
        /// A history that a relay translates for its origin is not sent so (one message of segments would have
        /// to be translated again, §7.5).
        /// </summary>
        void SendHistory(in NotesBundle m)
        {
            if (profile.ForSegmentedKind<NotesBundle>(MessageKind.Notes) is not { } history || sendOrigin != Local) return;
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                byte version = AgreedVersion(targets[i], history, out Peer peer, out PeerSession session);
                if (version == 0 || session.Peer != targets[i] || (session.AgreedCapabilities & Jfp2Capabilities.Segments) == 0) continue;
                historyBuffer ??= new byte[NotesHistoryV1Codec.MaxSize];
                int length = history.Codec(version).Encode(m, historyBuffer);
                SendApplication(peer, session, history, historyBuffer.AsSpan(0, length));
                targets.RemoveAt(i);
            }
        }

        // ================================================================== wire → canonical

        public void OnDatagram(IPEndPoint from, ReadOnlySpan<byte> datagram)
        {
            try
            {
                // an IPv4 peer as a dual-mode socket reports it is still that IPv4 endpoint: the source
                // compared with ProbeEndPoint and the occupants, and the observation sent back, use one
                // form (WireEndPoint.Normalize; no allocation unless mapped). Routes come from the legacy
                // side and are not normalized: with a dual-mode socket they would need it too
                Receive(WireEndPoint.Normalize(from), datagram);
            }
            catch (Exception ex)
            {
                host.Log(NetLogLevel.Event, "ERROR: Failed to read JFP2 message: " + ex.Message);
            }
        }

        void Receive(IPEndPoint from, ReadOnlySpan<byte> datagram)
        {
            if (!Envelope.TryReadFrom(datagram, out Envelope envelope, out int consumed, out string unsupported))
            {
                // well formed for a build newer than this one (another ProtoMajor, a flag we cannot
                // read): not an error, and it may arrive with every datagram from that peer
                host.Log(NetLogLevel.Network, "JFP2: datagram from " + from + " uses " + unsupported + ", which this build cannot read - dropped");
                return;
            }
            ReadOnlySpan<byte> payload = datagram[consumed..];

            // the handshake is the only traffic that has no session yet; it says who is speaking itself
            if (envelope.IsInternal && envelope.RawMessageClass == MessageClasses.Hello)
            {
                HandleHello(from, payload, datagram.Length);
                return;
            }
            if (envelope.IsInternal && envelope.RawMessageClass == MessageClasses.HelloAck)
            {
                HandleHelloAck(from, envelope, payload);
                return;
            }

            // everything else names our end of the hop's session, and the neighbor's end; the source
            // endpoint plays no part (several nodes can share one)
            if (!sessionsById.TryGetValue(envelope.RecipientPeerId, out PeerSession hop) || !hop.HandshakeComplete || hop.RemoteAssignedId != envelope.SenderPeerId)
            {
                host.Log(NetLogLevel.Network, "JFP2: datagram from " + from + " belongs to no session here (ids " + envelope.SenderPeerId + " -> " + envelope.RecipientPeerId + ") - ignored");
                return;
            }

            if (envelope.IsInternal && !envelope.IsForwarded && IsMembershipClass(envelope.RawMessageClass))
            {
                HandleMembership(from, hop, envelope.RawMessageClass, payload);
                return;
            }

            if (hop.NonMember)
            {
                ReceiveDirectory(from, hop, envelope, payload);
                return;
            }

            // consumed when the target is one of our names (our id, our key name or the alias we were given), compared
            // as bytes whatever the kind; relayed otherwise
            if (envelope.IsForwarded && !IsOwnName(envelope.Target))
            {
                Relay(from, hop, envelope, datagram, payload);
                return;
            }

            NodeId sender = hop.Peer;
            if (envelope.IsForwarded && !TryResolve(envelope.Origin, out sender))
            {
                // not acknowledged either: a guaranteed sender keeps retrying until it can be placed
                host.Log(NetLogLevel.Network, "JFP2: datagram from " + from + " names its origin " + envelope.Origin + ", which this build cannot resolve - dropped");
                return;
            }
            if (envelope.IsInternal)
            {
                if (envelope.RawMessageClass == MessageClasses.GuaranteedDone && TryReadGuaranteedDone(payload, out ushort id, out byte index))
                {
                    // from the final target (the neighbor, or the Forwarded ack's origin) for our message
                    reliability.Acknowledge(sender, Local, id, index);
                }
                return;
            }

            // checked before acking: a sender must not take a message we cannot read as delivered
            byte version = hop.AgreedAppVersion[envelope.RawMessageClass];
            if (version == 0)
            {
                host.Log(NetLogLevel.Network, "JFP2: class " + envelope.RawMessageClass + " from " + hop.Peer + " was never agreed on - ignored");
                return;
            }
            if (envelope.IsGuaranteed && envelope.GuaranteedCount > 1)
            {
                ReceiveSegment(from, hop, envelope, sender, payload, version);
                return;
            }
            // also before acking: a segment this build cannot reassemble is dropped, not acknowledged
            if (!TryComplete(sender, Local, envelope, payload, out ReadOnlySpan<byte> message))
            {
                return;
            }
            if (envelope.IsGuaranteed)
            {
                // ack every copy: a duplicate means our ack was lost
                SendGuaranteedDone(from, hop, envelope.GuaranteedId, envelope.GuaranteedIndex, envelope.IsForwarded ? sender : null, Local);
                if (reliability.IsDuplicate(sender, Local, envelope.GuaranteedId))
                {
                    return;
                }
            }
            Decode(MetaOf(sender, from, envelope), envelope.RawMessageClass, version, message);
        }

        MessageMeta MetaOf(NodeId sender, IPEndPoint from, in Envelope envelope) => new()
        {
            Sender = sender,
            Recipient = Local,
            EndPoint = host.Peers.TryGet(sender, out Peer senderPeer) && senderPeer.SendEstablished ? senderPeer.RouteEndPoint : from,
            Guaranteed = envelope.IsGuaranteed,
            Forwarded = envelope.IsForwarded,
        };

        /// <summary>
        /// One segment of a guaranteed message for this node (§7.5). Only a segmented class from a neighbour that
        /// agreed the capability is taken: any other is dropped, not acknowledged. A segment is acknowledged when
        /// it is kept, a duplicate again; the message is delivered once, when its last segment arrives.
        /// </summary>
        void ReceiveSegment(IPEndPoint from, PeerSession hop, in Envelope envelope, NodeId sender, ReadOnlySpan<byte> payload, byte version)
        {
            if (profile.ForClass(envelope.RawMessageClass) is not { Segmented: true } || (hop.AgreedCapabilities & Jfp2Capabilities.Segments) == 0)
            {
                host.Log(NetLogLevel.Network, "JFP2: segment " + envelope.GuaranteedIndex + "/" + envelope.GuaranteedCount + " of class " + envelope.RawMessageClass + " from " + hop.Peer + " was never agreed on - ignored");
                return;
            }
            bool complete = reliability.Reassemble(sender, Local, envelope.GuaranteedId, envelope.GuaranteedIndex, envelope.GuaranteedCount, payload, out bool accepted, out byte[] message);
            if (!accepted)
            {
                return;
            }
            SendGuaranteedDone(from, hop, envelope.GuaranteedId, envelope.GuaranteedIndex, envelope.IsForwarded ? sender : null, Local);
            if (!complete || reliability.IsDuplicate(sender, Local, envelope.GuaranteedId))
            {
                return;
            }
            host.Log(NetLogLevel.Network, "JFP2: class " + envelope.RawMessageClass + " of " + message.Length + " bytes complete from " + sender + " (" + envelope.GuaranteedCount + " segments)");
            Decode(MetaOf(sender, from, envelope), envelope.RawMessageClass, version, message);
        }

        /// <summary>
        /// The message a datagram carries: its own payload. False: drop it, without acknowledging it - a
        /// segment of several, which a relay does not translate (it passes the bytes on, or drops them) and
        /// <see cref="ReceiveSegment"/> alone puts together.
        /// </summary>
        bool TryComplete(NodeId origin, NodeId target, in Envelope envelope, ReadOnlySpan<byte> payload, out ReadOnlySpan<byte> message)
        {
            if (envelope.IsGuaranteed && envelope.GuaranteedCount > 1)
            {
                host.Log(NetLogLevel.Network, "JFP2: dropped segment " + envelope.GuaranteedIndex + "/" + envelope.GuaranteedCount + " of guaranteed id " + envelope.GuaranteedId + " from " + origin + " for " + target + " (a relay does not translate segments)");
                message = default;
                return false;
            }
            message = payload;
            return true;
        }

        /// <summary>A class agreed on (a version above 0) is one of the profile's: decode it and hand it to the core.</summary>
        void Decode(in MessageMeta meta, byte messageClass, byte version, ReadOnlySpan<byte> payload) =>
            profile.ForClass(messageClass).Deliver(host, meta, version, payload);
    }
}
