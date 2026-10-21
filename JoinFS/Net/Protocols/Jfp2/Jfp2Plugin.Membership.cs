using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;

namespace JoinFS.Net.Jfp2
{
    // Join over JFP2: a node in a non-member session (protocol.md §5.9) asks to become a member with Join, is
    // answered with JoinResult and then told of the members with MemberList (§5.10); the members already in
    // the session are told of the joiner with MemberAnnounce, and of a departure with MemberLeave (§5.11). The
    // membership itself is the mesh's: this file reports what the wire brought through IMembership.
    public sealed partial class Jfp2Plugin
    {
        const double JoinRetryInterval = 2.0;
        const double JoinTimeout = 15.0;
        const int JoinMaxAttempts = 5;
        /// <summary>How long a join that may fall back to legacy waits for any answer to its first Hello (protocol.md §5.12).</summary>
        const double JoinSilence = 2.0;

        /// <summary>The join this node started and has not finished: Join is repeated until JoinResult and every MemberList datagram it announced have arrived.</summary>
        sealed class PendingJoin
        {
            public IPEndPoint Target;
            public uint PasswordHash;
            public double Started;
            public double Deadline;
            /// <summary>When it fails without a refusal, the mesh joins over legacy (<see cref="IMembership.JoinFailedOverJfp2"/>).</summary>
            public bool LegacyFallback;
            /// <summary>The session with the target, once the cookie exchange has made it.</summary>
            public PeerSession Session;
            public int Attempts;
            public double NextSend;
            public bool Accepted;
            public int Segments;
            public readonly Dictionary<byte, List<MemberEntry>> Lists = [];
            public readonly HashSet<byte> Applied = [];
        }

        /// <summary>The joins under way, one per address: the global session joins several hubs at once.</summary>
        readonly List<PendingJoin> pendingJoins = [];

        /// <summary>For tests and diagnostics: a join started with <see cref="BeginJoin"/> is not finished.</summary>
        public bool JoinPending => pendingJoins.Count > 0;

        static bool IsMembershipClass(byte messageClass) =>
            messageClass is MessageClasses.Join or MessageClasses.JoinResult or MessageClasses.MemberList or MessageClasses.MemberAnnounce or MessageClasses.MemberLeave
                or MessageClasses.RouteQuery or MessageClasses.RouteAnswer;

        // ================================================================== asking (this node joins)

        public bool BeginJoin(IPEndPoint target, uint passwordHash, bool legacyFallback)
        {
            target = Copy(target);
            if (OwnNames().Count == 0)
            {
                return false;
            }
            // a join of the same address that is under way is replaced by this one
            pendingJoins.RemoveAll(other => other.Target.Equals(target));
            // a session left over from an earlier attempt has a cookie and ids that mean nothing to a fresh Join
            foreach (NodeId name in new List<NodeId>(nonMembers.Keys))
            {
                if (nonMembers[name].Endpoint != null && nonMembers[name].Endpoint.Equals(target))
                {
                    RemoveNonMember(name);
                }
            }
            double now = Now;
            pendingJoins.Add(new PendingJoin { Target = target, PasswordHash = passwordHash, Started = now, Deadline = now + JoinTimeout, LegacyFallback = legacyFallback });
            Introduce(target);
            host.Log(NetLogLevel.Network, "JFP2: joining the session of " + target + (legacyFallback ? ", legacy if it does not answer within " + JoinSilence + " s" : ""));
            return true;
        }

        void DoJoin()
        {
            if (pendingJoins.Count == 0)
            {
                return;
            }
            foreach (PendingJoin join in pendingJoins.ToArray())
            {
                DoJoin(join);
            }
        }

        void DoJoin(PendingJoin join)
        {
            double now = Now;
            if (now >= join.Deadline && !join.Accepted)
            {
                FailJoin(join, "no answer from " + join.Target + " within " + JoinTimeout + " s", fallback: true);
                return;
            }
            if (join.Session == null)
            {
                foreach (PeerSession session in nonMembers.Values)
                {
                    if (session.Endpoint != null && session.Endpoint.Equals(join.Target))
                    {
                        join.Session = session;
                        break;
                    }
                }
                if (join.Session == null)
                {
                    if (now >= join.Started + JoinSilence && !AnsweredIntroduction(join.Target))
                    {
                        FailJoin(join, "no answer from " + join.Target + " within " + JoinSilence + " s", fallback: true);
                    }
                    return;
                }
                if (join.Session.AgreedInternalVersion[MessageClasses.Join] == 0)
                {
                    FailJoin(join, join.Target + " does not take a join over JFP2", fallback: true);
                    return;
                }
            }
            if (now < join.NextSend)
            {
                return;
            }
            if (join.Attempts >= JoinMaxAttempts)
            {
                FailJoin(join, join.Accepted ? join.Target + " did not send all its members" : join.Target + " did not answer the Join", fallback: true);
                return;
            }
            join.Attempts++;
            join.NextSend = now + JoinRetryInterval;
            var body = new JoinBody { HasProof = join.PasswordHash != 0 };
            if (body.HasProof)
            {
                body.Proof = JoinBody.ComputeProof(join.PasswordHash, join.Session.AdmissionCookie, OwnNames()[0]);
            }
            SendInternal(join.Session, MessageClasses.Join, body.Serialize());
            host.Log(NetLogLevel.Network, "JFP2: Join sent to " + join.Session.Peer + " at " + join.Target + " (attempt " + join.Attempts + ")");
        }

        /// <summary>The first Hello of an introduction to this endpoint was answered (a cookie came back).</summary>
        bool AnsweredIntroduction(IPEndPoint target)
        {
            foreach (Introduction introduction in introductions.Values)
            {
                if (introduction.Cookie != null && introduction.EndPoint.Equals(target))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The join is over without a refusal. Before it was accepted, and when it was started with the fallback, the
        /// mesh joins over legacy; what this node began with the address (the introduction, a non-member session) ends.
        /// </summary>
        void FailJoin(PendingJoin join, string reason, bool fallback = false)
        {
            pendingJoins.Remove(join);
            host.Log(NetLogLevel.Event, "JFP2: join failed: " + reason);
            if (fallback && join.LegacyFallback && !join.Accepted)
            {
                foreach (Introduction introduction in new List<Introduction>(introductions.Values))
                {
                    if (introduction.EndPoint.Equals(join.Target))
                    {
                        introductions.Remove(introduction.Id);
                    }
                }
                if (join.Session != null)
                {
                    RemoveNonMember(join.Session.Peer);
                }
                host.Membership.JoinFailedOverJfp2(join.Target, join.PasswordHash);
            }
        }

        PendingJoin JoinOf(PeerSession hop) => pendingJoins.Find(join => join.Session == hop);

        void HandleJoinResult(PeerSession hop, ReadOnlySpan<byte> payload)
        {
            PendingJoin join = JoinOf(hop);
            if (join == null || !JoinResultBody.TryRead(payload, out JoinResultBody result))
            {
                host.Log(NetLogLevel.Network, "JFP2: JoinResult from " + hop.Peer + " answers no join of this node - ignored");
                return;
            }
            if (result.Result != JoinResult.Accepted)
            {
                pendingJoins.Remove(join);
                RemoveNonMember(hop.Peer);
                host.Membership.JoinRefused(result.Result);
                host.Log(NetLogLevel.Event, "JFP2: " + hop.Peer + " refused the join: " + result.Result);
                return;
            }
            if (!join.Accepted)
            {
                Peer admitter = host.Membership.Joined(result.Suid, hop.Peer, hop.Endpoint, OtherNames(hop));
                if (admitter == null)
                {
                    FailJoin(join, hop.Peer + " accepted the join but this node cannot take its session");
                    RemoveNonMember(hop.Peer);
                    return;
                }
                Promote(hop);
                host.LinkChanged(hop.Peer);
                join.Accepted = true;
                join.Segments = result.ListSegments;
            }
            ApplyLists(join);
        }

        void HandleMemberList(PeerSession hop, ReadOnlySpan<byte> payload)
        {
            PendingJoin join = JoinOf(hop);
            if (join == null || !MemberListBody.TryRead(payload, out MemberListBody list))
            {
                host.Log(NetLogLevel.Network, "JFP2: MemberList from " + hop.Peer + " answers no join of this node - ignored");
                return;
            }
            join.Lists.TryAdd(list.Index, list.Entries);
            ApplyLists(join);
        }

        /// <summary>Hand the members that have arrived to the mesh once the join is accepted; the join is finished when all the announced datagrams are in.</summary>
        void ApplyLists(PendingJoin join)
        {
            if (!join.Accepted)
            {
                return;
            }
            foreach (var (index, entries) in join.Lists)
            {
                if (join.Applied.Add(index))
                {
                    host.Membership.LearnMembers(join.Session.Peer, entries);
                }
            }
            if (join.Applied.Count >= join.Segments)
            {
                pendingJoins.Remove(join);
                host.Log(NetLogLevel.Event, "JFP2: joined through " + join.Session.Peer + " at " + join.Target + ", " + host.Peers.Count + " peer(s) in the session");
            }
        }

        // ================================================================== answering (this node admits)

        void HandleJoin(IPEndPoint from, PeerSession hop, ReadOnlySpan<byte> payload)
        {
            if (!JoinBody.TryRead(payload, out JoinBody join))
            {
                return;
            }
            if (!host.Membership.Admitting)
            {
                host.Log(NetLogLevel.Network, "JFP2: Join from " + from + " ignored: this node is in no session that takes joiners");
                return;
            }
            if (!hop.NonMember)
            {
                // the joiner asks again (our answer was lost): the same answer
                if (host.Peers.TryGet(hop.Peer, out Peer member))
                {
                    AnswerJoin(hop, member);
                }
                return;
            }
            // the cookie proved this endpoint answers; a Join from another is not this session's
            if (hop.Endpoint == null || !from.Equals(hop.Endpoint))
            {
                host.Log(NetLogLevel.Network, "JFP2: Join from " + from + " is not from " + hop.Endpoint + ", where " + hop.Peer + " was admitted - ignored");
                return;
            }
            NodeId joiner = hop.Peer;
            JoinResult verdict = host.Membership.Judge(hash =>
                join.HasProof && join.Proof != null && hop.AdmissionCookie != null
                && CryptographicOperations.FixedTimeEquals(JoinBody.ComputeProof(hash, hop.AdmissionCookie, joiner), join.Proof));
            if (verdict != JoinResult.Accepted)
            {
                SendInternal(hop, MessageClasses.JoinResult, new JoinResultBody { Result = verdict }.Serialize());
                host.Log(NetLogLevel.Event, "JFP2: join of " + joiner + " at " + from + " refused: " + verdict);
                return;
            }
            Peer peer = host.Membership.Admit(joiner, hop.Endpoint, OtherNames(hop), out bool isNew);
            if (peer == null)
            {
                host.Log(NetLogLevel.Network, "JFP2: join of " + joiner + " at " + from + " not taken: the mesh cannot add it");
                return;
            }
            Promote(hop);
            host.LinkChanged(joiner);
            if (isNew)
            {
                AnnounceJoined(peer);
            }
            else
            {
                // it restarted: what it was sent before it knew nothing of, so its identities go out again
                identitySent.RemoveWhere((key, _) => key.Peer == peer.Id);
            }
            AnswerJoin(hop, peer);
        }

        /// <summary>A member just admitted, or learned of over legacy: the other members that are neighbours of this node are told (§5.11, §5.13).</summary>
        public void AnnounceJoined(Peer member)
        {
            var names = new List<NodeId> { member.Id };
            if (member.Aliases != null)
            {
                names.AddRange(member.Aliases);
            }
            byte[] body = new MemberAnnounceBody { Entry = new MemberEntry(names.ToArray(), member.EndPoint) }.Serialize();
            int told = SendToMembers(MessageClasses.MemberAnnounce, body, member.Id);
            if (told > 0)
            {
                host.Log(NetLogLevel.Network, "JFP2: " + member.Id + " announced to " + told + " member(s)");
            }
        }

        /// <summary>An internal datagram to every neighbour that is a member and offered the class, except <paramref name="except"/>. Returns how many.</summary>
        int SendToMembers(byte messageClass, ReadOnlySpan<byte> payload, NodeId except)
        {
            int count = 0;
            foreach (PeerSession session in sessions.Values)
            {
                if (session.Verified && session.HandshakeComplete && !session.NonMember && session.Peer != except && session.AgreedInternalVersion[messageClass] != 0)
                {
                    SendInternal(session, messageClass, payload);
                    count++;
                }
            }
            return count;
        }

        // ================================================================== members coming and going

        void HandleMemberAnnounce(PeerSession hop, ReadOnlySpan<byte> payload)
        {
            if (!MemberOfThisSession(hop, out NodeId neighbor) || !MemberAnnounceBody.TryRead(payload, out MemberAnnounceBody announce))
            {
                host.Log(NetLogLevel.Network, "JFP2: MemberAnnounce from " + hop.Peer + " is not from a member or not readable - ignored");
                return;
            }
            host.Membership.LearnMembers(neighbor, [announce.Entry]);
        }

        void HandleMemberLeave(PeerSession hop, ReadOnlySpan<byte> payload)
        {
            if (!MemberOfThisSession(hop, out NodeId neighbor) || !MemberLeaveBody.TryRead(payload, out MemberLeaveBody leave))
            {
                host.Log(NetLogLevel.Network, "JFP2: MemberLeave from " + hop.Peer + " is not from a member or not readable - ignored");
                return;
            }
            host.Membership.Left(neighbor, leave.Name);
        }

        /// <summary>The neighbour of the session is a member of this node's session: a stranger on a non-member session has no say.</summary>
        bool MemberOfThisSession(PeerSession hop, out NodeId neighbor)
        {
            neighbor = host.Peers.Resolve(hop.Peer);
            return !hop.NonMember && host.Peers.Contains(neighbor);
        }

        /// <summary>A member this node held is gone (expired, or told to be): the neighbours it may have told of it are told (§5.11).</summary>
        void AnnounceGone(Peer peer)
        {
            if (!host.Connected)
            {
                return;
            }
            int told = SendToMembers(MessageClasses.MemberLeave, new MemberLeaveBody { Name = peer.Id }.Serialize(), peer.Id);
            if (told > 0)
            {
                host.Log(NetLogLevel.Network, "JFP2: " + peer.Id + " is gone: told " + told + " member(s)");
            }
        }

        public void AnnounceLeaving()
        {
            if (OwnNames() is not { Count: > 0 } names)
            {
                return;
            }
            int told = SendToMembers(MessageClasses.MemberLeave, new MemberLeaveBody { Name = names[0] }.Serialize(), default);
            if (told > 0)
            {
                host.Log(NetLogLevel.Network, "JFP2: this node left: told " + told + " member(s)");
            }
        }

        /// <summary>JoinResult, then the members other than the joiner in as many MemberList datagrams as they need.</summary>
        void AnswerJoin(PeerSession hop, Peer joiner)
        {
            var segments = new List<List<MemberEntry>>();
            List<MemberEntry> current = null;
            int bytes = 0;
            foreach (Peer other in host.Peers.All)
            {
                if (other == joiner)
                {
                    continue;
                }
                var names = new List<NodeId> { other.Id };
                if (other.Aliases != null)
                {
                    names.AddRange(other.Aliases);
                }
                var entry = new MemberEntry(names.ToArray(), other.EndPoint);
                int size = MemberListBody.SizeOf(entry);
                if (current == null || bytes + size > MemberListBody.MaxEntryBytes || current.Count == byte.MaxValue)
                {
                    current = [];
                    segments.Add(current);
                    bytes = 0;
                }
                current.Add(entry);
                bytes += size;
            }
            SendInternal(hop, MessageClasses.JoinResult, new JoinResultBody { Result = JoinResult.Accepted, Suid = host.Membership.Suid, ListSegments = (byte)segments.Count }.Serialize());
            for (int index = 0; index < segments.Count; index++)
            {
                SendInternal(hop, MessageClasses.MemberList, new MemberListBody { Index = (byte)index, Entries = segments[index] }.Serialize());
            }
            host.Log(NetLogLevel.Network, "JFP2: " + joiner.Id + " is a member: JoinResult and " + segments.Count + " MemberList datagram(s) sent");
        }

        // ================================================================== both ends

        void HandleMembership(IPEndPoint from, PeerSession hop, byte messageClass, ReadOnlySpan<byte> payload)
        {
            if (hop.AgreedInternalVersion[messageClass] == 0)
            {
                host.Log(NetLogLevel.Network, "JFP2: internal class " + messageClass + " from " + from + " was never agreed on - ignored");
                return;
            }
            switch (messageClass)
            {
                case MessageClasses.Join:
                    HandleJoin(from, hop, payload);
                    break;
                case MessageClasses.JoinResult:
                    HandleJoinResult(hop, payload);
                    break;
                case MessageClasses.MemberList:
                    HandleMemberList(hop, payload);
                    break;
                case MessageClasses.MemberAnnounce:
                    HandleMemberAnnounce(hop, payload);
                    break;
                case MessageClasses.MemberLeave:
                    HandleMemberLeave(hop, payload);
                    break;
                case MessageClasses.RouteQuery:
                    HandleRouteQuery(hop, payload);
                    break;
                case MessageClasses.RouteAnswer:
                    HandleRouteAnswer(hop, payload);
                    break;
            }
        }

        /// <summary>The non-member session becomes the member session: it is the one datagrams for the neighbour go through from now on.</summary>
        void Promote(PeerSession session)
        {
            nonMembers.Remove(session.Peer);
            if (sessions.TryGetValue(session.Peer, out PeerSession old) && old != session)
            {
                sessionsById.Remove(old.LocalAssignedId);
            }
            double now = Now;
            session.NonMember = false;
            session.LastAck = now;
            session.NextKeepAlive = now + KeepAliveInterval;
            sessions[session.Peer] = session;
        }

        /// <summary>The names the neighbour stated besides the first, which names its session.</summary>
        static List<NodeId> OtherNames(PeerSession session) =>
            session.Names is { Count: > 1 } names ? names.GetRange(1, names.Count - 1) : [];

        void SendInternal(PeerSession session, byte messageClass, ReadOnlySpan<byte> payload) =>
            SendDatagram(session.Endpoint, EnvelopeFlags.Internal, messageClass, session.LocalAssignedId, session.RemoteAssignedId, payload);
    }
}
