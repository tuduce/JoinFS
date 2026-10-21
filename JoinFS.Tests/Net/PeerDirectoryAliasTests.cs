using System.Net;
using JoinFS.Net;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// A peer's id is the name it was first learned by; every other name is an alias that resolves to
    /// it (docs/network/jfp2/design-node-identity.md §1.2). Nothing is ever re-keyed.
    /// </summary>
    public class PeerDirectoryAliasTests
    {
        static readonly IPEndPoint EndPoint = new(IPAddress.Parse("198.51.100.2"), 6112);
        static readonly NodeId Legacy = NodeId.FromLegacy(0xC6336402, 6112, 2);
        static readonly NodeId Other = NodeId.FromLegacy(0xC6336403, 6112, 3);
        static readonly NodeId Key = NodeId.ReadFrom([2, 1, 2, 3, 4, 5, 6, 7]);
        static readonly NodeId OtherKey = NodeId.ReadFrom([2, 7, 6, 5, 4, 3, 2, 1]);

        static PeerDirectory WithPeers(params NodeId[] ids)
        {
            var peers = new PeerDirectory();
            foreach (NodeId id in ids) peers.Add(id, EndPoint, true);
            return peers;
        }

        [Fact]
        public void APeerIdResolvesToItself()
        {
            PeerDirectory peers = WithPeers(Legacy);

            Assert.True(peers.TryResolve(Legacy, out NodeId id));
            Assert.Equal(Legacy, id);
        }

        [Fact]
        public void AnAliasResolvesToThePeerItWasAddedFor()
        {
            PeerDirectory peers = WithPeers(Legacy, Other);

            Assert.True(peers.AddAlias(Key, Legacy));

            Assert.True(peers.TryResolve(Key, out NodeId id));
            Assert.Equal(Legacy, id);
            Assert.Equal(Legacy, peers.Resolve(Key));
            Assert.False(peers.Contains(Key));
            Assert.Equal(2, peers.Count);
        }

        [Fact]
        public void AnUnknownNameResolvesToNothingAndResolveLeavesItAlone()
        {
            PeerDirectory peers = WithPeers(Legacy);

            Assert.False(peers.TryResolve(Key, out _));
            Assert.Equal(Key, peers.Resolve(Key));
        }

        [Fact]
        public void AliasIsRefusedForAnUnknownPeerAnInvalidNameOrANameInUse()
        {
            PeerDirectory peers = WithPeers(Legacy, Other);
            Assert.True(peers.AddAlias(Key, Legacy));

            Assert.False(peers.AddAlias(OtherKey, NodeId.FromLegacy(0xC6336409, 6112, 9)));
            Assert.False(peers.AddAlias(default, Legacy));
            Assert.False(peers.AddAlias(NodeId.FromLegacy(0, 6112, 1), Legacy));
            Assert.False(peers.AddAlias(Other, Legacy)); // another peer's id
            Assert.False(peers.AddAlias(Key, Other)); // another peer's alias is never taken over
            Assert.False(peers.AddAlias(Key, Legacy)); // nothing to do

            Assert.Equal(Legacy, peers.Resolve(Key));
            Assert.Equal(Other, peers.Resolve(Other));
        }

        [Fact]
        public void RemovingAPeerRemovesItsAliases()
        {
            PeerDirectory peers = WithPeers(Legacy, Other);
            peers.AddAlias(Key, Legacy);
            peers.AddAlias(OtherKey, Other);

            Assert.True(peers.Remove(Legacy));

            Assert.False(peers.TryResolve(Key, out _));
            Assert.Equal(Other, peers.Resolve(OtherKey));
            // the name is free again
            peers.Add(Legacy, EndPoint, true);
            Assert.True(peers.AddAlias(Key, Legacy));
        }

        [Fact]
        public void ClearForgetsEveryAlias()
        {
            PeerDirectory peers = WithPeers(Legacy);
            peers.AddAlias(Key, Legacy);

            peers.Clear();

            Assert.False(peers.TryResolve(Key, out _));
            Assert.False(peers.TryResolve(Legacy, out _));
        }

        [Fact]
        public void ALegacyIdIsItsOwnOrTheKindZeroAliasOfItsPeer()
        {
            PeerDirectory peers = WithPeers(Key, OtherKey);
            peers.AddAlias(Legacy, Key);

            Assert.True(peers.TryGetLegacy(Legacy, out LegacyNodeId own));
            Assert.Equal(new LegacyNodeId(0xC6336402, 6112, 2), own);
            Assert.True(peers.TryGetLegacy(Key, out LegacyNodeId viaAlias));
            Assert.Equal(own, viaAlias);
            Assert.False(peers.TryGetLegacy(OtherKey, out _));
            Assert.False(peers.TryGetLegacy(NodeId.ReadFrom([2, 9, 9, 9, 9, 9, 9, 9]), out _));
        }

        [Fact]
        public void AKeyedPeerIsItsOwnDeviceForTheDeviceLimit()
        {
            PeerDirectory peers = WithPeers(Legacy, Key);

            Assert.Equal(1, peers.CountSameDevice(Key));
            Assert.Equal(1, peers.CountSameDevice(Legacy));
        }
    }
}
