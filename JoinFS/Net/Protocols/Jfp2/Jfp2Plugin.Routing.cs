using System;
using System.Collections.Generic;

namespace JoinFS.Net.Jfp2
{
    // Finding a relay for a member there is no direct link to: RouteQuery and RouteAnswer (docs/network/jfp2/protocol.md
    // §5.13), the successor of legacy's Pathfinder on JFP2 links. One hop, as there: a neighbour answers for the members it
    // reaches directly itself.
    public sealed partial class Jfp2Plugin
    {
        const double RouteInterval = 5.0;
        /// <summary>A member whose route a neighbour confirmed this recently is not asked about again yet.</summary>
        const double RouteRefresh = 10.0;
        /// <summary>A route nobody confirmed for this long is given up for the one a neighbour answers with.</summary>
        const double RouteStale = 15.0;

        double nextRoute;
        /// <summary>The neighbour that last confirmed it reaches a member, and when.</summary>
        readonly Dictionary<NodeId, (NodeId Via, double Time)> routeConfirmed = [];
        /// <summary>When a member was first asked about: a route it was given is not replaced before it had its chance to be confirmed.</summary>
        readonly Dictionary<NodeId, double> routeAsked = [];
        readonly List<NodeId> routeNames = [];

        /// <summary>
        /// Every few seconds, the members with no direct link to this node, and without a route a neighbour confirmed lately,
        /// are asked about of every neighbour that is a member. Only members with no legacy link: legacy's Pathfinder keeps the others.
        /// </summary>
        void DoRoute()
        {
            double now = Now;
            if (now < nextRoute || !host.Connected)
            {
                return;
            }
            nextRoute = now + RouteInterval;
            routeNames.Clear();
            foreach (Peer peer in host.Peers.All)
            {
                if (peer.LegacyLink || (peer.SendEstablished && !peer.Relayed))
                {
                    continue;
                }
                routeAsked.TryAdd(peer.Id, now);
                if (now - ConfirmedAt(peer) >= RouteRefresh && routeNames.Count < RouteNamesBody.MaxNames)
                {
                    routeNames.Add(peer.Id);
                }
            }
            if (routeNames.Count == 0)
            {
                return;
            }
            int told = SendToMembers(MessageClasses.RouteQuery, new RouteNamesBody { Names = routeNames }.Serialize(), default);
            if (told > 0)
            {
                host.Log(NetLogLevel.Network, "JFP2: RouteQuery for " + routeNames.Count + " member(s) sent to " + told + " neighbour(s)");
            }
            routeNames.Clear();
        }

        /// <summary>When the relay the member has last confirmed it reaches it; 0 when it never did.</summary>
        double ConfirmedAt(Peer peer) =>
            routeConfirmed.TryGetValue(peer.Id, out var confirmed) && confirmed.Via == peer.RouteVia ? confirmed.Time : 0;

        /// <summary>The route the member has is not stale: its relay confirmed it within <see cref="RouteStale"/> seconds, or the member was first asked about that recently.</summary>
        bool RouteHolds(Peer peer, double now) =>
            now - Math.Max(ConfirmedAt(peer), routeAsked.GetValueOrDefault(peer.Id, now)) < RouteStale;

        void HandleRouteQuery(PeerSession hop, ReadOnlySpan<byte> payload)
        {
            if (!MemberOfThisSession(hop, out NodeId neighbor) || !RouteNamesBody.TryRead(payload, out RouteNamesBody query))
            {
                host.Log(NetLogLevel.Network, "JFP2: RouteQuery from " + hop.Peer + " is not from a member or not readable - ignored");
                return;
            }
            List<NodeId> reachable = host.Membership.Reaches(neighbor, query.Names);
            if (reachable.Count > 0)
            {
                SendInternal(hop, MessageClasses.RouteAnswer, new RouteNamesBody { Names = reachable }.Serialize());
            }
            host.Log(NetLogLevel.Network, "JFP2: RouteQuery from " + neighbor + " for " + query.Names.Count + " member(s): " + reachable.Count + " reached directly");
        }

        void HandleRouteAnswer(PeerSession hop, ReadOnlySpan<byte> payload)
        {
            if (!MemberOfThisSession(hop, out NodeId neighbor) || !RouteNamesBody.TryRead(payload, out RouteNamesBody answer))
            {
                host.Log(NetLogLevel.Network, "JFP2: RouteAnswer from " + hop.Peer + " is not from a member or not readable - ignored");
                return;
            }
            double now = Now;
            foreach (NodeId name in answer.Names)
            {
                if (!host.Peers.TryGet(host.Peers.Resolve(name), out Peer peer) || peer.LegacyLink || peer.Id == neighbor)
                {
                    continue;
                }
                if (peer.SendEstablished && !peer.Relayed)
                {
                    continue; // linked to it already
                }
                if (peer.Relayed && peer.RouteVia == neighbor)
                {
                    routeConfirmed[peer.Id] = (neighbor, now); // the relay it has says it still reaches it
                    continue;
                }
                // another neighbour reaches it: take that route when the one it has is not working
                if (!peer.SendEstablished || !RouteHolds(peer, now))
                {
                    host.Membership.RouteThrough(neighbor, peer.Id);
                    routeConfirmed[peer.Id] = (neighbor, now);
                    routeAsked[peer.Id] = now;
                }
            }
        }

        void ForgetRoute(NodeId member)
        {
            routeConfirmed.Remove(member);
            routeAsked.Remove(member);
        }
    }
}
