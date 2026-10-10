using JoinFS.UI.Models;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Overlays;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Tests;

public class ShortcutTests
{
    private static FakeShortcutSource Keys(Rig rig) => (FakeShortcutSource)rig.Services.Shortcuts;

    private static FakeRecorderSource Recorder(Rig rig) => (FakeRecorderSource)rig.Services.Recorder;

    /// <summary>Turns a shortcut on from the Settings card, as the user does, and presses its keys.</summary>
    private static Task Press(Rig rig, ShortcutAction action)
    {
        ShortcutRowViewModel row = rig.Main.Settings.Shortcuts.Rows[(int)action];
        row.Enabled = true;
        Keys(rig).Press(action);
        return rig.Main.PollShortcuts();
    }

    // ---- the keys

    [Theory]
    [InlineData("CTRL+SHIFT+R", "Ctrl+Shift+R")]
    [InlineData("CTRL+N", "Ctrl+N")]
    [InlineData("ALT+CTRL+X", "Alt+Ctrl+X")]
    public void Keys_are_shown_with_capitals_for_the_modifiers(string combination, string shown) =>
        Assert.Equal(shown, ShortcutText.Display(combination));

    [Theory]
    [InlineData("CTRL+N", true)]
    [InlineData("CTRL+SHIFT+ALT+Z", true)]
    [InlineData("A", true)]
    [InlineData("", false)]
    [InlineData("CTRL+", false)]
    [InlineData("CTRL+1", false)]
    [InlineData("CTRL+NN", false)]
    [InlineData("WIN+A", false)]
    public void A_shortcut_is_modifiers_and_one_letter(string combination, bool valid) =>
        Assert.Equal(valid, ShortcutText.IsValid(combination));

    [Fact]
    public void Capturing_builds_the_combination_from_the_modifiers_and_the_letter()
    {
        ShortcutCaptureViewModel capture = new("Follow", "CTRL+F");
        string? accepted = null;
        capture.Accepted += (_, combination) => accepted = combination;

        capture.KeyPressed(control: true, shift: true, alt: false, 'r');
        Assert.Equal("CTRL+SHIFT+R", capture.Combination);
        Assert.Equal("Ctrl+Shift+R", capture.Keys);

        capture.KeyPressed(control: false, shift: false, alt: false, '5'); // not a letter: nothing changes
        Assert.Equal("CTRL+SHIFT+R", capture.Combination);

        capture.SaveCommand.Execute(null);
        Assert.Equal("CTRL+SHIFT+R", accepted);
    }

    [Fact]
    public void Capturing_does_not_save_while_what_is_shown_is_not_a_shortcut()
    {
        ShortcutCaptureViewModel capture = new("Follow", "");

        Assert.False(capture.CanSave);
        Assert.False(capture.SaveCommand.CanExecute(null));
    }

    // ---- the Settings card

    [Fact]
    public void The_card_lists_the_ten_shortcuts_of_the_old_window_all_off()
    {
        Rig rig = new();
        ShortcutsSettingsViewModel card = rig.Main.Settings.Shortcuts;

        Assert.Equal(10, card.Rows.Count);
        Assert.All(card.Rows, row => Assert.False(row.Enabled));
        Assert.Equal("Ctrl+N", card.Rows[(int)ShortcutAction.Network].Keys);
        Assert.Equal("Ctrl+Shift+P", card.Rows[(int)ShortcutAction.Replay].Keys);
        Assert.Contains(card, rig.Main.Settings.Sections);
    }

    [Fact]
    public void Switching_a_shortcut_on_keeps_it_and_names_it_in_the_tooltips()
    {
        Rig rig = new();
        Assert.Null(rig.Main.Network.ShortcutHint);
        Assert.Equal("Record", rig.Main.Recorder.Hints.RecordTip);

        rig.Main.Settings.Shortcuts.Rows[(int)ShortcutAction.Network].Enabled = true;
        rig.Main.Settings.Shortcuts.Rows[(int)ShortcutAction.Record].Enabled = true;

        Assert.True(rig.Services.Shortcuts.Load()[(int)ShortcutAction.Network].Enabled);
        Assert.Equal("Shortcut: Ctrl+N", rig.Main.Network.ShortcutHint);
        Assert.Null(rig.Main.Simulator.ShortcutHint);
        Assert.Equal("Record (Ctrl+Shift+R)", rig.Main.Recorder.Hints.RecordTip);
    }

    [Fact]
    public void Changing_the_keys_asks_for_them_and_keeps_what_was_chosen()
    {
        Rig rig = new();
        ShortcutRowViewModel row = rig.Main.Settings.Shortcuts.Rows[(int)ShortcutAction.Follow];

        row.ChangeCommand.Execute(null);
        ShortcutCaptureViewModel capture = Assert.IsType<ShortcutCaptureViewModel>(rig.Main.Overlay);
        capture.KeyPressed(control: false, shift: false, alt: true, 'g');
        capture.SaveCommand.Execute(null);

        Assert.Null(rig.Main.Overlay);
        Assert.Equal("Alt+G", row.Keys);
        Assert.Equal("ALT+G", rig.Services.Shortcuts.Load()[(int)ShortcutAction.Follow].Combination);
    }

    [Fact]
    public async Task A_shortcut_does_nothing_while_its_keys_are_being_chosen()
    {
        Rig rig = new();
        rig.Main.Settings.Shortcuts.Rows[(int)ShortcutAction.Record].ChangeCommand.Execute(null);
        Assert.IsType<ShortcutCaptureViewModel>(rig.Main.Overlay);

        await Press(rig, ShortcutAction.Stop);
        await Press(rig, ShortcutAction.Replay);

        Assert.Empty(Recorder(rig).Calls);
    }

    [Fact]
    public async Task A_shortcut_that_is_off_does_nothing()
    {
        Rig rig = new();
        Keys(rig).Press(ShortcutAction.Replay);

        await rig.Main.PollShortcuts();

        Assert.Empty(Recorder(rig).Calls);
    }

    // ---- the connectors

    [Fact]
    public async Task The_network_shortcut_does_what_the_network_button_does()
    {
        Rig rig = new();

        await Press(rig, ShortcutAction.Network);
        Assert.True(rig.Main.Network.IsConnected);

        await Press(rig, ShortcutAction.Network);
        Assert.True(rig.Main.Network.IsDisconnected);
    }

    [Fact]
    public async Task The_simulator_shortcut_does_what_the_simulator_button_does()
    {
        Rig rig = new();

        await Press(rig, ShortcutAction.Simulator);

        Assert.True(rig.Main.Simulator.IsConnected);
    }

    // ---- the open row of the Session and Aircraft tabs

    [Fact]
    public async Task The_cockpit_shortcuts_act_on_the_user_whose_row_is_open()
    {
        Rig rig = new();
        PeerRowViewModel other = rig.Main.Session.Rows.First(r => r.IsOther);

        await Press(rig, ShortcutAction.AllowShared);
        Assert.False(other.CockpitEntry); // no row is open: no one to act on

        other.ToggleExpandedCommand.Execute(null);
        await Press(rig, ShortcutAction.AllowShared);
        Assert.True(other.CockpitEntry);
        Assert.True(rig.Services.Session.GetSettings(other.Id).CockpitEntry);

        await Press(rig, ShortcutAction.HandOver);
        Assert.True(rig.Services.Session.GetSettings(other.Id).HandOverControls);
        await Press(rig, ShortcutAction.HandOver);
        Assert.False(rig.Services.Session.GetSettings(other.Id).HandOverControls);

        await Press(rig, ShortcutAction.AllowShared);
        Assert.False(rig.Services.Session.GetSettings(other.Id).CockpitEntry);
    }

    [Fact]
    public async Task The_cockpit_shortcuts_leave_your_own_row_alone()
    {
        Rig rig = new();
        PeerRowViewModel me = rig.Main.Session.Rows.First(r => !r.IsOther);
        me.ToggleExpandedCommand.Execute(null);

        await Press(rig, ShortcutAction.AllowShared);

        Assert.False(me.CockpitEntry);
    }

    [Fact]
    public async Task The_enter_cockpit_shortcut_acts_on_the_aircraft_whose_row_is_open()
    {
        Rig rig = new();
        Assert.False(rig.Services.Traffic.InCockpit);

        await Press(rig, ShortcutAction.EnterCockpit);
        Assert.False(rig.Services.Traffic.InCockpit); // no row is open

        AircraftRowViewModel row = rig.Main.Aircraft.Rows.First(r => r.Info.Can.HasFlag(AircraftActions.EnterCockpit));
        row.ToggleExpandedCommand.Execute(null);
        await Press(rig, ShortcutAction.EnterCockpit);

        Assert.True(rig.Services.Traffic.InCockpit);
    }

    // ---- the recorder: the rules of the old window's hotkeys

    [Fact]
    public async Task Record_starts_a_recording_and_the_next_press_saves_it_and_starts_another()
    {
        Rig rig = new(); // the loaded take is on disk: nothing to save first

        await Press(rig, ShortcutAction.Record);
        Assert.True(rig.Main.Recorder.IsRecording);
        Assert.Equal(["record"], Recorder(rig).Calls);

        await Press(rig, ShortcutAction.Record);
        Assert.True(rig.Main.Recorder.IsRecording);
        Assert.Equal(["record", "stop", "autosave", "record"], Recorder(rig).Calls);
        Assert.Equal("Recording saved: 2026-10-10_120000_Me.jfs", rig.Main.Recorder.Status);
        Assert.Null(rig.Main.Overlay);
    }

    [Fact]
    public async Task Record_asks_no_dialog_for_a_recording_that_was_not_saved()
    {
        Rig rig = new();
        await rig.Main.Recorder.ToggleRecordingCommand.ExecuteAsync(null); // a take, not saved
        rig.Main.Recorder.StopCommand.Execute(null);

        await Press(rig, ShortcutAction.Record);

        Assert.True(rig.Main.Recorder.IsRecording);
        Assert.Contains("autosave", Recorder(rig).Calls);
        Assert.Null(rig.Main.Overlay);
    }

    [Fact]
    public async Task When_the_recording_cannot_be_saved_no_new_one_is_started()
    {
        Rig rig = new();
        await rig.Main.Recorder.ToggleRecordingCommand.ExecuteAsync(null);
        rig.Main.Recorder.StopCommand.Execute(null);
        Recorder(rig).AutoSaveFails = true;
        Recorder(rig).Calls.Clear();

        await Press(rig, ShortcutAction.Record);

        Assert.False(rig.Main.Recorder.IsRecording);
        Assert.Equal(["autosave"], Recorder(rig).Calls);
        MessageViewModel message = Assert.IsType<MessageViewModel>(rig.Main.Overlay);
        Assert.Contains("disk full", message.Message);
        Assert.Contains("No new recording was started", message.Message);
    }

    [Fact]
    public async Task Record_does_nothing_while_a_take_plays()
    {
        Rig rig = new();
        rig.Main.Recorder.TogglePlayCommand.Execute(null);
        Recorder(rig).Calls.Clear();

        await Press(rig, ShortcutAction.Record);

        Assert.Empty(Recorder(rig).Calls);
        Assert.True(rig.Main.Recorder.IsPlaying);
    }

    [Fact]
    public void Overdub_of_an_empty_recorder_is_a_plain_recording()
    {
        FakeRecorderSource nothing = new(empty: true);
        Rig rig = new();
        MainViewModel main = new(rig.Services with { Recorder = nothing });

        main.Recorder.HotkeyOverdub();

        Assert.Equal(["record"], nothing.Calls);
        Assert.True(main.Recorder.IsRecording);
        Assert.False(main.Recorder.IsOverdubbing);
    }

    [Fact]
    public async Task Overdub_lays_a_new_pass_over_the_take()
    {
        Rig rig = new();
        Recorder(rig).Calls.Clear();

        await Press(rig, ShortcutAction.Overdub);

        Assert.Equal(["overdub"], Recorder(rig).Calls);
        Assert.True(rig.Main.Recorder.IsOverdubbing);
    }

    [Fact]
    public async Task Overdub_pressed_again_stops_the_pass_and_starts_another()
    {
        Rig rig = new();
        await Press(rig, ShortcutAction.Overdub);
        Recorder(rig).Calls.Clear();

        await Press(rig, ShortcutAction.Overdub);

        Assert.Equal(["stop", "overdub"], Recorder(rig).Calls);
        Assert.True(rig.Main.Recorder.IsOverdubbing);
    }

    [Fact]
    public async Task Stop_stops_the_recorder_and_does_nothing_when_it_is_idle()
    {
        Rig rig = new();
        Recorder(rig).Calls.Clear();

        await Press(rig, ShortcutAction.Stop);
        Assert.Empty(Recorder(rig).Calls);

        rig.Main.Recorder.TogglePlayCommand.Execute(null);
        await Press(rig, ShortcutAction.Stop);
        Assert.Equal(RecorderMode.Idle, rig.Main.Recorder.Mode);
    }

    [Fact]
    public async Task Replay_plays_pauses_and_goes_on_but_not_while_recording()
    {
        Rig rig = new();

        await Press(rig, ShortcutAction.Replay);
        Assert.True(rig.Main.Recorder.IsPlaying);
        await Press(rig, ShortcutAction.Replay);
        Assert.False(rig.Main.Recorder.IsPlaying);
        await Press(rig, ShortcutAction.Replay);
        Assert.True(rig.Main.Recorder.IsPlaying);

        await Press(rig, ShortcutAction.Stop);
        await Press(rig, ShortcutAction.Record);
        Recorder(rig).Calls.Clear();
        await Press(rig, ShortcutAction.Replay);
        Assert.Empty(Recorder(rig).Calls);
    }

    [Fact]
    public async Task The_recorder_shortcuts_read_the_recorder_first_because_the_tab_is_not_refreshed_while_hidden()
    {
        Rig rig = new();
        Assert.Equal(RecorderMode.Idle, rig.Main.Recorder.Mode);

        // something outside the UI starts the take; the hidden tab has not seen it
        Recorder(rig).TogglePlay();
        Assert.Equal(RecorderMode.Idle, rig.Main.Recorder.Mode);

        await Press(rig, ShortcutAction.Stop);

        Assert.Equal(RecorderMode.Idle, rig.Main.Recorder.Mode);
        Assert.False(Recorder(rig).GetStatus().Playing);
    }
}
