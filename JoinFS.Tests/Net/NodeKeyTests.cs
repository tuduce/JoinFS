using System.Security.Cryptography;
using JoinFS.Net;

namespace JoinFS.Tests.Net
{
    /// <summary>The node's P-256 key pair (plan D3, §5.1).</summary>
    public class NodeKeyTests
    {
        [Fact]
        public void Generate_GivesAKeyPairWhoseIdIsDerivedFromThePublicKey()
        {
            NodeKey key = NodeKey.Generate();

            Assert.Equal(NodeKey.PublicKeySize, key.PublicKey.Length);
            Assert.Equal(NodeKey.PrivateKeySize, key.PrivateKey.Length);
            Assert.Equal(NodeId.FromPublicKey(key.PublicKey), key.Id);
            Assert.Equal(NodeId.KeyPairKind, key.Id.Kind);
            Assert.NotEqual(key.Id, NodeKey.Generate().Id);
        }

        [Fact]
        public void TryLoad_RoundTripsTheStoredHalves()
        {
            NodeKey key = NodeKey.Generate();

            Assert.True(NodeKey.TryLoad(key.PrivateKey, key.PublicKey, out NodeKey loaded));

            Assert.Equal(key.Id, loaded.Id);
            Assert.Equal(key.PublicKey.ToArray(), loaded.PublicKey.ToArray());
        }

        [Fact]
        public void TryLoad_RejectsAPublicKeyThatIsNotTheirs()
        {
            NodeKey a = NodeKey.Generate();
            NodeKey b = NodeKey.Generate();
            byte[] notOnTheCurve = a.PublicKey.ToArray();
            notOnTheCurve[40] ^= 1;

            Assert.False(NodeKey.TryLoad(a.PrivateKey, b.PublicKey, out _));
            Assert.False(NodeKey.TryLoad(a.PrivateKey, notOnTheCurve, out _));
            Assert.False(NodeKey.TryLoad(new byte[NodeKey.PrivateKeySize], a.PublicKey, out _));
            Assert.False(NodeKey.TryLoad(a.PrivateKey[..31], a.PublicKey, out _));
        }

        [Fact]
        public void KeyAgreement_TwoNodesDeriveOneSecret()
        {
            NodeKey a = NodeKey.Generate();
            NodeKey b = NodeKey.Generate();

            using ECDiffieHellman ecdhA = a.CreateKeyAgreement();
            using ECDiffieHellman ecdhB = b.CreateKeyAgreement();
            byte[] fromA = ecdhA.DeriveRawSecretAgreement(ecdhB.PublicKey);
            byte[] fromB = ecdhB.DeriveRawSecretAgreement(ecdhA.PublicKey);

            Assert.Equal(fromA, fromB);
        }
    }
}
