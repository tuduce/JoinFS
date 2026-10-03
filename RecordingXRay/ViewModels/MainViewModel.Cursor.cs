using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RecordingXRay.Services;

namespace RecordingXRay.ViewModels;

// The time cursor and the selected aircraft, and how they drive the frame browser, inspector and timeline.
// Rules (docs/README.md section 6):
//  - selecting an aircraft keeps the cursor if that aircraft has data there, else moves it to the aircraft's
//    first frame (cursor before the lane) or last frame (cursor after it);
//  - moving the cursor never changes the selected aircraft;
//  - the frame shown follows the cursor (the last position frame at or before it), unless the user picked a frame,
//    in which case the cursor moves to that frame's time.
public partial class MainViewModel
{
    private LaneViewModel? selectedLane;
    private double cursor;
    private bool followingCursor;
    private int shownFrameIndex = -1;

    /// <summary>The aircraft or object shown in the frame browser and inspector.</summary>
    public LaneViewModel? SelectedLane
    {
        get => selectedLane;
        set => SelectAircraft(value);
    }

    /// <summary>The time cursor, in seconds since the recording started.</summary>
    public double Cursor => cursor;

    /// <summary>Length of the recording in seconds (the latest frame time).</summary>
    public double Duration => Summary.DurationSeconds;

    public string CursorText => TimeFormat.Clock(cursor, padMinutes: true);

    public string DurationText => HasRecording ? "/ " + TimeFormat.Clock(Duration) : string.Empty;

    /// <summary>"frame [3] of 23,912" for the frame being shown.</summary>
    public string FrameCounterText => selectedLane is null || shownFrameIndex < 0
        ? string.Empty
        : $"frame [{shownFrameIndex}] of {selectedLane.FrameCountText}";

    /// <summary>Status bar, right side: "TIGER-6 › [3] IntegerVariables @ 0.154 s".</summary>
    public string FrameStatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    private TimelineViewModel timeline = TimelineViewModel.Empty;

    /// <summary>Selects an aircraft or object (map marker, timeline track, picker, or Up / Down), moving the cursor as needed.</summary>
    public void SelectAircraft(LaneViewModel? lane)
    {
        if (lane is null)
        {
            ApplySelection(null);
            return;
        }

        SetCursor(lane.ClampToExtent(cursor), syncFrame: false);
        ApplySelection(lane);
    }

    /// <summary>Moves the time cursor (clamped to the recording) and shows the frame at that time.</summary>
    public void MoveCursor(double seconds) => SetCursor(seconds, syncFrame: true);

    [RelayCommand]
    private void StepPrevious() => Step(-1);

    [RelayCommand]
    private void StepNext() => Step(1);

    [RelayCommand]
    private void GoToFirstFrame()
    {
        if (Browser.Rows.Count > 0)
        {
            Browser.SelectRow(Browser.Rows[0]);
        }
    }

    [RelayCommand]
    private void GoToLastFrame()
    {
        if (Browser.Rows.Count > 0)
        {
            Browser.SelectRow(Browser.Rows[Browser.Rows.Count - 1]);
        }
    }

    /// <summary>Selects the next or previous aircraft in the list (Down / Up), applying the usual cursor rule.</summary>
    public void SelectNeighbour(int direction)
    {
        if (Lanes.Count == 0)
        {
            return;
        }

        int index = selectedLane is null ? -1 : Lanes.ToList().IndexOf(selectedLane);
        int next = Math.Clamp(index + direction, 0, Lanes.Count - 1);
        SelectAircraft(Lanes[next]);
    }

    private void ApplySelection(LaneViewModel? lane)
    {
        bool changed = !ReferenceEquals(selectedLane, lane);
        selectedLane = lane;

        if (lane is null)
        {
            shownFrameIndex = -1;
            Inspector.Clear();
            Browser.SetLane(null);
        }
        else
        {
            if (changed)
            {
                shownFrameIndex = -1;
            }

            followingCursor = true;
            if (changed)
            {
                Browser.SetLane(lane, lane.FrameIndexAt(cursor));
            }
            else
            {
                SyncFrameToCursor(); // re-selecting the same aircraft re-shows the frame at the (possibly moved) cursor
            }

            followingCursor = false;
        }

        UpdateTimeline();
        OnPropertyChanged(nameof(FrameCounterText));
        OnPropertyChanged(nameof(SelectedLane));
    }

    private void SetCursor(double seconds, bool syncFrame)
    {
        double value = Math.Clamp(double.IsFinite(seconds) ? seconds : 0, 0, Math.Max(Duration, 0));
        if (value != cursor)
        {
            cursor = value;
            OnPropertyChanged(nameof(Cursor));
            OnPropertyChanged(nameof(CursorText));
        }

        if (syncFrame)
        {
            followingCursor = true;
            SyncFrameToCursor();
            followingCursor = false;
        }

        UpdateTimeline();
        Timeline.EnsureVisible(cursor);
    }

    private void SyncFrameToCursor()
    {
        if (selectedLane is not null)
        {
            int index = selectedLane.FrameIndexAt(cursor);
            if (index >= 0)
            {
                Browser.SelectFrame(index, widenFilter: false);
            }
        }
    }

    private void OnFrameSelected(LaneViewModel lane, FrameRow row)
    {
        Inspector.Show(lane, row);
        shownFrameIndex = row.Index;
        OnPropertyChanged(nameof(FrameCounterText));
        FrameStatusText = $"{lane.Name} › {row.IndexText} {row.TypeName} @ {row.TimeText}";
        OnPropertyChanged(nameof(FrameStatusText));

        if (!followingCursor)
        {
            // The user picked this frame: the cursor goes to its time and the frame stays shown.
            SetCursor(row.Frame.Time, syncFrame: false);
        }
    }

    private void Step(int direction)
    {
        FrameRowList rows = Browser.Rows;
        if (rows.Count == 0)
        {
            return;
        }

        int target;
        int position = rows.PositionOf(shownFrameIndex);
        if (position >= 0)
        {
            target = position + direction;
        }
        else
        {
            // The frame being shown is hidden by the filter: step to the nearest visible frame after / before the cursor.
            int at = rows.PositionAtOrAfter(Cursor);
            double time = rows[at].Frame.Time;
            target = direction > 0
                ? (time == Cursor ? at + 1 : at)
                : (time < Cursor ? at : at - 1);
        }

        Browser.SelectRow(rows[Math.Clamp(target, 0, rows.Count - 1)]);
    }

    private void UpdateTimeline() => Timeline.Update(selectedLane, cursor);

    private void ResetForRecording(IReadOnlyList<LaneViewModel> laneList, double durationSeconds)
    {
        ApplySelection(null);
        Lanes = laneList;
        cursor = 0;
        Timeline = new TimelineViewModel(laneList, durationSeconds, SelectAircraft);
        OnPropertyChanged(nameof(Cursor));
        OnPropertyChanged(nameof(CursorText));
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(DurationText));
        SelectAircraft(laneList.FirstOrDefault());
    }
}
