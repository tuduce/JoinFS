using System;
using System.Collections.Generic;
using System.Net;

namespace JoinFS.Net.Jfp2
{
    // Sessions with neighbors: the Hello/HelloAck handshake, the keepalive, and the
    // occupants seen answering at an endpoint (docs/reference/jfp2-protocol.md §5.2, §5.7).
    public sealed partial class Jfp2Plugin
    {
        // ================================================================== sessions

        PeerSession CreateSession(NodeId peer)
        {
            ushort id;
            do
            {
                // random, not sequential: every node counts from 1, so a datagram meant for another
                // node would otherwise match a session here by coincidence
                id = (ushort)random.Next(1, 65536);
            }
            while (sessionsById.ContainsKey(id));
            var session = new PeerSession { Peer = peer, LocalAssignedId = id };
            sessions[peer] = session;
            sessionsById[id] = session;
            return session;
        }

        void RemoveSession(NodeId peer)
        {
            if (sessions.Remove(peer, out PeerSession session))
            {
                sessionsById.Remove(session.LocalAssignedId);
            }
        }

        static IPEndPoint Copy(IPEndPoint endPoint) => new(endPoint.Address, endPoint.Port);

        /// <summary>For tests and diagnostics: the ids of the session with a neighbor.</summary>
        public bool TryGetHopIds(NodeId neighbor, out ushort local, out ushort remote)
        {
            if (sessions.TryGetValue(neighbor, out PeerSession session))
            {
                local = session.LocalAssignedId;
                remote = session.RemoteAssignedId;
                return session.HandshakeComplete;
            }
            local = remote = 0;
            return false;
        }

        /// <summary>For tests and diagnostics: the build a neighbor said it runs in its last handshake message, or null.</summary>
        public string BuildOf(NodeId neighbor) => sessions.TryGetValue(neighbor, out PeerSession session) ? session.Build : null;

        // ================================================================== handshake and keepalive

        void DoHandshake()
        {
            double now = Now;
            foreach (Peer peer in host.Peers.All)
            {
                sessions.TryGetValue(peer.Id, out PeerSession session);
                if (session != null && session.Verified)
                {
                    KeepAlive(peer, session, now);
                    continue;
                }
                // negotiation is with a neighbor: a peer behind a relay is served by the relay's session,
                // and a peer sharing an endpoint with a node that answers there is behind that node
                if (peer.Relayed || IsBehindOccupant(peer, now))
                {
                    continue;
                }
                // one Hello at a time per endpoint: each Hello tells the node that answers which id to
                // address us by, so two in flight to one endpoint would leave it holding the wrong one
                if (now >= (session?.NextHelloAttempt ?? 0) && EndPointBusy(peer))
                {
                    continue;
                }
                session ??= CreateSession(peer.Id);
                if (session.AssumedLegacy && now < session.RetryAt)
                {
                    continue;
                }
                if (now < session.NextHelloAttempt)
                {
                    continue;
                }
                if (session.HelloAttempts >= HelloMaxAttempts)
                {
                    bool first = !session.AssumedLegacy;
                    session.AssumedLegacy = true;
                    session.RetryAt = now + HelloCooldown;
                    session.HelloAttempts = 0;
                    host.Log(NetLogLevel.Network, "JFP2: " + peer.Id + " did not answer Hello after " + HelloMaxAttempts + " attempts - assuming legacy-only peer" + (first ? "" : " (again)"));
                    continue;
                }
                session.HelloAttempts++;
                session.NextHelloAttempt = now + HelloRetryInterval;
                session.ProbeEndPoint = Copy(peer.RouteEndPoint);
                SendHello(session.ProbeEndPoint, session);
                host.Log(NetLogLevel.Network, "JFP2: Send Hello to " + peer.Id + " at " + session.ProbeEndPoint + " (attempt " + session.HelloAttempts + ")");
            }
        }

        /// <summary>Another peer reached at the same endpoint has a Hello outstanding there.</summary>
        bool EndPointBusy(Peer peer)
        {
            foreach (Peer other in host.Peers.All)
            {
                if (other != peer && other.RouteEndPoint.Equals(peer.RouteEndPoint)
                    && sessions.TryGetValue(other.Id, out PeerSession session)
                    && !session.Verified && !session.AssumedLegacy && session.ProbeEndPoint != null && session.HelloAttempts > 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>A verified session must keep being answered, by the same node, at the same endpoint.</summary>
        void KeepAlive(Peer peer, PeerSession session, double now)
        {
            if (!session.Endpoint.Equals(peer.RouteEndPoint))
            {
                Demote(session, "its route moved to " + peer.RouteEndPoint);
            }
            else if (now > session.LastAck + SessionTimeout)
            {
                Demote(session, "no answer for " + SessionTimeout + " s");
            }
            else if (now >= session.NextKeepAlive)
            {
                session.NextKeepAlive = now + KeepAliveInterval;
                session.ProbeEndPoint = session.Endpoint;
                SendHello(session.Endpoint, session);
            }
        }

        /// <summary>The session no longer proves that our datagrams reach the peer: send it through legacy until verified again.</summary>
        void Demote(PeerSession session, string reason)
        {
            host.Log(NetLogLevel.Event, "JFP2: session with " + session.Peer + " is no longer verified: " + reason);
            if (occupants.TryGetValue(session.Endpoint, out Occupant occupant) && occupant.Node == session.Peer)
            {
                occupants.Remove(session.Endpoint);
            }
            session.Endpoint = null;
            session.ProbeEndPoint = null;
            session.HelloAttempts = 0;
            session.NextHelloAttempt = 0;
            host.LinkChanged(session.Peer);
        }

        /// <summary>Another node answers at this peer's endpoint, so the peer is not there.</summary>
        bool IsBehindOccupant(Peer peer, double now)
        {
            IPEndPoint route = peer.RouteEndPoint;
            if (!occupants.TryGetValue(route, out Occupant occupant))
            {
                return false;
            }
            if (now > occupant.Expire)
            {
                occupants.Remove(route);
                return false;
            }
            return occupant.Node != peer.Id;
        }

        HandshakeMessage MakeHandshake(PeerSession session, byte result) => new()
        {
            ProtoMajorMin = Envelope.ProtoMajor,
            ProtoMajorMax = Envelope.ProtoMajor,
            Capabilities = LocalCapabilities,
            SelfAssignedId = session.LocalAssignedId,
            Result = result,
            Node = ToRelay(Local),
            Build = build,
            Offers = new List<SchemaOffer>(profile.Offers),
        };

        // Internal and nothing else: no flag a peer could need a capability to read
        // (docs/reference/jfp2-protocol.md §4.2); the envelope goes out as Envelope.HandshakeProtoMajor
        void SendHello(IPEndPoint endPoint, PeerSession session) =>
            SendDatagram(endPoint, EnvelopeFlags.Internal, MessageClasses.Hello, session.LocalAssignedId, session.RemoteAssignedId, MakeHandshake(session, 0).Serialize());

        void SendHelloAck(IPEndPoint endPoint, PeerSession session, byte result) =>
            SendDatagram(endPoint, EnvelopeFlags.Internal, MessageClasses.HelloAck, session.LocalAssignedId, session.RemoteAssignedId, MakeHandshake(session, result).Serialize());

        /// <summary>
        /// Remember the build a neighbor says it runs (none: it restarted with a build that does not
        /// say), and log it: the first one at event level, later changes at network level only, so
        /// Hellos that keep changing it (buggy or forged) cannot flood the monitor.
        /// </summary>
        void LearnBuild(PeerSession session, string peerBuild)
        {
            if (peerBuild != null && peerBuild != session.Build)
            {
                host.Log(session.BuildLearned ? NetLogLevel.Network : NetLogLevel.Event, "JFP2: " + session.Peer + " runs build " + peerBuild);
                session.BuildLearned = true;
            }
            session.Build = peerBuild;
        }

        void HandleHello(IPEndPoint from, ReadOnlySpan<byte> payload)
        {
            HandshakeMessage hello = HandshakeMessage.Deserialize(payload);
            if (!hello.Node.HasValue)
            {
                host.Log(NetLogLevel.Network, "JFP2: Hello from " + from + " does not say who it is - ignored (legacy-only peer)");
                return;
            }
            NodeId sender = ToNodeId(hello.Node.Value);
            // only nodes the mesh already knows (the legacy Join always happens first)
            if (!host.Peers.Contains(sender))
            {
                host.Log(NetLogLevel.Network, "JFP2: Hello from unknown node " + sender + " at " + from + " - ignored");
                return;
            }
            if (!sessions.TryGetValue(sender, out PeerSession session))
            {
                session = CreateSession(sender);
            }
            session.RemoteAssignedId = hello.SelfAssignedId;
            LearnBuild(session, hello.Build);
            // a range that includes ours (2..3 from a later build, say) is fine: we speak the common one
            if (hello.ProtoMajorMin > Envelope.ProtoMajor || hello.ProtoMajorMax < Envelope.ProtoMajor)
            {
                SendHelloAck(from, session, result: 1);
                return;
            }
            if (Negotiator.Resolve(session, LocalCapabilities, profile.Offers, hello.Capabilities, hello.Offers) && session.HandshakeComplete)
            {
                host.LinkChanged(sender); // it restarted with different offers: cached routes are stale
            }
            // we can decode what it sends; whether ours reaches it is for our own Hello to prove
            session.HandshakeComplete = true;
            if (session.AssumedLegacy)
            {
                session.RetryAt = 0; // it speaks JFP2 after all: try it now
            }
            SendHelloAck(from, session, result: 0);
            host.Log(NetLogLevel.Network, "JFP2: Hello from " + sender + " at " + from + " - answered");
        }

        void HandleHelloAck(in Envelope envelope, ReadOnlySpan<byte> payload)
        {
            HandshakeMessage ack = HandshakeMessage.Deserialize(payload);
            // addressed to the id we gave the peer the Hello was aimed at
            if (!sessionsById.TryGetValue(envelope.RecipientPeerId, out PeerSession session) || session.ProbeEndPoint == null)
            {
                return;
            }
            double now = Now;
            if (ack.Result != 0 || !ack.Node.HasValue)
            {
                session.AssumedLegacy = true;
                session.RetryAt = now + HelloCooldown;
                session.HelloAttempts = 0;
                host.Log(NetLogLevel.Network, "JFP2: " + session.Peer + " refused or cannot identify itself - assuming legacy-only peer");
                return;
            }
            NodeId responder = ToNodeId(ack.Node.Value);
            if (!host.Peers.Contains(responder))
            {
                host.Log(NetLogLevel.Network, "JFP2: HelloAck from unknown node " + responder + " - ignored");
                return;
            }
            IPEndPoint probe = session.ProbeEndPoint;
            occupants[probe] = new Occupant(responder, now + OccupantTtl);
            if (responder != session.Peer)
            {
                // the node at that endpoint is not the one we asked for: two nodes share the endpoint
                // (or the network now steers it elsewhere). The datagrams for our peer go through that
                // node, which negotiates for itself (see NextHop).
                host.Log(NetLogLevel.Network, "JFP2: " + session.Peer + " is not the node answering at " + probe + " - " + responder + " is");
                if (session.Verified)
                {
                    Demote(session, "its endpoint " + probe + " is now answered by " + responder);
                }
                session.ProbeEndPoint = null;
                session.HelloAttempts = 0;
                if (sessions.TryGetValue(responder, out PeerSession occupantSession))
                {
                    occupantSession.NextHelloAttempt = 0;
                    occupantSession.NextKeepAlive = 0;
                    occupantSession.RetryAt = 0;
                }
                host.LinkChanged(session.Peer);
                return;
            }
            session.RemoteAssignedId = ack.SelfAssignedId;
            LearnBuild(session, ack.Build);
            bool agreementChanged = Negotiator.Resolve(session, LocalCapabilities, profile.Offers, ack.Capabilities, ack.Offers);
            bool wasVerified = session.Verified;
            session.HandshakeComplete = true;
            session.Endpoint = probe;
            session.LastAck = now;
            session.NextKeepAlive = now + KeepAliveInterval;
            session.HelloAttempts = 0;
            session.AssumedLegacy = false;
            if (!wasVerified)
            {
                host.LinkChanged(session.Peer);
                host.Log(NetLogLevel.Network, "JFP2: HelloAck from " + session.Peer + " at " + probe + " - verified");
            }
            else if (agreementChanged)
            {
                host.LinkChanged(session.Peer);
            }
        }
    }
}
