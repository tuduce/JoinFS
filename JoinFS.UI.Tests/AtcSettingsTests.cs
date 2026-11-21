using JoinFS.UI.Localization;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Tests;

/// <summary>The ATC card of Settings: the switch and the port are saved at once, and the status line tells what is true now.</summary>
public class AtcSettingsTests
{
    private static (MainViewModel Main, InMemoryPreferencesStore Store, FakeAtcStatusSource Atc) Open(Preferences? stored = null)
    {
        InMemoryPreferencesStore store = new(stored);
        FakeAtcStatusSource atc = new();
        AppServices services = FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }) with { Preferences = store, Atc = atc };
        return (new MainViewModel(services), store, atc);
    }

    // ---- the switch and the port

    [Fact]
    public void A_fresh_install_has_it_off_on_the_usual_port()
    {
        (MainViewModel main, InMemoryPreferencesStore store, _) = Open();

        AtcSettingsViewModel card = main.Settings.Atc;
        Assert.False(card.Enabled);
        Assert.Equal("6809", card.Port);
        Assert.Equal(AtcClientState.Off, card.State);
        Assert.Equal(Loc.T("Off"), card.StatusText);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void The_card_starts_with_what_was_stored()
    {
        (MainViewModel main, _, _) = Open(new Preferences { AtcFsd = true, AtcFsdPort = 7000 });

        Assert.True(main.Settings.Atc.Enabled);
        Assert.Equal("7000", main.Settings.Atc.Port);
    }

    [Fact]
    public void The_switch_is_saved_at_once()
    {
        (MainViewModel main, InMemoryPreferencesStore store, _) = Open();

        main.Settings.Atc.Enabled = true;
        Assert.True(store.Saved[^1].AtcFsd);

        main.Settings.Atc.Enabled = false;
        Assert.False(store.Saved[^1].AtcFsd);
    }

    [Fact]
    public void A_whole_port_is_saved()
    {
        (MainViewModel main, InMemoryPreferencesStore store, _) = Open();

        main.Settings.Atc.Enabled = true;
        main.Settings.Atc.Port = " 7123 ";

        Assert.Equal(7123, store.Saved[^1].AtcFsdPort);
        Assert.Null(main.Settings.Atc.PortError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-5")]
    public void Something_that_is_not_a_port_does_not_replace_the_port_and_says_so(string text)
    {
        (MainViewModel main, InMemoryPreferencesStore store, _) = Open(new Preferences { AtcFsd = true, AtcFsdPort = 7000 });

        main.Settings.Atc.Port = text;
        main.Settings.Network.LowBandwidth = true; // a save that carries the port as it stands

        Assert.Equal(7000, store.Saved[^1].AtcFsdPort);
        Assert.False(string.IsNullOrEmpty(main.Settings.Atc.PortError));
    }

    [Fact]
    public void A_bad_port_is_no_error_while_the_connection_is_off()
    {
        (MainViewModel main, _, _) = Open();

        main.Settings.Atc.Port = "abc";

        Assert.Null(main.Settings.Atc.PortError);
    }

    [Fact]
    public void Opening_the_card_or_reading_the_status_saves_nothing()
    {
        (MainViewModel main, InMemoryPreferencesStore store, _) = Open();

        main.Settings.Atc.ToggleCommand.Execute(null);
        main.Settings.Atc.Refresh();

        Assert.Empty(store.Saved);
    }

    // ---- what is true now

    [Fact]
    public void Listening_shows_the_port()
    {
        (MainViewModel main, _, FakeAtcStatusSource atc) = Open();

        atc.Status = new AtcClientStatus(AtcClientState.Listening, 6809);
        main.Settings.Atc.Refresh();

        Assert.Equal(AtcClientState.Listening, main.Settings.Atc.State);
        Assert.Equal(Loc.F("Listening on port {0}", 6809), main.Settings.Atc.StatusText);
        Assert.False(main.Settings.Atc.IsFailed);
    }

    [Fact]
    public void Connected_shows_the_station_and_its_frequency()
    {
        (MainViewModel main, _, FakeAtcStatusSource atc) = Open();

        atc.Status = new AtcClientStatus(AtcClientState.Connected, 6809, "EDDM_TWR", "118.700");
        main.Settings.Atc.Refresh();

        Assert.Equal(AtcClientState.Connected, main.Settings.Atc.State);
        Assert.Equal(Loc.F("Connected as {0} {1}", "EDDM_TWR", "118.700"), main.Settings.Atc.StatusText);
        Assert.Contains("EDDM_TWR 118.700", main.Settings.Atc.StatusText);
    }

    [Fact]
    public void Connected_without_a_frequency_yet_shows_the_callsign_alone()
    {
        (MainViewModel main, _, FakeAtcStatusSource atc) = Open();

        atc.Status = new AtcClientStatus(AtcClientState.Connected, 6809, "EDDM_TWR");
        main.Settings.Atc.Refresh();

        Assert.Equal(Loc.F("Connected as {0}", "EDDM_TWR"), main.Settings.Atc.StatusText);
    }

    [Fact]
    public void A_port_that_cannot_be_opened_is_said_and_marked_as_a_failure()
    {
        (MainViewModel main, _, FakeAtcStatusSource atc) = Open();

        atc.Status = new AtcClientStatus(AtcClientState.Failed, 6809);
        main.Settings.Atc.Refresh();

        Assert.True(main.Settings.Atc.IsFailed);
        Assert.False(main.Settings.Atc.IsNotFailed);
        Assert.Equal(Loc.F("Cannot listen on port {0}. Another program may be using it.", 6809), main.Settings.Atc.StatusText);
    }

    [Fact]
    public void The_status_goes_back_to_off()
    {
        (MainViewModel main, _, FakeAtcStatusSource atc) = Open();
        atc.Status = new AtcClientStatus(AtcClientState.Connected, 6809, "EDDM_TWR", "118.700");
        main.Settings.Atc.Refresh();

        atc.Status = AtcClientStatus.Off;
        main.Settings.Atc.Refresh();

        Assert.Equal(AtcClientState.Off, main.Settings.Atc.State);
        Assert.Equal(Loc.T("Off"), main.Settings.Atc.StatusText);
    }

    [Fact]
    public void Opening_the_card_reads_the_status_again()
    {
        (MainViewModel main, _, FakeAtcStatusSource atc) = Open();

        atc.Status = new AtcClientStatus(AtcClientState.Listening, 6809);
        main.Settings.Atc.ToggleCommand.Execute(null);

        Assert.Equal(AtcClientState.Listening, main.Settings.Atc.State);
    }

    [Fact]
    public void The_poll_keeps_the_open_card_up_to_date()
    {
        (MainViewModel main, _, FakeAtcStatusSource atc) = Open();
        main.GoTo(TabId.Settings);
        main.Settings.Atc.ToggleCommand.Execute(null);

        atc.Status = new AtcClientStatus(AtcClientState.Connected, 6809, "EDDM_TWR", "118.700");
        for (int i = 0; i < 4; i++)
            main.Poll(); // the tab is read every fourth poll

        Assert.Equal(AtcClientState.Connected, main.Settings.Atc.State);
    }
}
