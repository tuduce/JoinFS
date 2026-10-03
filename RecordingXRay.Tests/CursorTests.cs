using RecordingXRay.ViewModels;

namespace RecordingXRay.Tests;

public class LaneExtentTests
{
    [Fact]
    public void HasDataAt_and_ClampToExtent_use_the_first_and_last_frame()
    {
        LaneViewModel lane = new(TestFrames.Aircraft("A", TestFrames.Position(10), TestFrames.Position(20)));

        Assert.True(lane.HasDataAt(10));
        Assert.True(lane.HasDataAt(15));
        Assert.True(lane.HasDataAt(20));
        Assert.False(lane.HasDataAt(9.999));
        Assert.False(lane.HasDataAt(20.001));
        Assert.Equal(10, lane.ClampToExtent(0));
        Assert.Equal(15, lane.ClampToExtent(15));
        Assert.Equal(20, lane.ClampToExtent(99));
    }

    [Fact]
    public void An_empty_lane_never_has_data() =>
        Assert.False(new LaneViewModel(new RecordedObject()).HasDataAt(0));

    [Fact]
    public void FrameIndexAt_prefers_the_last_position_frame_at_or_before_the_time()
    {
        LaneViewModel lane = new(TestFrames.SampleAircraft()); // positions at 0, 1, 2, 6; others at 3 and 7

        Assert.Equal(0, lane.FrameIndexAt(0.0));   // before the first frame: the first position frame
        Assert.Equal(0, lane.FrameIndexAt(0.026));
        Assert.Equal(0, lane.FrameIndexAt(0.1));
        Assert.Equal(1, lane.FrameIndexAt(0.101));
        Assert.Equal(2, lane.FrameIndexAt(0.153)); // the variables frames at 0.154 are skipped
        Assert.Equal(2, lane.FrameIndexAt(0.154));
        Assert.Equal(6, lane.FrameIndexAt(0.25));
        Assert.Equal(6, lane.FrameIndexAt(99));
    }

    [Fact]
    public void FrameIndexAt_falls_back_to_any_frame_when_a_lane_has_no_position_frames()
    {
        LaneViewModel lane = new(TestFrames.Aircraft("A", TestFrames.Integers(1, (1, 1)), TestFrames.Floats(2, (1, 1f)), TestFrames.Event(3)));

        Assert.Equal(0, lane.FrameIndexAt(0));
        Assert.Equal(1, lane.FrameIndexAt(2.5));
        Assert.Equal(2, lane.FrameIndexAt(10));
        Assert.Equal(-1, new LaneViewModel(new RecordedObject()).FrameIndexAt(1));
    }

    [Fact]
    public void Times_are_kept_per_kind_and_sorted()
    {
        LaneViewModel lane = new(TestFrames.Aircraft("A", TestFrames.Position(3), TestFrames.Position(1), TestFrames.Integers(2, (1, 1))));

        Assert.Equal([1.0, 3.0], lane.TimesByKind[FrameKind.Position]);
        Assert.Equal([2.0], lane.TimesByKind[FrameKind.Integer]);
        Assert.False(lane.TimesByKind.ContainsKey(FrameKind.Float));
    }
}

public class TimelineViewModelTests
{
    private static TimelineViewModel Timeline(double duration = 600, Action<LaneViewModel>? pick = null) =>
        new([new LaneViewModel(TestFrames.Aircraft("A", TestFrames.Position(10), TestFrames.Position(20))), new LaneViewModel(TestFrames.Aircraft("B", TestFrames.Position(100), TestFrames.Integers(110, (1, 1))))], duration, pick);

    [Fact]
    public void Starts_showing_the_whole_recording_with_a_legend_of_the_kinds_present()
    {
        TimelineViewModel timeline = Timeline();

        Assert.Equal(0, timeline.ViewStart);
        Assert.Equal(600, timeline.ViewSpan);
        Assert.Equal(0, timeline.Zoom);
        Assert.Equal(["Position", "Integer"], timeline.Legend.Select(item => item.Label));
        Assert.Equal(2, timeline.Tracks.Count);
    }

    [Fact]
    public void Update_marks_the_selected_track_and_the_tracks_with_data_at_the_cursor()
    {
        TimelineViewModel timeline = Timeline();

        timeline.Update(timeline.Tracks[1].Lane, 15);

        Assert.False(timeline.Tracks[0].IsSelected);
        Assert.True(timeline.Tracks[1].IsSelected);
        Assert.True(timeline.Tracks[0].HasData);
        Assert.False(timeline.Tracks[1].HasData);
        Assert.Equal("No data at the cursor", timeline.Tracks[1].DataTip);
    }

    [Fact]
    public void Clicking_a_track_header_calls_the_pick_action()
    {
        LaneViewModel? picked = null;
        TimelineViewModel timeline = Timeline(pick: lane => picked = lane);

        timeline.Tracks[1].PickCommand.Execute(null);

        Assert.Same(timeline.Tracks[1].Lane, picked);
    }

    [Fact]
    public void ZoomBy_keeps_the_anchor_time_where_it_is_on_screen()
    {
        TimelineViewModel timeline = Timeline();

        timeline.ZoomBy(2, 300); // the anchor is in the middle of the view

        Assert.Equal(300, timeline.ViewSpan, precision: 6);
        Assert.Equal(150, timeline.ViewStart, precision: 6);

        timeline.ZoomBy(2, 150); // the anchor is at the left edge
        Assert.Equal(150, timeline.ViewSpan, precision: 6);
        Assert.Equal(150, timeline.ViewStart, precision: 6);
    }

    [Fact]
    public void Zoom_is_limited_to_the_whole_recording_and_the_minimum_span()
    {
        TimelineViewModel timeline = Timeline();

        timeline.ZoomBy(0.1, 300);
        Assert.Equal(600, timeline.ViewSpan);
        Assert.Equal(0, timeline.ViewStart);

        timeline.ZoomBy(1_000_000, 300);
        Assert.Equal(TimelineViewModel.MinSpan, timeline.ViewSpan, precision: 6);
        Assert.Equal(100, timeline.Zoom, precision: 6);
    }

    [Fact]
    public void The_zoom_slider_and_the_span_follow_each_other()
    {
        TimelineViewModel timeline = Timeline();

        timeline.Zoom = 50;
        double span = timeline.ViewSpan;
        Assert.InRange(span, 1, 600);
        Assert.Equal(300, timeline.ViewStart + span / 2, precision: 6); // zoomed around the middle

        timeline.ZoomBy(1, 300); // no change
        Assert.Equal(50, timeline.Zoom, precision: 6);

        timeline.ShowAll();
        Assert.Equal(0, timeline.Zoom, precision: 6);
        Assert.Equal(600, timeline.ViewSpan, precision: 6);
    }

    [Fact]
    public void Pan_and_EnsureVisible_stay_inside_the_recording()
    {
        TimelineViewModel timeline = Timeline();
        timeline.ZoomBy(6, 300); // 100 s across

        timeline.Pan(-1000);
        Assert.Equal(0, timeline.ViewStart);

        timeline.Pan(1000);
        Assert.Equal(500, timeline.ViewStart);

        timeline.EnsureVisible(250); // off screen: centred
        Assert.Equal(200, timeline.ViewStart, precision: 6);

        timeline.EnsureVisible(220); // already visible: untouched
        Assert.Equal(200, timeline.ViewStart, precision: 6);

        timeline.EnsureVisible(0); // near the start: clamped
        Assert.Equal(0, timeline.ViewStart);
    }

    [Fact]
    public void A_very_short_recording_does_not_break_the_view()
    {
        TimelineViewModel timeline = new([], 0.1);

        Assert.Equal(TimelineViewModel.MinSpan, timeline.Duration);
        timeline.ZoomBy(10, 0.2);
        Assert.Equal(0, timeline.ViewStart);
        Assert.Equal(TimelineViewModel.MinSpan, timeline.ViewSpan);
    }
}

public sealed class CursorSelectionTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("xray-cursor").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    // A: 0-100 s (every 10 s), B: 40-60 s, C: 80-100 s, D: 0-20 s.
    private async Task<MainViewModel> LoadAsync()
    {
        MainViewModel viewModel = new(_ => "name");
        await viewModel.LoadAsync(RecordingFiles.WriteAircraft(
            directory,
            ("A", RecordingFiles.Times(0, 10, 11)),
            ("B", RecordingFiles.Times(40, 10, 3)),
            ("C", RecordingFiles.Times(80, 10, 3)),
            ("D", RecordingFiles.Times(0, 10, 3))));
        return viewModel;
    }

    private static LaneViewModel Lane(MainViewModel viewModel, string name) => viewModel.Lanes.Single(lane => lane.Name == name);

    [Fact]
    public async Task Loading_selects_the_first_aircraft_with_the_cursor_on_its_first_frame()
    {
        MainViewModel viewModel = await LoadAsync();

        Assert.Equal("A", viewModel.SelectedLane!.Name);
        Assert.Equal(0, viewModel.Cursor);
        Assert.Equal(100, viewModel.Duration);
        Assert.Equal("00:00.000", viewModel.CursorText);
        Assert.Equal("/ 1:40.000", viewModel.DurationText);
        Assert.Equal("frame [0] of 11", viewModel.FrameCounterText);
        Assert.Equal("A › [0] AircraftPosition @ 0.000 s", viewModel.FrameStatusText);
        Assert.Equal([true, false, false, true], viewModel.Timeline.Tracks.Select(track => track.HasData));
        Assert.True(viewModel.Timeline.Tracks[0].IsSelected);
    }

    [Fact]
    public async Task Selecting_an_aircraft_with_data_at_the_cursor_keeps_the_cursor()
    {
        MainViewModel viewModel = await LoadAsync();
        viewModel.MoveCursor(45);

        viewModel.SelectAircraft(Lane(viewModel, "B"));

        Assert.Equal(45, viewModel.Cursor);
        Assert.Equal("B", viewModel.SelectedLane!.Name);
        Assert.Equal(0, viewModel.Browser.SelectedRow!.Index); // the last frame at or before 45 s is B's first, at 40 s
        Assert.Equal("B · frame [0] · 40.000 s", viewModel.Inspector.Subtitle);
    }

    [Fact]
    public async Task Selecting_an_aircraft_that_starts_after_the_cursor_jumps_to_its_first_frame()
    {
        MainViewModel viewModel = await LoadAsync();
        viewModel.MoveCursor(20);

        viewModel.SelectAircraft(Lane(viewModel, "C"));

        Assert.Equal(80, viewModel.Cursor);
        Assert.Equal("C · frame [0] · 80.000 s", viewModel.Inspector.Subtitle);
        Assert.True(viewModel.Timeline.Tracks[2].HasData);
    }

    [Fact]
    public async Task Selecting_an_aircraft_that_ended_before_the_cursor_jumps_to_its_last_frame()
    {
        MainViewModel viewModel = await LoadAsync();
        viewModel.MoveCursor(70);

        viewModel.SelectAircraft(Lane(viewModel, "D"));

        Assert.Equal(20, viewModel.Cursor);
        Assert.Equal("D · frame [2] · 20.000 s", viewModel.Inspector.Subtitle);
        Assert.Equal("frame [2] of 3", viewModel.FrameCounterText);
    }

    [Fact]
    public async Task Selecting_the_same_aircraft_again_brings_the_cursor_back_into_its_lane()
    {
        MainViewModel viewModel = await LoadAsync();
        viewModel.SelectAircraft(Lane(viewModel, "D"));
        viewModel.MoveCursor(90);
        Assert.False(viewModel.Timeline.Tracks[3].HasData);

        viewModel.SelectAircraft(Lane(viewModel, "D"));

        Assert.Equal(20, viewModel.Cursor);
        Assert.True(viewModel.Timeline.Tracks[3].HasData);
    }

    [Fact]
    public async Task Moving_the_cursor_never_changes_the_selected_aircraft_but_shows_the_frame_at_that_time()
    {
        MainViewModel viewModel = await LoadAsync();
        viewModel.SelectAircraft(Lane(viewModel, "D"));

        viewModel.MoveCursor(90);

        Assert.Equal("D", viewModel.SelectedLane!.Name);
        Assert.Equal(90, viewModel.Cursor);
        Assert.Equal("D · frame [2] · 20.000 s", viewModel.Inspector.Subtitle); // D has nothing later: its last frame
        Assert.False(viewModel.Timeline.Tracks[3].HasData);

        viewModel.MoveCursor(14);
        Assert.Equal("D · frame [1] · 10.000 s", viewModel.Inspector.Subtitle);
        Assert.Equal("00:14.000", viewModel.CursorText);
    }

    [Fact]
    public async Task The_cursor_is_clamped_to_the_recording()
    {
        MainViewModel viewModel = await LoadAsync();

        viewModel.MoveCursor(-5);
        Assert.Equal(0, viewModel.Cursor);

        viewModel.MoveCursor(5000);
        Assert.Equal(100, viewModel.Cursor);

        viewModel.MoveCursor(double.NaN);
        Assert.Equal(0, viewModel.Cursor);
    }

    [Fact]
    public async Task Picking_a_frame_in_the_list_moves_the_cursor_to_that_frame_and_keeps_it_shown()
    {
        MainViewModel viewModel = await LoadAsync();

        viewModel.Browser.SelectedRow = viewModel.Browser.Rows[3]; // A's frame at 30 s

        Assert.Equal(30, viewModel.Cursor);
        Assert.Equal("A · frame [3] · 30.000 s", viewModel.Inspector.Subtitle);
        Assert.Equal(3, viewModel.Browser.SelectedRow!.Index);
    }

    [Fact]
    public async Task Stepping_walks_the_frames_of_the_selected_aircraft_and_stops_at_the_ends()
    {
        MainViewModel viewModel = await LoadAsync();

        viewModel.StepNextCommand.Execute(null);
        viewModel.StepNextCommand.Execute(null);
        Assert.Equal(20, viewModel.Cursor);
        Assert.Equal("A · frame [2] · 20.000 s", viewModel.Inspector.Subtitle);

        viewModel.StepPreviousCommand.Execute(null);
        Assert.Equal(10, viewModel.Cursor);

        viewModel.GoToLastFrameCommand.Execute(null);
        Assert.Equal(100, viewModel.Cursor);
        viewModel.StepNextCommand.Execute(null);
        Assert.Equal(100, viewModel.Cursor);

        viewModel.GoToFirstFrameCommand.Execute(null);
        Assert.Equal(0, viewModel.Cursor);
        viewModel.StepPreviousCommand.Execute(null);
        Assert.Equal(0, viewModel.Cursor);
    }

    [Fact]
    public async Task Stepping_continues_from_the_frame_at_the_cursor_after_scrubbing()
    {
        MainViewModel viewModel = await LoadAsync();
        viewModel.MoveCursor(34);

        viewModel.StepNextCommand.Execute(null);

        Assert.Equal(40, viewModel.Cursor);
    }

    [Fact]
    public async Task Stepping_honours_the_type_filter()
    {
        MainViewModel viewModel = new(_ => "name");
        // Positions at 0, 10, 20, 30; integer variables at 5, 15, 25.
        await viewModel.LoadAsync(RecordingFiles.WriteMixed(directory, RecordingFiles.Times(0, 10, 4), RecordingFiles.Times(5, 10, 3)));

        viewModel.StepNextCommand.Execute(null);
        Assert.Equal(5, viewModel.Cursor); // all frames: the next one is the integer frame

        viewModel.Browser.TypeChips.Single(chip => chip.Kind == FrameKind.Integer).IsOn = false;
        viewModel.StepNextCommand.Execute(null);
        Assert.Equal(10, viewModel.Cursor); // positions only
        viewModel.StepNextCommand.Execute(null);
        Assert.Equal(20, viewModel.Cursor);
        Assert.Equal("MIX · frame [4] · 20.000 s", viewModel.Inspector.Subtitle);

        viewModel.StepPreviousCommand.Execute(null);
        Assert.Equal(10, viewModel.Cursor);
    }

    [Fact]
    public async Task Scrubbing_shows_position_frames_even_when_variables_share_the_time()
    {
        MainViewModel viewModel = new(_ => "name");
        await viewModel.LoadAsync(RecordingFiles.WriteMixed(directory, RecordingFiles.Times(0, 10, 4), RecordingFiles.Times(5, 10, 3)));

        viewModel.MoveCursor(17); // the last frame at or before is the integer frame at 15 s, but a position frame is shown

        Assert.Equal("MIX · frame [2] · 10.000 s", viewModel.Inspector.Subtitle);
        Assert.Equal("AircraftPosition", viewModel.Inspector.TypeName);
    }

    [Fact]
    public async Task Scrubbing_updates_the_inspector_even_when_the_filter_hides_position_frames_and_keeps_the_filter()
    {
        MainViewModel viewModel = new(_ => "name");
        await viewModel.LoadAsync(RecordingFiles.WriteAircraft(directory, ("A", RecordingFiles.Times(0, 10, 11))));
        viewModel.Browser.TypeChips.Single().IsOn = false; // hides every frame: the lane has only position frames

        viewModel.MoveCursor(55);

        Assert.Equal("A · frame [5] · 50.000 s", viewModel.Inspector.Subtitle);
        Assert.Empty(viewModel.Browser.Rows);
        Assert.False(viewModel.Browser.TypeChips.Single().IsOn);
    }

    [Fact]
    public async Task Up_and_Down_move_through_the_aircraft_with_the_usual_cursor_rule()
    {
        MainViewModel viewModel = await LoadAsync();
        viewModel.MoveCursor(90);

        viewModel.SelectNeighbour(1); // B ended at 60 s
        Assert.Equal("B", viewModel.SelectedLane!.Name);
        Assert.Equal(60, viewModel.Cursor);

        viewModel.SelectNeighbour(-1);
        Assert.Equal("A", viewModel.SelectedLane!.Name);
        Assert.Equal(60, viewModel.Cursor);

        viewModel.SelectNeighbour(-1); // already the first
        Assert.Equal("A", viewModel.SelectedLane!.Name);
    }

    [Fact]
    public async Task The_picker_setter_goes_through_the_same_rule()
    {
        MainViewModel viewModel = await LoadAsync();
        viewModel.MoveCursor(10);

        viewModel.SelectedLane = Lane(viewModel, "C");

        Assert.Equal(80, viewModel.Cursor);
    }

    [Fact]
    public async Task Clicking_a_track_header_selects_that_aircraft()
    {
        MainViewModel viewModel = await LoadAsync();
        viewModel.MoveCursor(95);

        viewModel.Timeline.Tracks[2].PickCommand.Execute(null);

        Assert.Equal("C", viewModel.SelectedLane!.Name);
        Assert.Equal(95, viewModel.Cursor);
    }

    [Fact]
    public async Task The_timeline_view_scrolls_to_keep_a_stepped_to_frame_visible()
    {
        MainViewModel viewModel = await LoadAsync();
        viewModel.Timeline.ZoomBy(10, 5); // 10 s across, near the start

        viewModel.GoToLastFrameCommand.Execute(null);

        Assert.InRange(viewModel.Cursor, viewModel.Timeline.ViewStart, viewModel.Timeline.ViewEnd);
    }

    [Fact]
    public async Task Loading_another_recording_resets_the_cursor_and_the_timeline()
    {
        MainViewModel viewModel = await LoadAsync();
        viewModel.MoveCursor(90);
        viewModel.Timeline.ZoomBy(4, 90);

        await viewModel.LoadAsync(RecordingFiles.WriteAircraft(directory, ("Z", RecordingFiles.Times(5, 1, 4))));

        Assert.Equal(5, viewModel.Cursor);
        Assert.Equal("Z", viewModel.SelectedLane!.Name);
        Assert.Single(viewModel.Timeline.Tracks);
        Assert.Equal(0, viewModel.Timeline.Zoom);
    }
}
