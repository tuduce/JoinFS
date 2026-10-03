using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RecordingXRay.ViewModels;

/// <summary>One aircraft or object in the timeline: header row plus lane.</summary>
public sealed partial class TrackViewModel : ObservableObject
{
    public TrackViewModel(LaneViewModel lane, Action<LaneViewModel>? pick = null)
    {
        Lane = lane;
        PickCommand = new RelayCommand(() => pick?.Invoke(lane));
    }

    /// <summary>Clicking the track header selects this aircraft.</summary>
    public IRelayCommand PickCommand { get; }

    public LaneViewModel Lane { get; }

    public string Name => Lane.Name;

    public string CountText => Lane.FrameCountText;

    [ObservableProperty]
    private bool isSelected;

    /// <summary>True when the time cursor is inside this lane's first-to-last-frame extent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DataTip))]
    private bool hasData;

    public string DataTip => HasData ? "Has data at the cursor" : "No data at the cursor";
}

/// <summary>A kind shown in the timeline legend.</summary>
public sealed record LegendItem(FrameKind Kind, string Label);

/// <summary>The timeline's tracks and the visible window (which part of the recording is on screen).</summary>
public sealed partial class TimelineViewModel : ObservableObject
{
    /// <summary>Zoom never goes closer than this many seconds across the whole timeline width.</summary>
    public const double MinSpan = 0.5;

    private bool updatingZoom;

    public TimelineViewModel(IReadOnlyList<LaneViewModel> lanes, double duration, Action<LaneViewModel>? pick = null)
    {
        Tracks = lanes.Select(lane => new TrackViewModel(lane, pick)).ToArray();
        Duration = Math.Max(duration, MinSpan);
        ViewSpan = Duration;
        Legend = FrameKinds.All
            .Where(kind => lanes.Any(lane => lane.Kinds.Contains(kind)))
            .Select(kind => new LegendItem(kind, FrameKinds.Label(kind)))
            .ToArray();
    }

    public static TimelineViewModel Empty { get; } = new([], 0);

    public IReadOnlyList<TrackViewModel> Tracks { get; }

    public IReadOnlyList<LegendItem> Legend { get; }

    /// <summary>Length of the recording in seconds (at least <see cref="MinSpan"/>).</summary>
    public double Duration { get; }

    /// <summary>Time at the left edge of the timeline.</summary>
    [ObservableProperty]
    private double viewStart;

    /// <summary>Seconds across the whole timeline width.</summary>
    [ObservableProperty]
    private double viewSpan;

    /// <summary>The zoom slider, 0 (whole recording) to 100 (closest). Logarithmic in the span.</summary>
    [ObservableProperty]
    private double zoom;

    public double ViewEnd => ViewStart + ViewSpan;

    private double MinimumSpan => Math.Min(MinSpan, Duration);

    partial void OnZoomChanged(double value)
    {
        if (!updatingZoom)
        {
            // The slider zooms around the middle of the current view.
            SetSpan(SpanForZoom(value), ViewStart + ViewSpan / 2, 0.5);
        }
    }

    /// <summary>Marks which track is selected and which have data at the cursor.</summary>
    public void Update(LaneViewModel? selected, double cursor)
    {
        foreach (TrackViewModel track in Tracks)
        {
            track.IsSelected = ReferenceEquals(track.Lane, selected);
            track.HasData = track.Lane.HasDataAt(cursor);
        }
    }

    /// <summary>Zooms by <paramref name="factor"/> (above 1 zooms in), keeping <paramref name="anchorTime"/> where it is on screen.</summary>
    public void ZoomBy(double factor, double anchorTime)
    {
        double fraction = ViewSpan > 0 ? (anchorTime - ViewStart) / ViewSpan : 0.5;
        SetSpan(ViewSpan / factor, anchorTime, Math.Clamp(fraction, 0, 1));
    }

    /// <summary>Shows the whole recording.</summary>
    public void ShowAll() => SetSpan(Duration, Duration / 2, 0.5);

    /// <summary>Scrolls the view by <paramref name="seconds"/>.</summary>
    public void Pan(double seconds) => ViewStart = ClampStart(ViewStart + seconds, ViewSpan);

    /// <summary>Scrolls the view, if needed, so <paramref name="time"/> is visible, centring it when it was off screen.</summary>
    public void EnsureVisible(double time)
    {
        if (time < ViewStart || time > ViewEnd)
        {
            ViewStart = ClampStart(time - ViewSpan / 2, ViewSpan);
        }
    }

    // Sets the span so that "anchor" ends up at "fraction" of the width, and keeps the start in range.
    private void SetSpan(double span, double anchor, double fraction)
    {
        span = Math.Clamp(span, MinimumSpan, Duration);
        ViewSpan = span;
        ViewStart = ClampStart(anchor - fraction * span, span);

        updatingZoom = true;
        Zoom = ZoomForSpan(span);
        updatingZoom = false;
    }

    private double ClampStart(double start, double span) => Math.Clamp(start, 0, Math.Max(0, Duration - span));

    private double SpanForZoom(double zoomValue) =>
        Duration * Math.Pow(MinimumSpan / Duration, Math.Clamp(zoomValue, 0, 100) / 100.0);

    private double ZoomForSpan(double span) =>
        MinimumSpan >= Duration ? 0 : Math.Clamp(100.0 * Math.Log(Duration / span) / Math.Log(Duration / MinimumSpan), 0, 100);
}
