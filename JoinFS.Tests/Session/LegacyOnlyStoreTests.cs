using System.Net;

namespace JoinFS.Tests.Session
{
    /// <summary>The memory of the addresses that took no conversation over JFP2, kept across runs (plan stage 9).</summary>
    public class LegacyOnlyStoreTests
    {
        static string NewFolder() => Directory.CreateTempSubdirectory("joinfs-legacyonly-").FullName;

        [Fact]
        public void WhatOneRunRemembers_TheNextRunLoadsWithTheTimeThatIsLeft()
        {
            string folder = NewFolder();
            var log = new FakeLog();
            IPEndPoint o = new(IPAddress.Parse("203.0.113.9"), 6112);
            IPEndPoint other = new(IPAddress.Parse("203.0.113.10"), 6113);
            var first = new LegacyOnlyStore(folder, log);
            first.Remember(o, 3600);
            first.Remember(other, 1800);

            var second = new LegacyOnlyStore(folder, log);
            var loaded = second.Load();

            Assert.Equal(2, loaded.Count);
            double left = loaded.Single(l => l.EndPoint.Equals(o)).Seconds;
            Assert.InRange(left, 3590, 3600);
            Assert.InRange(loaded.Single(l => l.EndPoint.Equals(other)).Seconds, 1790, 1800);
            Assert.Empty(log.Events);
        }

        [Fact]
        public void AMemoryThatHasRunOut_IsNotLoaded_AndAJunkLineIsSkipped()
        {
            string folder = NewFolder();
            File.WriteAllLines(Path.Combine(folder, "legacyonly.txt"),
            [
                "203.0.113.9:6112\t" + DateTime.UtcNow.AddSeconds(-5).Ticks,
                "not an endpoint\t1",
                "[2001:db8::1]:6112\t" + DateTime.UtcNow.AddHours(1).Ticks,
            ]);

            var loaded = new LegacyOnlyStore(folder, new FakeLog()).Load();

            Assert.Equal(new IPEndPoint(IPAddress.Parse("2001:db8::1"), 6112), Assert.Single(loaded).EndPoint);
        }

        [Fact]
        public void ARunWithNoFile_LoadsNothing()
        {
            Assert.Empty(new LegacyOnlyStore(NewFolder(), new FakeLog()).Load());
        }
    }
}
