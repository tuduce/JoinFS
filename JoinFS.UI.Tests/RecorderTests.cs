using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Overlays;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Tests;

public class RecorderTests
{
    private static FakeRecorderSource Source(Rig rig) => (FakeRecorderSource)rig.Services.Recorder;

    private static RecorderViewModel Open(Rig rig) => rig.Main.Recorder;

    // ---- the transport

    [Fact]
    public void It_starts_idle_on_the_recording_that_is_loaded()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);

        Assert.Equal(RecorderMode.Idle, recorder.Mode);
        Assert.Equal("00:01:32", recorder.TotalText);
        Assert.Equal("Loaded recording — session_2609.jfs", recorder.LoadedTitle);
        Assert.Equal(6, recorder.LoadedAircraft.Count);
        Assert.All(recorder.LoadedAircraft, a => Assert.True(a.IsChecked));
    }

    [Fact]
    public async Task Record_starts_a_new_recording_and_pressing_it_again_stops()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);

        await recorder.ToggleRecordingCommand.ExecuteAsync(null);
        Assert.True(recorder.IsRecording);
        Assert.Equal(RecorderMode.Recording, recorder.Mode);
        Assert.Contains("record", Source(rig).Calls);

        await recorder.ToggleRecordingCommand.ExecuteAsync(null);
        Assert.Equal(RecorderMode.Idle, recorder.Mode);
    }

    [Fact]
    public void Play_plays_and_then_pauses_and_goes_on()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);

        recorder.TogglePlayCommand.Execute(null);
        Assert.True(recorder.IsPlaying);

        recorder.TogglePlayCommand.Execute(null); // pause
        Assert.Equal(RecorderMode.Playing, recorder.Mode);
        Assert.False(recorder.IsPlaying); // a paused take does not look as if it played

        recorder.TogglePlayCommand.Execute(null); // go on
        Assert.True(recorder.IsPlaying);
    }

    [Fact]
    public void Overdub_records_on_top_of_the_take_and_pressing_it_again_stops()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);

        recorder.ToggleOverdubCommand.Execute(null);

        Assert.True(recorder.IsOverdubbing);
        Assert.True(recorder.IsRecording); // overdub is recording on top of the take
        Assert.False(recorder.IsPlaying);

        recorder.ToggleOverdubCommand.Execute(null);
        Assert.Equal(RecorderMode.Idle, recorder.Mode);
    }

    [Fact]
    public async Task Record_while_a_take_plays_stops_it_first()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        recorder.TogglePlayCommand.Execute(null);

        await recorder.ToggleRecordingCommand.ExecuteAsync(null);

        Assert.Equal(RecorderMode.Recording, recorder.Mode);
        Assert.Equal(["play", "stop", "record"], Source(rig).Calls.Where(c => c is "play" or "stop" or "record"));
    }

    [Fact]
    public async Task Stop_stops_whatever_is_going_on_and_is_off_when_nothing_is()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        Assert.False(recorder.StopCommand.CanExecute(null));

        await recorder.ToggleRecordingCommand.ExecuteAsync(null);
        Assert.True(recorder.StopCommand.CanExecute(null));
        recorder.StopCommand.Execute(null);

        Assert.Equal(RecorderMode.Idle, recorder.Mode);
    }

    [Fact]
    public async Task While_recording_there_is_nothing_to_play_or_lay_over()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);

        await recorder.ToggleRecordingCommand.ExecuteAsync(null);

        Assert.False(recorder.TogglePlayCommand.CanExecute(null));
        Assert.False(recorder.ToggleOverdubCommand.CanExecute(null));
    }

    private static MainViewModel OpenEmpty(out FakeRecorderSource source, out Services.Fake.NullPlatform platform)
    {
        source = new FakeRecorderSource(empty: true);
        platform = new Services.Fake.NullPlatform();
        return new MainViewModel(FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }, platform) with { Recorder = source });
    }

    [Fact]
    public void An_empty_recorder_has_nothing_to_play_or_lay_over_but_can_record()
    {
        RecorderViewModel recorder = OpenEmpty(out _, out _).Recorder;

        Assert.False(recorder.TogglePlayCommand.CanExecute(null));
        Assert.False(recorder.ToggleOverdubCommand.CanExecute(null));
        Assert.True(recorder.ToggleRecordingCommand.CanExecute(null));
        Assert.Equal("Loaded recording — nothing recorded", recorder.LoadedTitle);
    }

    [Fact]
    public async Task An_empty_recorder_has_nothing_to_save()
    {
        MainViewModel main = OpenEmpty(out FakeRecorderSource source, out Services.Fake.NullPlatform platform);
        platform.SavePath = @"C:\Recordings\out.jfs";

        await main.Recorder.SaveCommand.ExecuteAsync(null);

        Assert.DoesNotContain(source.Calls, c => c.StartsWith("save"));
        Assert.Contains("empty", main.Recorder.Status);
    }

    [Fact]
    public void The_time_follows_the_recorder()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        recorder.TogglePlayCommand.Execute(null);

        Source(rig).Advance(12.7);
        recorder.Refresh();

        Assert.Equal("00:00:12", recorder.PlayheadText);
        Assert.Equal(12, recorder.PlayheadSeconds);
    }

    [Fact]
    public void A_take_that_ends_goes_back_to_idle_and_loops_when_asked_to()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        recorder.TogglePlayCommand.Execute(null);
        Source(rig).Advance(100);
        recorder.Refresh();
        Assert.Equal(RecorderMode.Idle, recorder.Mode);

        recorder.ToggleLoopCommand.Execute(null);
        recorder.TogglePlayCommand.Execute(null);
        Source(rig).Advance(100);
        recorder.Refresh();
        Assert.Equal(RecorderMode.Playing, recorder.Mode);
    }

    [Fact]
    public void The_loop_switch_is_the_recorders()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);

        recorder.ToggleLoopCommand.Execute(null);
        Assert.True(Source(rig).Loop);

        Source(rig).Loop = false;
        recorder.Refresh();
        Assert.False(recorder.LoopEnabled);
    }

    // ---- the playhead

    [Fact]
    public void The_playhead_only_moves_while_a_take_plays()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        Assert.False(recorder.CanSeek);

        recorder.PlayheadFraction = 0.5;
        Assert.Equal(0, recorder.PlayheadSeconds);
        Assert.DoesNotContain(Source(rig).Calls, c => c.StartsWith("seek"));

        recorder.TogglePlayCommand.Execute(null);
        recorder.PlayheadFraction = 0.5;

        Assert.Equal(46, recorder.PlayheadSeconds);
        Assert.Contains("seek 46", Source(rig).Calls);
    }

    [Fact]
    public void Seeking_is_held_to_the_take()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        recorder.TogglePlayCommand.Execute(null);

        recorder.Seek(2);
        Assert.Equal(92, recorder.PlayheadSeconds);
        recorder.Seek(-1);
        Assert.Equal(0, recorder.PlayheadSeconds);
    }

    [Fact]
    public void Seeking_works_while_paused_too()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        recorder.TogglePlayCommand.Execute(null);
        recorder.TogglePlayCommand.Execute(null); // pause

        recorder.Seek(0.25);

        Assert.Equal(23, recorder.PlayheadSeconds);
    }

    [Fact]
    public void Trim_start_cuts_what_is_before_the_playhead()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        recorder.TogglePlayCommand.Execute(null);
        Source(rig).Advance(7);
        recorder.Refresh();

        recorder.TrimStartCommand.Execute(null);

        Assert.Equal((85, 0), (recorder.TotalSeconds, recorder.PlayheadSeconds));
    }

    [Fact]
    public void Trim_end_cuts_what_is_after_the_playhead()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        recorder.TogglePlayCommand.Execute(null);
        Source(rig).Advance(34);
        recorder.Refresh();

        recorder.TrimEndCommand.Execute(null);

        Assert.Equal(34, recorder.TotalSeconds);
    }

    [Fact]
    public void Trimming_at_the_start_or_the_end_says_there_is_nothing_to_cut()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        recorder.TogglePlayCommand.Execute(null);

        recorder.TrimStartCommand.Execute(null);
        Assert.Equal("Already at the start of the recording.", recorder.Status);
        Assert.DoesNotContain("trim start", Source(rig).Calls);

        Source(rig).Seek(92);
        recorder.Refresh();
        recorder.TrimEndCommand.Execute(null);
        Assert.Equal("Already at the end of the recording.", recorder.Status);
    }

    [Fact]
    public void Nothing_is_trimmed_while_the_take_is_not_playing()
    {
        // the playhead is only where it is while a take plays: trimming from it when it is not would cut the whole take
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);

        Assert.False(recorder.TrimStartCommand.CanExecute(null));
        Assert.False(recorder.TrimEndCommand.CanExecute(null));
    }

    // ---- files

    [Fact]
    public async Task Open_loads_the_chosen_file_and_names_the_recording_after_it()
    {
        Rig rig = new();
        rig.Platform.PickedFile = @"C:\Recordings\flight.jfs";

        await Open(rig).OpenCommand.ExecuteAsync(null);

        Assert.Contains(@"open C:\Recordings\flight.jfs", Source(rig).Calls);
        Assert.Equal("Loaded recording — flight.jfs", Open(rig).LoadedTitle);
        Assert.Equal(@"C:\Recordings", rig.Platform.LastStartFolder);
        Assert.Equal(RecorderMode.Playing, Open(rig).Mode); // as the old window, a recording that is opened plays
    }

    [Fact]
    public async Task Add_lays_a_file_after_the_end_of_the_recording()
    {
        Rig rig = new();
        rig.Platform.PickedFile = @"C:\Recordings\more.jfs";

        await Open(rig).AddCommand.ExecuteAsync(null);

        Assert.Contains(@"add C:\Recordings\more.jfs", Source(rig).Calls);
        Assert.Equal("00:03:04", Open(rig).TotalText);
        Assert.Equal(12, Open(rig).LoadedAircraft.Count);
    }

    [Fact]
    public async Task A_file_that_cannot_be_used_says_why_and_changes_nothing()
    {
        Rig rig = new();
        rig.Platform.PickedFile = @"C:\Recordings\bad.jfs";

        await Open(rig).OpenCommand.ExecuteAsync(null);

        Assert.Contains("old version", Open(rig).Status);
        Assert.Equal("Loaded recording — session_2609.jfs", Open(rig).LoadedTitle);
    }

    [Fact]
    public async Task Cancelling_the_file_dialog_opens_nothing()
    {
        Rig rig = new();
        rig.Platform.PickedFile = null;

        await Open(rig).OpenCommand.ExecuteAsync(null);

        Assert.DoesNotContain(Source(rig).Calls, c => c.StartsWith("open"));
    }

    [Fact]
    public async Task Nothing_is_opened_or_saved_while_the_recorder_is_active()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        recorder.TogglePlayCommand.Execute(null);
        rig.Platform.PickedFile = @"C:\Recordings\flight.jfs";
        rig.Platform.SavePath = @"C:\Recordings\out.jfs";

        await recorder.OpenCommand.ExecuteAsync(null);
        Assert.Contains("active", recorder.Status);
        await recorder.SaveCommand.ExecuteAsync(null);

        Assert.DoesNotContain(Source(rig).Calls, c => c.StartsWith("open") || c.StartsWith("save"));
    }

    [Fact]
    public async Task Save_writes_where_the_user_chose_and_names_the_recording_after_it()
    {
        Rig rig = new();
        rig.Platform.SavePath = @"C:\Recordings\mine.jfs";

        await Open(rig).SaveCommand.ExecuteAsync(null);

        Assert.Contains(@"save C:\Recordings\mine.jfs", Source(rig).Calls);
        Assert.Equal("Loaded recording — mine.jfs", Open(rig).LoadedTitle);
    }

    [Fact]
    public async Task Cancelling_the_save_dialog_saves_nothing()
    {
        Rig rig = new();
        rig.Platform.SavePath = null;

        await Open(rig).SaveCommand.ExecuteAsync(null);

        Assert.DoesNotContain(Source(rig).Calls, c => c.StartsWith("save"));
    }

    // ---- a recording that was not saved

    [Fact]
    public async Task A_recording_that_was_made_is_unsaved_until_it_is_saved()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        Assert.False(recorder.HasUnsavedRecording); // a file that was loaded is not

        await recorder.ToggleRecordingCommand.ExecuteAsync(null);
        recorder.StopCommand.Execute(null);
        Assert.True(recorder.HasUnsavedRecording);

        rig.Platform.SavePath = @"C:\Recordings\mine.jfs";
        await recorder.SaveCommand.ExecuteAsync(null);
        Assert.False(recorder.HasUnsavedRecording);
    }

    [Fact]
    public async Task Recording_again_asks_about_the_unsaved_recording_and_saves_it_when_told_to()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        await recorder.ToggleRecordingCommand.ExecuteAsync(null);
        recorder.StopCommand.Execute(null);
        rig.Platform.SavePath = @"C:\Recordings\first.jfs";

        Task again = recorder.ToggleRecordingCommand.ExecuteAsync(null);
        ConfirmViewModel ask = Assert.IsType<ConfirmViewModel>(rig.Main.Overlay);
        ask.YesCommand.Execute(null);
        await again;

        Assert.Equal(["record", "stop", "save C:\\Recordings\\first.jfs", "record"], Source(rig).Calls.Where(c => c is "record" or "stop" || c.StartsWith("save")));
        Assert.Equal(RecorderMode.Recording, recorder.Mode);
    }

    [Fact]
    public async Task Recording_again_without_saving_the_old_one_when_told_not_to()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        await recorder.ToggleRecordingCommand.ExecuteAsync(null);
        recorder.StopCommand.Execute(null);

        Task again = recorder.ToggleRecordingCommand.ExecuteAsync(null);
        ((ConfirmViewModel)rig.Main.Overlay!).NoCommand.Execute(null);
        await again;

        Assert.DoesNotContain(Source(rig).Calls, c => c.StartsWith("save"));
        Assert.Equal(RecorderMode.Recording, recorder.Mode);
    }

    [Fact]
    public async Task Changing_your_mind_leaves_the_recording_as_it_was()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        await recorder.ToggleRecordingCommand.ExecuteAsync(null);
        recorder.StopCommand.Execute(null);
        int recordings = Source(rig).Calls.Count(c => c == "record");

        Task again = recorder.ToggleRecordingCommand.ExecuteAsync(null);
        rig.Main.Overlay!.Close(); // the cross
        await again;

        Assert.Equal(recordings, Source(rig).Calls.Count(c => c == "record"));
        Assert.Equal(RecorderMode.Idle, recorder.Mode);
    }

    [Fact]
    public async Task A_save_that_was_cancelled_does_not_start_the_new_recording()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        await recorder.ToggleRecordingCommand.ExecuteAsync(null);
        recorder.StopCommand.Execute(null);
        rig.Platform.SavePath = null;

        Task again = recorder.ToggleRecordingCommand.ExecuteAsync(null);
        ((ConfirmViewModel)rig.Main.Overlay!).YesCommand.Execute(null);
        await again;

        Assert.Equal(RecorderMode.Idle, recorder.Mode);
        Assert.True(recorder.HasUnsavedRecording);
    }

    [Fact]
    public async Task Closing_the_window_asks_too_and_goes_on_when_nothing_is_unsaved()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        Assert.True(await recorder.AskToSaveAsync());
        Assert.Null(rig.Main.Overlay);

        await recorder.ToggleRecordingCommand.ExecuteAsync(null);
        recorder.StopCommand.Execute(null);
        Task<bool> close = recorder.AskToSaveAsync();
        ((ConfirmViewModel)rig.Main.Overlay!).NoCommand.Execute(null);

        Assert.True(await close);
    }

    [Fact]
    public async Task Opening_a_file_asks_about_an_unsaved_recording_first()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        await recorder.ToggleRecordingCommand.ExecuteAsync(null);
        recorder.StopCommand.Execute(null);
        rig.Platform.PickedFile = @"C:\Recordingslight.jfs";

        Task open = recorder.OpenCommand.ExecuteAsync(null);
        ((ConfirmViewModel)rig.Main.Overlay!).NoCommand.Execute(null); // do not save it
        await open;

        Assert.Contains(@"open C:\Recordingslight.jfs", Source(rig).Calls);
    }

    [Fact]
    public async Task Changing_your_mind_when_asked_before_opening_leaves_the_recording_alone()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        await recorder.ToggleRecordingCommand.ExecuteAsync(null);
        recorder.StopCommand.Execute(null);
        rig.Platform.PickedFile = @"C:\Recordingslight.jfs";

        Task open = recorder.OpenCommand.ExecuteAsync(null);
        rig.Main.Overlay!.Close();
        await open;

        Assert.DoesNotContain(Source(rig).Calls, c => c.StartsWith("open"));
        Assert.True(recorder.HasUnsavedRecording);
    }

    [Fact]
    public async Task A_file_that_was_opened_is_not_unsaved_but_a_file_that_was_added_changes_the_recording()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        rig.Platform.PickedFile = @"C:\Recordingslight.jfs";

        await recorder.OpenCommand.ExecuteAsync(null);
        recorder.StopCommand.Execute(null);
        Assert.False(recorder.HasUnsavedRecording);

        await recorder.AddCommand.ExecuteAsync(null);
        Assert.True(recorder.HasUnsavedRecording);
    }

    [Fact]
    public void An_aircraft_that_cannot_be_recorded_is_not_offered_to_record()
    {
        ScriptedTraffic traffic = new();
        traffic.Aircraft = [traffic.Info("a") with { Can = AircraftActions.All & ~AircraftActions.Record }, traffic.Info("b")];
        AppServices services = FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }) with { Traffic = traffic };
        MainViewModel main = new(services);

        Assert.Equal(["B"], main.Recorder.LiveAircraft.Select(a => a.Callsign));
    }

    // ---- the loaded recording's aircraft

    [Fact]
    public void Unticking_an_aircraft_leaves_it_out_of_playback_and_the_tick_goes_grey()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        LoadedAircraftViewModel aircraft = recorder.LoadedAircraft[2];
        Assert.True(aircraft.CanTick);

        aircraft.IsChecked = false;

        Assert.Contains("skip r2", Source(rig).Calls);
        Assert.False(aircraft.IsChecked);
        Assert.False(aircraft.CanTick);
    }

    [Fact]
    public void A_skipped_aircraft_cannot_be_ticked_again_until_the_recording_is_loaded_again()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        LoadedAircraftViewModel aircraft = recorder.LoadedAircraft[2];
        aircraft.IsChecked = false;

        aircraft.IsChecked = true;
        recorder.Refresh();

        Assert.False(aircraft.IsChecked);
        Assert.Equal(1, Source(rig).Calls.Count(c => c == "skip r2"));
    }

    [Fact]
    public async Task Loading_the_recording_again_brings_the_aircraft_back()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);
        recorder.LoadedAircraft[2].IsChecked = false;
        recorder.StopCommand.Execute(null);
        rig.Platform.PickedFile = @"C:\Recordings\again.jfs";

        await recorder.OpenCommand.ExecuteAsync(null);

        Assert.All(recorder.LoadedAircraft, a => Assert.True(a.IsChecked && a.CanTick));
    }

    [Fact]
    public void The_list_shows_a_skipped_aircraft_as_skipped_when_the_recorder_says_so()
    {
        Rig rig = new();
        RecorderViewModel recorder = Open(rig);

        Source(rig).SkipAircraft("r1"); // not through the tick
        recorder.Refresh();

        Assert.False(recorder.LoadedAircraft[1].IsChecked);
        Assert.False(recorder.LoadedAircraft[1].CanTick);
    }

    // ---- the tab follows the recorder

    [Fact]
    public void The_transport_is_read_every_poll_while_the_tab_is_shown_and_not_otherwise()
    {
        Rig rig = new();
        rig.Main.GoTo(TabId.Aircraft);
        rig.Main.Recorder.TogglePlayCommand.Execute(null);
        Source(rig).Advance(20);
        rig.Main.Poll();
        Assert.Equal(0, rig.Main.Recorder.PlayheadSeconds); // another tab is shown: the transport is not read

        rig.Main.GoTo(TabId.Recorder);
        Source(rig).Advance(5);
        rig.Main.Poll();

        Assert.Equal(25, rig.Main.Recorder.PlayheadSeconds);
    }
}
