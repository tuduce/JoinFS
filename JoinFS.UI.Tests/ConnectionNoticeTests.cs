using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.Tests;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>What the strip says when a connection fails, is lost or is slow (UX-REVIEW.md, finding 1).</summary>
public class ConnectionNoticeTests
{
    private static ConnectionViewModel Observed(ManualTime time, ConnectionLabels labels, Func<bool>? expected = null) =>
        new(labels, _ => Task.CompletedTask, observed: true, failureIsExpected: expected, time: time);

    [Fact]
    public void An_attempt_that_ends_by_itself_says_why_once_it_has_lasted()
    {
        ManualTime time = new();
        ConnectionViewModel sim = Observed(time, ConnectionLabels.Simulator);

        sim.Sync(ConnectionState.Connecting);
        sim.Sync(ConnectionState.Disconnected);
        Assert.Null(sim.Detail); // a moment of "disconnected" is no failure yet

        time.Advance(TimeSpan.FromSeconds(1));
        sim.Sync(ConnectionState.Disconnected);

        Assert.Equal("Simulator not found. Start it, then click Simulator.", sim.Detail);
        Assert.True(sim.DetailIsProblem);
    }

    [Fact]
    public void A_blip_of_disconnected_between_two_attempts_says_nothing()
    {
        ManualTime time = new();
        ConnectionViewModel net = Observed(time, ConnectionLabels.Network);

        net.Sync(ConnectionState.Connecting);
        net.Sync(ConnectionState.Disconnected);
        time.Advance(TimeSpan.FromMilliseconds(300));
        net.Sync(ConnectionState.Connecting); // the retry
        time.Advance(TimeSpan.FromSeconds(2));
        net.Sync(ConnectionState.Connecting);

        Assert.Null(net.Detail);
    }

    [Fact]
    public async Task Giving_up_is_no_failure()
    {
        ManualTime time = new();
        ConnectionViewModel net = Observed(time, ConnectionLabels.Network);
        net.Sync(ConnectionState.Connecting);

        await net.ToggleAsync(); // a click while connecting gives up
        net.Sync(ConnectionState.Disconnected);
        time.Advance(TimeSpan.FromSeconds(5));
        net.Sync(ConnectionState.Disconnected);

        Assert.Null(net.Detail);
    }

    [Fact]
    public async Task Disconnecting_is_no_loss()
    {
        ManualTime time = new();
        ConnectionViewModel net = Observed(time, ConnectionLabels.Network);
        net.Sync(ConnectionState.Connected);

        await net.ToggleAsync();
        net.Sync(ConnectionState.Disconnected);
        time.Advance(TimeSpan.FromSeconds(5));
        net.Sync(ConnectionState.Disconnected);

        Assert.Null(net.Detail);
    }

    [Fact]
    public void A_connection_that_drops_by_itself_is_lost()
    {
        ManualTime time = new();
        ConnectionViewModel net = Observed(time, ConnectionLabels.Network);
        net.Sync(ConnectionState.Connected);

        net.Sync(ConnectionState.Disconnected);
        time.Advance(TimeSpan.FromSeconds(2));
        net.Sync(ConnectionState.Disconnected);

        Assert.Equal("Network connection lost.", net.Detail);
    }

    [Fact]
    public void An_end_that_is_part_of_a_conversation_is_no_failure()
    {
        ManualTime time = new();
        bool asking = true;
        ConnectionViewModel net = Observed(time, ConnectionLabels.Network, () => asking);

        net.Sync(ConnectionState.Connecting);
        net.Sync(ConnectionState.Disconnected); // the session was left to ask for a password
        time.Advance(TimeSpan.FromSeconds(10));
        net.Sync(ConnectionState.Disconnected);

        Assert.Null(net.Detail);
    }

    [Fact]
    public void A_join_without_an_answer_says_so_after_fifteen_seconds_and_stops_when_it_connects()
    {
        ManualTime time = new();
        ConnectionViewModel net = Observed(time, ConnectionLabels.Network);

        net.Sync(ConnectionState.Connecting);
        time.Advance(TimeSpan.FromSeconds(14));
        net.Sync(ConnectionState.Connecting);
        Assert.Null(net.Detail);

        time.Advance(TimeSpan.FromSeconds(1));
        net.Sync(ConnectionState.Connecting);
        Assert.Equal("No answer from the hub yet. Click Network to cancel.", net.Detail);
        Assert.False(net.DetailIsProblem); // still trying, not a failure

        net.Sync(ConnectionState.Connected);
        Assert.Null(net.Detail);
    }

    [Fact]
    public void The_simulator_has_no_slow_hint_and_the_flight_plan_says_nothing_at_all()
    {
        ManualTime time = new();
        ConnectionViewModel sim = Observed(time, ConnectionLabels.Simulator);
        ConnectionViewModel plan = Observed(time, ConnectionLabels.FlightPlan);

        sim.Sync(ConnectionState.Connecting);
        plan.Sync(ConnectionState.Connecting);
        time.Advance(TimeSpan.FromMinutes(1));
        sim.Sync(ConnectionState.Connecting);
        plan.Sync(ConnectionState.Connecting);
        plan.Sync(ConnectionState.Disconnected);
        time.Advance(TimeSpan.FromSeconds(5));
        plan.Sync(ConnectionState.Disconnected);

        Assert.Null(sim.Detail);
        Assert.Null(plan.Detail);
    }

    [Fact]
    public void Trying_again_and_dismissing_both_clear_the_notice()
    {
        ManualTime time = new();
        ConnectionViewModel sim = Observed(time, ConnectionLabels.Simulator);
        sim.Sync(ConnectionState.Connecting);
        sim.Sync(ConnectionState.Disconnected);
        time.Advance(TimeSpan.FromSeconds(2));
        sim.Sync(ConnectionState.Disconnected);
        Assert.NotNull(sim.Detail);

        sim.DismissDetail();
        Assert.Null(sim.Detail);

        sim.Sync(ConnectionState.Connecting); // a new attempt
        sim.Sync(ConnectionState.Disconnected);
        time.Advance(TimeSpan.FromSeconds(2));
        sim.Sync(ConnectionState.Disconnected);
        Assert.NotNull(sim.Detail); // and its own failure is told

        sim.Sync(ConnectionState.Connecting);
        Assert.Null(sim.Detail);
    }

    [Fact]
    public void A_click_while_connecting_is_labelled_Cancel_only_where_it_does_cancel()
    {
        ManualTime time = new();
        ConnectionViewModel live = Observed(time, ConnectionLabels.Simulator);
        ConnectionViewModel owned = new(ConnectionLabels.Simulator, _ => Task.CompletedTask);

        live.Sync(ConnectionState.Connecting);
        owned.SetState(ConnectionState.Connecting);

        Assert.True(live.CanCancel);
        Assert.Equal("Cancel", live.ActionLabel);
        Assert.False(owned.CanCancel);
        Assert.Equal("Connecting…", owned.ActionLabel);
    }

    // ---- the shell

    private static (MainViewModel Main, ScriptedLink Sim, ScriptedLink Net, ManualTime Time) Shell()
    {
        ScriptedLink sim = new(), net = new();
        ManualTime time = new();
        AppServices fakes = FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "HB-TDX" });
        return (new MainViewModel(fakes with { Simulator = sim, Network = net, Updates = new CurrentBuild() }, time), sim, net, time);
    }

    [Fact]
    public void The_shell_shows_the_notice_of_both_buttons_and_dismisses_them_together()
    {
        var (main, sim, net, time) = Shell();
        Assert.False(main.HasNotice);

        sim.State = ConnectionState.Connecting;
        net.State = ConnectionState.Connected;
        main.Poll();
        sim.State = ConnectionState.Disconnected;
        net.State = ConnectionState.Disconnected;
        main.Poll();
        time.Advance(TimeSpan.FromSeconds(2));
        main.Poll();

        Assert.True(main.HasNotice);
        Assert.True(main.NoticeIsProblem);
        Assert.Equal("Simulator not found. Start it, then click Simulator." + Environment.NewLine + "Network connection lost.", main.Notice);

        main.DismissNoticeCommand.Execute(null);
        Assert.False(main.HasNotice);
    }

    [Fact]
    public void A_session_that_asks_for_a_password_is_not_reported_as_a_failed_join()
    {
        var (main, _, net, time) = Shell();

        net.State = ConnectionState.Connecting;
        main.Poll();
        net.PasswordRequestedBy = "Aidan's Hub"; // the session answers "password required" and is left
        net.State = ConnectionState.Disconnected;
        main.Poll();
        time.Advance(TimeSpan.FromSeconds(10));
        main.Poll();

        Assert.IsType<PasswordPromptViewModel>(main.Overlay);
        Assert.False(main.HasNotice);
    }

    [Fact]
    public void A_slow_join_shows_an_amber_notice_not_a_red_one()
    {
        var (main, _, net, time) = Shell();

        net.State = ConnectionState.Connecting;
        main.Poll();
        time.Advance(TimeSpan.FromSeconds(16));
        main.Poll();

        Assert.True(main.HasNotice);
        Assert.False(main.NoticeIsProblem);
    }
}
