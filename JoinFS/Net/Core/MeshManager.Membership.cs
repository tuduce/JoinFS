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
        public void JoinJfp2First(IPEndPoint endPoint, uint password) => JoinJfp2First([endPoint], password);

        /// <summary>
        /// <see cref="JoinJfp2First(IPEndPoint, uint)"/> for a node with several addresses (a host name with an A and an AAAA record):
        /// all are tried, IPv6 first, and legacy joins the IPv4 one. An IPv6 address is left out when this node has no IPv6.
        /// </summary>
        public void JoinJfp2First(IReadOnlyList<IPEndPoint> dial, uint password)
        {
            if (!core.IsOpen || !core.Identity.Ready)
            {
                return;
            }
            List<IPEndPoint> usable = UsableTargets(dial);
            if (usable.Count == 0)
            {
                core.Log(NetLogLevel.Event, "NETWORK: cannot join " + (dial.Count > 0 ? dial[0] : "nothing") + ": this node has no address in a family it uses");
                return;
            }
            IPEndPoint legacy = usable.Find(e => e.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (legacy != null && legacyOnly.TryGetValue(legacy, out double until) && Now < until)
            {
                core.Log(NetLogLevel.Network, "NETWORK: " + legacy + " took no join over JFP2 before - joining it over legacy");
            }
            else if (StartJfp2Join(usable, password, legacyFallback: legacy != null))
            {
                return;
            }
            if (legacy != null)
            {
                Join(legacy, password);
            }
        }

        /// <summary>The addresses of <paramref name="dial"/> this node can use, in the order given: an IPv6 one needs an IPv6 address of its own.</summary>
        List<IPEndPoint> UsableTargets(IReadOnlyList<IPEndPoint> dial)
        {
            var usable = new List<IPEndPoint>(dial.Count);
            foreach (IPEndPoint endPoint in dial)
            {
                if ((endPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork || core.Identity.HasIPv6) && !usable.Contains(endPoint))
                {
                    usable.Add(endPoint);
                }
            }
            return usable;
        }

        /// <summary>
        /// Join the session of the node at <paramref name="endPoint"/> over a protocol that carries
        /// membership (JFP2) and no other: no legacy Join follows a failure. False when none can start the join.
        /// </summary>
        public bool JoinOverJfp2(IPEndPoint endPoint, uint password) => StartJfp2Join([endPoint], password, legacyFallback: false);

        /// <summary><see cref="JoinOverJfp2(IPEndPoint, uint)"/> for a node with several addresses (IPv6 ones first): all are tried.</summary>
        public bool JoinOverJfp2(IReadOnlyList<IPEndPoint> endPoints, uint password) => StartJfp2Join(endPoints, password, legacyFallback: false);

        bool StartJfp2Join(IReadOnlyList<IPEndPoint> endPoints, uint password, bool legacyFallback)
        {
            IPEndPoint endPoint = endPoints[0];
            if (!core.IsOpen || !core.Identity.Ready)
            {
                return false;
            }
            foreach (IProtocolPlugin plugin in core.Plugins)
            {
                if (plugin is IMembershipExpression expression && expression.BeginJoin(endPoints, password, legacyFallback))
                {
                    passwordHash = password;
                    ActiveJoinResult = JoinResult.Accepted;
                    ActiveLoginResult = LoginResult.Accepted;
                    Creator = false;
                    allowJoin = true;
                    active = true;
                    core.Log(NetLogLevel.Event, "NETWORK: joining " + string.Join(", ", endPoints) + " over " + plugin.Name);
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

        Peer IMembership.Admit(NodeId joiner, IPEndPoint endPoint, IReadOnlyList<NodeId> otherNames, IReadOnlyList<IPEndPoint> candidates, out bool isNew)
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
                SetCandidates(existing, candidates);
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
            Peer peer = AddJfp2Peer(joiner, endPoint, otherNames, default, candidates);
            isNew = true;
            core.Log(NetLogLevel.Event, "NETWORK: " + joiner + " at " + endPoint + " (" + FamilyName(endPoint) + ") joined over JFP2");
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

        Peer IMembership.Joined(uint sessionId, NodeId admitter, IPEndPoint endPoint, IReadOnlyList<NodeId> otherNames, IReadOnlyList<IPEndPoint> candidates)
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
            Peer peer = AddJfp2Peer(admitter, endPoint, otherNames, default, candidates);
            core.Log(NetLogLevel.Event, "NETWORK: joined session " + sessionId + " through " + admitter + " at " + endPoint + " (" + FamilyName(endPoint) + ") over JFP2");
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
                Peer peer = AddJfp2Peer(id, member.EndPoint, member.Names[1..], via, member.Candidates);
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

        void IMembership.RouteDirect(NodeId member, IPEndPoint endPoint)
        {
            if (!Connected || !core.Peers.TryGet(member, out Peer peer) || !peer.IsOwnEndPoint(endPoint))
            {
                return;
            }
            bool established = peer.SendEstablished;
            NodeId was = peer.RouteVia;
            IPEndPoint before = peer.RouteEndPoint;
            peer.RouteEndPoint = new IPEndPoint(endPoint.Address, endPoint.Port);
            peer.RouteVia = default;
            peer.SendEstablished = true;
            peer.ExpireTime = Now + ExpireTime;
            peer.InvalidateRoutes();
            core.LinkChanged(peer.Id);
            if (!established)
            {
                core.RaisePeerEstablished(peer.Id);
            }
            core.Log(NetLogLevel.Event, "NETWORK: " + peer.Id + " is reached directly at " + endPoint + " (" + FamilyName(endPoint) + ")"
                + (was.Valid() ? ", no longer through " + was : !established ? "" : ", was at " + before + " (" + FamilyName(before) + ")"));
        }

        /// <summary>"IPv6" or "IPv4": the family of the path an endpoint is on.</summary>
        internal static string FamilyName(IPEndPoint endPoint) => endPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";

        void IMembership.Advertised(NodeId member, IReadOnlyList<IPEndPoint> candidates)
        {
            if (core.Peers.TryGet(core.Peers.Resolve(member), out Peer peer))
            {
                SetCandidates(peer, candidates);
            }
        }

        /// <summary>The usable candidates a node advertised replace what was known; a change is logged at event level (what the field test reads).</summary>
        void SetCandidates(Peer peer, IReadOnlyList<IPEndPoint> advertised)
        {
            if (advertised == null)
            {
                return;
            }
            var list = new List<IPEndPoint>(advertised.Count);
            foreach (IPEndPoint candidate in advertised)
            {
                if (Peer.IsUsableCandidate(candidate) && !list.Contains(candidate))
                {
                    list.Add(candidate);
                }
            }
            if (list.Count == peer.Candidates.Count)
            {
                int same = 0;
                while (same < list.Count && list[same].Equals(peer.Candidates[same])) same++;
                if (same == list.Count)
                {
                    return;
                }
            }
            peer.Candidates = list;
            core.Log(NetLogLevel.Event, "NETWORK: " + peer.Id + " can be tried at " + (list.Count == 0 ? "no endpoint" : string.Join(", ", list)));
        }

        void IMembership.RouteLost(NodeId member)
        {
            if (!Connected || !core.Peers.TryGet(member, out Peer peer) || peer.LegacyLink)
            {
                return;
            }
            peer.SendEstablished = false;
            peer.RouteEndPoint = peer.EndPoint;
            peer.RouteVia = default;
            peer.InvalidateRoutes();
            core.LinkChanged(peer.Id);
            core.Log(NetLogLevel.Event, "NETWORK: " + peer.Id + " no longer answers at " + peer.EndPoint + " - asking the neighbours for a route");
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
        Peer AddJfp2Peer(NodeId id, IPEndPoint endPoint, IReadOnlyList<NodeId> otherNames, NodeId via, IReadOnlyList<IPEndPoint> candidates = null)
        {
            Peer peer = core.Peers.Add(id, endPoint, true);
            SetCandidates(peer, candidates);
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
