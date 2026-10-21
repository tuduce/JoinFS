using System;
using System.Numerics;
using System.Security.Cryptography;

namespace JoinFS.Net.Crypto
{
    /// <summary>
    /// The NIST P-256 facts the protocol needs, and the one check .NET does not make the same way on
    /// every platform: that a received public key is a point of the curve.
    /// </summary>
    public static class P256
    {
        /// <summary>A public key on the wire: X then Y, 32 bytes each, big-endian, no SEC1 prefix.</summary>
        public const int PublicKeySize = 64;

        /// <summary>A private scalar, big-endian.</summary>
        public const int PrivateKeySize = 32;

        /// <summary>The x coordinate of the key agreement, which is the shared secret.</summary>
        public const int SharedSecretSize = 32;

        const int CoordinateSize = 32;

        static readonly BigInteger P = BigInteger.Parse("0FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF", System.Globalization.NumberStyles.HexNumber);
        static readonly BigInteger N = BigInteger.Parse("0FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551", System.Globalization.NumberStyles.HexNumber);
        static readonly BigInteger B = BigInteger.Parse("05AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B", System.Globalization.NumberStyles.HexNumber);

        /// <summary>
        /// True when the 64 bytes are a point on P-256: both coordinates below the field prime and
        /// y² = x³ - 3x + b. The curve has cofactor 1, so this is the whole validation (SP 800-56A 5.6.2.3.3).
        /// A receiver MUST reject any other key (design.md section 3).
        /// </summary>
        public static bool IsValidPublicKey(ReadOnlySpan<byte> publicKey)
        {
            if (publicKey.Length != PublicKeySize)
            {
                return false;
            }
            BigInteger x = new BigInteger(publicKey[..CoordinateSize], isUnsigned: true, isBigEndian: true);
            BigInteger y = new BigInteger(publicKey[CoordinateSize..], isUnsigned: true, isBigEndian: true);
            if (x >= P || y >= P)
            {
                return false;
            }
            BigInteger left = y * y % P;
            BigInteger right = ((x * x % P * x - 3 * x + B) % P + P) % P;
            return left == right;
        }

        /// <summary>True when the scalar is in [1, n - 1], n the group order.</summary>
        public static bool IsValidPrivateKey(ReadOnlySpan<byte> privateKey)
        {
            if (privateKey.Length != PrivateKeySize)
            {
                return false;
            }
            BigInteger d = new BigInteger(privateKey, isUnsigned: true, isBigEndian: true);
            return !d.IsZero && d < N;
        }

        public static ECParameters Parameters(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKey)
        {
            return new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = privateKey.IsEmpty ? null : privateKey.ToArray(),
                Q = new ECPoint { X = publicKey[..CoordinateSize].ToArray(), Y = publicKey[CoordinateSize..].ToArray() },
            };
        }
    }
}
