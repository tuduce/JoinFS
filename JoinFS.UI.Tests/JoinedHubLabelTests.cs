using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Tests;

/// <summary>
/// A hub joined from the directory that is not in the address book is shown as the picker's current text, in the small window and in the
/// strip of the full view (one control), without being added to the list.
/// </summary>
public class JoinedHubLabelTests
{
    private static HubRowViewModel NotInTheBook(Rig rig) => rig.Main.Hubs.Rows.Single(r => r.Name == "Flight Unlimited Network");

    [Fact]
    public async Task Joining_a_hub_that_is_not_in_the_book_shows_its_name_without_adding_it_to_the_list()
    {
        Rig rig = new();
        string[] before = [.. rig.Main.AddressBook.Entries.Select(e => e.Name)];

        await NotInTheBook(rig).JoinCommand.ExecuteAsync(null);

        Assert.Equal("Flight Unlimited Network", rig.Main.AddressBook.TransientLabel);
        Assert.Equal(before, rig.Main.AddressBook.Entries.Select(e => e.Name)); // the list is untouched
        Assert.Null(rig.Main.AddressBook.Selected); // nothing in the list is picked while the label shows
        Assert.True(rig.Main.Network.IsConnected);
    }

    [Fact]
    public async Task Joining_a_hub_that_is_in_the_book_picks_its_entry_and_shows_no_label()
    {
        Rig rig = new();

        await rig.Main.Hubs.Rows.Single(r => r.Name == "AirSherpa").JoinCommand.ExecuteAsync(null);

        Assert.Null(rig.Main.AddressBook.TransientLabel);
        Assert.Equal("AirSherpa", rig.Main.AddressBook.Selected?.Name);
    }

    [Fact]
    public async Task Picking_from_the_list_takes_over_from_the_label()
    {
        Rig rig = new();
        await NotInTheBook(rig).JoinCommand.ExecuteAsync(null);

        rig.Main.AddressBook.Selected = rig.Main.AddressBook.Entries.Single(e => e.Name == "AirSherpa");

        Assert.Null(rig.Main.AddressBook.TransientLabel);
        Assert.Equal("AirSherpa", rig.Main.AddressBook.Selected?.Name);
    }

    [Fact]
    public async Task When_the_network_leaves_the_hub_the_picker_goes_back_to_its_pick()
    {
        Rig rig = new();
        rig.Main.AddressBook.Select("swiss and europe");
        await NotInTheBook(rig).JoinCommand.ExecuteAsync(null);
        rig.Main.Poll(); // sees the network connected

        await rig.Main.Network.ToggleCommand.ExecuteAsync(null); // Disconnect
        rig.Main.Poll();

        Assert.Null(rig.Main.AddressBook.TransientLabel);
        Assert.Equal("swiss and europe", rig.Main.AddressBook.Selected?.Name);
    }

    [Fact]
    public async Task The_label_stays_while_the_network_is_still_connecting()
    {
        Rig rig = new();
        await NotInTheBook(rig).JoinCommand.ExecuteAsync(null);
        rig.Main.Network.SetState(ConnectionState.Connecting);

        for (int i = 0; i < 20; i++)
            rig.Main.Poll();

        Assert.Equal("Flight Unlimited Network", rig.Main.AddressBook.TransientLabel);
    }

    [Fact]
    public void A_join_that_is_not_yet_seen_connecting_does_not_lose_its_label_at_the_first_poll()
    {
        // The live app can still report "disconnected" for a moment after a join was asked for.
        ScriptedLink net = new();
        AppServices services = FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }) with { Network = net };
        MainViewModel main = new(services);
        main.Hubs.Rows.Single(r => r.Name == "Flight Unlimited Network").JoinCommand.Execute(null);
        Assert.NotNull(main.AddressBook.TransientLabel);

        main.Poll();
        main.Poll();
        Assert.NotNull(main.AddressBook.TransientLabel); // still disconnected, inside the grace

        net.State = ConnectionState.Connecting;
        main.Poll();
        net.State = ConnectionState.Connected;
        main.Poll();
        Assert.NotNull(main.AddressBook.TransientLabel);

        net.State = ConnectionState.Disconnected; // the session ended
        main.Poll();
        Assert.Null(main.AddressBook.TransientLabel);
    }

    [Fact]
    public void A_join_that_never_starts_gives_the_label_up_after_the_grace()
    {
        ScriptedLink net = new();
        AppServices services = FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }) with { Network = net };
        MainViewModel main = new(services);
        main.Hubs.Rows.Single(r => r.Name == "Flight Unlimited Network").JoinCommand.Execute(null);

        for (int i = 0; i < 30; i++)
            main.Poll();

        Assert.Null(main.AddressBook.TransientLabel);
    }

    [Fact]
    public async Task Creating_a_mesh_is_not_joining_a_hub_so_the_label_goes()
    {
        Rig rig = new();
        await NotInTheBook(rig).JoinCommand.ExecuteAsync(null);

        await rig.Main.CreateMeshAsync();

        Assert.Null(rig.Main.AddressBook.TransientLabel);
    }

    [Fact]
    public async Task Home_shows_the_joined_hub()
    {
        Rig rig = new();
        await NotInTheBook(rig).JoinCommand.ExecuteAsync(null);

        Assert.Equal("Flight Unlimited Network", rig.Main.Home.HubName);
    }

    [Fact]
    public async Task The_pick_under_the_label_is_what_the_network_button_joins_next()
    {
        Rig rig = new();
        rig.Main.AddressBook.Select("AirSherpa");
        await NotInTheBook(rig).JoinCommand.ExecuteAsync(null);
        rig.Main.Poll();
        await rig.Main.Network.ToggleCommand.ExecuteAsync(null); // leave the hub
        Assert.True(rig.Main.Network.IsDisconnected);

        rig.Main.Poll();
        await rig.Main.Network.ToggleCommand.ExecuteAsync(null); // the button

        Assert.True(rig.Main.Network.IsConnected);
        Assert.Equal("AirSherpa", rig.Main.AddressBook.Selected?.Name);
    }

    [Fact]
    public async Task Reading_the_address_book_again_keeps_the_label_and_the_pick_under_it()
    {
        Rig rig = new();
        rig.Main.AddressBook.Select("AirSherpa");
        await NotInTheBook(rig).JoinCommand.ExecuteAsync(null);

        rig.Main.AddressBook.Reload();

        Assert.Equal("Flight Unlimited Network", rig.Main.AddressBook.TransientLabel);
        Assert.Equal("AirSherpa", rig.Main.AddressBook.EffectiveSelection?.Name);
    }

    [Fact]
    public async Task Showing_the_label_does_not_change_what_is_stored_as_the_pick()
    {
        Rig rig = new();
        rig.Main.AddressBook.Select("AirSherpa");
        (_, string? stored) = rig.Services.AddressBook.Load();
        Assert.Equal("AirSherpa", stored);

        await NotInTheBook(rig).JoinCommand.ExecuteAsync(null);

        Assert.Equal("AirSherpa", rig.Services.AddressBook.Load().SelectedName);
    }
}
