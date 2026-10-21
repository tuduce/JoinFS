using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace JoinFS.Net.Jfp2
{
    // Where a member is reached when it has several endpoints (docs/network/jfp2/protocol.md §5.14, plan 5.4): its candidates are
    // tried IPv6 first and IPv4 a short head start later, the first to answer is the path, IPv6 is preferred whenever it answers
    // and is left for IPv4 when it falls silent. A datagram that arrives on the other family never moves a path by itself.
    public sealed partial class Jfp2Plugin
    {
        /// <summary>The head start IPv6 has over IPv4 when both are tried (Happy Eyeballs, RFC 8305's connection attempt delay).</summary>
        const double HappyEyeballsDelay = 0.25;
        /// <summary>A link on IPv4 tries the member's IPv6 candidates again this often.</summary>
        const double IPv6ProbeInterval = 60.0;
        /// <summary>After IPv6 fell silent, a member is not tried on it for this long.</summary>
        const double IPv6HoldOff = 60.0;

        sealed class PathState
        {
            /// <summary>IPv6 is not used for this member before this time.</summary>
            public double V6HoldUntil;
            /// <summary>When a link on IPv4 tries the member's IPv6 candidates next.</summary>
            public double NextV6Probe;
        }

        readonly Dictionary<NodeId, PathState> paths = [];

        static bool IsV6(IPEndPoint endPoint) => endPoint.AddressFamily == AddressFamily.InterNetworkV6;

        static string Family(IPEndPoint endPoint) => IsV6(endPoint) ? "IPv6" : "IPv4";

        /// <summary>This node's candidates as a Hello carries them (<see cref="LocalIdentity.Candidates"/>); null when it has none yet.</summary>
        List<IPEndPoint> OwnCandidates() => host.Identity.Candidates is { Count: > 0 } own ? [.. own] : null;

        PathState PathOf(NodeId peer)
        {
            if (!paths.TryGetValue(peer, out PathState state))
            {
                paths[peer] = state = new PathState();
            }
            return state;
        }

        /// <summary>
        /// The endpoints a Hello to <paramref name="peer"/> goes to, IPv6 ones first: for a member with a legacy link, where legacy has it;
        /// otherwise its candidates and where it was registered, minus the IPv6 ones when this node has no IPv6 or the member's IPv6 is held off.
        /// </summary>
        List<IPEndPoint> ProbeTargets(Peer peer, double now)
        {
            var targets = new List<IPEndPoint>();
            if (peer.LegacyLink)
            {
                targets.Add(Copy(peer.RouteEndPoint));
                return targets;
            }
            bool v6 = host.Identity.HasIPv6 && now >= (paths.TryGetValue(peer.Id, out PathState state) ? state.V6HoldUntil : 0);
            foreach (IPEndPoint candidate in peer.Candidates)
            {
                if ((v6 || !IsV6(candidate)) && !IsForeignLan(candidate) && !targets.Contains(candidate))
                {
                    targets.Add(candidate);
                }
            }
            if ((v6 || !IsV6(peer.EndPoint)) && !targets.Contains(peer.EndPoint))
            {
                targets.Add(peer.EndPoint);
            }
            // IPv6 first, in the order given within a family (List.Sort is not stable)
            var ordered = new List<IPEndPoint>(targets.Count);
            ordered.AddRange(targets.FindAll(IsV6));
            ordered.AddRange(targets.FindAll(t => !IsV6(t)));
            return ordered;
        }

        /// <summary>
        /// A private (RFC 1918), shared (CGNAT) or link-local IPv4 address that is not on this node's own /24: it is a LAN address of someone else's
        /// network, which no Hello of ours can reach and which would only probe whatever has that address at home. A member's registered endpoint is
        /// not filtered (it is where the node that admitted it saw it).
        /// </summary>
        bool IsForeignLan(IPEndPoint endPoint)
        {
            if (IsV6(endPoint))
            {
                return false;
            }
            Span<byte> b = stackalloc byte[4];
            endPoint.Address.TryWriteBytes(b, out _);
            bool lan = b[0] == 10 || (b[0] == 172 && (b[1] & 0xF0) == 16) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && (b[1] & 0xC0) == 64) || (b[0] == 169 && b[1] == 254);
            return lan && !host.Identity.IsLocalAddress(endPoint.Address);
        }

        /// <summary>The Hello of a round went to <paramref name="endPoint"/> (or is about to), so its answer is the session's.</summary>
        static bool IsProbe(PeerSession session, IPEndPoint endPoint) => endPoint.Equals(session.ProbeEndPoint) || session.Probes.Contains(endPoint);

        /// <summary>
        /// A round of Hellos to <paramref name="targets"/> (IPv6 ones first): the IPv6 ones now, the IPv4 ones a head start later when
        /// there is an IPv6 one. The first to answer becomes the session's endpoint (<see cref="HandleHelloAck"/>).
        /// </summary>
        void StartProbe(PeerSession session, List<IPEndPoint> targets, double now, string why)
        {
            session.Probes.Clear();
            session.PendingProbes.Clear();
            session.ProbeEndPoint = targets[0];
            bool v6 = false;
            foreach (IPEndPoint target in targets)
            {
                v6 |= IsV6(target);
                if (IsV6(target) || !v6)
                {
                    SendProbe(session, target);
                }
                else
                {
                    session.PendingProbes.Add((target, now + HappyEyeballsDelay));
                }
            }
            host.Log(NetLogLevel.Network, "JFP2: Send Hello to " + session.Peer + " at " + string.Join(", ", targets) + why);
        }

        void SendProbe(PeerSession session, IPEndPoint target)
        {
            if (!session.Probes.Contains(target))
            {
                session.Probes.Add(target);
            }
            SendHello(target, session);
        }

        /// <summary>Send the Hellos of a round that were waiting for IPv6's head start.</summary>
        void SendDueProbes()
        {
            double now = Now;
            foreach (PeerSession session in sessions.Values)
            {
                if (session.PendingProbes.Count == 0)
                {
                    continue;
                }
                for (int i = 0; i < session.PendingProbes.Count;)
                {
                    if (now >= session.PendingProbes[i].At)
                    {
                        SendProbe(session, session.PendingProbes[i].EndPoint);
                        session.PendingProbes.RemoveAt(i);
                    }
                    else
                    {
                        i++;
                    }
                }
            }
        }

        /// <summary>
        /// A link on IPv4 to a member that advertised IPv6 candidates tries them every <see cref="IPv6ProbeInterval"/>; if one answers,
        /// the link moves to it (<see cref="MovesPath"/>). Not for a member with a legacy link, and not while its IPv6 is held off.
        /// </summary>
        void ProbeIPv6(Peer peer, PeerSession session, double now)
        {
            if (peer.LegacyLink || IsV6(session.Endpoint) || !host.Identity.HasIPv6)
            {
                return;
            }
            PathState state = PathOf(peer.Id);
            if (now < state.V6HoldUntil || now < state.NextV6Probe)
            {
                return;
            }
            state.NextV6Probe = now + IPv6ProbeInterval;
            var tried = new List<IPEndPoint>();
            foreach (IPEndPoint candidate in peer.Candidates)
            {
                if (IsV6(candidate))
                {
                    SendProbe(session, candidate);
                    tried.Add(candidate);
                }
            }
            if (tried.Count > 0)
            {
                host.Log(NetLogLevel.Network, "JFP2: " + peer.Id + " is linked over IPv4 at " + session.Endpoint + " - trying its IPv6 at " + string.Join(", ", tried));
            }
        }

        /// <summary>
        /// An answer from <paramref name="probe"/> moves the verified link of <paramref name="session"/> only from IPv4 to IPv6, of a member
        /// with no legacy link, when its IPv6 is not held off: IPv6 is preferred. Any other answer from a path the link is not on (a
        /// straggler of an earlier round, the same node on the other family) does not move it, so a link never flips between the two.
        /// </summary>
        bool MovesPath(PeerSession session, IPEndPoint probe, double now) =>
            host.Peers.TryGet(session.Peer, out Peer peer) && !peer.LegacyLink && peer.IsOwnEndPoint(probe)
            && IsV6(probe) && !IsV6(session.Endpoint) && now >= PathOf(peer.Id).V6HoldUntil;

        /// <summary>
        /// The link to a member on IPv6 stopped answering: while it has an IPv4 endpoint to move to, its IPv6 is left alone for
        /// <see cref="IPv6HoldOff"/>, so a path that comes and goes is not tried again at once.
        /// </summary>
        void HoldOffIPv6(NodeId peerId, IPEndPoint lost, double now)
        {
            if (lost == null || !IsV6(lost) || !host.Peers.TryGet(peerId, out Peer peer) || peer.LegacyLink)
            {
                return;
            }
            bool alternative = !IsV6(peer.EndPoint);
            foreach (IPEndPoint candidate in peer.Candidates)
            {
                alternative |= !IsV6(candidate);
            }
            if (alternative)
            {
                PathOf(peerId).V6HoldUntil = now + IPv6HoldOff;
                host.Log(NetLogLevel.Event, "NETWORK: " + peerId + " does not answer on IPv6 at " + lost + " - moving to IPv4, and not using IPv6 for " + IPv6HoldOff + " s");
            }
        }
    }
}
