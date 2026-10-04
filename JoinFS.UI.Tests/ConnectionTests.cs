using JoinFS.UI.Models;
using JoinFS.UI.ViewModels;

namespace JoinFS.UI.Tests;

public class ConnectionTests
{
    private static ConnectionViewModel Machine(Func<CancellationToken, Task>? connect = null, Func<Task>? requestConnect = null) =>
        new(ConnectionLabels.Simulator, connect ?? (_ => Task.CompletedTask), requestConnect: requestConnect);

    [Fact]
    public void It_starts_disconnected_with_the_disconnected_words()
    {
        ConnectionViewModel simulator = Machine();

        Assert.Equal(ConnectionState.Disconnected, simulator.State);
        Assert.Equal("Disconnected", simulator.StateLabel);
        Assert.Equal("Connect", simulator.ActionLabel);
    }

    [Fact]
    public async Task Toggling_connects_and_toggling_again_disconnects()
    {
        ConnectionViewModel simulator = Machine();

        await simulator.ToggleAsync();
        Assert.True(simulator.IsConnected);
        Assert.Equal("Connected", simulator.StateLabel);
        Assert.Equal("Disconnect", simulator.ActionLabel);

        await simulator.ToggleAsync();
        Assert.True(simulator.IsDisconnected);
    }

    [Fact]
    public async Task It_is_connecting_while_the_connect_work_runs_and_a_click_then_does_nothing()
    {
        TaskCompletionSource gate = new();
        int connects = 0;
        ConnectionViewModel simulator = Machine(async _ => { connects++; await gate.Task; });

        Task first = simulator.ToggleAsync();
        Assert.True(simulator.IsConnecting);
        Assert.Equal("Connecting…", simulator.StateLabel);

        await simulator.ToggleAsync();
        Assert.True(simulator.IsConnecting);
        Assert.Equal(1, connects);

        gate.SetResult();
        await first;
        Assert.True(simulator.IsConnected);
    }

    [Fact]
    public async Task A_cancelled_connect_ends_disconnected_without_an_error()
    {
        ConnectionViewModel simulator = Machine(_ => throw new OperationCanceledException());

        await simulator.ToggleAsync();

        Assert.True(simulator.IsDisconnected);
        Assert.Null(simulator.Error);
    }

    [Fact]
    public async Task A_failed_connect_ends_disconnected_and_keeps_the_reason()
    {
        ConnectionViewModel simulator = Machine(_ => throw new InvalidOperationException("no sim"));

        await simulator.ToggleAsync();

        Assert.True(simulator.IsDisconnected);
        Assert.Equal("no sim", simulator.Error);
    }

    [Fact]
    public async Task A_connect_request_replaces_the_plain_connect_when_disconnected()
    {
        int requested = 0;
        ConnectionViewModel network = Machine(requestConnect: () => { requested++; return Task.CompletedTask; });

        await network.ToggleAsync();

        Assert.Equal(1, requested);
        Assert.True(network.IsDisconnected); // the request decides when to call ConnectAsync
    }

    [Fact]
    public void The_flight_plan_uses_its_own_words()
    {
        ConnectionViewModel plan = new(ConnectionLabels.FlightPlan, _ => Task.CompletedTask);

        Assert.Equal("Not loaded", plan.StateLabel);
        plan.SetState(ConnectionState.Connecting);
        Assert.Equal("Fetching…", plan.StateLabel);
        plan.SetState(ConnectionState.Connected);
        Assert.Equal("Loaded", plan.StateLabel);
    }
}
