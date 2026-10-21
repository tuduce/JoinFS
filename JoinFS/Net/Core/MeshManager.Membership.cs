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
            }
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
