using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Tests;

/// <summary>The Settings cards edit the preferences and save them at every change.</summary>
public class PreferencesTests
{
    private static (MainViewModel Main, InMemoryPreferencesStore Store) Open(Preferences? stored = null, bool xplane = false)
    {
        InMemoryPreferencesStore store = new(stored);
        AppServices services = FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }, xplaneBuild: xplane) with { Preferences = store };
        return (new MainViewModel(services), store);
    }

    private static Preferences Last(InMemoryPreferencesStore store) => store.Saved[^1];

    [Fact]
    public void The_cards_start_with_what_was_stored()
    {
        (MainViewModel main, _) = Open(new Preferences
        {
            AlwaysOnTop = true, ConnectOnLaunch = false, CircleOfActivityNm = 120, FollowDistanceM = 300, LowBandwidth = true,
            ChooseOwnPort = true, LocalPort = 41000, Password = "secret", HubMode = true, HubName = "My Hub", HubVoice = "voice.example", Tcas = true,
            XPlaneAddress = "192.168.1.5",
        }, xplane: true);

        Assert.True(main.Settings.UserInterface.AlwaysOnTop);
        Assert.False(main.Settings.Simulator.ConnectOnLaunch);
        Assert.Equal(120, main.Settings.Simulator.CircleOfActivityNm);
        Assert.Equal(300, main.Settings.Simulator.FollowDistanceM);
        Assert.True(main.Settings.Network.LowBandwidth);
        Assert.True(main.Settings.Network.ChooseOwnPort);
        Assert.Equal("41000", main.Settings.Network.LocalPort);
        Assert.Equal("secret", main.Settings.Network.Password);
        Assert.True(main.Settings.HubMode.Enabled);
        Assert.Equal("My Hub", main.Settings.HubMode.Fields[1].Value);
        Assert.Equal("voice.example", main.Settings.HubMode.Fields[3].Value);
        Assert.True(main.Settings.XPlane.Tcas);
        Assert.Equal("192.168.1.5", main.Settings.XPlane.IpAddress);
    }

    [Fact]
    public void Opening_the_tab_saves_nothing()
    {
        (_, InMemoryPreferencesStore store) = Open();

        Assert.Empty(store.Saved);
    }

    [Fact]
    public void A_stored_slider_value_out_of_range_is_held_to_the_range()
    {
        (MainViewModel main, _) = Open(new Preferences { CircleOfActivityNm = 9000, FollowDistanceM = 1 });

        Assert.Equal(SimulatorSettingsViewModel.CircleMaxNm, main.Settings.Simulator.CircleOfActivityNm);
        Assert.Equal(SimulatorSettingsViewModel.FollowMinM, main.Settings.Simulator.FollowDistanceM);
    }

    [Fact]
    public void A_change_is_saved_at_once()
    {
        (MainViewModel main, InMemoryPreferencesStore store) = Open();

        main.Settings.UserInterface.AlwaysOnTop = true;
        main.Settings.Simulator.ElevationCorrection = true;
        main.Settings.Simulator.CircleOfActivityNm = 77;
        main.Settings.Network.LowBandwidth = true;

        Preferences saved = Last(store);
        Assert.True(saved.AlwaysOnTop);
        Assert.True(saved.ElevationCorrection);
        Assert.Equal(77, saved.CircleOfActivityNm);
        Assert.True(saved.LowBandwidth);
    }

    [Fact]
    public void Opening_a_card_or_moving_the_colour_picker_is_not_a_change()
    {
        (MainViewModel main, InMemoryPreferencesStore store) = Open();

        main.Settings.Simulator.ToggleCommand.Execute(null);
        main.Settings.Simulator.ToggleLabelColorPickerCommand.Execute(null);

        Assert.Empty(store.Saved);
    }

    [Fact]
    public void A_card_does_not_undo_what_another_card_saved()
    {
        (MainViewModel main, InMemoryPreferencesStore store) = Open();

        main.Settings.Network.LowBandwidth = true;
        main.Settings.UserInterface.ToolTips = false;

        Assert.True(Last(store).LowBandwidth);
        Assert.False(Last(store).ToolTips);
    }

    [Fact]
    public void The_label_colour_is_saved_once_it_is_a_whole_colour()
    {
        (MainViewModel main, InMemoryPreferencesStore store) = Open();

        main.Settings.Simulator.LabelColor = "#12";
        main.Settings.Simulator.ShowSpeed = true; // some other change that saves
        Assert.Equal("#F2C400", Last(store).LabelColor);

        main.Settings.Simulator.LabelColor = "#3aa0ff";
        Assert.Equal("#3AA0FF", Last(store).LabelColor);
    }

    [Fact]
    public void Choosing_a_swatch_saves_its_colour()
    {
        (MainViewModel main, InMemoryPreferencesStore store) = Open();

        main.Settings.Simulator.ChooseLabelColorCommand.Execute("#FF5A4D");

        Assert.Equal("#FF5A4D", Last(store).LabelColor);
    }

    [Fact]
    public void The_x_plane_address_and_tcas_are_saved()
    {
        (MainViewModel main, InMemoryPreferencesStore store) = Open(xplane: true);

        main.Settings.XPlane.IpAddress = "  10.0.0.7 ";
        main.Settings.XPlane.Tcas = true;

        Assert.Equal("10.0.0.7", Last(store).XPlaneAddress);
        Assert.True(Last(store).Tcas);
    }

    // ---- the port

    [Fact]
    public void A_valid_port_is_saved_when_the_own_port_is_chosen()
    {
        (MainViewModel main, InMemoryPreferencesStore store) = Open();

        main.Settings.Network.ChooseOwnPort = true;
        main.Settings.Network.LocalPort = "41234";

        Assert.True(Last(store).ChooseOwnPort);
        Assert.Equal(41234, Last(store).LocalPort);
        Assert.Null(main.Settings.Network.PortError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-5")]
    [InlineData("12 34")]
    public void Something_that_is_not_a_port_does_not_replace_the_port_and_says_so(string text)
    {
        (MainViewModel main, InMemoryPreferencesStore store) = Open(new Preferences { ChooseOwnPort = true, LocalPort = 40500 });

        main.Settings.Network.LocalPort = text;
        main.Settings.Network.LowBandwidth = true; // a save that carries the port as it stands

        Assert.Equal(40500, Last(store).LocalPort);
        Assert.False(string.IsNullOrEmpty(main.Settings.Network.PortError));
    }

    [Fact]
    public void A_bad_port_is_no_error_while_the_own_port_is_not_chosen()
    {
        (MainViewModel main, _) = Open();

        main.Settings.Network.LocalPort = "abc";

        Assert.Null(main.Settings.Network.PortError);
    }

    [Fact]
    public void The_password_is_saved_trimmed()
    {
        (MainViewModel main, InMemoryPreferencesStore store) = Open();

        main.Settings.Network.Password = "  hunter2 ";

        Assert.Equal("hunter2", Last(store).Password);
    }

    // ---- hub mode

    [Fact]
    public void The_hub_fields_are_editable_and_saved_while_hub_mode_is_off()
    {
        (MainViewModel main, InMemoryPreferencesStore store) = Open();

        main.Settings.HubMode.Fields[1].Value = "x"; // too short, but hub mode is off

        Assert.False(Last(store).HubMode);
        Assert.Equal("x", Last(store).HubName);
        Assert.Null(main.Settings.HubMode.NameError);
    }

    [Fact]
    public void Hub_mode_needs_a_name_of_three_characters_and_nothing_is_saved_without_it()
    {
        (MainViewModel main, InMemoryPreferencesStore store) = Open();
        main.Settings.HubMode.Fields[1].Value = "ab";
        int saved = store.Saved.Count;

        main.Settings.HubMode.Enabled = true;

        Assert.Equal(saved, store.Saved.Count);
        Assert.False(Last(store).HubMode);
        Assert.NotNull(main.Settings.HubMode.NameError);
    }

    [Fact]
    public void Fixing_the_name_turns_hub_mode_on()
    {
        (MainViewModel main, InMemoryPreferencesStore store) = Open();
        main.Settings.HubMode.Fields[1].Value = "ab";
        main.Settings.HubMode.Enabled = true;

        main.Settings.HubMode.Fields[1].Value = "abc";

        Assert.True(Last(store).HubMode);
        Assert.Equal("abc", Last(store).HubName);
        Assert.Null(main.Settings.HubMode.NameError);
    }

    [Fact]
    public void Shortening_the_name_of_a_running_hub_saves_nothing_until_it_is_valid_again()
    {
        (MainViewModel main, InMemoryPreferencesStore store) = Open(new Preferences { HubMode = true, HubName = "Hangar" });

        main.Settings.HubMode.Fields[1].Value = "H";

        Assert.Empty(store.Saved);
        Assert.NotNull(main.Settings.HubMode.NameError);

        main.Settings.HubMode.Fields[1].Value = "Hangar 2";
        Assert.Equal("Hangar 2", Last(store).HubName);
        Assert.True(Last(store).HubMode);
    }

    [Fact]
    public void All_five_hub_fields_are_saved()
    {
        (MainViewModel main, InMemoryPreferencesStore store) = Open();

        main.Settings.HubMode.Fields[0].Value = "hub.example.com";
        main.Settings.HubMode.Fields[1].Value = "Example Hub";
        main.Settings.HubMode.Fields[2].Value = "About it";
        main.Settings.HubMode.Fields[3].Value = "voice.example.com";
        main.Settings.HubMode.Fields[4].Value = "Friday 20:00Z";
        main.Settings.HubMode.Enabled = true;

        Preferences saved = Last(store);
        Assert.Equal(("hub.example.com", "Example Hub", "About it", "voice.example.com", "Friday 20:00Z"),
            (saved.HubDomain, saved.HubName, saved.HubAbout, saved.HubVoice, saved.HubEvent));
        Assert.True(saved.HubMode);
    }
}
