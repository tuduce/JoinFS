using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Overlays;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Tests;

public class HubsTests
{
    [Fact]
    public async Task Offline_hubs_are_hidden_until_asked_for_and_the_count_covers_them_all()
    {
        Rig rig = await Rig.WithHubsAsync();
        HubsViewModel hubs = rig.Main.Hubs;

        Assert.DoesNotContain(hubs.Rows, r => r.Status == "Offline");
        Assert.Equal(8, hubs.HubCount);

        hubs.ShowOfflineHubs = true;
        Assert.Contains(hubs.Rows, r => r.Status == "Offline");
    }

    [Fact]
    public async Task It_starts_sorted_by_name_and_clicking_a_header_sorts_it_then_flips_it()
    {
        Rig rig = await Rig.WithHubsAsync();
        HubsViewModel hubs = rig.Main.Hubs;
        Assert.Equal("Name ↑", hubs.NameColumn.Header);

        hubs.UsersColumn.SortCommand.Execute(null);
        Assert.Equal("Users ↑", hubs.UsersColumn.Header);
        Assert.Equal("Name", hubs.NameColumn.Header);
        Assert.Equal(hubs.Rows.Select(r => r.Users).Order(), hubs.Rows.Select(r => r.Users));

        hubs.UsersColumn.SortCommand.Execute(null);
        Assert.Equal("Users ↓", hubs.UsersColumn.Header);
        Assert.Equal(hubs.Rows.Select(r => r.Users).OrderDescending(), hubs.Rows.Select(r => r.Users));
    }

    [Fact]
    public async Task One_row_is_open_at_a_time_and_clicking_the_open_row_closes_it()
    {
        Rig rig = await Rig.WithHubsAsync();
        HubsViewModel hubs = rig.Main.Hubs;

        hubs.Rows[0].ToggleExpandedCommand.Execute(null);
        hubs.Rows[1].ToggleExpandedCommand.Execute(null);
        Assert.Equal([false, true], hubs.Rows.Take(2).Select(r => r.IsExpanded));

        hubs.Rows[1].ToggleExpandedCommand.Execute(null);
        Assert.DoesNotContain(hubs.Rows, r => r.IsExpanded);
    }

    [Fact]
    public async Task Ignoring_is_per_hub_and_the_prototypes_noise_hub_starts_ignored()
    {
        Rig rig = await Rig.WithHubsAsync();
        HubsViewModel hubs = rig.Main.Hubs;
        HubRowViewModel noise = hubs.Rows.Single(r => r.Name == "NoiseAbatement Hub");
        Assert.True(noise.IsIgnored);
        Assert.Equal("Unignore", noise.IgnoreLabel);

        noise.ToggleIgnoreCommand.Execute(null);
        Assert.Equal("Ignore", noise.IgnoreLabel);
    }

    [Fact]
    public async Task Add_to_address_book_adds_the_hub_once_with_its_real_address()
    {
        Rig rig = await Rig.WithHubsAsync();
        HubRowViewModel row = rig.Main.Hubs.Rows.Single(r => r.Name == "AirSherpa");
        AddressBookViewModel book = rig.Main.AddressBook;
        HubRowViewModel dtp = rig.Main.Hubs.Rows.Single(r => r.Name == "DigitalThemePark");

        dtp.AddToAddressBookCommand.Execute(null);
        dtp.AddToAddressBookCommand.Execute(null);
        row.AddToAddressBookCommand.Execute(null); // already there

        Assert.Single(book.Entries, e => e.Name == "DigitalThemePark");
        Assert.Equal("dtp-network.com:24192", book.Entries.Single(e => e.Name == "DigitalThemePark").Address);
        Assert.Single(book.Entries, e => e.Name == "AirSherpa");
    }

    [Fact]
    public async Task Joining_a_password_hub_asks_for_the_password_before_connecting()
    {
        Rig rig = await Rig.WithHubsAsync();
        HubRowViewModel hub = rig.Main.Hubs.Rows.Single(r => r.Name == "Aidan's Hub");

        await hub.JoinCommand.ExecuteAsync(null);

        PasswordPromptViewModel prompt = Assert.IsType<PasswordPromptViewModel>(rig.Main.Overlay);
        Assert.Equal("Aidan's Hub requires a password to join.", prompt.Message);
        Assert.True(rig.Main.Network.IsDisconnected);

        await prompt.ConfirmCommand.ExecuteAsync(null);
        Assert.Null(rig.Main.Overlay);
        Assert.True(rig.Main.Network.IsConnected);
    }

    [Fact]
    public async Task Cancelling_the_password_prompt_leaves_the_network_disconnected()
    {
        Rig rig = await Rig.WithHubsAsync();
        await rig.Main.Hubs.Rows.Single(r => r.Name == "Aidan's Hub").JoinCommand.ExecuteAsync(null);

        rig.Main.Overlay!.Close();

        Assert.Null(rig.Main.Overlay);
        Assert.True(rig.Main.Network.IsDisconnected);
    }

    [Fact]
    public async Task Joining_an_open_hub_connects_straight_away_and_picks_it_in_the_strip()
    {
        Rig rig = await Rig.WithHubsAsync();

        await rig.Main.Hubs.Rows.Single(r => r.Name == "AirSherpa").JoinCommand.ExecuteAsync(null);

        Assert.True(rig.Main.Network.IsConnected);
        Assert.Equal("AirSherpa", rig.Main.AddressBook.Selected?.Name);
    }

    [Fact]
    public async Task The_strip_network_button_joins_the_picked_hub_asking_for_a_password_if_it_needs_one()
    {
        Rig rig = new();
        rig.Main.AddressBook.Select("Aidan's Hub");

        await rig.Main.Network.ToggleCommand.ExecuteAsync(null);
        Assert.IsType<PasswordPromptViewModel>(rig.Main.Overlay);
        Assert.True(rig.Main.Network.IsDisconnected);

        rig.Main.Overlay!.Close();
        rig.Main.AddressBook.Select("Planet FsHub");
        await rig.Main.Network.ToggleCommand.ExecuteAsync(null);
        Assert.True(rig.Main.Network.IsConnected);

        await rig.Main.Network.ToggleCommand.ExecuteAsync(null);
        Assert.True(rig.Main.Network.IsDisconnected);
    }

    [Fact]
    public async Task Create_mesh_connects_and_shows_the_new_code()
    {
        Rig rig = await Rig.WithHubsAsync();
        string before = rig.Main.Hubs.MeshCode;

        await rig.Main.Hubs.CreateMeshCommand.ExecuteAsync(null);

        Assert.True(rig.Main.Network.IsConnected);
        Assert.Equal(rig.Services.Network.MeshCode, rig.Main.Hubs.MeshCode);
        Assert.NotEqual(before, rig.Main.Hubs.MeshCode);
    }
}

public class SessionTests
{
    [Fact]
    public void The_table_lists_the_peers_and_the_protocol_decides_the_legacy_flag()
    {
        Rig rig = new();
        SessionViewModel session = rig.Main.Session;

        Assert.Equal(8, session.PeerCount);
        PeerRowViewModel legacy = session.Rows.Single(r => r.Nick == "6Knotts");
        PeerRowViewModel modern = session.Rows.Single(r => r.Nick == "David18");
        Assert.True(legacy.IsLegacy);
        Assert.Equal("18.2.4", legacy.Version);
        Assert.False(modern.IsLegacy);
        Assert.Equal("26.4.0", modern.Version);
        Assert.Equal(6809 + "6Knotts".Length % 40, legacy.Port);
    }

    [Fact]
    public void Your_own_row_has_no_settings_or_actions()
    {
        SessionViewModel session = new Rig().Main.Session;

        Assert.False(session.Rows.Single(r => r.Peer.IsMe).IsOther);
        Assert.True(session.Rows.Single(r => r.Nick == "6Knotts").IsOther);
    }

    [Fact]
    public void Hand_over_controls_needs_cockpit_entry()
    {
        PeerRowViewModel peer = new Rig().Main.Session.Rows[0];
        Assert.False(peer.CanHandOverControls);

        peer.HandOverControls = true; // refused without cockpit entry
        Assert.False(peer.HandOverControls);

        peer.CockpitEntry = true;
        Assert.True(peer.CanHandOverControls);
        peer.HandOverControls = true;
        Assert.True(peer.HandOverControls);

        peer.CockpitEntry = false;
        Assert.False(peer.HandOverControls);
    }

    [Fact]
    public void What_you_tick_goes_to_the_service_and_comes_back_on_the_next_refresh()
    {
        Rig rig = new();
        SessionViewModel session = rig.Main.Session;
        PeerRowViewModel peer = session.Rows[0];

        peer.CockpitEntry = true;
        peer.HandOverControls = true;
        peer.MultipleObjects = true;

        PeerSettings stored = rig.Services.Session.GetSettings(peer.Id);
        Assert.Equal((true, true, true), (stored.CockpitEntry, stored.HandOverControls, stored.MultipleObjects));

        session.RefreshCommand.Execute(null);
        Assert.Equal((true, true, true), (peer.CockpitEntry, peer.HandOverControls, peer.MultipleObjects));
    }

    [Fact]
    public void A_refresh_does_not_write_what_it_reads_back_to_the_service()
    {
        ScriptedSession source = new();
        SessionViewModel session = new(source);
        source.Settings["a"] = new PeerSettings(true, true, true, false, true);

        session.Refresh();

        Assert.Empty(source.Writes);
        PeerRowViewModel row = session.Rows.Single();
        Assert.Equal((true, true, true, true), (row.CockpitEntry, row.HandOverControls, row.MultipleObjects, row.IsIgnored));
    }

    [Fact]
    public void A_refresh_updates_rows_in_place_adds_new_users_and_drops_the_ones_gone()
    {
        ScriptedSession source = new();
        SessionViewModel session = new(source);
        PeerRowViewModel a = session.Rows.Single();
        a.ToggleExpandedCommand.Execute(null);

        source.Peers = [source.Peer("b", "Bravo"), source.Peer("a", "Alpha 2", latency: 99), source.Peer("c", "Charlie")];
        session.Refresh();

        Assert.Equal(["b", "a", "c"], session.Rows.Select(r => r.Id));
        Assert.Same(a, session.Rows[1]); // the same row, not a new one
        Assert.True(a.IsExpanded);
        Assert.Equal(("Alpha 2", 99), (a.Nick, a.Latency));
        Assert.Equal(3, session.PeerCount);

        source.Peers = [source.Peer("c", "Charlie")];
        session.Refresh();

        Assert.Equal(["c"], session.Rows.Select(r => r.Id));
    }

    [Fact]
    public void Ignore_goes_to_the_service_and_the_row_shows_what_the_service_says()
    {
        Rig rig = new();
        PeerRowViewModel peer = rig.Main.Session.Rows[0];

        peer.ToggleIgnoreCommand.Execute(null);

        Assert.True(peer.IsIgnored);
        Assert.Equal("Unignore", peer.IgnoreLabel);
        Assert.True(rig.Services.Session.GetSettings(peer.Id).IsIgnored);
    }

    [Fact]
    public void Save_adds_the_user_to_the_address_book_and_the_pickers_list_shows_it()
    {
        Rig rig = new();
        PeerRowViewModel peer = rig.Main.Session.Rows.Single(r => r.Nick == "CarGuy86");
        Assert.Equal("Save", peer.SaveLabel);
        Assert.DoesNotContain(rig.Main.AddressBook.Entries, e => e.Name == "CarGuy86");

        peer.ToggleSaveCommand.Execute(null);

        Assert.True(peer.IsSaved);
        Assert.Equal("Remove From Address Book", peer.SaveLabel);
        Assert.Contains(rig.Main.AddressBook.Entries, e => e.Name == "CarGuy86");

        peer.ToggleSaveCommand.Execute(null);
        Assert.False(peer.IsSaved);
        Assert.DoesNotContain(rig.Main.AddressBook.Entries, e => e.Name == "CarGuy86");
    }

    [Fact]
    public void Reloading_the_address_book_keeps_the_picked_hub_and_writes_nothing()
    {
        Rig rig = new();
        rig.Main.AddressBook.Select("AirSherpa");
        (IReadOnlyList<AddressBookEntry> before, _) = rig.Services.AddressBook.Load();

        rig.Main.AddressBook.Reload();

        Assert.Equal("AirSherpa", rig.Main.AddressBook.Selected?.Name);
        Assert.Equal(before.Count, rig.Main.AddressBook.Entries.Count);
    }

    [Fact]
    public void A_poll_reads_the_session_only_while_the_tab_is_open_and_only_every_fourth_time()
    {
        ScriptedSession source = new();
        AppServices services = JoinFS.UI.Services.Fake.FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }) with { Session = source };
        MainViewModel main = new(services);
        int atStart = source.Reads;

        for (int i = 0; i < 8; i++)
            main.Poll();
        Assert.Equal(atStart, source.Reads); // the tab is not open

        main.GoTo(TabId.Session);
        int opened = source.Reads;
        Assert.True(opened > atStart); // opening it reads at once

        for (int i = 0; i < 8; i++)
            main.Poll();
        Assert.Equal(opened + 2, source.Reads);
    }
}

/// <summary>A session source the test scripts: users it returns, settings it holds, and what the UI wrote to it.</summary>
public sealed class ScriptedSession : ISessionSource
{
    public List<PeerInfo> Peers { get; set; } = [new PeerInfo("a", "Alpha", "A1", "Yes", 10, 1, 0, "MSFS", "26.6.0", "JFP2", 6112)];
    public Dictionary<string, PeerSettings> Settings { get; } = [];
    public List<string> Writes { get; } = [];
    public int Reads { get; private set; }

    public PeerInfo Peer(string id, string nick, int latency = 10) => new(id, nick, "", "Yes", latency, 1, 0, "MSFS", "26.6.0", "JFP2", 6112);

    public IReadOnlyList<PeerInfo> GetPeers()
    {
        Reads++;
        return Peers;
    }

    public PeerSettings GetSettings(string peerId) => Settings.GetValueOrDefault(peerId) ?? new PeerSettings(false, false, false, false, false);
    public void SetCockpitEntry(string peerId, bool allowed) => Writes.Add("cockpit " + peerId);
    public void SetHandOverControls(string peerId, bool handedOver) => Writes.Add("handover " + peerId);
    public void SetMultipleObjects(string peerId, bool allowed) => Writes.Add("multiple " + peerId);
    public void SetSaved(string peerId, bool saved) => Writes.Add("saved " + peerId);
    public void SetIgnored(string peerId, bool ignored) => Writes.Add("ignored " + peerId);
}

public class AircraftAndObjectsTests
{
    private static ActionLink Link(AircraftRowViewModel row, string label) =>
        row.Actions.Single(l => l.Label == label || l.Label.StartsWith(label, StringComparison.Ordinal));

    [Fact]
    public void Aircraft_sorts_by_a_column_and_numbers_sort_as_numbers()
    {
        AircraftViewModel aircraft = new Rig().Main.Aircraft;
        Assert.Equal(8, aircraft.AircraftCount);

        aircraft.AltitudeColumn.SortCommand.Execute(null);

        Assert.Equal(aircraft.Rows.Select(r => r.Info.AltitudeFt).Order(), aircraft.Rows.Select(r => r.Info.AltitudeFt));
        Assert.Equal("Altitude ↑", aircraft.AltitudeColumn.Header);
    }

    [Fact]
    public void Aircraft_beyond_3000_nm_are_flagged_far_and_a_refused_one_is_red_instead()
    {
        Rig rig = new();
        AircraftViewModel aircraft = rig.Main.Aircraft;

        Assert.False(aircraft.Rows.Single(r => r.Callsign == "9H-WDR").IsFar);
        Assert.True(aircraft.Rows.Single(r => r.Callsign == "ASXGS").IsFar);

        AircraftInfo far = aircraft.Rows.Single(r => r.Callsign == "ASXGS").Info;
        AircraftRowViewModel failed = new Rig().Main.Aircraft.Rows[0];
        failed.Update(far with { Link = AircraftLinkState.Failed });
        Assert.True(failed.IsFailed);
        Assert.False(failed.IsFar);
    }

    [Fact]
    public void Values_that_are_not_known_show_as_a_dash_and_sort_before_the_known_ones()
    {
        AircraftRowViewModel row = new Rig().Main.Aircraft.Rows[0];

        row.Update(row.Info with { DistanceNm = null, Heading = null, AltitudeFt = null, Bearing = null });

        Assert.Equal(("-", "-", "-", "-"), (row.Distance, row.Heading, row.Altitude, row.Bearing));
        Assert.True(row.DistanceValue < 0 && row.HeadingValue < 0);
    }

    [Fact]
    public void Speed_is_in_knots_and_in_mach_from_600()
    {
        AircraftRowViewModel row = new Rig().Main.Aircraft.Rows[0];

        row.Update(row.Info with { SpeedKnots = 263 });
        Assert.EndsWith(" kt", row.GroundSpeed);

        row.Update(row.Info with { SpeedKnots = 1334 });
        Assert.EndsWith(" M", row.GroundSpeed);
        Assert.StartsWith("2", row.GroundSpeed);
    }

    [Fact]
    public void An_expanded_row_lists_every_action_in_the_designs_order()
    {
        AircraftRowViewModel row = new Rig().Main.Aircraft.Rows[1];

        Assert.Equal(15, row.Actions.Count);
        Assert.Equal(
            ["Substitute…", "Explain Match…", "Copy Flight Plan…", "Assign Variables…", "Adjust Height…", "Follow '9H-WDR'", "Enter Cockpit",
             "Track Heading On Hdg", "Track Bearing On Hdg", "Copy Weather", "Remove From Recorder", "Include All Hub Aircraft",
             "Include All Simulator Aircraft", "Ignore", "Stop Tracking"],
            row.Actions.Select(a => a.Label));
    }

    [Fact]
    public void Only_the_actions_the_service_allows_can_be_used()
    {
        AircraftRowViewModel row = new Rig().Main.Aircraft.Rows[1];
        Assert.All(row.Actions.Take(14), a => Assert.True(a.Command.CanExecute(null), a.Label));
        Assert.False(Link(row, "Stop Tracking").Command.CanExecute(null)); // nothing is tracked

        row.Update(row.Info with { Can = AircraftActions.Ignore | AircraftActions.Record });

        Assert.False(Link(row, "Substitute").Command.CanExecute(null));
        Assert.False(Link(row, "Follow").Command.CanExecute(null));
        Assert.False(Link(row, "Enter Cockpit").Command.CanExecute(null));
        Assert.False(Link(row, "Track Heading").Command.CanExecute(null));
        Assert.True(Link(row, "Ignore").Command.CanExecute(null));
        Assert.True(Link(row, "Remove From Recorder").Command.CanExecute(null));
        Assert.True(Link(row, "Include All Hub Aircraft").Command.CanExecute(null)); // a list filter, always there
    }

    [Fact]
    public void An_aircraft_you_cannot_record_has_its_box_disabled_and_ignores_the_tick()
    {
        AircraftRowViewModel row = new Rig().Main.Aircraft.Rows.First(r => !r.Recording);
        row.Update(row.Info with { Can = AircraftActions.None });
        Assert.False(row.CanRecord);

        row.Recording = true;

        Assert.False(row.Recording);
    }

    [Fact]
    public void Follow_enter_cockpit_and_the_tracking_links_go_to_the_service()
    {
        Rig rig = new();
        AircraftViewModel aircraft = rig.Main.Aircraft;
        AircraftRowViewModel row = aircraft.Rows.Single(r => r.Callsign == "9H-WDR");

        Link(row, "Enter Cockpit").Command.Execute(null);
        aircraft.Refresh();
        Assert.True(rig.Services.Traffic.InCockpit);
        Assert.Equal("Leave Cockpit", Link(row, "Leave Cockpit").Label);

        Link(row, "Track Heading").Command.Execute(null);
        aircraft.Refresh();
        Assert.True(row.IsTracked);
        Assert.True(Link(row, "Stop Tracking").Command.CanExecute(null));

        Link(row, "Stop Tracking").Command.Execute(null);
        aircraft.Refresh();
        Assert.False(row.IsTracked);
        Assert.False(Link(row, "Stop Tracking").Command.CanExecute(null));
    }

    [Fact]
    public void Ignore_goes_to_the_service_and_the_link_words_itself_by_it()
    {
        Rig rig = new();
        AircraftRowViewModel row = rig.Main.Aircraft.Rows[1];

        Link(row, "Ignore").Command.Execute(null);

        Assert.True(row.IsIgnored);
        Assert.Equal("Unignore", row.Actions[^2].Label);
        Assert.Contains(rig.Services.Traffic.GetAircraft(), a => a.Id == row.Id && a.Ignored);
    }

    [Fact]
    public void Recording_is_written_to_the_service_and_a_refresh_does_not_write_it_back()
    {
        ScriptedTraffic traffic = new();
        AircraftViewModel aircraft = new(traffic, new JoinFS.UI.Services.Fake.FakeModelCatalog(), new JoinFS.UI.Services.Fake.NullPlatform(),
            new ProfileViewModel(new JoinFS.UI.Services.Fake.InMemorySettingsStore()), new RecordSelection(), new NullShell());
        AircraftRowViewModel row = aircraft.Rows.Single();
        Assert.Empty(traffic.Writes); // reading the list wrote nothing

        row.Recording = true;
        Assert.Equal(["record a=True"], traffic.Writes);

        traffic.Aircraft = [traffic.Info("a") with { Recording = false }];
        aircraft.Refresh();
        Assert.False(row.Recording);
        Assert.Equal(["record a=True"], traffic.Writes); // the refresh took the service's word and wrote nothing
    }

    [Fact]
    public void A_refresh_updates_rows_in_place_adds_new_aircraft_and_drops_the_ones_gone()
    {
        ScriptedTraffic traffic = new();
        AircraftViewModel aircraft = new(traffic, new JoinFS.UI.Services.Fake.FakeModelCatalog(), new JoinFS.UI.Services.Fake.NullPlatform(),
            new ProfileViewModel(new JoinFS.UI.Services.Fake.InMemorySettingsStore()), new RecordSelection(), new NullShell());
        AircraftRowViewModel a = aircraft.Rows.Single();
        a.ToggleExpandedCommand.Execute(null);

        traffic.Aircraft = [traffic.Info("b"), traffic.Info("a") with { AltitudeFt = 9999 }, traffic.Info("c")];
        aircraft.Refresh();

        Assert.Equal(["b", "a", "c"], aircraft.Rows.Select(r => r.Id));
        Assert.Same(a, aircraft.Rows[1]);
        Assert.True(a.IsExpanded);
        Assert.Contains("9,999", a.Altitude.Replace(".", ",").Replace(" ", ","));

        traffic.Aircraft = [traffic.Info("c")];
        aircraft.Refresh();
        Assert.Equal(["c"], aircraft.Rows.Select(r => r.Id));
    }

    [Fact]
    public void The_two_include_links_are_list_filters_and_add_the_other_aircraft()
    {
        Rig rig = new();
        AircraftViewModel aircraft = rig.Main.Aircraft;
        AircraftRowViewModel any = aircraft.Rows[0];
        Assert.Equal(8, aircraft.AircraftCount);

        Link(any, "Include All Hub Aircraft").Command.Execute(null);
        Assert.True(rig.Services.Traffic.IncludeHubAircraft);
        Assert.Equal(10, aircraft.AircraftCount);
        Assert.Equal("Exclude Hub Aircraft", Link(any, "Exclude Hub Aircraft").Label);

        Link(any, "Include All Simulator Aircraft").Command.Execute(null);
        Assert.Equal(12, aircraft.AircraftCount);

        Link(any, "Exclude Hub Aircraft").Command.Execute(null);
        Link(any, "Exclude Simulator Aircraft").Command.Execute(null);
        Assert.Equal(8, aircraft.AircraftCount);
    }

    [Fact]
    public void The_row_actions_open_the_shared_overlays_on_the_right_model()
    {
        Rig rig = new();
        AircraftRowViewModel row = rig.Main.Aircraft.Rows.Single(r => r.Callsign == "A320");

        Link(row, "Substitute").Command.Execute(null);
        SubstituteViewModel substitute = Assert.IsType<SubstituteViewModel>(rig.Main.Overlay);
        Assert.Equal("GC1a Swift (Factory)", substitute.Original); // the livery suffix is dropped

        Link(row, "Explain Match").Command.Execute(null);
        Assert.Equal("GC1a Swift (Factory) (D)", Assert.IsType<ExplainMatchViewModel>(rig.Main.Overlay).Model);

        Link(row, "Adjust Height").Command.Execute(null);
        Assert.IsType<AdjustHeightViewModel>(rig.Main.Overlay);

        Link(row, "Assign Variables").Command.Execute(null);
        Assert.IsType<VariablesOverlayViewModel>(rig.Main.Overlay);
    }

    [Fact]
    public void Copy_flight_plan_puts_the_plan_on_the_clipboard()
    {
        Rig rig = new();

        Link(rig.Main.Aircraft.Rows.Single(r => r.Callsign == "9H-WDR"), "Copy Flight Plan").Command.Execute(null);

        Assert.Equal(["9H-WDR — VFR, 8 nm route"], rig.Platform.Copied);
    }

    [Fact]
    public void A_poll_reads_the_aircraft_only_while_the_tab_is_open()
    {
        ScriptedTraffic traffic = new();
        AppServices services = JoinFS.UI.Services.Fake.FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }) with { Traffic = traffic };
        MainViewModel main = new(services);
        int atStart = traffic.Reads;

        for (int i = 0; i < 8; i++)
            main.Poll();
        Assert.Equal(atStart, traffic.Reads);

        main.GoTo(TabId.Aircraft);
        int opened = traffic.Reads;
        Assert.True(opened > atStart);
        for (int i = 0; i < 8; i++)
            main.Poll();
        Assert.Equal(opened + 2, traffic.Reads);
    }

    [Fact]
    public void Ignored_objects_are_hidden_unless_listed()
    {
        ObjectsViewModel objects = new Rig().Main.Objects;
        Assert.Equal(4, objects.Rows.Count); // two of six are ignored in the sample

        objects.ListIgnoredObjects = true;
        Assert.Equal(6, objects.Rows.Count);
    }

    [Fact]
    public void Ticking_ignore_owner_hides_the_row()
    {
        ObjectsViewModel objects = new Rig().Main.Objects;
        ObjectRowViewModel row = objects.Rows[0];

        row.IgnoreOwner = true;

        Assert.DoesNotContain(row, objects.Rows);
    }

    [Fact]
    public void Clicking_an_object_opens_its_row_and_clicking_it_again_closes_it()
    {
        ObjectsViewModel objects = new Rig().Main.Objects;

        objects.Rows[0].SelectCommand.Execute(null);
        objects.Rows[1].SelectCommand.Execute(null);
        Assert.Equal([false, true], objects.Rows.Take(2).Select(r => r.IsSelected));

        objects.Rows[1].SelectCommand.Execute(null);
        Assert.DoesNotContain(objects.Rows, r => r.IsSelected);
        Assert.Null(objects.SelectedRow);
    }

    [Fact]
    public void An_open_object_offers_substitute_and_the_four_broadcast_options_of_the_old_dialog()
    {
        ObjectRowViewModel row = new Rig().Main.Objects.Rows.First(r => !r.Broadcast);

        Assert.Equal(
            ["Substitute…", "Broadcast This Object", $"Broadcast All '{row.Model}'", "Broadcast VRS TacPack", "Broadcast Everything"],
            row.Actions.Select(a => a.Label));
    }

    [Fact]
    public void Each_broadcast_link_toggles_its_state_and_words_itself_by_it()
    {
        Rig rig = new();
        ObjectsViewModel objects = rig.Main.Objects;
        ObjectRowViewModel row = objects.Rows.First(r => !r.Broadcast);
        ActionLink thisObject = row.Actions[1], model = row.Actions[2], tacpack = row.Actions[3], everything = row.Actions[4];

        thisObject.Command.Execute(null);
        Assert.True(row.Broadcast);
        Assert.Equal("Stop Broadcasting This Object", thisObject.Label);

        model.Command.Execute(null);
        Assert.Equal($"Stop Broadcasting All '{row.Model}'", model.Label);
        model.Command.Execute(null);
        Assert.Equal($"Broadcast All '{row.Model}'", model.Label);

        tacpack.Command.Execute(null);
        everything.Command.Execute(null);
        Assert.Equal("Stop Broadcasting VRS TacPack", tacpack.Label);
        Assert.Equal("Stop Broadcasting Everything", everything.Label);
        Assert.True(rig.Settings.BroadcastTacpack);
        Assert.True(rig.Settings.BroadcastEverything);
    }

    [Fact]
    public void The_model_link_covers_every_row_of_that_model()
    {
        ObjectsViewModel objects = new Rig().Main.Objects;
        objects.ListIgnoredObjects = true; // two rows of this model are ignored in the sample
        ObjectRowViewModel[] same = [.. objects.Rows.Where(r => r.Model == "GC1a Swift (Factory) (D)")];
        Assert.True(same.Length > 1);

        same[0].Actions[2].Command.Execute(null);

        Assert.All(same, r => Assert.StartsWith("Stop Broadcasting All", r.Actions[2].Label));
    }

    [Fact]
    public void The_tacpack_link_and_the_settings_checkbox_are_the_same_setting()
    {
        Rig rig = new();
        ObjectRowViewModel row = rig.Main.Objects.Rows[0];

        rig.Main.Profile.BroadcastTacpack = true;

        Assert.Equal("Stop Broadcasting VRS TacPack", row.Actions[3].Label);
        row.Actions[3].Command.Execute(null);
        Assert.False(rig.Main.Profile.BroadcastTacpack);
    }

    [Fact]
    public void While_grouped_by_model_broadcasting_a_single_object_is_unavailable()
    {
        ObjectsViewModel objects = new Rig().Main.Objects;
        ActionLink thisObject = objects.Rows[0].Actions[1];
        Assert.True(thisObject.Command.CanExecute(null));

        objects.GroupByModel = true;
        Assert.False(thisObject.Command.CanExecute(null));
    }

    [Fact]
    public void The_table_checkbox_and_the_this_object_link_agree()
    {
        ObjectRowViewModel row = new Rig().Main.Objects.Rows.First(r => !r.Broadcast);

        row.Broadcast = true;

        Assert.Equal("Stop Broadcasting This Object", row.Actions[1].Label);
    }

    [Fact]
    public void Substitute_needs_a_selected_row()
    {
        Rig rig = new();
        ObjectsViewModel objects = rig.Main.Objects;
        Assert.False(objects.SubstituteCommand.CanExecute(null));

        objects.Rows[0].SelectCommand.Execute(null);
        Assert.True(objects.SubstituteCommand.CanExecute(null));
        objects.SubstituteCommand.Execute(null);

        Assert.IsType<SubstituteViewModel>(rig.Main.Overlay);
    }
}

public class ModelMatchingTests
{
    [Fact]
    public void The_defaults_are_listed_and_cannot_be_removed()
    {
        ModelMatchingViewModel models = new Rig().Main.ModelMatching;

        Assert.Equal(8, models.Rows.Count);
        Assert.All(models.Rows, r => Assert.False(r.IsRemovable));
    }

    [Fact]
    public void Saving_a_substitution_changes_the_row()
    {
        Rig rig = new();
        ModelRuleRowViewModel row = rig.Main.ModelMatching.Rows[0];
        row.EditCommand.Execute(null);
        SubstituteViewModel edit = (SubstituteViewModel)rig.Main.Overlay!;

        edit.SelectedType = "Latecoere 631";
        edit.SelectedVariation = "Livery A";
        Assert.Equal("Latecoere 631 [+] Livery A", edit.Preview);
        edit.SaveCommand.Execute(null);

        Assert.Null(rig.Main.Overlay);
        Assert.Equal("Latecoere 631 [+] Livery A", rig.Main.ModelMatching.Rows[0].Substitute);
        Assert.Equal("Latecoere 631 [+] Livery A", rig.Settings.ModelOverrides["Default SingleProp"]);
    }

    [Fact]
    public void Use_original_makes_the_model_stand_in_for_itself()
    {
        Rig rig = new();
        rig.Main.ModelMatching.Rows[1].EditCommand.Execute(null);

        ((SubstituteViewModel)rig.Main.Overlay!).UseOriginalCommand.Execute(null);

        Assert.Equal("Default TwinProp", rig.Main.ModelMatching.Rows[1].Substitute);
    }

    [Fact]
    public void A_substitution_for_an_aircraft_model_becomes_a_removable_row()
    {
        Rig rig = new();
        rig.Main.Aircraft.Rows[0].Actions[0].Command.Execute(null); // Substitute…
        ((SubstituteViewModel)rig.Main.Overlay!).SaveCommand.Execute(null);

        ModelRuleRowViewModel added = rig.Main.ModelMatching.Rows.Last();
        Assert.True(added.IsRemovable);
        Assert.Equal("GC1a Swift", added.Original); // the design drops the trailing "(…)" to get the original model

        added.RemoveCommand.Execute(null);
        Assert.Equal(8, rig.Main.ModelMatching.Rows.Count);
    }

    [Fact]
    public void Filter_words_narrow_the_type_list_to_entries_with_every_word()
    {
        Rig rig = new();
        SubstituteViewModel edit = new("X", "X", rig.Services.Models, rig.Main.Profile);

        edit.FilterWords = "pmdg 777";
        Assert.Equal(["PMDG 777-200ER GE PMDG House"], edit.Types);

        edit.FilterWords = "";
        Assert.True(edit.Types.Count > 1);
    }

    [Fact]
    public void A_substitute_that_is_not_in_the_catalogue_stays_selectable()
    {
        Rig rig = new();

        SubstituteViewModel edit = new("X", "Some Custom Model [+] Livery B", rig.Services.Models, rig.Main.Profile);

        Assert.Equal("Some Custom Model", edit.SelectedType);
        Assert.Equal("Livery B", edit.SelectedVariation);
    }
}

public class AdjustHeightTests
{
    [Fact]
    public void The_steppers_add_and_off_resets_with_the_label_following()
    {
        Rig rig = new();
        AdjustHeightViewModel height = new("M", rig.Main.Profile);
        Assert.Equal("Off", height.AdjustmentLabel);

        height.Up50Command.Execute(null);
        height.Up5Command.Execute(null);
        Assert.Equal("+55 cm", height.AdjustmentLabel);

        height.OffCommand.Execute(null);
        height.Down5Command.Execute(null);
        height.Down50Command.Execute(null);
        Assert.Equal("-55 cm", height.AdjustmentLabel);
    }

    [Fact]
    public void Ok_keeps_the_adjustment_and_cancel_throws_it_away()
    {
        Rig rig = new();
        AdjustHeightViewModel cancelled = new("M", rig.Main.Profile);
        cancelled.Up50Command.Execute(null);
        cancelled.CloseCommand.Execute(null);
        Assert.Equal(0, rig.Main.Profile.GetHeightAdjustmentCm("M"));

        AdjustHeightViewModel confirmed = new("M", rig.Main.Profile);
        confirmed.Up5Command.Execute(null);
        confirmed.OkCommand.Execute(null);
        Assert.Equal(5, rig.Main.Profile.GetHeightAdjustmentCm("M"));
        Assert.Equal(5, new AdjustHeightViewModel("M", rig.Main.Profile).AdjustmentCm);
    }
}

public class FlightPlanTests
{
    [Fact]
    public void With_no_stored_username_import_asks_once_then_remembers_and_imports()
    {
        Rig rig = new();
        FlightPlanViewModel plan = rig.Main.FlightPlan;

        plan.ImportFromSimbriefCommand.Execute(null);
        SimbriefPromptViewModel prompt = Assert.IsType<SimbriefPromptViewModel>(rig.Main.Overlay);
        Assert.False(prompt.ImportCommand.CanExecute(null));

        prompt.Username = " HBTDXnav ";
        prompt.ImportCommand.Execute(null);

        Assert.Null(rig.Main.Overlay);
        Assert.Equal("HBTDXnav", rig.Settings.SimbriefUsername);
        Assert.Equal("LSZH DCT KLO DCT LSGG", plan.Route);
        Assert.Equal("Imported from SimBrief", plan.Remarks);
        Assert.True(rig.Main.FlightPlanLoad.IsConnected);

        plan.ImportFromSimbriefCommand.Execute(null);
        Assert.Null(rig.Main.Overlay); // never asked again
    }

    [Fact]
    public void An_import_keeps_the_aircrafts_callsign()
    {
        Rig rig = new(settings: new Services.UserSettings { Onboarded = true, SimbriefUsername = "me" });

        rig.Main.FlightPlan.ImportFromSimbriefCommand.Execute(null);

        Assert.Equal("HB-TDX", rig.Main.FlightPlan.Callsign);
        Assert.Equal("LSGG", rig.Main.FlightPlan.To);
    }

    [Fact]
    public async Task The_strip_button_fetches_straight_away_with_a_stored_username()
    {
        Rig rig = new(settings: new Services.UserSettings { Onboarded = true, SimbriefUsername = "me" });

        await rig.Main.FlightPlanLoad.ToggleCommand.ExecuteAsync(null);

        Assert.True(rig.Main.FlightPlanLoad.IsConnected);
        Assert.Equal("Loaded", rig.Main.FlightPlanLoad.StateLabel);
    }

    [Fact]
    public async Task The_strip_button_without_a_username_asks_and_ends_not_loaded_until_the_import_finishes()
    {
        Rig rig = new();

        await rig.Main.FlightPlanLoad.ToggleCommand.ExecuteAsync(null);

        Assert.IsType<SimbriefPromptViewModel>(rig.Main.Overlay);
        Assert.True(rig.Main.FlightPlanLoad.IsDisconnected);

        ((SimbriefPromptViewModel)rig.Main.Overlay!).Username = "me";
        ((SimbriefPromptViewModel)rig.Main.Overlay!).ImportCommand.Execute(null);
        Assert.True(rig.Main.FlightPlanLoad.IsConnected);
    }

    [Fact]
    public void Clear_blanks_everything_but_the_rules_which_go_back_to_vfr_and_save_stores_the_plan()
    {
        Rig rig = new();
        FlightPlanViewModel plan = rig.Main.FlightPlan;
        plan.Rules = "IFR";
        plan.Route = "A B";

        plan.SaveCommand.Execute(null);
        Assert.Equal("A B", rig.Services.FlightPlan.Load().Route);

        plan.ClearCommand.Execute(null);
        Assert.Equal(("", "", "VFR", ""), (plan.Callsign, plan.Type, plan.Rules, plan.Route));
    }
}

public class RecorderTests
{
    [Fact]
    public void Record_play_and_overdub_switch_each_other_off_and_stop_clears_all()
    {
        RecorderViewModel recorder = new Rig().Main.Recorder;

        recorder.ToggleRecordingCommand.Execute(null);
        Assert.True(recorder.IsRecording);

        recorder.TogglePlayCommand.Execute(null);
        Assert.True(recorder.IsPlaying);
        Assert.False(recorder.IsRecording);

        recorder.ToggleOverdubCommand.Execute(null);
        Assert.True(recorder.IsOverdubbing);
        Assert.True(recorder.IsRecording); // overdub is recording on top of the take
        Assert.False(recorder.IsPlaying);

        recorder.ToggleOverdubCommand.Execute(null);
        Assert.Equal(RecorderMode.Idle, recorder.Mode);

        recorder.ToggleRecordingCommand.Execute(null);
        recorder.StopCommand.Execute(null);
        Assert.Equal(RecorderMode.Idle, recorder.Mode);
    }

    [Fact]
    public void The_readout_is_hh_mm_ss_over_the_total()
    {
        RecorderViewModel recorder = new Rig().Main.Recorder;

        Assert.Equal("00:00:07", recorder.PlayheadText);
        Assert.Equal("00:01:32", recorder.TotalText);
        Assert.Equal("01:01:01", RecorderViewModel.FormatTime(3661));
    }

    [Fact]
    public void Seeking_moves_the_playhead_in_whole_seconds_and_clamps()
    {
        RecorderViewModel recorder = new Rig().Main.Recorder;

        recorder.PlayheadFraction = 0.5;
        Assert.Equal(46, recorder.PlayheadSeconds);

        recorder.Seek(2);
        Assert.Equal(92, recorder.PlayheadSeconds);
        recorder.Seek(-1);
        Assert.Equal(0, recorder.PlayheadSeconds);
    }

    [Fact]
    public void Trim_start_cuts_before_the_playhead_and_trim_end_cuts_after_it()
    {
        RecorderViewModel recorder = new Rig().Main.Recorder;

        recorder.TrimStartCommand.Execute(null); // playhead 7 of 92
        Assert.Equal((85, 0), (recorder.TotalSeconds, recorder.PlayheadSeconds));

        recorder.Seek(0.4); // 34
        recorder.TrimEndCommand.Execute(null);
        Assert.Equal((34, 34), (recorder.TotalSeconds, recorder.PlayheadSeconds));
    }

    [Fact]
    public void A_trim_never_leaves_an_empty_recording()
    {
        RecorderViewModel recorder = new Rig().Main.Recorder;
        recorder.Seek(0);

        recorder.TrimEndCommand.Execute(null);

        Assert.Equal((1, 1), (recorder.TotalSeconds, recorder.PlayheadSeconds));
    }
}

public class SettingsTests
{
    [Fact]
    public void The_sections_start_closed_and_the_x_plane_card_is_only_in_the_xplane_build()
    {
        SettingsViewModel normal = new Rig().Main.Settings;
        Assert.Equal(["Simulator", "User Interface", "Network", "Hub Mode (Public)", "Address Book", "Variables"], normal.Sections.Select(s => s.Title));
        Assert.All(normal.Sections, s => Assert.False(s.IsOpen));
        Assert.Null(normal.OpenSection);
        Assert.False(normal.IsXPlaneBuild);
        Assert.False(normal.Simulator.IsXPlaneBuild);

        SettingsViewModel xplane = new Rig(xplaneBuild: true).Main.Settings;
        Assert.Equal(7, xplane.Sections.Count);
        Assert.Contains(xplane.XPlane, xplane.Sections);
        Assert.True(xplane.Simulator.IsXPlaneBuild);
    }

    [Fact]
    public void Install_plugin_opens_the_install_overlay_and_install_c_plus_plus_opens_the_download()
    {
        Rig rig = new(xplaneBuild: true);

        rig.Main.Settings.XPlane.InstallPluginCommand.Execute(null);
        Assert.IsType<InstallXPlanePluginViewModel>(rig.Main.Overlay);

        rig.Main.Settings.XPlane.InstallCppCommand.Execute(null);
        Assert.Equal(["https://aka.ms/vs/17/release/VC_redist.x64.exe"], rig.Platform.OpenedUrls);
    }

    [Fact]
    public async Task The_install_overlay_starts_on_the_saved_folder_needs_one_and_installs_into_it()
    {
        Rig rig = new(xplaneBuild: true);
        InstallXPlanePluginViewModel install = new(rig.Services.XPlanePlugin, rig.Platform);
        Assert.Equal(@"C:\X-Plane 12", install.Folder);

        install.Folder = "  ";
        Assert.False(install.InstallCommand.CanExecute(null));

        install.Folder = @" D:\Games\X-Plane 12 ";
        await install.InstallCommand.ExecuteAsync(null);

        Assert.Equal(@"D:\Games\X-Plane 12", rig.Services.XPlanePlugin.SavedFolder);
    }

    [Fact]
    public async Task A_failed_install_stays_open_and_says_why()
    {
        Rig rig = new(xplaneBuild: true);
        InstallXPlanePluginViewModel install = new(new FailingInstaller(), rig.Platform);
        rig.Main.ShowOverlay(install);

        await install.InstallCommand.ExecuteAsync(null);

        Assert.Same(install, rig.Main.Overlay);
        Assert.Equal("Access denied", install.Error);
    }

    private sealed class FailingInstaller : JoinFS.UI.Services.IXPlanePluginInstaller
    {
        public string SavedFolder => "C:\\X-Plane";
        public Task InstallAsync(string folder, CancellationToken cancellationToken) => throw new IOException("Access denied");
    }

    [Fact]
    public void Opening_one_closes_the_other_and_clicking_the_open_one_closes_it()
    {
        SettingsViewModel settings = new Rig().Main.Settings;

        settings.Network.ToggleCommand.Execute(null);
        settings.HubMode.ToggleCommand.Execute(null);
        Assert.Same(settings.HubMode, settings.OpenSection);
        Assert.False(settings.Network.IsOpen);

        settings.HubMode.ToggleCommand.Execute(null);
        Assert.Null(settings.OpenSection);
    }

    [Fact]
    public void The_whazzup_sub_options_follow_the_whazzup_box()
    {
        NetworkSettingsViewModel network = new Rig().Main.Settings.Network;
        Assert.True(network.GenerateWhazzup);
        Assert.True(network.WhazzupOptionsEnabled);

        network.GenerateWhazzup = false;
        Assert.False(network.WhazzupOptionsEnabled);
    }

    [Fact]
    public void The_port_box_is_only_enabled_for_your_own_port()
    {
        NetworkSettingsViewModel network = new Rig().Main.Settings.Network;
        Assert.False(network.PortEnabled);

        network.ChooseOwnPort = true;
        Assert.True(network.PortEnabled);
    }

    [Fact]
    public void Hub_mode_offers_the_five_fields()
    {
        Assert.Equal(["Domain", "Name", "About", "Voice Server", "Next Event"], new Rig().Main.Settings.HubMode.Fields.Select(f => f.Label));
    }

    [Fact]
    public void The_nickname_and_simbrief_username_are_the_profiles()
    {
        Rig rig = new();

        rig.Main.Settings.Simulator.Profile.SimbriefUsername = "abc";
        rig.Main.Settings.Simulator.Profile.Nickname = "Goose";

        Assert.Equal("abc", rig.Settings.SimbriefUsername);
        Assert.Equal("Goose", rig.Settings.Nickname);
    }

    [Fact]
    public void Open_model_matching_goes_to_that_tab_and_scanning_opens_the_overlay()
    {
        Rig rig = new();

        rig.Main.Settings.Simulator.OpenModelMatchingCommand.Execute(null);
        Assert.Equal(TabId.Models, rig.Main.SelectedTab);

        rig.Main.Settings.Simulator.OpenModelScanningCommand.Execute(null);
        ScanModelsViewModel scan = Assert.IsType<ScanModelsViewModel>(rig.Main.Overlay);
        Assert.Equal("Not connected", scan.SimulatorLabel);
    }

    [Fact]
    public async Task The_scan_dialog_says_connected_when_the_simulator_is()
    {
        Rig rig = new();
        await rig.Main.Simulator.ToggleAsync();

        rig.Main.Settings.Simulator.OpenModelScanningCommand.Execute(null);

        Assert.Equal("Connected", ((ScanModelsViewModel)rig.Main.Overlay!).SimulatorLabel);
    }

    [Fact]
    public void Editing_a_variables_row_saves_its_files_back()
    {
        Rig rig = new();
        VariableAssignmentViewModel row = rig.Main.Settings.Variables.Assignments[1];
        Assert.Equal("Custom_FMC_Vars, ListBox_Sets", row.FilesText);

        row.EditCommand.Execute(null);
        VariablesOverlayViewModel overlay = Assert.IsType<VariablesOverlayViewModel>(rig.Main.Overlay);
        overlay.SelectedFile = "Custom_FMC_Vars";
        overlay.RemoveCommand.Execute(null);
        overlay.OkCommand.Execute(null);

        Assert.Equal("ListBox_Sets", row.FilesText);
    }
}

public class AddressBookTests
{
    [Fact]
    public void Add_needs_a_name_appends_and_clears_the_draft()
    {
        Rig rig = new();
        AddressBookViewModel book = rig.Main.AddressBook;
        Assert.False(book.AddCommand.CanExecute(null));

        book.NewName = "  My hub ";
        book.NewAddress = " 1.2.3.4:24192 ";
        Assert.True(book.AddCommand.CanExecute(null));
        book.AddCommand.Execute(null);

        AddressBookRow added = book.Entries.Last();
        Assert.Equal(("My hub", "1.2.3.4:24192"), (added.Name, added.Address));
        Assert.Equal(("", ""), (book.NewName, book.NewAddress));
    }

    [Fact]
    public void The_global_entry_cannot_be_removed_and_removing_the_selection_selects_another()
    {
        Rig rig = new();
        AddressBookViewModel book = rig.Main.AddressBook;

        AddressBookRow global = book.Entries.Single(e => e.IsBuiltIn);
        Assert.False(global.RemoveCommand.CanExecute(null));

        AddressBookRow selected = book.Selected!;
        selected.RemoveCommand.Execute(null);
        Assert.DoesNotContain(selected, book.Entries);
        Assert.NotNull(book.Selected);
        Assert.NotSame(selected, book.Selected);
    }

    [Fact]
    public void Changes_are_saved_through_the_store()
    {
        Rig rig = new();
        rig.Main.AddressBook.NewName = "Saved";
        rig.Main.AddressBook.AddCommand.Execute(null);

        (IReadOnlyList<AddressBookEntry> entries, string? selected) = rig.Services.AddressBook.Load();

        Assert.Contains(entries, e => e.Name == "Saved");
        Assert.Equal("Planet FsHub", selected);
    }
}

public class ChatMonitorHomeTests
{
    [Fact]
    public void Sending_needs_text_appends_the_message_and_clears_the_box()
    {
        ChatViewModel chat = new Rig().Main.Chat;
        Assert.False(chat.SendCommand.CanExecute(null));

        chat.Draft = " hello ";
        chat.SendCommand.Execute(null);

        Assert.Equal("hello", chat.Messages.Last().Text);
        Assert.Equal("", chat.Draft);
    }

    [Fact]
    public void The_monitor_filters_start_as_the_design_has_them_and_toggle()
    {
        MonitorViewModel monitor = new Rig().Main.Monitor;

        Assert.Equal([false, false, true, false], monitor.Filters.Select(f => f.IsOn));
        monitor.Filters[0].ToggleCommand.Execute(null);
        Assert.True(monitor.Filters[0].IsOn);
        Assert.Equal("FPS: 48", monitor.FpsText);
    }

    [Fact]
    public async Task Home_follows_the_connections()
    {
        Rig rig = new();
        HomeViewModel home = rig.Main.Home;
        Assert.Equal("Planet FsHub", home.HubName);
        Assert.Equal(8, home.ConnectedUsers);
        string idle = home.Subtitle;

        await rig.Main.Simulator.ToggleAsync();
        await rig.Main.Network.ToggleAsync();

        Assert.NotEqual(idle, home.Subtitle);
        Assert.StartsWith("Simulator and network are both connected.", home.Subtitle);
    }
}


public class RecordSelectionTests
{
    [Fact]
    public void The_recorders_list_is_the_aircraft_list_with_the_same_ticks()
    {
        Rig rig = new();

        Assert.Equal(rig.Main.Aircraft.Rows.Select(r => r.Id).Order(), rig.Main.Recorder.LiveAircraft.Select(r => r.Callsign).Order());
        foreach (AircraftRowViewModel row in rig.Main.Aircraft.Rows)
            Assert.Equal(row.Recording, rig.Main.Recorder.LiveAircraft.Single(r => r.Callsign == row.Id).IsChecked);
        Assert.Equal(["9H-WDR", "AAL2693", "ASXGS", "LV-ALB"], rig.Main.Aircraft.Rows.Where(r => r.Recording).Select(r => r.Callsign).Order());
    }

    [Fact]
    public void Ticking_in_either_tab_ticks_in_the_other_and_reaches_the_service()
    {
        Rig rig = new();
        AircraftRowViewModel row = rig.Main.Aircraft.Rows.Single(r => r.Callsign == "A320");
        RecordItemViewModel item = rig.Main.Recorder.LiveAircraft.Single(r => r.Callsign == "A320");
        Assert.False(row.Recording);

        row.Recording = true;
        Assert.True(item.IsChecked);
        Assert.Contains(rig.Services.Traffic.GetAircraft(), a => a.Id == "A320" && a.Recording);

        item.IsChecked = false;
        Assert.False(row.Recording);
        Assert.DoesNotContain(rig.Services.Traffic.GetAircraft(), a => a.Id == "A320" && a.Recording);
    }

    [Fact]
    public void Remove_from_recorder_and_add_to_recorder_toggle_the_aircraft()
    {
        Rig rig = new();
        AircraftRowViewModel row = rig.Main.Aircraft.Rows.Single(r => r.Callsign == "9H-WDR");
        ActionLink link = row.Actions.Single(a => a.Label.EndsWith("Recorder"));
        Assert.Equal("Remove From Recorder", link.Label);

        link.Command.Execute(null);
        Assert.False(row.Recording);
        Assert.Equal("Add To Recorder", link.Label);
        Assert.False(rig.Main.Recorder.LiveAircraft.Single(r => r.Callsign == "9H-WDR").IsChecked);

        link.Command.Execute(null);
        Assert.True(row.Recording);
    }
}

/// <summary>A traffic source the test scripts: the aircraft it returns and what the UI wrote to it.</summary>
public sealed class ScriptedTraffic : ITrafficSource
{
    public List<AircraftInfo> Aircraft { get; set; }
    public List<string> Writes { get; } = [];
    public int Reads { get; private set; }

    public ScriptedTraffic() => Aircraft = [Info("a")];

    public AircraftInfo Info(string id) =>
        new(id, id.ToUpperInvariant(), "Pilot", 10, 90, 3000, 120, "Model", 90, "1200", "118.000", "121.500", "MSFS", "Model", "-", "-",
            AircraftLinkState.Created, false, false, false, AircraftActions.All);

    public IReadOnlyList<AircraftInfo> GetAircraft()
    {
        Reads++;
        return Aircraft;
    }

    public IReadOnlyList<ObjectInfo> GetObjects() => [];
    public bool IncludeHubAircraft { get; set; }
    public bool IncludeSimulatorAircraft { get; set; }
    public bool InCockpit => false;
    public bool IsTracking => false;
    public void SetRecording(string aircraftId, bool recording) => Writes.Add($"record {aircraftId}={recording}");
    public void SetIgnored(string aircraftId, bool ignored) => Writes.Add($"ignore {aircraftId}={ignored}");
    public void Follow(string aircraftId) => Writes.Add("follow " + aircraftId);
    public void EnterCockpit(string aircraftId) => Writes.Add("enter " + aircraftId);
    public void TrackHeading(string aircraftId) => Writes.Add("heading " + aircraftId);
    public void TrackBearing(string aircraftId) => Writes.Add("bearing " + aircraftId);
    public void StopTracking() => Writes.Add("stop tracking");
    public void CopyWeather(string aircraftId) => Writes.Add("weather " + aircraftId);
}

/// <summary>An IShell that does nothing, for view models built on their own.</summary>
public sealed class NullShell : IShell
{
    public void GoTo(TabId tab) { }
    public void ShowOverlay(OverlayViewModel overlay) { }
    public Task JoinAsync(AddressBookEntry hub) => Task.CompletedTask;
    public Task<string?> CreateMeshAsync() => Task.FromResult<string?>(null);
}

public class ScanModelsTests
{
    [Fact]
    public void Scan_for_models_is_the_x_plane_dialog_in_the_xplane_build_and_the_other_dialog_elsewhere()
    {
        Rig normal = new();
        normal.Main.Settings.Simulator.OpenModelScanningCommand.Execute(null);
        Assert.IsType<ScanModelsViewModel>(normal.Main.Overlay);

        Rig xplane = new(xplaneBuild: true);
        xplane.Main.Settings.Simulator.OpenModelScanningCommand.Execute(null);
        Assert.IsType<ScanXPlaneModelsViewModel>(xplane.Main.Overlay);
    }

    [Fact]
    public void The_csl_folder_follows_the_x_plane_folder()
    {
        Rig rig = new(xplaneBuild: true);
        ScanXPlaneModelsViewModel scan = new(rig.Main.Profile, rig.Services.XPlaneScan, rig.Platform);
        Assert.Equal(@"C:\X-Plane 12\Resources\plugins\JoinFS\Resources\CSL", scan.CslFolder);

        scan.XplaneFolder = "";
        Assert.Equal("", scan.CslFolder);
        Assert.Empty(scan.AircraftFolders);
    }

    [Fact]
    public void The_aircraft_folders_come_from_the_x_plane_folder_and_the_last_scan_is_ticked()
    {
        Rig rig = new(xplaneBuild: true);
        ScanXPlaneModelsViewModel scan = new(rig.Main.Profile, rig.Services.XPlaneScan, rig.Platform);

        Assert.Equal(["Extra Aircraft", "FlyJSim", "Laminar Research", "Zibo 737"], scan.AircraftFolders.Select(f => f.Name));
        Assert.Equal(["Laminar Research"], scan.AircraftFolders.Where(f => f.IsChecked).Select(f => f.Name));
        Assert.Equal([@"C:\X-Plane 12\Aircraft\Laminar Research"], scan.SelectedFolderPaths);
    }

    [Fact]
    public void Ticks_survive_a_change_of_folder()
    {
        Rig rig = new(xplaneBuild: true);
        ScanXPlaneModelsViewModel scan = new(rig.Main.Profile, rig.Services.XPlaneScan, rig.Platform);
        scan.AircraftFolders.Single(f => f.Name == "FlyJSim").IsChecked = true;

        scan.XplaneFolder = @"D:\Other X-Plane";

        Assert.Equal(["FlyJSim", "Laminar Research"], scan.AircraftFolders.Where(f => f.IsChecked).Select(f => f.Name));
    }

    [Fact]
    public void Skip_only_applies_while_generating_and_the_options_are_remembered()
    {
        Rig rig = new(xplaneBuild: true);
        ScanXPlaneModelsViewModel scan = new(rig.Main.Profile, rig.Services.XPlaneScan, rig.Platform);
        Assert.False(scan.CanSkipCsl);

        scan.GenerateCsl = true;
        scan.SkipCsl = true;

        Assert.True(scan.CanSkipCsl);
        Assert.True(rig.Settings.GenerateCsl);
        Assert.True(rig.Settings.SkipCsl);
    }

    [Fact]
    public void Scan_at_launch_and_model_scan_on_connect_are_one_setting()
    {
        Rig rig = new(xplaneBuild: true);

        rig.Main.Settings.Simulator.Profile.ModelScanOnConnect = true;

        Assert.True(rig.Settings.ModelScanOnConnect);
        ScanXPlaneModelsViewModel scan = new(rig.Main.Profile, rig.Services.XPlaneScan, rig.Platform);
        Assert.True(scan.Profile.ModelScanOnConnect);
    }

    [Fact]
    public void Scan_closes_the_overlay()
    {
        Rig rig = new(xplaneBuild: true);
        rig.Main.Settings.Simulator.OpenModelScanningCommand.Execute(null);

        ((ScanXPlaneModelsViewModel)rig.Main.Overlay!).ScanCommand.Execute(null);

        Assert.Null(rig.Main.Overlay);
    }
}
