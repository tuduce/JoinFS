using System;
using System.Collections.Generic;
using System.Net;
using JoinFS.Net.Jfp2;

namespace JoinFS.Net
{
    // Membership learned over a protocol other than legacy (IMembership): the joiner and the admitting
    // node of a JFP2 join, and the members the admitting node lists. Every one of them is a Peer like
    // any other; what differs is that no legacy datagram goes to it (Peer.LegacyLink).
    public sealed partial class MeshManager
    {
        bool IMembership.Admitting => Connected && allowJoin;

        uint IMembership.Suid => suid;

        /// <summary>How long an address that did not take a join over JFP2 is joined over legacy at once (protocol.md §5.12).</summary>
        const double LegacyOnlyMemory = 3600;

        /// <summary>Addresses that took no join over JFP2, and until when they are joined over legacy straight away.</summary>
        readonly Dictionary<IPEndPoint, double> legacyOnly = [];

        /// <summary>
        /// Join the session of the node at <paramref name="endPoint"/> over JFP2 first, and over legacy when the
        /// node does not answer it within a short silence or takes no join over JFP2; an address that did not is
        /// remembered, and joined over legacy at once (protocol.md §5.12). Legacy's Join alone when no protocol
        /// carries membership.
        /// </summary>
        public void JoinJfp2First(IPEndPoint endPoint, uint password)
        {
            if (!core.IsOpen || !core.Identity.Ready)
            {
                return;
            }
            if (legacyOnly.TryGetValue(endPoint, out double until) && Now < until)
            {
                core.Log(NetLogLevel.Network, "NETWORK: " + endPoint + " took no join over JFP2 before - joining it over legacy");
            }
            else if (StartJfp2Join(endPoint, password, legacyFallback: true))
            {
                return;
            }
            Join(endPoint, password);
        }

        /// <summary>
        /// Join the session of the node at <paramref name="endPoint"/> over a protocol that carries
        /// membership (JFP2) and no other: no legacy Join follows a failure. False when none can start the join.
        /// </summary>
        public bool JoinOverJfp2(IPEndPoint endPoint, uint password) => StartJfp2Join(endPoint, password, legacyFallback: false);

        bool StartJfp2Join(IPEndPoint endPoint, uint password, bool legacyFallback)
        {
            if (!core.IsOpen || !core.Identity.Ready)
            {
                return false;
            }
            foreach (IProtocolPlugin plugin in core.Plugins)
            {
                if (plugin is IMembershipExpression expression && expression.BeginJoin(endPoint, password, legacyFallback))
                {
                    passwordHash = password;
                    ActiveJoinResult = JoinResult.Accepted;
                    ActiveLoginResult = LoginResult.Accepted;
                    Creator = false;
                    allowJoin = true;
                    active = true;
                    core.Log(NetLogLevel.Event, "NETWORK: joining " + endPoint + " over " + plugin.Name);
                    return true;
                }
            }
            return false;
        }

        JoinResult IMembership.Judge(Func<uint, bool> passwordMatches)
        {
            if (passwordHash != 0 && !passwordMatches(passwordHash))
            {
                return JoinResult.PasswordRequired;
            }
            return LoginRequired ? JoinResult.LoginRequired : JoinResult.Accepted;
        }

        Peer IMembership.Admit(NodeId joiner, IPEndPoint endPoint, IReadOnlyList<NodeId> otherNames, out bool isNew)
        {
            isNew = false;
            if (!Connected || !joiner.Valid() || joiner == LocalId)
            {
                return null;
            }
            if (core.Peers.TryResolve(joiner, out NodeId known))
            {
                if (!core.Peers.TryGet(known, out Peer existing))
                {
                    return null;
                }
                // it restarted before its expiry, or was learned through another member and has now found us:
                // the cookie proved the endpoint answers, so that is where it is, and the link is JFP2's
                existing.EndPoint = new IPEndPoint(endPoint.Address, endPoint.Port);
                existing.RouteEndPoint = existing.EndPoint;
                existing.RouteVia = default;
                existing.LegacyLink = false;
                existing.SendEstablished = true;
                existing.ReceiveEstablished = true;
                existing.ExpireTime = Now + ExpireTime;
                foreach (NodeId name in otherNames)
                {
                    core.Peers.AddAlias(name, existing.Id);
                }
                core.LinkChanged(existing.Id);
                core.Log(NetLogLevel.Event, "NETWORK: " + joiner + " at " + endPoint + " joined again over JFP2, still a member");
                return existing;
            }
            if (NodeCountDevice(joiner) >= MaxNodesPerDevice)
            {
                core.Log(NetLogLevel.Network, "NETWORK: Exceeded MAX_NODES_PER_DEVICE - " + joiner + " - " + endPoint);
                return null;
            }
            Peer peer = AddJfp2Peer(joiner, endPoint, otherNames, default);
            isNew = true;
            core.Log(NetLogLevel.Event, "NETWORK: " + joiner + " at " + endPoint + " joined over JFP2");
            AnnounceToLegacy(peer);
            return peer;
        }

        void IMembership.JoinFailedOverJfp2(IPEndPoint target, uint passwordHash)
        {
            if (!active)
            {
                return;
            }
            legacyOnly[new IPEndPoint(target.Address, target.Port)] = Now + LegacyOnlyMemory;
            core.Log(NetLogLevel.Event, "NETWORK: " + target + " takes no join over JFP2 - joining it over legacy, and remembering that for " + LegacyOnlyMemory / 3600 + " h");
            core.SendToEndPoint(target, new JoinRequest { PasswordHash = passwordHash }, guaranteed: true);
        }

        bool IMembership.Left(NodeId neighbor, NodeId member)
        {
            if (!Connected || !core.Peers.TryResolve(member, out NodeId id) || !core.Peers.TryGet(id, out Peer peer))
            {
                return false;
            }
            // only the member itself, or the node it is reached through, can say it is gone
            if (id != core.Peers.Resolve(neighbor) && !(peer.Relayed && peer.RouteVia == core.Peers.Resolve(neighbor)))
            {
                core.Log(NetLogLevel.Network, "NETWORK: " + neighbor + " says " + member + " left, which is none of its business - ignored");
                return false;
            }
            removeList.Add(id);
            removeRelayList.Add(id);
            core.Log(NetLogLevel.Event, "NETWORK: " + id + " left the session (told by " + neighbor + ")");
            return true;
        }

        Peer IMembership.Joined(uint sessionId, NodeId admitter, IPEndPoint endPoint, IReadOnlyList<NodeId> otherNames)
        {
            if (!active || Creator || !admitter.Valid() || (Connected && sessionId != suid))
            {
                return null;
            }
            if (!Connected)
            {
                suid = sessionId;
                core.RaiseSessionJoined();
            }
            if (core.Peers.TryResolve(admitter, out NodeId known) && core.Peers.TryGet(known, out Peer existing))
            {
                return existing;
            }
            Peer peer = AddJfp2Peer(admitter, endPoint, otherNames, default);
            core.Log(NetLogLevel.Event, "NETWORK: joined session " + sessionId + " through " + admitter + " at " + endPoint + " over JFP2");
            return peer;
        }

        void IMembership.JoinRefused(JoinResult result)
        {
            if (active && !Connected)
            {
                ActiveJoinResult = result;
            }
        }

        void IMembership.LearnMembers(NodeId via, IReadOnlyList<MemberEntry> members)
        {
            if (!Connected || !core.Peers.TryGet(via, out Peer relay))
            {
                return;
            }
            foreach (MemberEntry member in members)
            {
                NodeId id = member.Names[0];
                bool known = false;
                foreach (NodeId name in member.Names)
                {
                    known |= name == LocalId || name == core.Identity.KeyId || core.Peers.TryResolve(name, out _);
                }
                if (known || NodeCountDevice(id) >= MaxNodesPerDevice)
                {
                    continue;
                }
                Peer peer = AddJfp2Peer(id, member.EndPoint, member.Names[1..], via);
                peer.RouteEndPoint = relay.RouteEndPoint;
                core.Log(NetLogLevel.Network, "NETWORK: member " + id + " at " + member.EndPoint + " learned from " + via + ", reached through it");
                AnnounceToLegacy(peer);
            }
        }

        /// <summary>
        /// A member with no legacy link is introduced to the members that have one, in legacy's own words (AddNode), so
        /// that a node released before this work knows it exists: its datagrams for the member go to this node, which
        /// carries them on (protocol.md §8.4).
        /// </summary>
        void AnnounceToLegacy(Peer member)
        {
            foreach (Peer other in core.Peers.All)
            {
                if (other != member && other.LegacyLink)
                {
                    core.Broadcast(new AddNode { Suid = suid, Node = new KnownNode { Node = member.Id, Port = (ushort)member.EndPoint.Port } }, guaranteed: true);
                    core.Log(NetLogLevel.Event, "NETWORK: " + member.Id + " announced to the legacy members");
                    return;
                }
            }
        }

        /// <summary>
        /// A member with no legacy link has left: the members that have one are told with a legacy Leave in its name, since the
        /// member cannot say it to them (they know it by AddNode only). Before the peer is removed, so the name is still known.
        /// </summary>
        void TellLegacyLeft(Peer gone)
        {
            List<NodeId> legacy = null;
            foreach (Peer other in core.Peers.All)
            {
                if (other != gone && other.LegacyLink)
                {
                    (legacy ??= []).Add(other.Id);
                }
            }
            if (legacy != null)
            {
                core.Send(new MessageMeta { Sender = gone.Id }, new Leave { Suid = suid }, legacy.ToArray());
                core.Log(NetLogLevel.Network, "NETWORK: " + gone.Id + " is gone: told " + legacy.Count + " legacy member(s)");
            }
        }

        /// <summary>
        /// A legacy member's pulse for a member this node is the only way to: the member has no legacy link and cannot answer
        /// it, so this node answers in its name (it is a verified neighbour of the member, which is why the member is alive). The
        /// pulse is then not carried on. False when the pulse is not that.
        /// </summary>
        internal bool ProxyPulse(in MessageMeta meta, in Pulse pulse)
        {
            if (!Connected || pulse.Suid != suid || meta.Forwarded
                || !core.Peers.TryGet(meta.Recipient, out Peer target) || target.LegacyLink || !target.SendEstablished || !target.RouteIsOwnEndPoint
                || !core.Peers.TryGet(meta.Sender, out Peer asker) || !asker.LegacyLink)
            {
                return false;
            }
            core.Send(new MessageMeta { Sender = target.Id }, new PulseResponse { Time = pulse.Time }, new ReadOnlySpan<NodeId>(in asker.Id));
            core.Log(NetLogLevel.Network, "NETWORK: Pulse of " + asker.Id + " answered for " + target.Id);
            return true;
        }

        List<NodeId> IMembership.Reaches(NodeId asker, IReadOnlyList<NodeId> asked)
        {
            List<NodeId> reachable = [];
            if (!Connected)
            {
                return reachable;
            }
            NodeId from = core.Peers.Resolve(asker);
            foreach (NodeId name in asked)
            {
                if (core.Peers.TryGet(core.Peers.Resolve(name), out Peer peer) && peer.Id != from && peer.SendEstablished && peer.RouteIsOwnEndPoint
                    && relayNodes.Count + reachable.Count < RelayBudget)
                {
                    reachable.Add(name);
                }
            }
            return reachable;
        }

        void IMembership.RouteThrough(NodeId via, NodeId member)
        {
            if (!Connected || !core.Peers.TryGet(via, out Peer relay) || !relay.RouteIsOwnEndPoint
                || !core.Peers.TryGet(member, out Peer peer) || peer == relay)
            {
                return;
            }
            bool established = peer.SendEstablished;
            peer.RouteEndPoint = relay.RouteEndPoint;
            peer.RouteVia = relay.Id;
            peer.SendEstablished = true;
            peer.ExpireTime = Now + ExpireTime;
            peer.InvalidateRoutes();
            core.LinkChanged(peer.Id);
            if (!established)
            {
                core.RaisePeerEstablished(peer.Id);
            }
            core.Log(NetLogLevel.Event, "NETWORK: " + peer.Id + " is reached through " + relay.Id + " (told by it)");
        }

        void IMembership.Alive(NodeId neighbor)
        {
            double expire = Now + ExpireTime;
            foreach (Peer peer in core.Peers.All)
            {
                if (!peer.LegacyLink && (peer.Id == neighbor || peer.RouteVia == neighbor))
                {
                    peer.ExpireTime = expire;
                }
            }
        }

        /// <summary>A member that has no legacy link: the peer is there, answering, and the app is told.</summary>
        Peer AddJfp2Peer(NodeId id, IPEndPoint endPoint, IReadOnlyList<NodeId> otherNames, NodeId via)
        {
            Peer peer = core.Peers.Add(id, endPoint, true);
            peer.LegacyLink = false;
            peer.SendEstablished = true;
            peer.RouteVia = via;
            peer.ExpireTime = Now + ExpireTime;
            foreach (NodeId name in otherNames)
            {
                core.Peers.AddAlias(name, id);
            }
            core.LinkChanged(id);
            core.RaisePeerJoined(id, endPoint);
            core.RaisePeerEstablished(id);
            return peer;
        }
    }
}
