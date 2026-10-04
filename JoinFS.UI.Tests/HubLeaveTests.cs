using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Tests;

/// <summary>In the hub list, the row of the hub the network is connected to offers Leave instead of Join.</summary>
public class HubLeaveTests
{
    private static HubRowViewModel Row(Rig rig, string name) => rig.Main.Hubs.Rows.Single(r => r.Name == name);

    [Fact]
    public async Task After_joining_the_hubs_link_reads_Leave_and_the_others_still_read_Join()
    {
        Rig rig = new();

        await Row(rig, "AirSherpa").JoinCommand.ExecuteAsync(null);

        Assert.Equal("Leave", Row(rig, "AirSherpa").JoinLabel);
        Assert.All(rig.Main.Hubs.Rows.Where(r => r.Name != "AirSherpa"), r => Assert.Equal("Join", r.JoinLabel));
    }

    [Fact]
    public async Task Leave_disconnects_and_joins_nothing_else()
    {
        Rig rig = new();
        await Row(rig, "AirSherpa").JoinCommand.ExecuteAsync(null);

        await Row(rig, "AirSherpa").JoinCommand.ExecuteAsync(null); // Leave

        Assert.True(rig.Main.Network.IsDisconnected);
        Assert.Equal("Join", Row(rig, "AirSherpa").JoinLabel);
        Assert.All(rig.Main.Hubs.Rows, r => Assert.False(r.IsJoined));
    }

    [Fact]
    public async Task Leaving_a_hub_that_is_not_in_the_book_brings_the_picker_back_to_its_pick()
    {
        Rig rig = new();
        rig.Main.AddressBook.Select("swiss and europe");
        await Row(rig, "Flight Unlimited Network").JoinCommand.ExecuteAsync(null);
        rig.Main.Poll();

        await Row(rig, "Flight Unlimited Network").JoinCommand.ExecuteAsync(null); // Leave
        rig.Main.Poll();

        Assert.Null(rig.Main.AddressBook.TransientLabel);
        Assert.Equal("swiss and europe", rig.Main.AddressBook.Selected?.Name);
        Assert.True(rig.Main.Network.IsDisconnected);
    }

    [Fact]
    public async Task The_user_can_join_again_after_leaving()
    {
        Rig rig = new();
        await Row(rig, "AirSherpa").JoinCommand.ExecuteAsync(null);
        await Row(rig, "AirSherpa").JoinCommand.ExecuteAsync(null); // Leave

        await Row(rig, "AirSherpa").JoinCommand.ExecuteAsync(null);

        Assert.True(rig.Main.Network.IsConnected);
        Assert.Equal("Leave", Row(rig, "AirSherpa").JoinLabel);
    }

    [Fact]
    public async Task Joining_another_hub_moves_the_Leave_link_to_it()
    {
        Rig rig = new();
        await Row(rig, "AirSherpa").JoinCommand.ExecuteAsync(null);

        await Row(rig, "Flight Unlimited Network").JoinCommand.ExecuteAsync(null);

        Assert.Equal("Join", Row(rig, "AirSherpa").JoinLabel);
        Assert.Equal("Leave", Row(rig, "Flight Unlimited Network").JoinLabel);
    }

    [Fact]
    public async Task A_disconnect_from_the_strip_turns_Leave_back_into_Join_at_the_next_poll()
    {
        Rig rig = new();
        await Row(rig, "AirSherpa").JoinCommand.ExecuteAsync(null);

        await rig.Main.Network.ToggleCommand.ExecuteAsync(null);
        rig.Main.Poll();

        Assert.Equal("Join", Row(rig, "AirSherpa").JoinLabel);
    }

    [Fact]
    public async Task On_a_mesh_of_your_own_no_hub_reads_Leave()
    {
        Rig rig = new();
        rig.Main.AddressBook.Select("AirSherpa");

        await rig.Main.CreateMeshAsync();
        rig.Main.Hubs.SyncJoined();

        Assert.True(rig.Main.Network.IsConnected);
        Assert.All(rig.Main.Hubs.Rows, r => Assert.Equal("Join", r.JoinLabel));
    }
}
