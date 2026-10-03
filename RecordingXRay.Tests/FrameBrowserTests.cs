using System.Collections;
using RecordingXRay.ViewModels;

namespace RecordingXRay.Tests;

public class FrameRowListTests
{
    private static readonly IReadOnlyList<RecordedFrame> Frames = TestFrames.SampleAircraft().Frames;

    [Fact]
    public void Unfiltered_list_has_a_row_per_frame_with_deltas()
    {
        FrameRowList rows = new(Frames, null);

        Assert.Equal(8, rows.Count);
        Assert.Equal("[0]", rows[0].IndexText);
        Assert.Equal("0.026 s", rows[0].TimeText);
        Assert.Equal("—", rows[0].DeltaText);
        Assert.Equal("+75 ms", rows[1].DeltaText);
        Assert.Equal("+0 ms", rows[4].DeltaText);
        Assert.Equal("IntegerVariables", rows[3].TypeName);
        Assert.Equal(FrameKind.Integer, rows[3].Kind);
    }

    [Fact]
    public void Filtered_list_keeps_the_frame_index_and_the_delta_to_the_previous_frame_of_the_lane()
    {
        FrameRowList rows = new(Frames, [0, 6]);

        Assert.Equal(2, rows.Count);
        Assert.Equal("[6]", rows[1].IndexText);
        Assert.Equal("+53 ms", rows[1].DeltaText); // 0.207 - 0.154, the frame before it in the lane
    }

    [Fact]
    public void Rows_compare_by_value_so_selection_survives_re_creation()
    {
        FrameRowList rows = new(Frames, null);

        Assert.Equal(rows[2], rows[2]);
        Assert.Equal(2, ((IList)rows).IndexOf(rows[2]));
        Assert.True(((IList)rows).Contains(rows[5]));
    }

    [Fact]
    public void PositionOf_and_PositionAtOrAfter_work_on_filtered_lists()
    {
        FrameRowList rows = new(Frames, [0, 3, 6]);

        Assert.Equal(1, rows.PositionOf(3));
        Assert.Equal(-1, rows.PositionOf(4));
        Assert.Equal(0, rows.PositionAtOrAfter(0));
        Assert.Equal(1, rows.PositionAtOrAfter(0.15));
        Assert.Equal(2, rows.PositionAtOrAfter(0.2));
        Assert.Equal(2, rows.PositionAtOrAfter(99)); // past the end: the last row
    }

    [Fact]
    public void Empty_list_has_no_rows()
    {
        Assert.Empty(FrameRowList.Empty);
        Assert.Equal(-1, FrameRowList.Empty.PositionAtOrAfter(1));
    }

    [Fact]
    public void Long_gaps_are_shown_in_seconds()
    {
        FrameRow row = new(1, TestFrames.Event(250), 100);

        Assert.Equal("+150.0 s", row.DeltaText);
    }
}

public class FrameBrowserViewModelTests
{
    private static LaneViewModel Lane() => new(TestFrames.SampleAircraft());

    [Fact]
    public void SetLane_selects_the_first_frame_and_offers_chips_for_the_kinds_present()
    {
        FrameBrowserViewModel browser = new();
        List<int> selected = [];
        browser.FrameSelected += (_, row) => selected.Add(row.Index);

        browser.SetLane(Lane());

        Assert.Equal([0], selected);
        Assert.Equal(0, browser.SelectedRow!.Index);
        Assert.Equal(["Position", "Integer", "Float", "String8", "Events"], browser.TypeChips.Select(chip => chip.Label));
        Assert.Equal("8 frames", browser.CountText);
        Assert.Equal("YR-SCD", browser.Title);
    }

    [Fact]
    public void Turning_a_chip_off_hides_that_kind_and_updates_the_count()
    {
        FrameBrowserViewModel browser = new();
        browser.SetLane(Lane());

        browser.TypeChips.Single(chip => chip.Kind == FrameKind.Position).IsOn = false;

        Assert.Equal(4, browser.Rows.Count);
        Assert.DoesNotContain(browser.Rows, row => row.Kind == FrameKind.Position);
        Assert.Equal("4 of 8 frames", browser.CountText);

        browser.TypeChips.Single(chip => chip.Kind == FrameKind.Position).IsOn = true;
        Assert.Equal(8, browser.Rows.Count);
    }

    [Fact]
    public void Text_filters_by_type_name_but_a_time_does_not()
    {
        FrameBrowserViewModel browser = new();
        browser.SetLane(Lane());

        browser.FilterText = "float";
        Assert.Single(browser.Rows);
        Assert.Equal("FloatVariables", browser.Rows[0].TypeName);

        browser.FilterText = "0.2";
        Assert.Equal(8, browser.Rows.Count);
    }

    [Fact]
    public void GoToTime_selects_the_first_frame_at_or_after_the_time_and_asks_to_reveal_it()
    {
        FrameBrowserViewModel browser = new();
        browser.SetLane(Lane());
        FrameRow? revealed = null;
        browser.RevealRequested += row => revealed = row;

        browser.FilterText = "0:00.15";
        browser.GoToTimeCommand.Execute(null);

        Assert.Equal(3, browser.SelectedRow!.Index);
        Assert.Equal(3, revealed!.Index);
    }

    [Fact]
    public void GoToTime_ignores_text_that_is_not_a_time()
    {
        FrameBrowserViewModel browser = new();
        browser.SetLane(Lane());

        browser.FilterText = "Float";
        browser.GoToTimeCommand.Execute(null);

        Assert.Equal(0, browser.SelectedRow!.Index);
    }

    [Fact]
    public void SelectFrame_widens_the_filter_when_it_hides_the_frame()
    {
        FrameBrowserViewModel browser = new();
        browser.SetLane(Lane());
        browser.FilterText = "float";

        browser.SelectFrame(7);

        Assert.Equal(7, browser.SelectedRow!.Index);
        Assert.Equal(8, browser.Rows.Count);
        Assert.Equal(string.Empty, browser.FilterText);
    }

    [Fact]
    public void Filtering_out_the_selected_frame_does_not_raise_a_selection()
    {
        FrameBrowserViewModel browser = new();
        browser.SetLane(Lane());
        int raised = 0;
        browser.FrameSelected += (_, _) => raised++;

        browser.SelectedRow = null; // what the list does when the selected row leaves the list

        Assert.Equal(0, raised);
    }

    [Fact]
    public void SetLane_to_null_clears_the_browser()
    {
        FrameBrowserViewModel browser = new();
        browser.SetLane(Lane());

        browser.SetLane(null);

        Assert.False(browser.HasLane);
        Assert.Empty(browser.Rows);
        Assert.Empty(browser.TypeChips);
    }
}

public class LaneViewModelTests
{
    [Fact]
    public void Lane_reports_its_extent_kinds_and_details()
    {
        LaneViewModel lane = new(TestFrames.SampleAircraft());

        Assert.True(lane.IsAircraft);
        Assert.Equal("YR-SCD", lane.Name);
        Assert.Equal(0.026, lane.FirstTime);
        Assert.Equal(0.3, lane.LastTime);
        Assert.Equal("8", lane.FrameCountText);
        Assert.Equal([FrameKind.Position, FrameKind.Integer, FrameKind.Float, FrameKind.String8, FrameKind.SimEvent], lane.Kinds);
        Assert.Contains(lane.Info, row => row is { Key: "Type role", Value: "1 (SingleProp)" });
    }

    [Fact]
    public void An_object_is_named_by_its_model_and_an_empty_lane_has_a_zero_extent()
    {
        LaneViewModel lane = new(new RecordedObject { Model = "Windsock" });

        Assert.False(lane.IsAircraft);
        Assert.Equal("Windsock", lane.Name);
        Assert.Equal(0, lane.FirstTime);
        Assert.Equal(0, lane.LastTime);
    }
}
