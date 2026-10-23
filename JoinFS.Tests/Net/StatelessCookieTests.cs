using System.Net;
using System.Security.Cryptography;
using JoinFS.Net.Crypto;

namespace JoinFS.Tests.Net
{
    /// <summary>The admission cookie: bound to the source and the names the asker stated, rotated every 60 s.</summary>
    public class StatelessCookieTests
    {
        static readonly IPEndPoint Source = new(IPAddress.Parse("203.0.113.9"), 6112);

        static byte[] Context() => RandomNumberGenerator.GetBytes(16);

        static byte[] Issue(StatelessCookie jar, double now, IPEndPoint source, byte[] context)
        {
            byte[] cookie = new byte[StatelessCookie.Size];
            jar.Issue(now, source, context, cookie);
            return cookie;
        }

        [Fact]
        public void Cookie_ValidatesOnlyForTheSameSourceAndContext()
        {
            var jar = new StatelessCookie();
            byte[] context = Context();
            byte[] cookie = Issue(jar, 0, Source, context);

            Assert.True(jar.Validate(0, Source, context, cookie));
            Assert.False(jar.Validate(0, new IPEndPoint(Source.Address, 6113), context, cookie));
            Assert.False(jar.Validate(0, new IPEndPoint(IPAddress.Parse("203.0.113.10"), 6112), context, cookie));
            Assert.False(jar.Validate(0, new IPEndPoint(IPAddress.Parse("2001:db8::9"), 6112), context, cookie));
            Assert.False(jar.Validate(0, Source, Context(), cookie));
            Assert.False(jar.Validate(0, Source, context[..15], cookie));
            for (int i = 0; i < cookie.Length; i++)
            {
                byte[] tampered = (byte[])cookie.Clone();
                tampered[i] ^= 1;
                Assert.False(jar.Validate(0, Source, context, tampered));
            }
            Assert.False(jar.Validate(0, Source, context, cookie[..15]));
        }

        [Fact]
        public void Cookie_IsSixteenBytesAndPerSource()
        {
            var jar = new StatelessCookie();
            byte[] context = Context();

            Assert.Equal(16, Issue(jar, 0, Source, context).Length);
            Assert.NotEqual(Issue(jar, 0, Source, context), Issue(jar, 0, new IPEndPoint(Source.Address, 7000), context));
            Assert.Equal(Issue(jar, 0, Source, context), Issue(jar, 0, Source, context));
        }

        [Fact]
        public void Cookie_AnIpv4MappedAddressIsTheIpv4Address()
        {
            // a dual-mode socket reports IPv4 peers as ::ffff:a.b.c.d
            var jar = new StatelessCookie();
            byte[] context = Context();
            var mapped = new IPEndPoint(Source.Address.MapToIPv6(), Source.Port);

            Assert.True(jar.Validate(0, mapped, context, Issue(jar, 0, Source, context)));
            Assert.True(jar.Validate(0, Source, context, Issue(jar, 0, mapped, context)));
        }

        [Fact]
        public void Cookie_WorksForIpv6Sources()
        {
            var jar = new StatelessCookie();
            byte[] context = Context();
            var source = new IPEndPoint(IPAddress.Parse("2001:db8::1234"), 6112);

            Assert.True(jar.Validate(0, source, context, Issue(jar, 0, source, context)));
        }

        [Fact]
        public void Cookie_AnotherJarDoesNotAcceptIt()
        {
            byte[] context = Context();

            Assert.False(new StatelessCookie().Validate(0, Source, context, Issue(new StatelessCookie(), 0, Source, context)));
        }

        [Fact]
        public void Cookie_LivesBetweenOneAndTwoSecretLifetimes()
        {
            var jar = new StatelessCookie();
            byte[] context = Context();
            byte[] cookie = Issue(jar, 0, Source, context);

            Assert.True(jar.Validate(59, Source, context, cookie));
            Assert.True(jar.Validate(61, Source, context, cookie));   // the secret rotated, the previous one is still accepted
            Assert.False(jar.Validate(121, Source, context, cookie)); // rotated again
        }

        [Fact]
        public void Cookie_IsRefusedAfterTwoLifetimesEvenWithNoCallInBetween()
        {
            var jar = new StatelessCookie();
            byte[] context = Context();
            byte[] cookie = Issue(jar, 0, Source, context);

            Assert.False(jar.Validate(500, Source, context, cookie));
        }

        [Fact]
        public void Cookie_ContextIsBounded()
        {
            var jar = new StatelessCookie();

            Assert.Throws<ArgumentException>(() => Issue(jar, 0, Source, new byte[StatelessCookie.MaxContext + 1]));
            Assert.False(jar.Validate(0, Source, new byte[StatelessCookie.MaxContext + 1], new byte[StatelessCookie.Size]));
        }
    }
}
