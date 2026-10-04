using JoinFS.Net;

namespace JoinFS.Tests.Session
{
    /// <summary>
    /// ReconnectSupervisor: when to retry a lost session, and when to give up (tuduce/JoinFS#177, review of
    /// PR #186). The loop tests run it against a simulated client whose snapshot is published every 100 ms
    /// while the supervisor is ticked every 5 ms, like DoWork - the lag that the earlier pure-decision
    /// tests could not see.
    /// </summary>
    public class ReconnectSupervisorTests
    {
        const double Interval = 10.0;
        const double Max = 300.0;

        static ReconnectSupervisor Create() => new(Interval, Max);

        static ReconnectAction Tick(ReconnectSupervisor s, double now, SessionState state, int peers, bool hasJoinTarget = true) =>
            s.Tick(hasJoinTarget, state, peers, now);

        // ---------------------------------------------------------------- decision

        [Fact]
        public void HealthySession_NeverRetries()
        {
            var s = Create();
            Assert.Equal(ReconnectAction.None, Tick(s, 0, SessionState.Connected, 1));
            Assert.Equal(ReconnectAction.None, Tick(s, 100, SessionState.Connected, 1));
            Assert.False(s.Pending);
        }

        [Fact]
        public void WithoutAJoinTarget_NeverRetries()
        {
            // a hub that created its own session has no join target; losing every client is its idle state
            var s = Create();
            Assert.Equal(ReconnectAction.None, Tick(s, 0, SessionState.Connected, 0, hasJoinTarget: false));
            Assert.Equal(ReconnectAction.None, Tick(s, 1000, SessionState.Connected, 0, hasJoinTarget: false));
            Assert.False(s.Pending);
        }

        [Fact]
        public void NeverJoined_NeverRetries()
        {
            var s = Create();
            Assert.Equal(ReconnectAction.None, Tick(s, 0, SessionState.Unconnected, 0));
            Assert.Equal(ReconnectAction.None, Tick(s, 1000, SessionState.Unconnected, 0));
        }

        [Fact]
        public void FirstOrphanedTick_RetriesImmediately_AndIsPending()
        {
            var s = Create();
            Assert.Equal(ReconnectAction.Retry, Tick(s, 0, SessionState.Connected, 0));
            Assert.True(s.Pending);
        }

        [Fact]
        public void StillOrphaned_RetriesEveryInterval_NotBefore()
        {
            var s = Create();
            Tick(s, 0, SessionState.Connected, 0);

            Assert.Equal(ReconnectAction.None, Tick(s, Interval - 0.001, SessionState.Connected, 0));
            Assert.Equal(ReconnectAction.Retry, Tick(s, Interval, SessionState.Connected, 0));
            Assert.Equal(ReconnectAction.None, Tick(s, Interval + 0.001, SessionState.Connected, 0));
            Assert.Equal(ReconnectAction.Retry, Tick(s, Interval * 2, SessionState.Connected, 0));
        }

        [Fact]
        public void PendingSurvivesConnecting_AndKeepsRetrying()
        {
            // after Leave+Join the mesh reports Connecting; the loop must not stop there
            var s = Create();
            Tick(s, 0, SessionState.Connected, 0);

            Assert.Equal(ReconnectAction.None, Tick(s, 5, SessionState.Connecting, 0));
            Assert.True(s.Pending);
            Assert.Equal(ReconnectAction.Retry, Tick(s, Interval, SessionState.Connecting, 0));
        }

        [Fact]
        public void PendingSurvivesTheUnconnectedGapInsideARetry()
        {
            var s = Create();
            Tick(s, 0, SessionState.Connected, 0);

            Assert.Equal(ReconnectAction.None, Tick(s, 0.05, SessionState.Unconnected, 0));
            Assert.True(s.Pending);
        }

        [Fact]
        public void RecoveringALivePeer_ClearsPending()
        {
            var s = Create();
            Tick(s, 0, SessionState.Connected, 0);

            Assert.Equal(ReconnectAction.None, Tick(s, 1, SessionState.Connected, 1));
            Assert.False(s.Pending);
            // quiet again later = a fresh streak, retried immediately
            Assert.Equal(ReconnectAction.Retry, Tick(s, 2, SessionState.Connected, 0));
        }

        [Fact]
        public void UserLeave_CancelsPending_EvenWhileTheSnapshotStillShowsTheOldSession()
        {
            var s = Create();
            Tick(s, 0, SessionState.Connected, 0);

            s.NotifyUserLeft();

            Assert.False(s.Pending);
            Assert.Equal(ReconnectAction.None, Tick(s, 1, SessionState.Connected, 0)); // stale snapshot
            Assert.Equal(ReconnectAction.None, Tick(s, 20, SessionState.Connected, 0));
            Assert.Equal(ReconnectAction.None, Tick(s, 21, SessionState.Unconnected, 0)); // snapshot caught up
        }

        [Fact]
        public void AfterUserLeave_ANewOrphanedSessionIsRetriedAgain()
        {
            var s = Create();
            Tick(s, 0, SessionState.Connected, 0);
            s.NotifyUserLeft();
            Tick(s, 1, SessionState.Unconnected, 0);   // snapshot caught up
            Tick(s, 2, SessionState.Connected, 1);     // manual rejoin worked

            Assert.Equal(ReconnectAction.Retry, Tick(s, 100, SessionState.Connected, 0));
        }

        [Fact]
        public void GivesUpOnce_AfterTheMaximumDuration()
        {
            var s = Create();
            Tick(s, 0, SessionState.Connected, 0);

            Assert.Equal(ReconnectAction.GiveUp, Tick(s, Max, SessionState.Connecting, 0));
            Assert.False(s.Pending);
            Assert.Equal(ReconnectAction.None, Tick(s, Max + 1, SessionState.Connecting, 0));
        }

        [Fact]
        public void AfterGivingUp_AStaleSnapshotDoesNotStartANewCycle()
        {
            var s = Create();
            Tick(s, 0, SessionState.Connected, 0);
            Tick(s, Max, SessionState.Connected, 0); // GiveUp

            Assert.Equal(ReconnectAction.None, Tick(s, Max + 0.005, SessionState.Connected, 0)); // snapshot lags
            Assert.Equal(ReconnectAction.None, Tick(s, Max + 0.1, SessionState.Unconnected, 0)); // caught up
            Assert.False(s.Pending);
        }

        // ---------------------------------------------------------------- the loop against a lagging snapshot

        /// <summary>
        /// A client whose real state changes immediately but is only visible in the snapshot every 100 ms,
        /// like NetworkService. A retry (Leave+Join) puts it into Connecting; if the hub is up it reaches
        /// Connected with one peer shortly after.
        /// </summary>
        sealed class SimulatedClient
        {
            const double SnapshotInterval = 0.1;
            const double JoinDuration = 0.05;

            readonly ReconnectSupervisor supervisor = Create();
            SessionState state = SessionState.Connected;
            int peers;
            SessionState snapshotState = SessionState.Connected;
            int snapshotPeers;
            double nextSnapshot;
            double joinCompletes = double.MaxValue;

            public Func<double, bool> HubUp = _ => false;
            public readonly List<double> Retries = [];
            public readonly List<double> GiveUps = [];

            public bool Pending => supervisor.Pending;

            public void Run(double seconds, double step = 0.005)
            {
                for (double t = 0; t < seconds; t += step)
                {
                    if (t >= joinCompletes)
                    {
                        state = SessionState.Connected;
                        peers = 1;
                        joinCompletes = double.MaxValue;
                    }
                    if (t >= nextSnapshot)
                    {
                        snapshotState = state;
                        snapshotPeers = peers;
                        nextSnapshot = t + SnapshotInterval;
                    }
                    switch (supervisor.Tick(true, snapshotState, snapshotPeers, t))
                    {
                        case ReconnectAction.Retry:
                            Retries.Add(Math.Round(t, 3));
                            state = SessionState.Connecting;
                            peers = 0;
                            joinCompletes = HubUp(t) ? t + JoinDuration : double.MaxValue;
                            break;
                        case ReconnectAction.GiveUp:
                            GiveUps.Add(Math.Round(t, 3));
                            state = SessionState.Unconnected;
                            peers = 0;
                            break;
                    }
                }
            }
        }

        [Fact]
        public void HubDown_RetriesOncePerInterval_NotOncePerTickDuringTheSnapshotLag()
        {
            var client = new SimulatedClient();
            client.Run(35);

            Assert.Equal([0.0, 10.0, 20.0, 30.0], client.Retries.Select(retry => Math.Round(retry)));
        }

        [Fact]
        public void HubDownForTheWholeWindow_Retries30Times_ThenGivesUpOnce()
        {
            var client = new SimulatedClient();
            client.Run(Max + 60);

            Assert.Equal(30, client.Retries.Count);
            Assert.Single(client.GiveUps);
            Assert.InRange(client.GiveUps[0], Max, Max + 0.1);
            Assert.False(client.Pending);
        }

        [Fact]
        public void HubBackAfter45Seconds_Reconnects_AndStopsRetrying()
        {
            var client = new SimulatedClient { HubUp = t => t >= 45 };
            client.Run(120);

            Assert.Equal(6, client.Retries.Count); // t = 0, 10, 20, 30, 40, 50 -> the 50 s attempt succeeds
            Assert.Empty(client.GiveUps);
            Assert.False(client.Pending);
        }
    }
}
