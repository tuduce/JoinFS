using System.Collections.Concurrent;

namespace JoinFS.Tests
{
    /// <summary>
    /// SimService, the sim thread (docs/sim-thread-architecture.md): posted work runs in order on
    /// the sim thread, the thread survives failures, closes the simulator link on itself when
    /// stopped, and sleeps until the next due work.
    /// </summary>
    public class SimServiceTests
    {
        sealed class FakeWork : ISimThreadWork
        {
            public readonly AutoResetEvent Message = new(false);
            public volatile bool UseMessageEvent;
            public int DoWorkCount;
            public int? ClosedOnThread;
            public double Due = double.MaxValue;

            public WaitHandle MessageEvent => UseMessageEvent ? Message : null!;
            public void DoWork() => Interlocked.Increment(ref DoWorkCount);
            public double NextDue(double now) => Due;
            public void Close() => ClosedOnThread = Environment.CurrentManagedThreadId;
            public readonly ConcurrentQueue<MailboxStats> Published = new();
            public bool Publish(double now, MailboxStats mailbox) { Published.Enqueue(mailbox); return true; }
        }

        static (SimService Service, FakeWork Work, ConcurrentQueue<string> Log) Start()
        {
            var work = new FakeWork();
            var log = new ConcurrentQueue<string>();
            var service = new SimService(work, new object(), () => 0.0, log.Enqueue);
            service.Start();
            return (service, work, log);
        }

        static void WaitFor(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition() && DateTime.UtcNow < deadline) Thread.Sleep(1);
            Assert.True(condition());
        }

        [Fact]
        public void PostedWork_RunsInOrder_OnTheSimThread()
        {
            var (service, _, _) = Start();
            var order = new ConcurrentQueue<int>();
            string? threadName = null;
            for (int i = 0; i < 100; i++)
            {
                int n = i;
                service.Post(() => { order.Enqueue(n); threadName = Thread.CurrentThread.Name; });
            }
            WaitFor(() => order.Count == 100);
            service.Stop();

            Assert.Equal(Enumerable.Range(0, 100), order);
            Assert.Equal("JoinFS-Sim", threadName);
        }

        [Fact]
        public void FailingWork_IsLogged_AndTheThreadCarriesOn()
        {
            var (service, _, log) = Start();
            bool ran = false;
            service.Post(() => throw new InvalidOperationException("boom"));
            service.Post(() => ran = true);
            WaitFor(() => ran);
            service.Stop();

            Assert.Contains(log, l => l.Contains("boom"));
        }

        [Fact]
        public void Stop_ClosesTheSimulatorLinkOnTheSimThread()
        {
            var (service, work, _) = Start();
            int simThread = 0;
            service.Post(() => simThread = Environment.CurrentManagedThreadId);
            WaitFor(() => simThread != 0);
            service.Stop();

            Assert.Equal(simThread, work.ClosedOnThread);
            Assert.NotEqual(Environment.CurrentManagedThreadId, work.ClosedOnThread);
            Assert.False(service.Running);
        }

        [Fact]
        public void Stop_BeforeStart_ClosesOnTheCaller()
        {
            var work = new FakeWork();
            var service = new SimService(work, new object(), () => 0.0, _ => { });
            service.Stop();
            Assert.Equal(Environment.CurrentManagedThreadId, work.ClosedOnThread);
        }

        [Fact]
        public void SimulatorMessage_WakesTheThread()
        {
            var (service, work, _) = Start();
            work.UseMessageEvent = true;
            WaitFor(() => work.DoWorkCount > 0);
            int before = work.DoWorkCount;
            work.Message.Set();
            WaitFor(() => work.DoWorkCount > before);
            service.Stop();
        }

        [Fact]
        public void Mailbox_ReportsHowLongWorkWaited_AndWarnsWhenSlow()
        {
            var work = new FakeWork();
            var log = new ConcurrentQueue<string>();
            double clock = 1.0;
            var service = new SimService(work, new object(), () => Volatile.Read(ref clock), log.Enqueue);
            service.Start();

            // hold the sim thread in one item while another is posted, then advance the clock
            using var gate = new ManualResetEventSlim(false);
            bool blocked = false, ran = false;
            service.Post(() => { blocked = true; gate.Wait(); });
            WaitFor(() => blocked);
            service.Post(() => ran = true);          // posted at 1.0
            Volatile.Write(ref clock, 1.5);          // it will have waited 500 ms
            gate.Set();
            WaitFor(() => ran);
            WaitFor(() => work.Published.Any(m => m.MaxAge >= 0.5));
            service.Stop();

            Assert.Contains(log, l => l.Contains("WARNING - Sim thread mailbox"));
        }

        [Theory]
        [InlineData(10.050, 10.000, 51)]     // due in 50 ms: sleep until just past it
        [InlineData(10.000, 10.000, 1)]      // due now: 1 ms, so the timer has elapsed when we wake
        [InlineData(9.000, 10.000, 0)]       // overdue: don't sleep
        [InlineData(double.MaxValue, 10.0, 100)] // nothing due: MaxWait
        public void WaitMilliseconds_SleepsUntilJustAfterTheNextDueWork(double next, double now, int expected)
        {
            Assert.Equal(expected, SimService.WaitMilliseconds(next, now));
        }
    }
}
