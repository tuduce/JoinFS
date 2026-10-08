using System;
using System.Buffers.Binary;
using System.Net;

namespace JoinFS.Net.Jfp2
{
    // Next hop: which neighbor carries a peer's traffic; and the relay of Forwarded envelopes
    // for another node (docs/jfp2/protocol.md §3.5, §9.3, §8).
    public sealed partial class Jfp2Plugin
    {
        // ================================================================== next hop

        /// <summary>The session can carry datagrams to <paramref name="route"/>: its peer answered there, and the route has not moved.</summary>
        static bool IsUsable(PeerSession session, IPEndPoint route) => session.Verified && session.HandshakeComplete && session.Endpoint.Equals(route);

        /// <summary>
        /// The session through which datagrams for <paramref name="target"/> go, or null when JFP2 has
        /// no way to reach it (then the legacy plugin carries everything for it).
        /// </summary>
        PeerSession NextHop(NodeId target, out Peer targetPeer)
        {
            if (!host.Peers.TryGet(target, out targetPeer))
            {
                return null;
            }
            IPEndPoint route = targetPeer.RouteEndPoint;
            // the peer itself answered at the endpoint we currently reach it on
            if (sessions.TryGetValue(target, out PeerSession session) && IsUsable(session, route))
            {
                return session;
            }
            // the relay the mesh routes it through
            if (targetPeer.Relayed && host.Peers.TryGet(targetPeer.RouteVia, out Peer via)
                && sessions.TryGetValue(via.Id, out session) && IsUsable(session, via.RouteEndPoint))
            {
                return session;
            }
            // some other node answered at this very endpoint: the target shares it and is behind that node
            foreach (PeerSession other in sessions.Values)
            {
                if (other.Peer != target && IsUsable(other, route))
                {
                    return other;
                }
            }
            return null;
        }

        byte AgreedVersion(NodeId target, ClassDescriptor messageClass, out Peer peer, out PeerSession hop)
        {
            peer = null;
            hop = null;
            if (messageClass == null)
            {
                return 0;
            }
            hop = NextHop(target, out peer);
            return hop == null ? (byte)0 : hop.AgreedAppVersion[messageClass.MessageClass];
        }

        public bool CanCarry(NodeId peer, MessageKind kind) => peer.Valid() && AgreedVersion(peer, profile.ForKind(kind), out _, out _) > 0;

        public PeerLinkState? DescribeLink(Peer peer)
        {
            if (NextHop(peer.Id, out _) != null) return PeerLinkState.Negotiated;
            if (sessions.TryGetValue(peer.Id, out PeerSession session) && session.AssumedLegacy) return PeerLinkState.Legacy;
            if (peer.Relayed && sessions.TryGetValue(peer.RouteVia, out session) && session.AssumedLegacy) return PeerLinkState.Legacy;
            return PeerLinkState.Negotiating;
        }

        /// <summary>For tests and diagnostics: some hop carries JFP2 to the peer.</summary>
        public bool IsNegotiated(NodeId peer) => NextHop(peer, out _) != null;

        /// <summary>For tests and diagnostics: the neighbor JFP2 datagrams for the peer go to (invalid when JFP2 cannot reach it).</summary>
        public NodeId NextHopNode(NodeId peer) => NextHop(peer, out _)?.Peer ?? default;

        /// <summary>For tests and diagnostics: the schema version agreed for a kind with the peer's next hop (0: JFP2 does not carry it there).</summary>
        public byte VersionFor(NodeId peer, MessageKind kind) => AgreedVersion(peer, profile.ForKind(kind), out _, out _);

        // ================================================================== relay

        /// <summary>
        /// A Forwarded envelope for another node. Only a direct neighbor of ours can be relayed to. If
        /// that neighbor agreed the same schema as the sender's hop used (or the datagram is internal,
        /// e.g. a relayed ack), pass the datagram on with the two hop ids rewritten. Otherwise - a
        /// legacy-only target, or a different schema version - decode it and let the core re-send it in
        /// the target's terms, the one place translation happens since every node speaks legacy. An
        /// Identity passed on is decoded for the core too, since a translated position needs it. A
        /// datagram whose origin or target this build cannot resolve (a name of any kind but 0) is
        /// dropped, and not acknowledged.
        /// </summary>
        void Relay(IPEndPoint from, PeerSession hop, in Envelope envelope, ReadOnlySpan<byte> datagram, ReadOnlySpan<byte> payload)
        {
            if (!TryResolve(envelope.Target, out NodeId target) || !TryResolve(envelope.Origin, out NodeId origin))
            {
                host.Log(NetLogLevel.Network, "JFP2: relay from " + envelope.Origin + " to " + envelope.Target + " names a node this build cannot resolve - dropped");
                return;
            }
            if (envelope.IsInternal && envelope.RawMessageClass == MessageClasses.GuaranteedDone && TryReadGuaranteedDone(payload, out ushort id, out byte index)
                && reliability.Acknowledge(acker: origin, ackedOrigin: target, id, index))
            {
                return; // the ack of a message this node re-sent on the origin's behalf ends here
            }
            if (!host.Peers.TryGet(target, out Peer targetPeer) || !targetPeer.RouteIsOwnEndPoint)
            {
                host.Log(NetLogLevel.Network, "JFP2: relay target " + target + " is not a direct neighbor - dropped");
                return;
            }
            if (!host.TryAcquireRelay(origin))
            {
                return; // TryAcquireRelay logs the refusal
            }
            byte messageClass = envelope.RawMessageClass;
            byte version = envelope.IsInternal ? (byte)0 : hop.AgreedAppVersion[messageClass];
            if (sessions.TryGetValue(target, out PeerSession targetSession) && IsUsable(targetSession, targetPeer.RouteEndPoint)
                && (envelope.IsInternal || (version > 0 && targetSession.AgreedAppVersion[messageClass] == version)))
            {
                datagram.CopyTo(datagramBuffer);
                BinaryPrimitives.WriteUInt16LittleEndian(datagramBuffer.AsSpan(3, 2), targetSession.LocalAssignedId);
                BinaryPrimitives.WriteUInt16LittleEndian(datagramBuffer.AsSpan(5, 2), targetSession.RemoteAssignedId);
                host.Transport.Send(targetSession.Endpoint, datagramBuffer.AsSpan(0, datagram.Length));
                if (version > 0 && !envelope.IsGuaranteed && profile.ForClass(messageClass)?.Kind == MessageKind.Identity
                    && profile.ForKind<IdentityUpdate>(MessageKind.Identity) is { } identityClass)
                {
                    // the core keeps identity as state for whoever it is for: a position of this object
                    // the target agreed another version of is translated here, and goes out only after
                    // it (EnsureIdentity), which now knows the target has it
                    IdentityUpdate identity = identityClass.Codec(version).Decode(payload);
                    host.Deliver(new MessageMeta { Sender = origin, Recipient = target, Forwarded = true }, identity);
                    identitySent[(origin, identity.ObjectId, target)] = new IdentitySent { Last = identity, Time = Now };
                }
                return;
            }
            if (envelope.IsInternal || version == 0)
            {
                host.Log(NetLogLevel.Network, "JFP2: relay from " + origin + " to " + target + " cannot be carried (class " + messageClass + ") - dropped");
                return;
            }
            if (!TryComplete(origin, target, envelope, payload, out ReadOnlySpan<byte> message))
            {
                return; // a segment this build cannot reassemble: dropped, not acknowledged
            }
            if (envelope.IsGuaranteed)
            {
                // this hop is complete once we have it; the downstream protocol takes over delivery.
                // The ack goes back end to end in the target's name, so the origin clears exactly the
                // copy for this target. A retransmission means our ack was lost: ack again, but it was
                // translated already - for this target: one id may go to several (origin, target, id).
                SendGuaranteedDone(from, hop, envelope.GuaranteedId, envelope.GuaranteedIndex, origin, acker: target);
                if (reliability.IsDuplicate(origin, target, envelope.GuaranteedId))
                {
                    return;
                }
            }
            // a JFP2 re-send keeps the origin's id (OriginGuaranteedId); legacy uses its own, hop by hop
            var meta = new MessageMeta
            {
                Sender = origin, Recipient = target, Guaranteed = envelope.IsGuaranteed, Forwarded = true,
                OriginGuaranteedId = envelope.IsGuaranteed ? envelope.GuaranteedId : (ushort)0,
            };
            Decode(meta, messageClass, version, message);
        }
    }
}
