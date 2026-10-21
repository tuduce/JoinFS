using System;
using System.Security.Cryptography;

namespace JoinFS.Net.Crypto
{
    /// <summary>
    /// A P-256 key pair: the 32-byte private scalar and the 64-byte public key of the wire. Immutable.
    /// Used for a node's static key and for the handshake's ephemeral keys.
    /// </summary>
    public sealed class P256KeyPair
    {
        readonly byte[] privateKey;
        readonly byte[] publicKey;

        P256KeyPair(byte[] privateKey, byte[] publicKey)
        {
            this.privateKey = privateKey;
            this.publicKey = publicKey;
        }

        public ReadOnlySpan<byte> PublicKey => publicKey;

        /// <summary>The private scalar, for the key store only.</summary>
        public ReadOnlySpan<byte> PrivateKey => privateKey;

        public static P256KeyPair Generate()
        {
            using ECDiffieHellman ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            ECParameters parameters = ecdh.ExportParameters(true);
            byte[] pub = new byte[P256.PublicKeySize];
            parameters.Q.X.CopyTo(pub, 0);
            parameters.Q.Y.CopyTo(pub, P256.PublicKeySize / 2);
            return new P256KeyPair(parameters.D, pub);
        }

        /// <summary>
        /// Rebuild a key pair from its stored halves. False when the scalar is not a valid private key, the
        /// public key is not a point of the curve, or the public key is not the scalar's. The last check is
        /// done by key agreement, because OpenSSL accepts a mismatched pair on import and only Windows
        /// refuses it.
        /// </summary>
        public static bool TryLoad(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKey, out P256KeyPair pair)
        {
            pair = null;
            if (!P256.IsValidPrivateKey(privateKey) || !P256.IsValidPublicKey(publicKey))
            {
                return false;
            }
            try
            {
                P256KeyPair candidate = new P256KeyPair(privateKey.ToArray(), publicKey.ToArray());
                if (!candidate.Matches())
                {
                    return false;
                }
                pair = candidate;
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        /// <summary>
        /// ECDH with a peer's public key; writes the 32-byte shared secret. Throws
        /// <see cref="CryptographicException"/> when the peer key is not a point of the curve.
        /// </summary>
        public void Agree(ReadOnlySpan<byte> remotePublicKey, Span<byte> secret)
        {
            if (!P256.IsValidPublicKey(remotePublicKey))
            {
                throw new CryptographicException("The peer key is not a point of P-256.");
            }
            if (secret.Length != P256.SharedSecretSize)
            {
                throw new ArgumentException("The secret is 32 bytes.", nameof(secret));
            }
            using ECDiffieHellman local = ECDiffieHellman.Create(P256.Parameters(privateKey, publicKey));
            using ECDiffieHellman remote = ECDiffieHellman.Create(P256.Parameters(default, remotePublicKey));
            byte[] raw = local.DeriveRawSecretAgreement(remote.PublicKey);
            raw.CopyTo(secret);
            CryptographicOperations.ZeroMemory(raw);
        }

        // d * (e * G) = e * (d * G): a fresh key e agrees with this pair's private half and with its public
        // half alone exactly when the public half is d * G
        bool Matches()
        {
            P256KeyPair probe = Generate();
            Span<byte> viaPrivate = stackalloc byte[P256.SharedSecretSize];
            Span<byte> viaPublic = stackalloc byte[P256.SharedSecretSize];
            Agree(probe.publicKey, viaPrivate);
            probe.Agree(publicKey, viaPublic);
            return CryptographicOperations.FixedTimeEquals(viaPrivate, viaPublic);
        }
    }
}
