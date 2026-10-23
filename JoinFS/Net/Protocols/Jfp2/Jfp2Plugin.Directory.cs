using System;
using System.Net;

namespace JoinFS.Net.Jfp2
{
    // The directory over JFP2 (docs/network/jfp2/protocol.md §5.16): status, hub list, users, online and lookups, between a node and a hub
    // that are not members of one session. They travel over the non-member session of §5.9, which carries these classes and no others.
    public sealed partial class Jfp2Plugin : IDirectoryLink
    {
        /// <summary>The kinds a non-member session carries: the directory's (the classes Status and StatusRequest, 10 to 17).</summary>
        static bool IsDirectoryKind(MessageKind kind) => kind is MessageKind.Status or MessageKind.StatusRequest or MessageKind.HubList
            or MessageKind.UserListRequest or MessageKind.HubUser or MessageKind.UserPositionsRequest or MessageKind.UserPositions
            or MessageKind.Online or MessageKind.UserNuidRequest or MessageKind.UserNuid;

        /// <summary>The verified non-member session whose endpoint is <paramref name="endPoint"/>, or null.</summary>
        PeerSession NonMemberAt(IPEndPoint endPoint)
        {
            endPoint = WireEndPoint.Normalize(endPoint);
            foreach (PeerSession session in nonMembers.Values)
            {
                // a host with several IPv6 addresses answers from its preferred one, whichever was asked (protocol.md §5.14): the session is under
                // that address, and what was asked of another address of its network goes there
                if (session.Verified && session.HandshakeComplete && (session.Endpoint.Equals(endPoint) || SameSubnet6(session.Endpoint, endPoint)))
                {
                    return session;
                }
            }
            return null;
        }

        public bool HasLink(IPEndPoint endPoint) => NonMemberAt(endPoint) != null;

        bool IDirectoryLink.Introduce(IPEndPoint endPoint) => NonMemberAt(endPoint) == null && Introduce(WireEndPoint.Normalize(endPoint));

        public bool TrySend<T>(IPEndPoint endPoint, in T message) where T : struct, IMessage
        {
            if (!IsDirectoryKind(T.Kind) || profile.ForKind<T>(T.Kind) is not { } messageClass || NonMemberAt(endPoint) is not { } session)
            {
                return false;
            }
            byte version = session.AgreedAppVersion[messageClass.MessageClass];
            if (version == 0)
            {
                return false;
            }
            int length = messageClass.Codec(version).Encode(message, payloadBuffer);
            SendDatagram(session.Endpoint, EnvelopeFlags.None, messageClass.MessageClass, session.LocalAssignedId, session.RemoteAssignedId, payloadBuffer.AsSpan(0, length));
            return true;
        }

        /// <summary>
        /// An application datagram on a non-member session: only a directory class, straight from the stranger (never Forwarded). It is
        /// decoded and handed to the core with the stranger's first name as its sender and the endpoint it came from.
        /// </summary>
        void ReceiveDirectory(IPEndPoint from, PeerSession hop, in Envelope envelope, ReadOnlySpan<byte> payload)
        {
            ClassDescriptor messageClass = envelope.IsInternal || envelope.IsForwarded ? null : profile.ForClass(envelope.RawMessageClass);
            if (messageClass == null || !IsDirectoryKind(messageClass.Kind))
            {
                // a stranger that echoed its cookie carries nothing but the handshake, the join and the directory (protocol.md §5.9, §5.10, §5.16)
                host.Log(NetLogLevel.Network, "JFP2: class " + envelope.RawMessageClass + " from " + from + " on a non-member session - dropped");
                return;
            }
            byte version = hop.AgreedAppVersion[messageClass.MessageClass];
            if (version == 0)
            {
                host.Log(NetLogLevel.Network, "JFP2: class " + envelope.RawMessageClass + " from " + hop.Peer + " was never agreed on - ignored");
                return;
            }
            var meta = new MessageMeta { Sender = hop.Peer, Recipient = Local, EndPoint = from };
            host.Log(NetLogLevel.Network, "JFP2: " + messageClass.Kind + " from " + hop.Peer + " at " + from + " (directory)");
            Decode(meta, envelope.RawMessageClass, version, payload);
        }
    }
}
