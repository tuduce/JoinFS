using System;
using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;

namespace JoinFS.Net.Crypto
{
    /// <summary>
    /// The admission cookie (docs/network/jfp2/protocol.md section 5.9): <c>HMAC-SHA256(secret, source address and
    /// port, context)</c> cut to 16 bytes, where the context is what the asker said about itself (its names).
    /// Nothing is kept per asker. The secret changes every <see cref="SecretLifetimeSeconds"/> and the previous
    /// one is still accepted, so a cookie is valid for at least one lifetime and at most two (60 to 120 s). Time
    /// is passed in (seconds, any steady clock), so the library has no clock of its own and tests drive it. Not
    /// thread-safe: the network thread owns it.
    /// </summary>
    public sealed class StatelessCookie
    {
        public const int Size = 16;

        /// <summary>The longest context accepted, in bytes (four names of 8 bytes, with room).</summary>
        public const int MaxContext = 64;

        public const double SecretLifetimeSeconds = 60;

        readonly byte[] current = new byte[32];
        readonly byte[] previous = new byte[32];
        double currentSince;
        bool started;

        /// <summary>Write the cookie for <paramref name="source"/> and <paramref name="context"/> into <paramref name="cookie"/> (16 bytes).</summary>
        public void Issue(double now, IPEndPoint source, ReadOnlySpan<byte> context, Span<byte> cookie)
        {
            if (cookie.Length != Size)
            {
                throw new ArgumentException("A cookie is 16 bytes.", nameof(cookie));
            }
            Advance(now);
            Compute(current, source, context, cookie);
        }

        /// <summary>True when <paramref name="cookie"/> was issued for this source and context under a secret still accepted.</summary>
        public bool Validate(double now, IPEndPoint source, ReadOnlySpan<byte> context, ReadOnlySpan<byte> cookie)
        {
            if (cookie.Length != Size || context.Length > MaxContext)
            {
                return false;
            }
            Advance(now);
            Span<byte> expected = stackalloc byte[Size];
            Compute(current, source, context, expected);
            bool ok = CryptographicOperations.FixedTimeEquals(expected, cookie);
            Compute(previous, source, context, expected);
            ok |= CryptographicOperations.FixedTimeEquals(expected, cookie);
            return ok;
        }

        void Advance(double now)
        {
            double age = now - currentSince;
            if (!started || age >= SecretLifetimeSeconds * 2 || age < 0)
            {
                // nothing issued under the old secrets can still be accepted
                RandomNumberGenerator.Fill(current);
                RandomNumberGenerator.Fill(previous);
                currentSince = now;
                started = true;
            }
            else if (age >= SecretLifetimeSeconds)
            {
                current.CopyTo(previous, 0);
                RandomNumberGenerator.Fill(current);
                currentSince = now;
            }
        }

        static void Compute(byte[] secret, IPEndPoint source, ReadOnlySpan<byte> context, Span<byte> cookie)
        {
            if (context.Length > MaxContext)
            {
                throw new ArgumentException("The context is at most " + MaxContext + " bytes.", nameof(context));
            }
            // an address, 4 or 16 bytes (an IPv4-mapped one counts as IPv4, as a dual-mode socket reports it),
            // the port, then the context; the address length makes the input unambiguous
            IPAddress address = source.Address.IsIPv4MappedToIPv6 ? source.Address.MapToIPv4() : source.Address;
            Span<byte> input = stackalloc byte[16 + 2 + MaxContext];
            address.TryWriteBytes(input, out int length);
            BinaryPrimitives.WriteUInt16BigEndian(input[length..], (ushort)source.Port);
            length += 2;
            context.CopyTo(input[length..]);
            length += context.Length;
            Span<byte> mac = stackalloc byte[32];
            HMACSHA256.HashData(secret, input[..length], mac);
            mac[..Size].CopyTo(cookie);
        }
    }
}
