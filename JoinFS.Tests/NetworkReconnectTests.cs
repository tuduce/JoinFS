using JoinFS;

namespace JoinFS.Tests;

/// <summary>
/// Regression coverage for tuduce/JoinFS#177 (no reconnect after the joined hub/session connection
/// is lost): SessionState.Connected only ever reflects "do we have a non-zero session id", which the
/// per-peer pulse timeout never touches - so once every peer has expired this way, a client is left
/// reporting Connected with zero live peers and nothing ever retries the join/login.
///
/// Exercised through Network.Tick, the pure decision step (no Network/Main instance needed - same
/// reasoning as NetworkIsKnownOwnerTests/NetworkIsKnownOwnerTests-style fixes elsewhere in this
/// codebase). Network itself isn't practically constructable in a unit test (its constructor wires up
/// a full Main), so the wiring this feeds - CheckForOrphanedSession replaying Join vs Login with the
/// in-memory-only credential, and Leave() clearing it - is covered by the plan's manual verification
/// step instead (join a login-required hub, restart it, confirm a silent reconnect) rather than a
/// forced integration test for what's a few lines of straight-line field assignment.
/// </summary>
public class NetworkReconnectTests
{
    const double Interval = 10.0;

    [Fact]
    public void NotOrphaned_WhileALivePeerExists_NeverRetries()
    {
        var state = new Network.ReconnectState();

        // hasJoinTarget=true, isConnected=true, hasLiveNode=true - the ordinary, healthy case
        Assert.False(Network.Tick(ref state, true, true, true, 0.0, Interval));
        Assert.False(Network.Tick(ref state, true, true, true, 100.0, Interval));
    }

    [Fact]
    public void NotOrphaned_WithoutAJoinTarget_NeverRetries()
    {
        // never joined/logged into anything (fresh app, an explicit Leave that cleared the target, or
        // - importantly - a CONSOLE hub that created its own session via Create() rather than joining
        // one: Create() never sets joinEndPoint, so hasJoinTarget is false for it regardless of how
        // many clients connect or disconnect. A hub losing every client is its normal idle state, not
        // an orphaned session, and must never trigger a reconnect attempt.
        var state = new Network.ReconnectState();

        Assert.False(Network.Tick(ref state, false, true, false, 0.0, Interval));
        Assert.False(Network.Tick(ref state, false, true, false, 1000.0, Interval));
    }

    [Fact]
    public void NotOrphaned_WhileDisconnected_NeverRetries()
    {
        // an explicit Leave (or never having joined) reports Unconnected, which must not be
        // treated as "orphaned" - the user asked to leave, don't fight that
        var state = new Network.ReconnectState();

        Assert.False(Network.Tick(ref state, true, false, false, 0.0, Interval));
        Assert.False(Network.Tick(ref state, true, false, false, 1000.0, Interval));
    }

    [Fact]
    public void FirstTickOfBeingOrphaned_RetriesImmediately()
    {
        // the orphaned signal is already debounced upstream (MeshManager's peer-expiry timeout
        // only flips hasLiveNode to false after a sustained silence, not one missed packet), so
        // there's nothing to gain by waiting out a further interval before the first attempt -
        // every second of delay here is a second the hub/peer stays unreachable for no reason.
        var state = new Network.ReconnectState();

        Assert.True(Network.Tick(ref state, true, true, false, 0.0, Interval));
    }

    [Fact]
    public void StillOrphaned_BeforeNextIntervalElapses_DoesNotRetryAgainYet()
    {
        var state = new Network.ReconnectState();
        Network.Tick(ref state, true, true, false, 0.0, Interval); // retries immediately at t=0

        Assert.False(Network.Tick(ref state, true, true, false, Interval - 0.001, Interval));
    }

    [Fact]
    public void StillOrphaned_OnceIntervalElapses_RetriesAgain()
    {
        var state = new Network.ReconnectState();
        Network.Tick(ref state, true, true, false, 0.0, Interval); // retries immediately at t=0

        Assert.True(Network.Tick(ref state, true, true, false, Interval, Interval));
        // immediately re-checking before the next interval must not retry again
        Assert.False(Network.Tick(ref state, true, true, false, Interval + 0.001, Interval));
    }

    [Fact]
    public void StillOrphaned_RetriesAgainEveryFurtherInterval()
    {
        var state = new Network.ReconnectState();
        Assert.True(Network.Tick(ref state, true, true, false, 0.0, Interval));

        Assert.False(Network.Tick(ref state, true, true, false, Interval * 0.5, Interval));
        Assert.True(Network.Tick(ref state, true, true, false, Interval, Interval));
        Assert.True(Network.Tick(ref state, true, true, false, Interval * 2, Interval));
    }

    [Fact]
    public void RecoveringALivePeer_StopsTheRetryCycle()
    {
        // the retried Join()/Login() succeeded (or some other peer showed up) - a live node again
        // means "not orphaned", resetting the state machine cleanly
        var state = new Network.ReconnectState();
        Assert.True(Network.Tick(ref state, true, true, false, 0.0, Interval));

        Assert.False(Network.Tick(ref state, true, true, true, 1.0, Interval));

        // if it goes quiet again later, this is a fresh orphaned streak - retries immediately
        // again, same as the very first one did
        Assert.True(Network.Tick(ref state, true, true, false, 2.0, Interval));
    }

    [Fact]
    public void ExplicitLeaveWhileOrphaned_CancelsTheRetryCycle()
    {
        // the user leaves while we were mid-retry-cycle - must not keep retrying afterwards
        var state = new Network.ReconnectState();
        Network.Tick(ref state, true, true, false, 0.0, Interval);

        Assert.False(Network.Tick(ref state, false, false, false, 5.0, Interval));
        Assert.False(Network.Tick(ref state, false, false, false, 1000.0, Interval));
    }
}
