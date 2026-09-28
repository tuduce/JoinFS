using JoinFS;

namespace JoinFS.Tests;

/// <summary>
/// Regression coverage for tuduce/JoinFS#177 (no reconnect after the joined hub/session connection
/// is lost): LocalNode.CurrentState only ever reports Connected/Unconnected based on the session id,
/// which the 30s per-peer pulse timeout never touches - so once every peer has expired this way, a
/// client is left reporting Connected with zero live peers and nothing ever retries the join.
///
/// Exercised through Network.Tick, the pure decision step (no Main/Sim/Network/LocalNode instance
/// needed - same reasoning as NetworkIsKnownOwnerTests).
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
        // never joined anything (fresh app, or an explicit Leave that also cleared the target)
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
    public void FirstTickOfBeingOrphaned_DoesNotRetryImmediately()
    {
        // a single bad tick shouldn't trigger an instant reconnect - wait one full interval first,
        // in case it's a momentary blip (e.g. one missed pulse round trip)
        var state = new Network.ReconnectState();

        Assert.False(Network.Tick(ref state, true, true, false, 0.0, Interval));
    }

    [Fact]
    public void StillOrphaned_BeforeIntervalElapses_DoesNotRetryYet()
    {
        var state = new Network.ReconnectState();
        Network.Tick(ref state, true, true, false, 0.0, Interval); // becomes orphaned at t=0

        Assert.False(Network.Tick(ref state, true, true, false, Interval - 0.001, Interval));
    }

    [Fact]
    public void StillOrphaned_OnceIntervalElapses_RetriesExactlyOnce()
    {
        var state = new Network.ReconnectState();
        Network.Tick(ref state, true, true, false, 0.0, Interval); // becomes orphaned at t=0

        Assert.True(Network.Tick(ref state, true, true, false, Interval, Interval));
        // immediately re-checking before the next interval must not retry again
        Assert.False(Network.Tick(ref state, true, true, false, Interval + 0.001, Interval));
    }

    [Fact]
    public void StillOrphaned_RetriesAgainEveryFurtherInterval()
    {
        var state = new Network.ReconnectState();
        Network.Tick(ref state, true, true, false, 0.0, Interval);
        Assert.True(Network.Tick(ref state, true, true, false, Interval, Interval));

        Assert.False(Network.Tick(ref state, true, true, false, Interval * 1.5, Interval));
        Assert.True(Network.Tick(ref state, true, true, false, Interval * 2, Interval));
        Assert.True(Network.Tick(ref state, true, true, false, Interval * 3, Interval));
    }

    [Fact]
    public void RecoveringALivePeer_StopsTheRetryCycle()
    {
        // the retried Join() succeeded (or some other peer showed up) - a live node again means
        // "not orphaned", resetting the state machine cleanly
        var state = new Network.ReconnectState();
        Network.Tick(ref state, true, true, false, 0.0, Interval);
        Assert.True(Network.Tick(ref state, true, true, false, Interval, Interval));

        Assert.False(Network.Tick(ref state, true, true, true, Interval + 1, Interval));

        // if it goes quiet again later, it must wait a full interval again, not retry immediately
        Assert.False(Network.Tick(ref state, true, true, false, Interval + 2, Interval));
        Assert.True(Network.Tick(ref state, true, true, false, Interval + 2 + Interval, Interval));
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
