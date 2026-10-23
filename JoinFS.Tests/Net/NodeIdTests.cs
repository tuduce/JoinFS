using System.Net;
using JoinFS.Net;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// NodeId is the 8-byte name (docs/network/jfp2/design-node-identity.md §1.3); a kind-0 id holds a
    /// legacy node id and converts both ways without a table.
    /// </summary>
    public class NodeIdTests
    {
        static NodeId Name(byte kind, params byte[] value)
        {
            byte[] wire = new byte[NodeId.WireSize];
            wire[0] = kind;
            value.CopyTo(wire, 1);
            return NodeId.ReadFrom(wire);
        }

        [Fact]
        public void Legacy_RoundTripsThroughKindZero()
        {
            NodeId id = NodeId.FromLegacy(0xCB007101, 6112, 20);

            Assert.Equal(NodeId.LegacyKind, id.Kind);
            Assert.True(id.TryGetLegacy(out LegacyNodeId legacy));
            Assert.Equal(new LegacyNodeId(0xCB007101, 6112, 20), legacy);
            Assert.Equal(id, NodeId.FromLegacy(legacy));
        }

        [Fact]
        public void Legacy_FromAddressAndEndPointMatchesTheNumbers()
        {
            NodeId expected = NodeId.FromLegacy(0xC6336402, 6112, 2);

            Assert.Equal(expected, NodeId.FromLegacy(IPAddress.Parse("198.51.100.2"), 6112, 2));
            Assert.Equal(expected, NodeId.FromLegacy(new IPEndPoint(IPAddress.Parse("198.51.100.2"), 6112), 2));
        }

        [Fact]
        public void Wire_IsKindThenTheLegacyLayout()
        {
            byte[] wire = new byte[NodeId.WireSize];

            NodeId.FromLegacy(0xCB007101, 6112, 20).WriteTo(wire);

            Assert.Equal(new byte[] { 0, 0x01, 0x71, 0x00, 0xCB, 0xE0, 0x17, 20 }, wire);
            Assert.Equal(NodeId.FromLegacy(0xCB007101, 6112, 20), NodeId.ReadFrom(wire));
        }

        [Fact]
        public void Valid_NeedsABitAndForKindZeroAnAddress()
        {
            Assert.False(default(NodeId).Valid());
            Assert.True(default(NodeId).IsNone);
            Assert.False(NodeId.FromLegacy(0, 6112, 1).Valid());
            Assert.False(NodeId.FromLegacy(0, 6112, 1).IsNone);
            Assert.True(NodeId.FromLegacy(0x0A000001, 0, 0).Valid());
            Assert.True(Name(2, 1, 2, 3, 4, 5, 6, 7).Valid());
            Assert.True(Name(0).Invalid());
        }

        [Fact]
        public void OtherKinds_HaveNoLegacyId()
        {
            NodeId key = Name(2, 1, 2, 3, 4, 5, 6, 7);

            Assert.False(key.TryGetLegacy(out LegacyNodeId legacy));
            Assert.Equal(default, legacy);
        }

        [Fact]
        public void Equality_IsTheEightBytes_WhateverTheKind()
        {
            Assert.Equal(Name(2, 1, 2, 3, 4, 5, 6, 7), Name(2, 1, 2, 3, 4, 5, 6, 7));
            Assert.Equal(Name(2, 1, 2, 3, 4, 5, 6, 7).GetHashCode(), Name(2, 1, 2, 3, 4, 5, 6, 7).GetHashCode());
            Assert.NotEqual(Name(2, 1, 2, 3, 4, 5, 6, 7), Name(1, 1, 2, 3, 4, 5, 6, 7));
            Assert.NotEqual(NodeId.FromLegacy(0x0A000001, 6112, 1), NodeId.FromLegacy(0x0A000001, 6113, 1));
            Assert.True(NodeId.FromLegacy(0x0A000001, 6112, 1) == NodeId.FromLegacy(0x0A000001, 6112, 1));
        }

        [Fact]
        public void ToString_KindZeroAsTheLegacyId_KindTwoAsK2_OthersAsKindAndHex()
        {
            LegacyNodeId legacy = new(0xCB007101, 6112, 20);

            Assert.Equal(legacy.ToString(), NodeId.FromLegacy(legacy).ToString());
            Assert.Equal("k2:01020304050607", Name(2, 1, 2, 3, 4, 5, 6, 7).ToString());
            Assert.Equal("kind 255 AABBCCDDEEFF11", Name(255, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11).ToString());
            Assert.Equal("kind 1 AABBCCDDEEFF11", Name(1, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11).ToString());
        }

        [Fact]
        public void SameDevice_LegacyComparesAddressAndLanOctet_OthersOnlyThemselves()
        {
            NodeId a = NodeId.FromLegacy(0x0A000001, 6112, 5);

            Assert.True(NodeId.SameDevice(a, NodeId.FromLegacy(0x0A000001, 6200, 5)));
            Assert.False(NodeId.SameDevice(a, NodeId.FromLegacy(0x0A000001, 6112, 6)));
            Assert.False(NodeId.SameDevice(a, NodeId.FromLegacy(0x0A000002, 6112, 5)));

            NodeId key = Name(2, 1, 2, 3, 4, 5, 6, 7);
            Assert.True(NodeId.SameDevice(key, key));
            Assert.False(NodeId.SameDevice(key, Name(2, 1, 2, 3, 4, 5, 6, 8)));
            Assert.False(NodeId.SameDevice(key, a));
        }

        [Fact]
        public void FromPublicKey_IsKindTwoThenTheFirstSevenBytesOfSha256()
        {
            byte[] key = Enumerable.Range(0, NodeId.PublicKeySize).Select(i => (byte)i).ToArray();
            byte[] hash = System.Security.Cryptography.SHA256.HashData(key);

            NodeId id = NodeId.FromPublicKey(key);

            byte[] wire = new byte[NodeId.WireSize];
            id.WriteTo(wire);
            Assert.Equal(NodeId.KeyPairKind, id.Kind);
            Assert.Equal(hash[..7], wire[1..]);
            Assert.True(id.Valid());
        }

        [Fact]
        public void FromPublicKey_WrongLength_Throws()
        {
            Assert.Throws<ArgumentException>(() => NodeId.FromPublicKey(new byte[33]));
        }
    }
}
