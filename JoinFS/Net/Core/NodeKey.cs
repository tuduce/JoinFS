using System;
using System.Security.Cryptography;
using JoinFS.Net.Crypto;

namespace JoinFS.Net
{
    /// <summary>
    /// A node's P-256 key pair (docs/network/jfp2/standalone-ipv6-plan.md D3, §5.1). Immutable. The public key is the 64
    /// bytes the wire carries (X then Y, big-endian, no SEC1 prefix) and <see cref="Id"/> is the kind-2
    /// name derived from it. No wire carries either yet.
    /// </summary>
    public sealed class NodeKey
    {
        public const int PublicKeySize = NodeId.PublicKeySize;
        public const int PrivateKeySize = P256.PrivateKeySize;

        NodeKey(P256KeyPair pair)
        {
            Pair = pair;
            Id = NodeId.FromPublicKey(pair.PublicKey);
        }

        public NodeId Id { get; }

        /// <summary>The key pair, as the handshake takes it (<see cref="NoiseHandshake"/>).</summary>
        public P256KeyPair Pair { get; }

        public ReadOnlySpan<byte> PublicKey => Pair.PublicKey;

        /// <summary>The private scalar, for the key store only.</summary>
        public ReadOnlySpan<byte> PrivateKey => Pair.PrivateKey;

        public static NodeKey Generate() => new NodeKey(P256KeyPair.Generate());

        /// <summary>
        /// Rebuild a key pair from its stored halves. False when the scalar is not a valid private key or
        /// the public key is not its point (checked by key agreement, so on every platform).
        /// </summary>
        public static bool TryLoad(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKey, out NodeKey key)
        {
            key = null;
            if (!P256KeyPair.TryLoad(privateKey, publicKey, out P256KeyPair pair))
            {
                return false;
            }
            key = new NodeKey(pair);
            return true;
        }

        /// <summary>A key agreement object holding this key pair; the caller disposes it.</summary>
        public ECDiffieHellman CreateKeyAgreement() => ECDiffieHellman.Create(P256.Parameters(Pair.PrivateKey, Pair.PublicKey));
    }
}
