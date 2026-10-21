using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;

namespace JoinFS.Net.Jfp2
{
    // Join over JFP2: a node in a non-member session (protocol.md §5.9) asks to become a member with Join, is
    // answered with JoinResult and then told of the members with MemberList (§5.10). The membership itself
    // is the mesh's: this file reports what the wire brought through IMembership.
    public sealed partial class Jfp2Plugin
    {
        const double JoinRetryInterval = 2.0;
        const double JoinTimeout = 15.0;
        const int JoinMaxAttempts = 5;

        /// <summary>The join this node started and has not finished: Join is repeated until JoinResult and every MemberList datagram it announced have arrived.</summary>
        sealed class PendingJoin
        {
            public IPEndPoint Target;
            public uint PasswordHash;
            public double Deadline;
            /// <summary>The session with the target, once the cookie exchange has made it.</summary>
            public PeerSession Session;
            public int Attempts;
            public double NextSend;
            public bool Accepted;
            public int Segments;
            public readonly Dictionary<byte, List<MemberEntry>> Lists = [];
            public readonly HashSet<byte> Applied = [];
        }

        PendingJoin pendingJoin;

        /// <summary>For tests and diagnostics: a join started with <see cref="BeginJoin"/> is not finished.</summary>
        public bool JoinPending => pendingJoin != null;

        static bool IsMembershipClass(byte messageClass) => messageClass is MessageClasses.Join or MessageClasses.JoinResult or MessageClasses.MemberList;

        // ================================================================== asking (this node joins)

        public bool BeginJoin(IPEndPoint target, uint passwordHash)
        {
            target = Copy(target);
            if (OwnNames().Count == 0)
            {
                return false;
            }
            // a session left over from an earlier attempt has a cookie and ids that mean nothing to a fresh Join
            foreach (NodeId name in new List<NodeId>(nonMembers.Keys))
            {
                if (nonMembers[name].Endpoint != null && nonMembers[name].Endpoint.Equals(target))
                {
                    RemoveNonMember(name);
                }
            }
            pendingJoin = new PendingJoin { Target = target, PasswordHash = passwordHash, Deadline = Now + JoinTimeout };
            Introduce(target);
            host.Log(NetLogLevel.Network, "JFP2: joining the session of " + target);
            return true;
        }

        void DoJoin()
        {
            PendingJoin join = pendingJoin;
            if (join == null)
            {
                return;
            }
            double now = Now;
            if (now >= join.Deadline && !join.Accepted)
            {
                FailJoin(join, "no answer from " + join.Target + " within " + JoinTimeout + " s");
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
                    return;
                }
                if (join.Session.AgreedInternalVersion[MessageClasses.Join] == 0)
                {
                    FailJoin(join, join.Target + " does not take a join over JFP2");
                    return;
                }
            }
            if (now < join.NextSend)
            {
                return;
            }
            if (join.Attempts >= JoinMaxAttempts)
            {
                FailJoin(join, join.Accepted ? join.Target + " did not send all its members" : join.Target + " did not answer the Join");
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

        void FailJoin(PendingJoin join, string reason)
        {
            pendingJoin = null;
            host.Log(NetLogLevel.Event, "JFP2: join failed: " + reason);
        }

        void HandleJoinResult(PeerSession hop, ReadOnlySpan<byte> payload)
        {
            PendingJoin join = pendingJoin;
            if (join == null || join.Session != hop || !JoinResultBody.TryRead(payload, out JoinResultBody result))
            {
                host.Log(NetLogLevel.Network, "JFP2: JoinResult from " + hop.Peer + " answers no join of this node - ignored");
                return;
            }
            if (result.Result != JoinResult.Accepted)
            {
                pendingJoin = null;
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
            PendingJoin join = pendingJoin;
            if (join == null || join.Session != hop || !MemberListBody.TryRead(payload, out MemberListBody list))
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
                pendingJoin = null;
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
            Peer peer = host.Membership.Admit(joiner, hop.Endpoint, OtherNames(hop));
            if (peer == null)
            {
                host.Log(NetLogLevel.Network, "JFP2: join of " + joiner + " at " + from + " not taken: the mesh cannot add it");
                return;
            }
            Promote(hop);
            host.LinkChanged(joiner);
            AnswerJoin(hop, peer);
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
