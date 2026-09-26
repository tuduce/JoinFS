using System;
using System.Collections.Concurrent;
using System.Threading;

namespace JoinFS
{
    /// <summary>
    /// The work the sim thread runs: Sim and the Recorder (<see cref="MainSimWork"/>), or a fake in tests.
    /// </summary>
    public interface ISimThreadWork
    {
        /// <summary>Signalled when the simulator has messages waiting; null when there is nothing to wait on.</summary>
        WaitHandle MessageEvent { get; }

        /// <summary>Handle simulator messages and whatever timed work is due.</summary>
        void DoWork();

        /// <summary>When timed work is next due (same clock as <c>now</c>).</summary>
        double NextDue(double now);

        /// <summary>Close the simulator link (on the sim thread, at shutdown).</summary>
        void Close();

        /// <summary>After each pass: publish what other threads read (Sim.View) if due, with the mailbox figures. True when published.</summary>
        bool Publish(double now, MailboxStats mailbox);
    }

    /// <summary>
    /// Sim thread mailbox load (docs/sim-thread-architecture.md §2.8): items waiting when last
    /// drained, the most waiting at once since the previous publish, and the longest any item
    /// waited since then (seconds).
    /// </summary>
    public readonly record struct MailboxStats(int Depth, int Peak, double MaxAge);

    /// <summary>
    /// The sim thread (docs/sim-thread-architecture.md). It runs Sim and the Recorder, woken by:
    /// - the simulator: SimConnect's message event (SimConnect builds), or a datagram from the
    ///   X-Plane plugin, which the link posts here (X-Plane builds);
    /// - work posted by other threads (<see cref="Post"/>);
    /// - the next timed job (<see cref="ISimThreadWork.NextDue"/>).
    ///
    /// Every SimConnect call happens on this thread: the connection is created here (by
    /// Sim.CheckConnection), and calls from other threads are re-posted here by
    /// SimConnectInterface.
    ///
    /// Phase 1: the thread still holds Main.conch (<c>sync</c>) while it works, so state shared
    /// with the app thread and the UI stays consistent.
    /// </summary>
    public sealed class SimService
    {
        /// <summary>Longest sleep, whatever the timers say</summary>
        public const double MaxWait = 0.1;

        /// <summary>A posted item waiting longer than this is reported (seconds)</summary>
        public const double SlowItemAge = 0.020;

        /// <summary>Shortest time between two slow-mailbox reports (seconds)</summary>
        const double SlowReportInterval = 10.0;

        readonly ISimThreadWork work;
        readonly object sync;
        readonly Func<double> now;
        readonly Action<string> log;
        readonly ConcurrentQueue<(Action Action, double Posted)> mailbox = new();
        int peakDepth;
        double maxAge;
        double nextSlowReport;
        readonly AutoResetEvent wake = new(false);
        Thread thread;
        volatile bool running;

        /// <param name="work">What the thread runs</param>
        /// <param name="sync">Held while the thread works (Main.conch)</param>
        /// <param name="now">Clock, in seconds (Main.ElapsedTime)</param>
        /// <param name="log">Error log</param>
        public SimService(ISimThreadWork work, object sync, Func<double> now, Action<string> log)
        {
            this.work = work;
            this.sync = sync;
            this.now = now;
            this.log = log;
        }

        /// <summary>Run <paramref name="action"/> on the sim thread (in order, as soon as possible).</summary>
        public void Post(Action action)
        {
            mailbox.Enqueue((action, now()));
            wake.Set();
        }

        /// <summary>Wake the sim thread to run its work now.</summary>
        public void Wake() => wake.Set();

        public bool IsSimThread => Thread.CurrentThread == thread;

        public bool Running => running;

        public void Start()
        {
            if (running) return;
            running = true;
            thread = new Thread(Run) { IsBackground = true, Name = "JoinFS-Sim" };
            thread.Start();
        }

        /// <summary>
        /// Close the simulator link on the sim thread, then stop the thread. Don't call while
        /// holding <c>sync</c>: the sim thread needs it to finish.
        /// </summary>
        public void Stop()
        {
            if (!running)
            {
                // never started - close here
                Execute(work.Close);
                return;
            }
            running = false;
            wake.Set();
            if (!thread.Join(5000))
            {
                log("ERROR - Sim thread did not stop");
            }
        }

        /// <summary>How long to sleep before <paramref name="next"/> is due, in whole milliseconds (0 to <see cref="MaxWait"/>).</summary>
        public static int WaitMilliseconds(double next, double current)
        {
            // just past the due time: timers elapse when now > due
            double wait = next - current + 0.001;
            // round up to whole milliseconds, ignoring floating-point noise (0.051000000000000045)
            return (int)Math.Ceiling(Math.Clamp(wait, 0.0, MaxWait) * 1000.0 - 1e-6);
        }

        void Run()
        {
            int milliseconds = 0;
            while (running)
            {
                WaitHandle simEvent = work.MessageEvent;
                try
                {
                    if (simEvent != null)
                    {
                        WaitHandle.WaitAny([wake, simEvent], milliseconds);
                    }
                    else
                    {
                        wake.WaitOne(milliseconds);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // the connection closed while we waited
                }

                lock (sync)
                {
                    // posted work first: calls deferred from other threads, plugin datagrams
                    int depth = mailbox.Count;
                    peakDepth = Math.Max(peakDepth, depth);
                    while (mailbox.TryDequeue(out var item))
                    {
                        double age = now() - item.Posted;
                        maxAge = Math.Max(maxAge, age);
                        Execute(item.Action);
                    }

                    if (!running)
                    {
                        // final pass: close the simulator link on this thread
                        Execute(work.Close);
                        break;
                    }

                    Execute(work.DoWork);

                    double current = now();
                    if (maxAge > SlowItemAge && current >= nextSlowReport)
                    {
                        log("WARNING - Sim thread mailbox: work waited " + (maxAge * 1000.0).ToString("F0") + " ms (" + peakDepth + " items queued)");
                        nextSlowReport = current + SlowReportInterval;
                    }
                    MailboxStats stats = new(depth, peakDepth, maxAge);
                    bool published = false;
                    Execute(() => published = work.Publish(current, stats));
                    if (published)
                    {
                        peakDepth = 0;
                        maxAge = 0.0;
                    }

                    double next = double.MaxValue;
                    Execute(() => next = work.NextDue(current));
                    milliseconds = WaitMilliseconds(next, current);
                }
            }
        }

        void Execute(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                log("ERROR - Sim thread: " + ex.Message);
            }
        }
    }

    /// <summary>The app's sim-thread work: Sim, then the Recorder.</summary>
    sealed class MainSimWork(Main main) : ISimThreadWork
    {
        public WaitHandle MessageEvent => main.sim?.MessageEvent;

        public void DoWork()
        {
            main.sim?.DoWork();
            main.recorder?.DoWork();
        }

        public double NextDue(double now) =>
            Math.Min(main.sim?.NextDue(now) ?? double.MaxValue, main.recorder?.NextDue(now) ?? double.MaxValue);

        public void Close() => main.sim?.Close();

        public bool Publish(double now, MailboxStats mailbox) => main.sim?.PublishViewIfDue(now, mailbox.Depth, mailbox.Peak, mailbox.MaxAge) ?? true;
    }
}
