using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;

namespace JoinFS.Net.Jfp2
{
    // The bodies of the internal classes that carry membership: Join, JoinResult and MemberList
    // (docs/network/jfp2/protocol.md §5.10). They are not canonical messages: the plugin reads and writes
    // them itself and talks to the mesh through IMembership.

    /// <summary>Join (internal class 2, version 1): a node in a non-member session asks to become a member.</summary>
    public struct JoinBody
    {
        public const int ProofSize = 32;
        public const int WireSize = 1 + ProofSize;

        /// <summary>The joiner knows a password and sent a proof of it; otherwise <see cref="Proof"/> is zeros.</summary>
        public bool HasProof;

        /// <summary><c>HMAC-SHA256(LE32(password hash), "JFP2 Join" + cookie + first name)</c>, see <see cref="ComputeProof"/>.</summary>
        public byte[] Proof;

        static readonly byte[] Label = "JFP2 Join"u8.ToArray();

        /// <summary>
        /// The proof of knowing the session's password hash that a Join carries: the hash itself never travels.
        /// It is bound to the cookie the session was admitted with and to the name the joiner stated first, so a
        /// captured Join proves nothing on another session.
        /// </summary>
        public static byte[] ComputeProof(uint passwordHash, ReadOnlySpan<byte> cookie, NodeId joiner)
        {
            Span<byte> key = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(key, passwordHash);
            Span<byte> data = stackalloc byte[Label.Length + cookie.Length + NodeId.WireSize];
            Label.CopyTo(data);
            cookie.CopyTo(data[Label.Length..]);
            joiner.WriteTo(data[(Label.Length + cookie.Length)..]);
            return HMACSHA256.HashData(key, data);
        }

        public readonly byte[] Serialize()
        {
            byte[] bytes = new byte[WireSize];
            bytes[0] = (byte)(HasProof ? 1 : 0);
            if (HasProof && Proof != null)
            {
                Proof.AsSpan(0, ProofSize).CopyTo(bytes.AsSpan(1));
            }
            return bytes;
        }

        public static bool TryRead(ReadOnlySpan<byte> src, out JoinBody body)
        {
            body = default;
            if (src.Length < WireSize)
            {
                return false;
            }
            body.HasProof = (src[0] & 1) != 0;
            body.Proof = src.Slice(1, ProofSize).ToArray();
            return true;
        }
    }

    /// <summary>JoinResult (internal class 3, version 1): the admitting node's answer to a Join.</summary>
    public struct JoinResultBody
    {
        public const int WireSize = 1 + 4 + 1;

        public JoinResult Result;

        /// <summary>The session's id when accepted, else 0.</summary>
        public uint Suid;

        /// <summary>How many MemberList datagrams follow (0 when the joiner is alone with the admitting node).</summary>
        public byte ListSegments;

        public readonly byte[] Serialize()
        {
            byte[] bytes = new byte[WireSize];
            bytes[0] = (byte)Result;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1), Suid);
            bytes[5] = ListSegments;
            return bytes;
        }

        public static bool TryRead(ReadOnlySpan<byte> src, out JoinResultBody body)
        {
            body = default;
            if (src.Length < WireSize)
            {
                return false;
            }
            body.Result = (JoinResult)src[0];
            body.Suid = BinaryPrimitives.ReadUInt32LittleEndian(src[1..]);
            body.ListSegments = src[5];
            return true;
        }
    }

    /// <summary>A member as MemberList lists it: the names it is known by (preferred first) and where it is.</summary>
    public readonly record struct MemberEntry(NodeId[] Names, IPEndPoint EndPoint);

    /// <summary>MemberList (internal class 4, version 1): some of the members the admitting node knows, for a node that just joined.</summary>
    public struct MemberListBody
    {
        public const int MaxNames = 4;

        /// <summary>The most bytes of entries one MemberList carries, to keep the datagram under the payload limit (§3.6).</summary>
        public const int MaxEntryBytes = 1000;

        public byte Index;
        public List<MemberEntry> Entries;

        public static int SizeOf(MemberEntry entry) => 1 + Math.Min(entry.Names.Length, MaxNames) * NodeId.WireSize + 1 + WireEndPoint.SizeOf(entry.EndPoint);

        public readonly byte[] Serialize()
        {
            int size = 2;
            foreach (MemberEntry entry in Entries) size += SizeOf(entry);
            byte[] bytes = new byte[size];
            bytes[0] = Index;
            bytes[1] = (byte)Entries.Count;
            int at = 2;
            foreach (MemberEntry entry in Entries)
            {
                at += WriteEntry(entry, bytes.AsSpan(at));
            }
            return bytes;
        }

        /// <summary>Writes one entry (<see cref="SizeOf"/> bytes) and returns how many it wrote.</summary>
        public static int WriteEntry(MemberEntry entry, Span<byte> dest)
        {
            int at = 0;
            int names = Math.Min(entry.Names.Length, MaxNames);
            dest[at++] = (byte)names;
            for (int n = 0; n < names; n++)
            {
                entry.Names[n].WriteTo(dest.Slice(at, NodeId.WireSize));
                at += NodeId.WireSize;
            }
            int length = WireEndPoint.SizeOf(entry.EndPoint);
            dest[at++] = (byte)length;
            WireEndPoint.WriteTo(entry.EndPoint, dest.Slice(at, length));
            return at + length;
        }

        /// <summary>
        /// Reads one entry at <paramref name="at"/> and moves <paramref name="at"/> past it. False when the
        /// bytes end inside the entry (<paramref name="at"/> is then the end); true with a null
        /// <paramref name="entry"/> for an entry that is skipped, since its declared sizes tell where the next begins.
        /// </summary>
        public static bool TryReadEntry(ReadOnlySpan<byte> src, ref int at, out MemberEntry? entry)
        {
            entry = null;
            if (at >= src.Length)
            {
                return false;
            }
            int names = src[at++];
            if (at + names * NodeId.WireSize + 1 > src.Length)
            {
                at = src.Length;
                return false;
            }
            var list = new NodeId[names];
            for (int n = 0; n < names; n++)
            {
                list[n] = NodeId.ReadFrom(src.Slice(at, NodeId.WireSize));
                at += NodeId.WireSize;
            }
            int length = src[at++];
            if (at + length > src.Length)
            {
                at = src.Length;
                return false;
            }
            if (names > 0 && list[0].Valid() && WireEndPoint.TryReadFrom(src.Slice(at, length), out IPEndPoint endPoint))
            {
                entry = new MemberEntry(list, endPoint);
            }
            at += length;
            return true;
        }

        /// <summary>
        /// Reads a MemberList. An entry with no name this build can use, or an endpoint of an unassigned family,
        /// is skipped (its declared sizes tell where the next begins); a list cut short ends where it is cut.
        /// </summary>
        public static bool TryRead(ReadOnlySpan<byte> src, out MemberListBody body)
        {
            body = default;
            if (src.Length < 2)
            {
                return false;
            }
            body.Index = src[0];
            body.Entries = [];
            int count = src[1];
            int at = 2;
            for (int e = 0; e < count; e++)
            {
                if (!TryReadEntry(src, ref at, out MemberEntry? entry))
                {
                    break;
                }
                if (entry.HasValue)
                {
                    body.Entries.Add(entry.Value);
                }
            }
            return true;
        }
    }

    /// <summary>MemberAnnounce (internal class 5, version 1): a node that admitted a member tells its other neighbours of it. One member entry, as in MemberList.</summary>
    public struct MemberAnnounceBody
    {
        public MemberEntry Entry;

        public readonly byte[] Serialize()
        {
            byte[] bytes = new byte[MemberListBody.SizeOf(Entry)];
            MemberListBody.WriteEntry(Entry, bytes);
            return bytes;
        }

        /// <summary>False for an entry this build cannot use (no usable first name, an endpoint of an unassigned family) or a cut one.</summary>
        public static bool TryRead(ReadOnlySpan<byte> src, out MemberAnnounceBody body)
        {
            body = default;
            int at = 0;
            if (!MemberListBody.TryReadEntry(src, ref at, out MemberEntry? entry) || !entry.HasValue)
            {
                return false;
            }
            body.Entry = entry.Value;
            return true;
        }
    }

    /// <summary>MemberLeave (internal class 6, version 1): a member is gone. One name, 8 bytes.</summary>
    public struct MemberLeaveBody
    {
        public NodeId Name;

        public readonly byte[] Serialize()
        {
            byte[] bytes = new byte[NodeId.WireSize];
            Name.WriteTo(bytes);
            return bytes;
        }

        public static bool TryRead(ReadOnlySpan<byte> src, out MemberLeaveBody body)
        {
            body = default;
            if (src.Length < NodeId.WireSize)
            {
                return false;
            }
            body.Name = NodeId.ReadFrom(src[..NodeId.WireSize]);
            return body.Name.Valid();
        }
    }
}
