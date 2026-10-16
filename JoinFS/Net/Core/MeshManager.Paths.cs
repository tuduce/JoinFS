using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace JoinFS.Net
{
    /// <summary>
    /// A peer's path: the one endpoint all traffic to it uses, and how it moves between IPv4 and IPv6
    /// (docs/jfp2/design-future.md §4.1). A peer has IPv6 endpoint candidates, self-reported in a handshake
    /// or observed as the source of its Pulses; they are probed with Pulses for 30 s, the first PulseResponse
    /// from one moves the path to IPv6 (the path upgrade), the IPv4 endpoint is kept aside, and a path that
    /// answers no Pulse for 10 s goes back to it (the fallback), after which IPv6 is left alone for 5 minutes.
    /// A hub tells the members it admitted where the other members are (PeerCandidates), so that two members
    /// that cannot reach each other over IPv4 still open an IPv6 path, both probing at once.
    /// Network thread only.
    /// </summary>
    public sealed partial class MeshManager
    {
        /// <summary>How long a peer's candidates are probed after they became known, in seconds.</summary>
        public const double ProbeWindow = 30;

        /// <summary>A peer on an upgraded path that answered no Pulse over IPv6 for this long goes back to IPv4, in seconds.</summary>
        public const double FallbackAfter = 10;

        /// <summary>After a fallback, IPv6 is neither probed nor used for this long, in seconds.</summary>
        public const double HoldOff = 300;

        /// <summary>The most candidates kept for a peer.</summary>
        public const int MaxCandidates = 4;

        /// <summary>How long announced candidates of a member that is not registered yet wait for it, in seconds.</summary>
        public const double HeldCandidatesTime = 10;

        /// <summary>The most members whose announced candidates are held at once.</summary>
        const int MaxHeldCandidates = 64;

        readonly Dictionary<NodeId, (List<IPEndPoint> Candidates, double Expire)> heldCandidates = [];

        /// <summary>The last <see cref="Peer.CandidatesRevision"/> given out.</summary>
        int candidatesRevision;

        // ------------------------------------------------------------------ candidates

        /// <summary>This node can send IPv6 (its socket is dual-mode and the OS has a global IPv6 address).</summary>
        bool Ipv6Available => core.Transport.LocalIpv6Candidate != null;

        /// <summary>
        /// A peer says where it can be reached over IPv6 (its handshake's Candidates). The usable ones replace
        /// the peer's candidate list; a changed list starts a new probe window.
        /// </summary>
        public void OfferCandidates(NodeId id, IReadOnlyList<IPEndPoint> offered)
        {
            if (!core.Peers.TryGet(id, out Peer peer) || !Ipv6Available)
            {
                return;
            }
            List<IPEndPoint> list = UsableCandidates(offered);
            if (list.Count > 0)
            {
                SetCandidates(peer, list, "reported");
            }
        }

        /// <summary>The usable ones of <paramref name="offered"/> (global IPv6 with a port), without repeats, at most <see cref="MaxCandidates"/>, as new objects.</summary>
        static List<IPEndPoint> UsableCandidates(IReadOnlyList<IPEndPoint> offered)
        {
            List<IPEndPoint> list = [];
            foreach (IPEndPoint endPoint in offered)
            {
                if (Ipv6Address.IsUsableCandidate(endPoint) && !list.Contains(endPoint) && list.Count < MaxCandidates)
                {
                    list.Add(new IPEndPoint(endPoint.Address, endPoint.Port));
                }
            }
            return list;
        }

        /// <summary>
        /// Make <paramref name="list"/> the peer's candidates, if they changed: probing starts for a non-empty list,
        /// and a hub tells its members (<see cref="AnnounceCandidates"/>).
        /// </summary>
        void SetCandidates(Peer peer, List<IPEndPoint> list, string how)
        {
            if (list.SequenceEqual(peer.candidates))
            {
                return;
            }
            peer.candidates = list;
            peer.CandidatesRevision = ++candidatesRevision;
            if (list.Count > 0)
            {
                StartProbing(peer);
            }
            core.Log(NetLogLevel.Network, "NETWORK: Candidates of " + peer.Id + " " + how + " " + (list.Count == 0 ? "none" : string.Join(" ", list)));
        }

        /// <summary>
        /// A direct datagram of a peer on an IPv4 path came from this IPv6 source: it is a candidate, tried
        /// first. Only a source in the /64 of a candidate the peer reported itself is taken (a temporary address
        /// rotating, say), or any source for a peer that reported none: a forged Pulse cannot make this node probe
        /// arbitrary addresses once the peer has said where it is.
        /// </summary>
        void ObserveCandidate(Peer peer, IPEndPoint source)
        {
            if (peer.OnIpv6 || peer.Relayed || !Ipv6Available || !Ipv6Address.IsUsableCandidate(source))
            {
                return;
            }
            foreach (IPEndPoint known in peer.candidates)
            {
                if (known.Equals(source))
                {
                    return;
                }
            }
            if (peer.candidates.Count > 0 && !peer.candidates.Exists(c => Ipv6Address.SamePrefix64(c.Address, source.Address)))
            {
                return;
            }
            peer.candidates.Insert(0, new IPEndPoint(source.Address, source.Port));
            if (peer.candidates.Count > MaxCandidates)
            {
                peer.candidates.RemoveAt(MaxCandidates);
            }
            peer.CandidatesRevision = ++candidatesRevision;
            StartProbing(peer);
            core.Log(NetLogLevel.Network, "NETWORK: Candidate of " + peer.Id + " observed " + source);
        }

        /// <summary>Probe the peer's candidates for a window from now, or from the end of the hold-off.</summary>
        void StartProbing(Peer peer) => peer.ProbeUntil = System.Math.Max(Now, peer.HoldOffUntil) + ProbeWindow;

        // ------------------------------------------------------------------ probing and upgrade

        /// <summary>Send a Pulse to each candidate of the peers that are being probed, with the broadcast Pulse.</summary>
        void ProbeCandidates(in Pulse pulse)
        {
            double now = Now;
            foreach (Peer peer in core.Peers.All)
            {
                if (peer.candidates.Count == 0 || peer.OnIpv6 || now < peer.HoldOffUntil || now >= peer.ProbeUntil)
                {
                    continue;
                }
                foreach (IPEndPoint candidate in peer.candidates)
                {
                    core.SendToEndPoint(candidate, pulse, guaranteed: false, recipient: peer.Id);
                }
            }
        }

        /// <summary>
        /// Where the answer to a Pulse goes: to its actual source when that is the other address family than the
        /// sender's route, or an IPv6 address other than the route's (a probe; the route can be a relay's, in the
        /// same family), else to the route as before.
        /// </summary>
        static IPEndPoint ReplyEndPoint(in MessageMeta meta)
        {
            IPEndPoint source = meta.Source;
            bool probe = !meta.Forwarded && source != null
                && (source.AddressFamily != meta.EndPoint.AddressFamily
                    || (source.AddressFamily == AddressFamily.InterNetworkV6 && !source.Address.Equals(meta.EndPoint.Address)));
            return probe ? source : meta.EndPoint;
        }

        /// <summary>A Pulse arrived directly from <paramref name="peer"/> over IPv6 while its path is IPv4: its source is a candidate.</summary>
        void PulseSource(Peer peer, in MessageMeta meta)
        {
            if (!meta.Forwarded && meta.Source != null && meta.Source.AddressFamily == AddressFamily.InterNetworkV6)
            {
                ObserveCandidate(peer, meta.Source);
            }
        }

        /// <summary>
        /// A PulseResponse arrived directly from <paramref name="peer"/> over IPv6. On an IPv6 path it shows
        /// the path alive; on an IPv4 path, from one of the peer's candidates, it upgrades the path.
        /// </summary>
        void PulseAnswered(Peer peer, in MessageMeta meta, bool wasEstablished)
        {
            IPEndPoint source = meta.Source;
            if (meta.Forwarded || source == null || source.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return;
            }
            if (peer.OnIpv6)
            {
                if (source.Address.Equals(peer.EndPoint.Address))
                {
                    peer.Ipv6AnsweredAt = Now;
                }
                return;
            }
            if (Now < peer.HoldOffUntil || !peer.candidates.Exists(c => c.Address.Equals(source.Address)))
            {
                return;
            }
            var ipv6 = new IPEndPoint(source.Address, source.Port);
            peer.Ipv4EndPoint = peer.EndPoint;
            peer.Ipv6AnsweredAt = Now;
            peer.Ipv4Unproven = !wasEstablished;
            if (peer.Relayed)
            {
                // a pair with no IPv4 path between them (the hub relays it): the candidate answered, so the peer is
                // direct now, over IPv6; a fallback goes back to the relay
                peer.RelayBeforeUpgrade = peer.RouteVia;
                peer.RouteVia = default;
            }
            core.Log(NetLogLevel.Network, "NETWORK: Path upgrade " + peer.Id + " " + peer.EndPoint + " -> " + ipv6 + (peer.RelayBeforeUpgrade.Valid() ? " (was relayed through " + peer.RelayBeforeUpgrade + ")" : ""));
            SetPath(peer, ipv6);
        }

        // ------------------------------------------------------------------ fallback

        void CheckPaths()
        {
            double now = Now;
            foreach (Peer peer in core.Peers.All)
            {
                if (peer.Ipv4EndPoint != null && now > peer.Ipv6AnsweredAt + FallbackAfter)
                {
                    IPEndPoint ipv4 = peer.Ipv4EndPoint;
                    peer.Ipv4EndPoint = null;
                    peer.HoldOffUntil = now + HoldOff;
                    StartProbing(peer);
                    core.Log(NetLogLevel.Network, "NETWORK: Path fallback " + peer.Id + " " + peer.EndPoint + " -> " + ipv4 + " (no answer over IPv6 for " + FallbackAfter + " s, IPv6 off for " + HoldOff + " s)");
                    NodeId relay = peer.RelayBeforeUpgrade;
                    peer.RelayBeforeUpgrade = default;
                    if (relay.Valid() || peer.Ipv4Unproven)
                    {
                        // the peer never answered over IPv4: it is reached through its relay again, or searched for again
                        if (relay.Valid() && core.Peers.TryGet(relay, out Peer via) && via != peer && via.SendEstablished)
                        {
                            peer.RouteVia = relay;
                            peer.RouteEndPoint = via.RouteEndPoint;
                        }
                        else
                        {
                            peer.SendEstablished = false;
                        }
                    }
                    peer.Ipv4Unproven = false;
                    SetPath(peer, ipv4);
                }
            }
        }

        // ------------------------------------------------------------------ the path itself

        /// <summary>
        /// Move <paramref name="peer"/> to <paramref name="endPoint"/>, an endpoint of the same node. The
        /// node's own endpoint and its route are replaced together, by a new object (the old one stays valid
        /// for whoever still holds it), unless the peer is reached through a relay: then only its own endpoint
        /// changes and the route stays the relay's. Peers routed through this one follow it, and the plugins move their
        /// per-endpoint state (a JFP2 session, the legacy guaranteed de-duplication).
        /// </summary>
        void SetPath(Peer peer, IPEndPoint endPoint)
        {
            IPEndPoint previous = peer.EndPoint;
            peer.EndPoint = endPoint;
            if (!peer.Relayed)
            {
                peer.RouteEndPoint = endPoint;
            }
            foreach (Peer other in core.Peers.All)
            {
                if (other != peer && other.RouteVia == peer.Id && !peer.Relayed)
                {
                    other.RouteEndPoint = endPoint;
                    other.InvalidateRoutes();
                }
            }
            core.PathChanged(peer, previous);
        }

        // ------------------------------------------------------------------ PeerCandidates: the hub's side

        /// <summary>A peer joined through this node (this node answered its Join): this node is its hub.</summary>
        void MarkJoinedThroughMe(NodeId id)
        {
            if (core.Peers.TryGet(id, out Peer peer))
            {
                peer.JoinedThroughMe = true;
            }
        }

        /// <summary>
        /// Tell each member this node is the hub of what it has not been told: the candidates of every other
        /// peer, as they are now (new members, and members whose candidates changed since). Sent once the member
        /// has agreed the class (a member that is still negotiating gets it right after), to members reached
        /// directly only, in messages the plugin cuts to fit a datagram. Runs with the Pulse, once a second.
        /// </summary>
        void AnnounceCandidates()
        {
            List<PeerCandidateEntry> entries = null;
            List<(NodeId Node, int Revision)> told = null;
            foreach (Peer member in core.Peers.All)
            {
                if (!member.JoinedThroughMe || member.Relayed)
                {
                    continue;
                }
                entries?.Clear();
                told?.Clear();
                foreach (Peer subject in core.Peers.All)
                {
                    if (subject == member || subject.candidates.Count == 0
                        || (member.Announced != null && member.Announced.TryGetValue(subject.Id, out int revision) && revision == subject.CandidatesRevision))
                    {
                        continue;
                    }
                    entries ??= [];
                    told ??= [];
                    entries.Add(new PeerCandidateEntry { Node = subject.Id, Candidates = [.. subject.candidates] });
                    told.Add((subject.Id, subject.CandidatesRevision));
                }
                if (entries == null || entries.Count == 0 || core.Route(member.Id, MessageKind.PeerCandidates) == null)
                {
                    continue;
                }
                core.SendTo(member.Id, new PeerCandidates { Entries = entries }, guaranteed: true);
                member.Announced ??= [];
                foreach ((NodeId node, int revision) in told)
                {
                    member.Announced[node] = revision;
                }
                core.Log(NetLogLevel.Network, "NETWORK: PeerCandidates to " + member.Id + " about " + entries.Count + " peer(s)");
            }
        }

        // ------------------------------------------------------------------ PeerCandidates: the member's side

        /// <summary>
        /// The hub says where members can be reached. Counted only from the node this one joined through, directly (not forwarded); an entry
        /// replaces the peer's candidates (an empty one clears them), as if the peer had reported them itself; an entry
        /// for this node is ignored, one for a member not registered yet is held for <see cref="HeldCandidatesTime"/>.
        /// </summary>
        public void Handle(in MessageMeta meta, in PeerCandidates message)
        {
            if (!Connected || message.Entries == null)
            {
                return;
            }
            if (meta.Forwarded || !joinedThrough.Valid() || meta.Sender != joinedThrough)
            {
                core.Log(NetLogLevel.Network, "NETWORK: PeerCandidates from " + meta.Sender + " ignored (not directly from the node this one joined through)");
                return;
            }
            if (!Ipv6Available)
            {
                return;
            }
            foreach (PeerCandidateEntry entry in message.Entries)
            {
                if (!entry.Node.Valid() || entry.Node == LocalId)
                {
                    continue;
                }
                List<IPEndPoint> list = UsableCandidates(entry.Candidates ?? []);
                if (core.Peers.TryGet(entry.Node, out Peer peer))
                {
                    SetCandidates(peer, list, "announced by " + meta.Sender);
                }
                else if (heldCandidates.ContainsKey(entry.Node) || heldCandidates.Count < MaxHeldCandidates)
                {
                    heldCandidates[entry.Node] = (list, Now + HeldCandidatesTime);
                }
            }
        }

        /// <summary>A member that was announced before it was registered is here now: its held candidates apply.</summary>
        void ApplyHeldCandidates(Peer peer)
        {
            if (heldCandidates.Remove(peer.Id, out var held) && Now < held.Expire && Ipv6Available)
            {
                SetCandidates(peer, held.Candidates, "announced");
            }
        }

        void ExpireHeldCandidates()
        {
            if (heldCandidates.Count == 0)
            {
                return;
            }
            double now = Now;
            foreach (NodeId id in heldCandidates.Where(h => now >= h.Value.Expire).Select(h => h.Key).ToList())
            {
                heldCandidates.Remove(id);
            }
        }
    }
}
