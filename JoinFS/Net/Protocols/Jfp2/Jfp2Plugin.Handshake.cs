using System;
using System.Collections.Generic;
using System.Net;

namespace JoinFS.Net.Jfp2
{
    // Sessions with neighbors: the Hello/HelloAck handshake, the keepalive, and the
    // occupants seen answering at an endpoint (docs/jfp2/protocol.md §6, §9).
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

        static IPEndPoint Copy(IPEndPoint endPoint) => WireEndPoint.Normalize(new IPEndPoint(endPoint.Address, endPoint.Port));

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

        /// <summary>For tests and diagnostics: the capabilities agreed with a neighbor (both sides advertise them), 0 when none or no session.</summary>
        public ulong AgreedCapabilitiesWith(NodeId neighbor) => sessions.TryGetValue(neighbor, out PeerSession session) ? session.AgreedCapabilities : 0;

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
                if ((session.AssumedLegacy || session.Refused != HandshakeMessage.ResultAccepted) && now < session.RetryAt)
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
                    session.Refused = HandshakeMessage.ResultAccepted;
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
            Capabilities = profile.Capabilities,
            SelfAssignedId = session.LocalAssignedId,
            Result = result,
            Names = [NameOf(Local)],
            Build = build,
            Offers = new List<SchemaOffer>(profile.Offers),
        };

        // Internal and nothing else: no flag a peer could need a capability to read
        // (docs/jfp2/protocol.md §3.2); the envelope goes out as Envelope.HandshakeProtoMajor
        void SendHello(IPEndPoint endPoint, PeerSession session) =>
            SendDatagram(endPoint, EnvelopeFlags.Internal, MessageClasses.Hello, session.LocalAssignedId, session.RemoteAssignedId, MakeHandshake(session, 0).Serialize());

        /// <summary>The answer to a Hello from <paramref name="endPoint"/>, its source as we received it, which every HelloAck tells the asker (§5.5).</summary>
        void SendHelloAck(IPEndPoint endPoint, PeerSession session, byte result)
        {
            HandshakeMessage ack = MakeHandshake(session, result);
            ack.ObservedEndPoint = endPoint;
            SendDatagram(endPoint, EnvelopeFlags.Internal, MessageClasses.HelloAck, session.LocalAssignedId, session.RemoteAssignedId, ack.Serialize());
        }

        /// <summary>
        /// A neighbor that answered our Hello says where it saw it come from. Counted only from a
        /// HelloAck that answers our own Hello: addressed to the session's id, from the endpoint the
        /// Hello went to, naming the node asked for or the one found answering at its endpoint (the
        /// callers check). The session id alone is 16 bits, constant for the session and guessable
        /// off the path, so an answer from elsewhere could plant any address. Only a change is
        /// reported, to the core and on to the app, which classifies and logs it and nothing else.
        /// </summary>
        void Observe(NodeId reporter, IPEndPoint observed)
        {
            if (observed == null || (observedBy.TryGetValue(reporter, out IPEndPoint last) && last.Equals(observed)))
            {
                return;
            }
            observedBy[reporter] = observed;
            host.Log(NetLogLevel.Network, "JFP2: " + reporter + " sees this node at " + observed);
            host.EndPointObserved(reporter, observed);
        }

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

        /// <summary>
        /// Which node a handshake message comes from, by the names it lists: the first kind-0 name (the
        /// only kind this build resolves) that is a known mesh peer, else the first kind-0 name (for the
        /// "unknown node" log). False when it lists none, so the speaker cannot be placed.
        /// </summary>
        bool TryGetSpeaker(List<NodeName> names, out NodeId speaker)
        {
            speaker = default;
            bool found = false;
            foreach (NodeName name in names)
            {
                if (!TryResolve(name, out NodeId node))
                {
                    continue;
                }
                if (host.Peers.Contains(node))
                {
                    speaker = node;
                    return true;
                }
                if (!found)
                {
                    speaker = node;
                    found = true;
                }
            }
            return found;
        }

        void HandleHello(IPEndPoint from, ReadOnlySpan<byte> payload)
        {
            HandshakeMessage hello = HandshakeMessage.Deserialize(payload);
            if (!TryGetSpeaker(hello.Names, out NodeId sender))
            {
                host.Log(NetLogLevel.Network, "JFP2: Hello from " + from + " does not say who it is - ignored (legacy-only peer)");
                return;
            }
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
                SendHelloAck(from, session, HandshakeMessage.ResultNoCompatibleProtoMajor);
                return;
            }
            if (Negotiator.Resolve(session, profile.Capabilities, profile.Offers, hello.Capabilities, hello.Offers) && session.HandshakeComplete)
            {
                host.LinkChanged(sender); // it restarted with different offers: cached routes are stale
            }
            // we can decode what it sends; whether ours reaches it is for our own Hello to prove
            session.HandshakeComplete = true;
            if (session.AssumedLegacy)
            {
                session.RetryAt = 0; // it speaks JFP2 after all: try it now
            }
            SendHelloAck(from, session, HandshakeMessage.ResultAccepted);
            host.Log(NetLogLevel.Network, "JFP2: Hello from " + sender + " at " + from + " - answered");
        }

        void HandleHelloAck(IPEndPoint from, in Envelope envelope, ReadOnlySpan<byte> payload)
        {
            HandshakeMessage ack = HandshakeMessage.Deserialize(payload);
            // addressed to the id we gave the peer the Hello was aimed at
            if (!sessionsById.TryGetValue(envelope.RecipientPeerId, out PeerSession session) || session.ProbeEndPoint == null)
            {
                return;
            }
            double now = Now;
            if (ack.Result != HandshakeMessage.ResultAccepted)
            {
                // a refusal takes a working link off JFP2 for the cooldown, so it counts only from the
                // endpoint our Hello went to and from the peer asked: the session id alone is 16 bits,
                // and guessable off the path
                if (!from.Equals(session.ProbeEndPoint) || !TryGetSpeaker(ack.Names, out NodeId refuser) || refuser != session.Peer)
                {
                    host.Log(NetLogLevel.Network, "JFP2: HelloAck with Result " + ack.Result + " from " + from + " is not the answer of " + session.Peer + " at " + session.ProbeEndPoint + " - ignored");
                    return;
                }
                Refused(session, ack, now);
                return;
            }
            if (!TryGetSpeaker(ack.Names, out NodeId responder))
            {
                session.AssumedLegacy = true;
                session.RetryAt = now + HelloCooldown;
                session.HelloAttempts = 0;
                host.Log(NetLogLevel.Network, "JFP2: " + session.Peer + " cannot identify itself - assuming legacy-only peer");
                return;
            }
            if (!host.Peers.Contains(responder))
            {
                host.Log(NetLogLevel.Network, "JFP2: HelloAck from unknown node " + responder + " - ignored");
                return;
            }
            IPEndPoint probe = session.ProbeEndPoint;
            occupants[probe] = new Occupant(responder, now + OccupantTtl);
            if (from.Equals(probe))
            {
                Observe(responder, ack.ObservedEndPoint);
            }
            else if (ack.ObservedEndPoint != null)
            {
                host.Log(NetLogLevel.Network, "JFP2: HelloAck from " + from + " is not from " + probe + ", where the Hello went - its observed endpoint is ignored");
            }
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
            bool agreementChanged = Negotiator.Resolve(session, profile.Capabilities, profile.Offers, ack.Capabilities, ack.Offers);
            bool wasVerified = session.Verified;
            session.HandshakeComplete = true;
            session.Endpoint = probe;
            session.LastAck = now;
            session.NextKeepAlive = now + KeepAliveInterval;
            session.HelloAttempts = 0;
            session.AssumedLegacy = false;
            session.Refused = HandshakeMessage.ResultAccepted;
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

        /// <summary>
        /// A HelloAck with a non-zero Result: no session now, whatever the value (§5.2). A later build
        /// may refuse for a reason this one does not know (not admitted, too many sessions), which says
        /// nothing about whether it speaks JFP2, so the peer is not taken for legacy-only: its traffic
        /// goes through legacy meanwhile, as for any peer without a session, and it is asked again
        /// after the cooldown. The answer's extensions are still read (the build, the observed endpoint). Only a refusal from
        /// the endpoint our Hello went to, naming the peer asked, gets here (<see cref="HandleHelloAck"/>).
        /// </summary>
        void Refused(PeerSession session, HandshakeMessage ack, double now)
        {
            if (session.Verified)
            {
                Demote(session, "it refused a session (Result " + ack.Result + ")");
            }
            session.AssumedLegacy = false;
            session.Refused = ack.Result;
            session.RetryAt = now + HelloCooldown;
            session.HelloAttempts = 0;
            LearnBuild(session, ack.Build);
            Observe(session.Peer, ack.ObservedEndPoint);
            host.Log(NetLogLevel.Network, "JFP2: " + session.Peer + " answered Result " + ack.Result + " (no session now) - asking again in " + HelloCooldown + " s");
        }
    }
}
