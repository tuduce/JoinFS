using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.Tests;

/// <summary>A stand-in for the live app: state it changes by itself, and a record of what the UI asked of it.</summary>
public sealed class ScriptedLink : ISimulatorLink, INetworkLink
{
    public bool ReportsState => true;
    public ConnectionState State { get; set; }
    public string? PasswordRequestedBy { get; set; }
    public string MeshCode => "11111 22222";

    public int Polls { get; private set; }
    public List<string> Requests { get; } = [];
    public List<string> Passwords { get; } = [];

    public void Poll() => Polls++;

    // The simulator and the network share this stand-in, so each test uses one of them.
    Task ISimulatorLink.ConnectAsync(CancellationToken cancellationToken) { Requests.Add("sim connect"); return Task.CompletedTask; }
    Task ISimulatorLink.DisconnectAsync() { Requests.Add("sim disconnect"); return Task.CompletedTask; }

    public Task JoinAsync(AddressBookEntry hub, string? password, CancellationToken cancellationToken)
    {
        Requests.Add("join " + hub.Name + (password is null ? "" : " with password"));
        return Task.CompletedTask;
    }

    public Task<string> CreateMeshAsync(CancellationToken cancellationToken)
    {
        Requests.Add("create");
        return Task.FromResult(MeshCode);
    }

    Task INetworkLink.DisconnectAsync() { Requests.Add("net disconnect"); return Task.CompletedTask; }

    public void SubmitPassword(string password)
    {
        Passwords.Add(password);
        PasswordRequestedBy = null;
    }

    public void CancelPasswordRequest()
    {
        Requests.Add("cancel password");
        PasswordRequestedBy = null;
    }
}

public class LiveLinkTests
{
    private static (MainViewModel Main, ScriptedLink Sim, ScriptedLink Net) Shell()
    {
        ScriptedLink sim = new(), net = new();
        AppServices fakes = FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "HB-TDX" });
        return (new MainViewModel(fakes with { Simulator = sim, Network = net }), sim, net);
    }

    [Fact]
    public void An_observed_connector_follows_the_real_state_and_poll_asks_the_links_to_do_their_housekeeping()
    {
        var (main, sim, net) = Shell();
        Assert.True(main.Simulator.IsDisconnected);

        sim.State = ConnectionState.Connecting;
        net.State = ConnectionState.Connected;
        main.Poll();

        Assert.True(main.Simulator.IsConnecting);
        Assert.True(main.Network.IsConnected);
        Assert.Equal((1, 1), (sim.Polls, net.Polls));

        sim.State = ConnectionState.Connected; // the sim came up on its own
        main.Poll();
        Assert.True(main.Simulator.IsConnected);

        sim.State = ConnectionState.Disconnected; // and dropped on its own
        main.Poll();
        Assert.True(main.Simulator.IsDisconnected);
    }

    [Fact]
    public async Task A_click_only_asks_the_state_changes_when_the_app_says_so()
    {
        var (main, sim, _) = Shell();

        await main.Simulator.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(["sim connect"], sim.Requests);
        Assert.True(main.Simulator.IsDisconnected); // not until the app reports it

        sim.State = ConnectionState.Connected;
        main.Poll();
        await main.Simulator.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(["sim connect", "sim disconnect"], sim.Requests);
        Assert.True(main.Simulator.IsConnected);
    }

    [Fact]
    public async Task A_click_while_connecting_gives_up_the_attempt()
    {
        var (main, sim, _) = Shell();
        sim.State = ConnectionState.Connecting;
        main.Poll();

        await main.Simulator.ToggleCommand.ExecuteAsync(null);

        Assert.Equal(["sim disconnect"], sim.Requests);
    }

    [Fact]
    public async Task The_fakes_still_own_their_state()
    {
        Rig rig = new();

        await rig.Main.Simulator.ToggleAsync();
        rig.Main.Poll();

        Assert.True(rig.Main.Simulator.IsConnected); // Poll must not undo it
    }

    [Fact]
    public async Task Joining_a_hub_asks_the_network_and_leaves_the_state_to_the_app()
    {
        var (main, _, net) = Shell();
        main.AddressBook.Select("AirSherpa");

        await main.Network.ToggleCommand.ExecuteAsync(null);

        Assert.Equal(["join AirSherpa"], net.Requests);
        Assert.True(main.Network.IsDisconnected);
        net.State = ConnectionState.Connected;
        main.Poll();
        Assert.True(main.Network.IsConnected);
    }

    [Fact]
    public async Task Create_mesh_asks_for_a_mesh_and_returns_its_code()
    {
        var (main, _, net) = Shell();

        string? code = await main.CreateMeshAsync();

        Assert.Equal("11111 22222", code);
        Assert.Equal(["create"], net.Requests);
    }

    // --- the password conversation ---

    [Fact]
    public void A_session_that_wants_a_password_gets_one_prompt_however_often_we_poll()
    {
        var (main, _, net) = Shell();
        net.PasswordRequestedBy = "Aidan's Hub";

        main.Poll();
        PasswordPromptViewModel prompt = Assert.IsType<PasswordPromptViewModel>(main.Overlay);
        Assert.Equal("Aidan's Hub requires a password to join.", prompt.Message);

        main.Poll();
        main.Poll();
        Assert.Same(prompt, main.Overlay);
    }

    [Fact]
    public async Task Confirming_sends_the_password_to_the_session()
    {
        var (main, _, net) = Shell();
        net.PasswordRequestedBy = "Aidan's Hub";
        main.Poll();
        PasswordPromptViewModel prompt = (PasswordPromptViewModel)main.Overlay!;

        prompt.Password = "hunter2";
        await prompt.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(["hunter2"], net.Passwords);
        Assert.DoesNotContain("cancel password", net.Requests);
        Assert.Null(main.Overlay);
        main.Poll();
        Assert.Null(main.Overlay); // answered: not asked again
    }

    [Fact]
    public void Cancel_and_escape_give_up_on_the_session()
    {
        var (main, _, net) = Shell();
        net.PasswordRequestedBy = "Aidan's Hub";
        main.Poll();

        main.Overlay!.CloseCommand.Execute(null); // Cancel, ✕ and Escape all close the overlay this way

        Assert.Equal(["cancel password"], net.Requests);
        Assert.Empty(net.Passwords);
        main.Poll();
        Assert.Null(main.Overlay);
    }

    [Fact]
    public void A_password_request_waits_while_another_overlay_is_open()
    {
        var (main, _, net) = Shell();
        main.OpenAboutCommand.Execute(null);
        OverlayViewModel about = main.Overlay!;
        net.PasswordRequestedBy = "Aidan's Hub";

        main.Poll();
        Assert.Same(about, main.Overlay);

        about.Close();
        main.Poll();
        Assert.IsType<PasswordPromptViewModel>(main.Overlay);
    }

    // --- messages of the app ---

    private sealed class ScriptedMessages : IMessageSource
    {
        public string? Pending;

        public string? TakeMessage()
        {
            string? message = Pending;
            Pending = null;
            return message;
        }
    }

    private static (MainViewModel Main, ScriptedMessages Messages) ShellWithMessages()
    {
        ScriptedMessages messages = new();
        AppServices fakes = FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "HB-TDX" });
        return (new MainViewModel(fakes with { Messages = messages }), messages);
    }

    [Fact]
    public void A_message_of_the_app_is_shown_over_the_window_until_it_is_read()
    {
        var (main, messages) = ShellWithMessages();
        main.Poll();
        Assert.Null(main.Overlay);

        messages.Pending = "Invalid address";
        main.Poll();
        MessageViewModel message = Assert.IsType<MessageViewModel>(main.Overlay);
        Assert.Equal("Invalid address", message.Message);

        main.Poll();
        Assert.Same(message, main.Overlay);

        message.CloseCommand.Execute(null);
        Assert.Null(main.Overlay);
        main.Poll();
        Assert.Null(main.Overlay); // read once
    }

    [Fact]
    public void A_message_waits_while_another_overlay_is_open()
    {
        var (main, messages) = ShellWithMessages();
        main.OpenAboutCommand.Execute(null);
        OverlayViewModel about = main.Overlay!;
        messages.Pending = "Invalid address";

        main.Poll();
        Assert.Same(about, main.Overlay);
        Assert.Equal("Invalid address", messages.Pending); // not taken yet

        about.Close();
        main.Poll();
        Assert.IsType<MessageViewModel>(main.Overlay);
    }

    [Fact]
    public async Task Confirming_the_prompt_is_not_giving_up()
    {
        bool cancelled = false;
        bool confirmed = false;
        PasswordPromptViewModel prompt = new("Hub", _ => { confirmed = true; return Task.CompletedTask; }, onCancel: () => cancelled = true);

        await prompt.ConfirmCommand.ExecuteAsync(null); // confirming closes the prompt too, which must not count as giving up

        Assert.True(confirmed);
        Assert.False(cancelled);
    }

    [Fact]
    public void Closing_the_prompt_any_other_way_is_giving_up()
    {
        bool cancelled = false;
        PasswordPromptViewModel prompt = new("Hub", _ => Task.CompletedTask, onCancel: () => cancelled = true);

        prompt.CloseCommand.Execute(null);

        Assert.True(cancelled);
    }
}
