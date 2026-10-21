using System;
using System.Collections.Generic;
using System.Net;

namespace JoinFS.Net.Jfp2
{
    // Strangers: a node in a session answers a Hello from a node it does not know with a cookie, and a
    // Hello that echoes the cookie opens a non-member session. The first JFP2 conversation between two
    // nodes that have not met through legacy (docs/network/jfp2/protocol.md §5.9).
    public sealed partial class Jfp2Plugin
    {
        /// <summary>A conversation this node started with a stranger: asking for a cookie, then echoing it.</summary>
        sealed class Introduction
        {
            public ushort Id;
            public IPEndPoint EndPoint;
            /// <summary>The cookie to echo, null while the first Hello is still unanswered.</summary>
            public byte[] Cookie;
            public int Attempts;
            public double NextAttempt;
        }

        readonly List<NodeId> expired = [];
        readonly List<Introduction> due = [];

        /// <summary>For tests and diagnostics: non-member sessions open now.</summary>
        public int NonMemberCount => nonMembers.Count;

        /// <summary>For tests and diagnostics: introductions started with <see cref="Introduce"/> and not yet finished or given up.</summary>
        public int IntroductionCount => introductions.Count;

        /// <summary>For tests and diagnostics: the stranger that stated this name has a verified non-member session here.</summary>
        public bool HasNonMemberSession(NodeId name) => nonMembers.TryGetValue(name, out PeerSession session) && session.Verified;

        /// <summary>For tests and diagnostics: the endpoint of the non-member session with the stranger that stated this name, or null.</summary>
        public IPEndPoint NonMemberEndPoint(NodeId name) => nonMembers.TryGetValue(name, out PeerSession session) ? session.Endpoint : null;

        /// <summary>This node's names for a stranger: its legacy id when it has an IPv4 address, then its key name.</summary>
        List<NodeId> OwnNames()
        {
            var names = new List<NodeId>(2);
            if (Local.Valid())
            {
                names.Add(Local);
            }
            if (host.Identity.KeyId.Valid())
            {
                names.Add(host.Identity.KeyId);
            }
            return names;
        }

        /// <summary>What a cookie is bound to besides the source: the names the stranger stated (at most four), as bytes.</summary>
        static byte[] NamesContext(List<NodeId> names)
        {
            int count = Math.Min(names.Count, 4);
            var context = new byte[count * NodeId.WireSize];
            for (int n = 0; n < count; n++)
            {
                names[n].WriteTo(context.AsSpan(n * NodeId.WireSize, NodeId.WireSize));
            }
            return context;
        }

        // ================================================================== asking (this node is the stranger)

        /// <summary>
        /// Start a JFP2 conversation with the node at <paramref name="target"/>, which this node has not met
        /// through legacy: a Hello that asks for admission, then the Hello that echoes the cookie it is given. On
        /// success this node holds a verified non-member session with it (<see cref="HasNonMemberSession"/>).
        /// False when this node has no name to state or already has the endpoint under way.
        /// </summary>
        public bool Introduce(IPEndPoint target)
        {
            target = Copy(target);
            if (OwnNames().Count == 0)
            {
                return false;
            }
            foreach (Introduction other in introductions.Values)
            {
                if (other.EndPoint.Equals(target))
                {
                    return false;
                }
            }
            var introduction = new Introduction { Id = NewId(), EndPoint = target };
            introductions[introduction.Id] = introduction;
            SendIntroduction(introduction);
            return true;
        }

        /// <summary>Two IPv6 endpoints of one network: the same port and the same /64 (an interface can have several addresses in it).</summary>
        static bool SameSubnet6(IPEndPoint a, IPEndPoint b)
        {
            if (!IsV6(a) || !IsV6(b) || a.Port != b.Port)
            {
                return false;
            }
            Span<byte> x = stackalloc byte[16];
            Span<byte> y = stackalloc byte[16];
            a.Address.TryWriteBytes(x, out _);
            b.Address.TryWriteBytes(y, out _);
            return x[..8].SequenceEqual(y[..8]);
        }

        void SendIntroduction(Introduction introduction)
        {
            introduction.Attempts++;
            introduction.NextAttempt = Now + HelloRetryInterval;
            HandshakeMessage hello = MakeHandshake(new PeerSession { LocalAssignedId = introduction.Id }, 0, OwnNames());
            hello.Admission = introduction.Cookie ?? [];
            SendDatagram(introduction.EndPoint, EnvelopeFlags.Internal, MessageClasses.Hello, introduction.Id, 0, hello.Serialize());
            host.Log(NetLogLevel.Network, "JFP2: Send Hello to stranger at " + introduction.EndPoint + (introduction.Cookie == null ? " asking for admission" : " with its cookie") + " (attempt " + introduction.Attempts + ")");
        }

        /// <summary>The answer to an introduction's Hello: a cookie, or the non-member session.</summary>
        void HandleIntroductionAnswer(IPEndPoint from, Introduction introduction, HandshakeMessage ack)
        {
            // the id alone is 16 bits and guessable off the path: it counts from the endpoint asked, or from another address of the same
            // IPv6 /64 and port (a host with several addresses answers from its preferred one, whichever was asked: protocol.md §5.14)
            if (!from.Equals(introduction.EndPoint))
            {
                if (!SameSubnet6(from, introduction.EndPoint))
                {
                    host.Log(NetLogLevel.Network, "JFP2: HelloAck from " + from + " is not from " + introduction.EndPoint + ", where the stranger Hello went - ignored");
                    return;
                }
                host.Log(NetLogLevel.Event, "JFP2: " + introduction.EndPoint + " answered from " + from + ", another address of its network - going on there");
                IPEndPoint asked = introduction.EndPoint;
                foreach (PendingJoin pending in pendingJoins)
                {
                    int at = pending.Targets.IndexOf(asked);
                    if (at >= 0)
                    {
                        pending.Targets[at] = from;
                    }
                    if (asked.Equals(pending.Target))
                    {
                        pending.Target = from;
                    }
                }
                introduction.EndPoint = from;
            }
            double now = Now;
            if (ack.Result == HandshakeMessage.ResultNotAdmitted)
            {
                // a cookie, and the endpoint it saw us at: an answer without both is not one (§5.9)
                if (ack.Admission == null || ack.Admission.Length != HandshakeMessage.AdmissionCookieSize || ack.ObservedEndPoint == null)
                {
                    host.Log(NetLogLevel.Network, "JFP2: " + from + " answered Result 2 without a cookie and an observed endpoint - ignored");
                    return;
                }
                if (introduction.Cookie != null && introduction.Attempts >= IntroduceMaxAttempts)
                {
                    return; // the retry timer ends it; a loop of new cookies does not get another go
                }
                // the first address of a join to give a cookie is the one the join goes on at: the node would otherwise hold one non-member
                // session for this node with the ids and the endpoint of whichever echo came last
                PendingJoin asking = pendingJoins.Find(j => j.Targets.Contains(from));
                if (asking != null && !CommitTo(asking, from))
                {
                    introductions.Remove(introduction.Id);
                    host.Log(NetLogLevel.Network, "JFP2: " + from + " (" + Family(from) + ") gave a cookie after " + asking.Committed + " (" + Family(asking.Committed) + ") did - not used");
                    return;
                }
                host.Log(NetLogLevel.Network, "JFP2: " + from + " sees this node at " + ack.ObservedEndPoint + " and gave it a cookie");
                introduction.Cookie = ack.Admission;
                SendIntroduction(introduction);
                return;
            }
            if (ack.Result != HandshakeMessage.ResultAccepted || introduction.Cookie == null || ack.Names.Count == 0 || !ack.Names[0].Valid())
            {
                host.Log(NetLogLevel.Network, "JFP2: " + from + " answered the stranger Hello with Result " + ack.Result + (ack.Names.Count == 0 ? " and no name" : "") + " - not admitted");
                introductions.Remove(introduction.Id);
                return;
            }
            introductions.Remove(introduction.Id);
            NodeId name = ack.Names[0];
            // the join this conversation is for has an answer already, from another of the node's addresses: the first to answer is the one joined
            PendingJoin join = pendingJoins.Find(j => j.Targets.Contains(from));
            if (join?.Session != null)
            {
                host.Log(NetLogLevel.Network, "JFP2: " + name + " at " + from + " (" + Family(from) + ") also admitted this node, after " + join.Target + " (" + Family(join.Target) + ") - not used");
                return;
            }
            RemoveNonMember(name);
            var session = new PeerSession
            {
                Peer = name, LocalAssignedId = introduction.Id, RemoteAssignedId = ack.SelfAssignedId,
                NonMember = true, NonMemberUntil = now + NonMemberLifetime, Names = ack.Names, AdmissionCookie = introduction.Cookie,
                Candidates = ack.Candidates,
            };
            OpenNonMember(session, name, from, ack.Capabilities, ack.Offers, ack.Build, now);
            host.Log(NetLogLevel.Event, "JFP2: " + name + " at " + from + " (" + Family(from) + ") admitted this node: non-member session");
            if (join != null)
            {
                ChooseTarget(join, session);
            }
        }

        // ================================================================== answering (this node admits)

        /// <summary>
        /// A Hello with an Admission extension from a node that is not a known peer. Empty, or a cookie that is
        /// not ours (expired, or forged): answer with a cookie and nothing else, keeping no state (§5.9). A
        /// cookie that is ours, for this source and these names: open a non-member session and answer as for any
        /// Hello. Only a node in a session answers; every other node is silent, as a released build is.
        /// </summary>
        void HandleAdmission(IPEndPoint from, HandshakeMessage hello, int datagramLength)
        {
            if (!host.Connected)
            {
                host.Log(NetLogLevel.Network, "JFP2: stranger Hello from " + from + " - this node is in no session, ignored");
                return;
            }
            if (hello.Names.Count == 0 || !hello.Names[0].Valid())
            {
                host.Log(NetLogLevel.Network, "JFP2: stranger Hello from " + from + " does not say who it is - ignored");
                return;
            }
            byte[] context = NamesContext(hello.Names);
            double now = Now;
            if (hello.Admission.Length == HandshakeMessage.AdmissionCookieSize && cookies.Validate(now, from, context, hello.Admission))
            {
                Admit(from, hello, now);
                return;
            }
            Challenge(from, hello, context, datagramLength, now);
        }

        /// <summary>Answer a stranger with Result 2, no offers, the endpoint it came from and a cookie: never larger than its Hello, rate-limited, no state.</summary>
        void Challenge(IPEndPoint from, HandshakeMessage hello, byte[] context, int datagramLength, double now)
        {
            if (!TakeAdmissionToken(now))
            {
                return;
            }
            Span<byte> cookie = stackalloc byte[HandshakeMessage.AdmissionCookieSize];
            cookies.Issue(now, from, context, cookie);
            var ack = new HandshakeMessage
            {
                ProtoMajorMin = Envelope.ProtoMajor, ProtoMajorMax = Envelope.ProtoMajor, Capabilities = profile.Capabilities,
                Result = HandshakeMessage.ResultNotAdmitted, Names = OwnNames(), ObservedEndPoint = from, Admission = cookie.ToArray(),
            };
            byte[] payload = ack.Serialize();
            if (new Envelope(EnvelopeFlags.Internal, 0, hello.SelfAssignedId, MessageClasses.HelloAck).WireSize + payload.Length > datagramLength)
            {
                // a stranger's Hello that is smaller than the answer would turn this node into an amplifier
                host.Log(NetLogLevel.Network, "JFP2: stranger Hello from " + from + " is smaller than the answer - ignored");
                return;
            }
            SendDatagram(from, EnvelopeFlags.Internal, MessageClasses.HelloAck, 0, hello.SelfAssignedId, payload);
            host.Log(NetLogLevel.Network, "JFP2: stranger Hello from " + from + " answered with a cookie (Result 2)");
        }

        void Admit(IPEndPoint from, HandshakeMessage hello, double now)
        {
            NodeId name = hello.Names[0];
            if (!nonMembers.TryGetValue(name, out PeerSession session))
            {
                if (nonMembers.Count >= MaxNonMembers)
                {
                    host.Log(NetLogLevel.Network, "JFP2: stranger " + name + " at " + from + " not admitted: " + MaxNonMembers + " non-member sessions are open");
                    return;
                }
                session = new PeerSession
                {
                    Peer = name, LocalAssignedId = NewId(), RemoteAssignedId = hello.SelfAssignedId,
                    NonMember = true, NonMemberUntil = now + NonMemberLifetime, Names = hello.Names, AdmissionCookie = hello.Admission,
                    Candidates = hello.Candidates,
                };
                OpenNonMember(session, name, from, hello.Capabilities, hello.Offers, hello.Build, now);
                host.Log(NetLogLevel.Event, "JFP2: admitted stranger " + name + " at " + from + ": non-member session for " + NonMemberLifetime + " s");
            }
            else
            {
                // a retransmission, or the same node from another endpoint (its cookie proved that one answers); the lifetime is not extended
                session.RemoteAssignedId = hello.SelfAssignedId;
                session.Endpoint = from;
                session.AdmissionCookie = hello.Admission;
                session.Candidates = hello.Candidates ?? session.Candidates;
                session.LastAck = now;
                LearnBuild(session, hello.Build);
            }
            SendHelloAck(from, session, HandshakeMessage.ResultAccepted, OwnNames());
        }

        /// <summary>Both ends of a cookie exchange finish here: the session is verified, because the cookie (or the answer) came back from <paramref name="endPoint"/>.</summary>
        void OpenNonMember(PeerSession session, NodeId name, IPEndPoint endPoint, ulong capabilities, List<SchemaOffer> offers, string peerBuild, double now)
        {
            Negotiator.Resolve(session, profile.Capabilities, profile.Offers, capabilities, offers);
            session.HandshakeComplete = true;
            session.Endpoint = endPoint;
            session.LastAck = now;
            LearnBuild(session, peerBuild);
            nonMembers[name] = session;
            sessionsById[session.LocalAssignedId] = session;
        }

        void RemoveNonMember(NodeId name)
        {
            if (nonMembers.Remove(name, out PeerSession session))
            {
                sessionsById.Remove(session.LocalAssignedId);
            }
        }

        bool TakeAdmissionToken(double now)
        {
            admissionTokens = Math.Min(AdmissionBurst, admissionTokens + Math.Max(0, now - admissionTokensAt) * AdmissionRate);
            admissionTokensAt = now;
            if (admissionTokens < 1)
            {
                return false;
            }
            admissionTokens--;
            return true;
        }

        // ================================================================== periodic

        void DoAdmission()
        {
            if (nonMembers.Count == 0 && introductions.Count == 0)
            {
                return;
            }
            double now = Now;
            expired.Clear();
            foreach (var (name, session) in nonMembers)
            {
                if (now >= session.NonMemberUntil)
                {
                    expired.Add(name);
                }
            }
            foreach (NodeId name in expired)
            {
                RemoveNonMember(name);
                host.Log(NetLogLevel.Network, "JFP2: non-member session with " + name + " ended (no Join within " + NonMemberLifetime + " s)");
            }
            due.Clear();
            foreach (Introduction introduction in introductions.Values)
            {
                if (now >= introduction.NextAttempt)
                {
                    due.Add(introduction);
                }
            }
            foreach (Introduction introduction in due)
            {
                if (introduction.Attempts >= IntroduceMaxAttempts)
                {
                    introductions.Remove(introduction.Id);
                    host.Log(NetLogLevel.Event, "JFP2: " + introduction.EndPoint + " did not admit this node after " + introduction.Attempts + " attempts (a released build ignores a stranger's Hello)");
                }
                else
                {
                    SendIntroduction(introduction);
                }
            }
        }
    }
}
