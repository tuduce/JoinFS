using JoinFS.Net;

namespace JoinFS
{
    /// <summary>What the caller has to do after a <see cref="ReconnectSupervisor.Tick"/>. Append new members only.</summary>
    public enum ReconnectAction
    {
        /// <summary>Nothing to do.</summary>
        None = 0,
        /// <summary>Re-establish the session now.</summary>
        Retry = 1,
        /// <summary>Stop trying: the retry window is used up, tell the user.</summary>
        GiveUp = 2,
    }

    /// <summary>
    /// Decides when a lost session is retried, and when to stop trying (tuduce/JoinFS#177).
    ///
    /// A session is lost when we still hold a session id (<see cref="SessionState.Connected"/>) but every peer,
    /// the joined hub included, has expired. From then on a reconnect is pending until the session is back
    /// (connected with a peer), the user leaves, or the retry window runs out. The retry's own Leave+Join moves
    /// the mesh through Connecting and Unconnected, and the network snapshot lags the real state by up to
    /// 100 ms, so none of those states may restart or cancel the pending reconnect.
    /// </summary>
    public sealed class ReconnectSupervisor
    {
        readonly double intervalSeconds;
        readonly double maxSeconds;

        double startedAt;
        double nextAttempt;

        /// <summary>
        /// Set when the user left or we gave up: the snapshot may still show the old, peerless session for a
        /// moment, which must not look like a new lost session.
        /// </summary>
        bool awaitingSnapshotReset;

        public ReconnectSupervisor(double intervalSeconds, double maxSeconds)
        {
            this.intervalSeconds = intervalSeconds;
            this.maxSeconds = maxSeconds;
        }

        /// <summary>A reconnect is under way (retrying, or between retries).</summary>
        public bool Pending { get; private set; }

        /// <summary>The user left the session: no automatic reconnect, whatever the snapshot still shows.</summary>
        public void NotifyUserLeft()
        {
            Pending = false;
            awaitingSnapshotReset = true;
        }

        /// <param name="hasJoinTarget">A hub to go back to exists; a session we created ourselves has none.</param>
        /// <param name="state">Session state from the latest network snapshot.</param>
        /// <param name="peerCount">Live peers (the joined hub counts) from the latest network snapshot.</param>
        /// <param name="now">Seconds on any monotonic clock.</param>
        public ReconnectAction Tick(bool hasJoinTarget, SessionState state, int peerCount, double now)
        {
            bool sessionLost = state == SessionState.Connected && peerCount == 0;
            bool sessionAlive = state == SessionState.Connected && peerCount > 0;

            if (!hasJoinTarget || sessionAlive)
            {
                Pending = false;
                awaitingSnapshotReset = false;
                return ReconnectAction.None;
            }

            if (awaitingSnapshotReset)
            {
                awaitingSnapshotReset = sessionLost;
                return ReconnectAction.None;
            }

            if (!Pending)
            {
                return sessionLost ? StartReconnect(now) : ReconnectAction.None;
            }

            return ContinueReconnect(now);
        }

        ReconnectAction StartReconnect(double now)
        {
            Pending = true;
            startedAt = now;
            nextAttempt = now + intervalSeconds;
            return ReconnectAction.Retry;
        }

        ReconnectAction ContinueReconnect(double now)
        {
            if (now - startedAt >= maxSeconds)
            {
                Pending = false;
                awaitingSnapshotReset = true;
                return ReconnectAction.GiveUp;
            }
            if (now < nextAttempt)
            {
                return ReconnectAction.None;
            }
            nextAttempt = now + intervalSeconds;
            return ReconnectAction.Retry;
        }
    }
}
