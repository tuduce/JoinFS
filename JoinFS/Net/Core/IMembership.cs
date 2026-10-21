using System;
using System.Collections.Generic;
using System.Net;
using JoinFS.Net.Jfp2;

namespace JoinFS.Net
{
    /// <summary>
    /// The session's membership as a protocol plugin sees it: who may join, who is a member, which neighbour
    /// is alive. Membership is canonical (<see cref="MeshManager"/>); a plugin reports what it learns over
    /// its wire through this interface and never touches <see cref="PeerDirectory"/> to add a member
    /// itself, so a member learned over any protocol is one <see cref="Peer"/>. Network thread only.
    /// </summary>
    public interface IMembership
    {
        /// <summary>This node is in a session and takes joiners.</summary>
        bool Admitting { get; }

        /// <summary>The id of the session this node is in, 0 when it is in none.</summary>
        uint Suid { get; }

        /// <summary>
        /// Whether <paramref name="joiner"/> may join, by the rules of any join: the session's password
        /// (<paramref name="passwordMatches"/> is asked once with the session's password hash, only when
        /// there is one) and whether it asks for a login.
        /// </summary>
        JoinResult Judge(Func<uint, bool> passwordMatches);

        /// <summary>
        /// <paramref name="joiner"/> is a member now, linked over JFP2 only (no legacy datagram goes to it).
        /// Registers the peer, tells the app and returns it; null when the node cannot be added (too many
        /// nodes on its device). A node that is a member already (it rejoins, or was learned through another
        /// member) is the same peer: it becomes a direct neighbour at <paramref name="endPoint"/>, linked over
        /// JFP2 only, and its expiry is renewed; <paramref name="isNew"/> tells which of the two it was.
        /// </summary>
        Peer Admit(NodeId joiner, IPEndPoint endPoint, IReadOnlyList<NodeId> otherNames, out bool isNew);

        /// <summary>
        /// The node this one asked to join accepted it: the session is joined, and <paramref name="admitter"/>
        /// is the first neighbour. Null when no join is under way or the session is another.
        /// </summary>
        Peer Joined(uint suid, NodeId admitter, IPEndPoint endPoint, IReadOnlyList<NodeId> otherNames);

        /// <summary>The node this one asked to join refused it (what the app reads as <see cref="MeshManager.ActiveJoinResult"/>).</summary>
        void JoinRefused(JoinResult result);

        /// <summary>
        /// The join over JFP2 of <paramref name="target"/> did not happen (no answer, or it takes none) and was
        /// started with the legacy fallback: join it over legacy instead, and remember that it speaks nothing else.
        /// </summary>
        void JoinFailedOverJfp2(IPEndPoint target, uint passwordHash);

        /// <summary>
        /// Members the neighbour <paramref name="via"/> told this node of, reached through it until a direct
        /// link exists. A member already known by any of its names is left as it is.
        /// </summary>
        void LearnMembers(NodeId via, IReadOnlyList<MemberEntry> members);

        /// <summary>
        /// A neighbour reported that <paramref name="member"/> left the session. The member is dropped as if it had
        /// expired when the neighbour is the member itself or the member is reached through it; any other report is
        /// ignored. False when it was not acted on.
        /// </summary>
        bool Left(NodeId neighbor, NodeId member);

        /// <summary>
        /// <paramref name="neighbor"/> answered over a link that is not legacy's: it, and the members reached
        /// through it, live for another expiry time (legacy refreshes a peer with its pulses).
        /// </summary>
        void Alive(NodeId neighbor);
    }

    /// <summary>A protocol that can carry the session's membership (docs/network/jfp2/standalone-ipv6-plan.md §5.3).</summary>
    public interface IMembershipExpression
    {
        /// <summary>
        /// Start joining the session of the node at <paramref name="target"/> over this protocol. False when it
        /// cannot (nothing to state as a name, or no way to reach the node). With
        /// <paramref name="legacyFallback"/> the attempt is given up after a short silence, or when the node takes no
        /// join over this protocol, and reported through <see cref="IMembership.JoinFailedOverJfp2"/> so the mesh
        /// joins over legacy; a refusal is final either way.
        /// </summary>
        bool BeginJoin(IPEndPoint target, uint passwordHash, bool legacyFallback);

        /// <summary>This node leaves the session: tell the neighbours this protocol holds as members, before its state is reset.</summary>
        void AnnounceLeaving();
    }
}
