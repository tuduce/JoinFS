using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Tests;

public class VersionKeyTests
{
    [Theory]
    [InlineData("26.2.0", "26.6.0")]
    [InlineData("26.9.0", "26.10.0")] // numbers, not text: "10" is more than "9"
    [InlineData("3.2.17", "26.3.1")] // and "3" is less than "26"
    [InlineData("26.3.1", "26.4.0")]
    [InlineData("26.6", "26.6.1")]
    [InlineData("", "0.0.1")]
    [InlineData("—", "1")]
    public void The_first_version_is_below_the_second(string lower, string higher)
    {
        Assert.True(new VersionKey(lower).CompareTo(new VersionKey(higher)) < 0);
        Assert.True(new VersionKey(higher).CompareTo(new VersionKey(lower)) > 0);
    }

    [Theory]
    [InlineData("26.6.0", "26.6.0")]
    [InlineData("26.6", "26.6.0")] // a missing number is a zero
    [InlineData("26.6.0-beta", "26.6.0")] // what follows the numbers is not looked at
    [InlineData("", "")]
    [InlineData("", null)]
    public void These_versions_are_the_same(string a, string? b) => Assert.Equal(0, new VersionKey(a).CompareTo(new VersionKey(b)));

    [Fact]
    public void A_version_sorts_with_the_others_of_its_kind_in_a_list()
    {
        string[] versions = ["3.2.17", "26.10.0", "", "26.2.0", "26.9.1", "26.6.0"];

        Assert.Equal(["", "3.2.17", "26.2.0", "26.6.0", "26.9.1", "26.10.0"], versions.OrderBy(v => new VersionKey(v)));
    }
}

public class SortDirectionTests
{
    [Fact]
    public async Task The_first_click_on_Version_in_the_hubs_table_puts_the_highest_version_first()
    {
        Rig rig = await Rig.WithHubsAsync();
        HubsViewModel hubs = rig.Main.Hubs;

        hubs.VersionColumn.SortCommand.Execute(null);

        Assert.Equal("Version ↓", hubs.VersionColumn.Header);
        List<string> versions = [.. hubs.Rows.Select(r => r.Version)];
        Assert.Equal(versions.OrderByDescending(v => new VersionKey(v)), versions);
        Assert.Equal("26.5.0", versions[0]);
        Assert.Equal("3.2.17", versions[^1]);
    }

    [Fact]
    public async Task The_second_click_on_Version_flips_it_to_the_lowest_first()
    {
        Rig rig = await Rig.WithHubsAsync();
        HubsViewModel hubs = rig.Main.Hubs;

        hubs.VersionColumn.SortCommand.Execute(null);
        hubs.VersionColumn.SortCommand.Execute(null);

        Assert.Equal("Version ↑", hubs.VersionColumn.Header);
        List<string> versions = [.. hubs.Rows.Select(r => r.Version)];
        Assert.Equal(versions.OrderBy(v => new VersionKey(v)), versions);
        Assert.Equal("3.2.17", versions[0]);
    }

    [Fact]
    public async Task The_first_click_on_Aircraft_puts_the_hub_with_most_aircraft_first()
    {
        Rig rig = await Rig.WithHubsAsync();
        HubsViewModel hubs = rig.Main.Hubs;

        hubs.AircraftColumn.SortCommand.Execute(null);

        Assert.Equal("Aircraft ↓", hubs.AircraftColumn.Header);
        Assert.Equal(hubs.Rows.Select(r => r.Aircraft).OrderDescending(), hubs.Rows.Select(r => r.Aircraft));
        Assert.Equal("Planet FsHub", hubs.Rows[0].Name);

        hubs.AircraftColumn.SortCommand.Execute(null);

        Assert.Equal("Aircraft ↑", hubs.AircraftColumn.Header);
        Assert.Equal(hubs.Rows.Select(r => r.Aircraft).Order(), hubs.Rows.Select(r => r.Aircraft));
    }

    [Fact]
    public async Task Switching_to_another_column_starts_it_in_its_own_direction()
    {
        Rig rig = await Rig.WithHubsAsync();
        HubsViewModel hubs = rig.Main.Hubs;

        hubs.AircraftColumn.SortCommand.Execute(null); // ↓
        hubs.NameColumn.SortCommand.Execute(null); // a name goes A to Z
        Assert.Equal("Name ↑", hubs.NameColumn.Header);
        Assert.Equal("Aircraft", hubs.AircraftColumn.Header);

        hubs.AircraftColumn.SortCommand.Execute(null); // back to it: highest first again, not where it was left
        Assert.Equal("Aircraft ↓", hubs.AircraftColumn.Header);
    }

    [Fact]
    public void A_column_that_starts_the_table_sorted_shows_its_own_first_direction()
    {
        SortController<(string Name, int Count)> sort = new(() => { }, initialKey: "count");
        SortColumn count = sort.Add("count", "Count", r => r.Count, highestFirst: true);

        Assert.Equal("Count ↓", count.Header);
        Assert.Equal([3, 2, 1], sort.Apply([("a", 2), ("b", 3), ("c", 1)]).Select(r => r.Count));
    }

    [Fact]
    public async Task Users_and_the_other_tables_still_start_ascending()
    {
        Rig rig = await Rig.WithHubsAsync();

        rig.Main.Hubs.UsersColumn.SortCommand.Execute(null);
        Assert.Equal("Users ↑", rig.Main.Hubs.UsersColumn.Header);

        rig.Main.Aircraft.DistanceColumn.SortCommand.Execute(null);
        Assert.Equal("Distance ↑", rig.Main.Aircraft.DistanceColumn.Header);
    }
}
