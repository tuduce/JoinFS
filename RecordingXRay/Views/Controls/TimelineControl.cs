using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using RecordingXRay.ViewModels;

namespace RecordingXRay.Views.Controls;

/// <summary>
/// Draws the ruler, one lane per track and the playhead. A lane is drawn between its first and last frame; inside it a
/// column is lit wherever frames of that kind fall, counted straight from the frame times (no bins, so any zoom is exact).
/// Pointer input is handled by the hosting <c>TimelineView</c>.
/// </summary>
public sealed class TimelineControl : Control
{
    public const double RulerHeight = 26;
    public const double RowHeight = 34;

    public static readonly StyledProperty<IReadOnlyList<TrackViewModel>?> TracksProperty =
        AvaloniaProperty.Register<TimelineControl, IReadOnlyList<TrackViewModel>?>(nameof(Tracks));

    public static readonly StyledProperty<LaneViewModel?> SelectedLaneProperty =
        AvaloniaProperty.Register<TimelineControl, LaneViewModel?>(nameof(SelectedLane));

    public static readonly StyledProperty<double> CursorTimeProperty = AvaloniaProperty.Register<TimelineControl, double>(nameof(CursorTime));
    public static readonly StyledProperty<double> ViewStartProperty = AvaloniaProperty.Register<TimelineControl, double>(nameof(ViewStart));
    public static readonly StyledProperty<double> ViewSpanProperty = AvaloniaProperty.Register<TimelineControl, double>(nameof(ViewSpan), 1);

    private static readonly double[] TickSteps =
    [
        0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600,
    ];

    private static readonly double[] DensityAlpha = [0.5, 0.75, 1.0];

    private readonly Dictionary<(string Key, int Level), IBrush> brushCache = [];

    static TimelineControl()
    {
        AffectsRender<TimelineControl>(SelectedLaneProperty, CursorTimeProperty, ViewStartProperty, ViewSpanProperty);
        AffectsMeasure<TimelineControl>(TracksProperty);
        AffectsRender<TimelineControl>(TracksProperty);
    }

    public IReadOnlyList<TrackViewModel>? Tracks { get => GetValue(TracksProperty); set => SetValue(TracksProperty, value); }

    public LaneViewModel? SelectedLane { get => GetValue(SelectedLaneProperty); set => SetValue(SelectedLaneProperty, value); }

    /// <summary>The time cursor, in seconds.</summary>
    public double CursorTime { get => GetValue(CursorTimeProperty); set => SetValue(CursorTimeProperty, value); }

    /// <summary>Time at the left edge, in seconds.</summary>
    public double ViewStart { get => GetValue(ViewStartProperty); set => SetValue(ViewStartProperty, value); }

    /// <summary>Seconds across the full width.</summary>
    public double ViewSpan { get => GetValue(ViewSpanProperty); set => SetValue(ViewSpanProperty, value); }

    /// <summary>The time at a horizontal position inside this control.</summary>
    public double TimeAt(double x) => Bounds.Width <= 0 ? ViewStart : ViewStart + x / Bounds.Width * ViewSpan;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsFinite(availableSize.Width) ? availableSize.Width : 0, RulerHeight + RowHeight * (Tracks?.Count ?? 0));

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        brushCache.Clear();
    }

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || ViewSpan <= 0)
        {
            return;
        }

        IBrush divider = Brush("DividerBrush");
        Pen dividerPen = new(divider, 1);

        DrawRuler(context, width, dividerPen);

        IReadOnlyList<TrackViewModel> tracks = Tracks ?? [];
        for (int i = 0; i < tracks.Count; i++)
        {
            TrackViewModel track = tracks[i];
            double top = RulerHeight + i * RowHeight;
            bool selected = ReferenceEquals(track.Lane, SelectedLane);

            if (selected)
            {
                context.DrawRectangle(Brush("SelectedLaneBrush"), null, new Rect(0, top, width, RowHeight));
            }

            DrawLane(context, track.Lane, top, width, selected);
            context.DrawLine(dividerPen, new Point(0, top + RowHeight - 0.5), new Point(width, top + RowHeight - 0.5));
        }

        DrawPlayhead(context, height);
    }

    private void DrawRuler(DrawingContext context, double width, Pen dividerPen)
    {
        context.DrawLine(dividerPen, new Point(0, RulerHeight - 0.5), new Point(width, RulerHeight - 0.5));

        double step = NiceStep(ViewSpan / Math.Max(width / 90.0, 1));
        Pen tick = new(Brush("StrongBorderBrush"), 1);
        IBrush label = Brush("MutedBrush");
        Typeface typeface = new(Font("MonoFont"));

        double firstTick = Math.Ceiling(ViewStart / step) * step;
        for (double t = firstTick; t <= ViewStart + ViewSpan + step * 0.001; t += step)
        {
            double x = Math.Round((t - ViewStart) / ViewSpan * width) + 0.5;
            context.DrawLine(tick, new Point(x, RulerHeight - 9), new Point(x, RulerHeight - 1));
            FormattedText text = new(TickLabel(t, step), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10.5, label);
            context.DrawText(text, new Point(x + 4, RulerHeight - 6 - text.Height));
        }
    }

    private void DrawLane(DrawingContext context, LaneViewModel lane, double top, double width, bool selected)
    {
        if (lane.Frames.Count == 0)
        {
            return;
        }

        double x0 = Math.Max(0, XOf(lane.FirstTime, width));
        double x1 = Math.Min(width, XOf(lane.LastTime, width));
        if (x1 < 0 || x0 > width)
        {
            return;
        }

        x1 = Math.Max(x1, x0 + 1);
        using (context.PushOpacity(selected ? 1.0 : 0.5))
        {
            // Position frames in the upper bar.
            context.DrawRectangle(Brush("PositionLaneBrush"), null, new Rect(x0, top + 7, x1 - x0, 10), 2, 2);
            DrawColumns(context, lane, FrameKind.Position, "TypePositionBrush", top + 7, 10, x0, x1, width);

            // Variable frames in three thin slices below it.
            DrawColumns(context, lane, FrameKind.Integer, "TypeIntegerBrush", top + 20, 2, x0, x1, width);
            DrawColumns(context, lane, FrameKind.Float, "TypeFloatBrush", top + 22, 2, x0, x1, width);
            DrawColumns(context, lane, FrameKind.String8, "TypeString8Brush", top + 24, 2, x0, x1, width);
        }
    }

    // Lights one pixel column for every pixel that has at least one frame of the kind; busier columns are brighter.
    private void DrawColumns(DrawingContext context, LaneViewModel lane, FrameKind kind, string brushKey, double y, double barHeight, double x0, double x1, double width)
    {
        if (!lane.TimesByKind.TryGetValue(kind, out double[]? times) || times.Length == 0)
        {
            return;
        }

        double secondsPerPixel = ViewSpan / width;
        int firstPixel = (int)Math.Floor(x0);
        int lastPixel = (int)Math.Ceiling(x1);
        int index = LowerBound(times, ViewStart + firstPixel * secondsPerPixel);

        for (int pixel = firstPixel; pixel < lastPixel && index < times.Length; pixel++)
        {
            double end = ViewStart + (pixel + 1) * secondsPerPixel;
            int start = index;
            while (index < times.Length && times[index] < end)
            {
                index++;
            }

            int count = index - start;
            if (count > 0)
            {
                int level = count >= 4 ? 2 : (count >= 2 ? 1 : 0);
                context.DrawRectangle(LevelBrush(brushKey, level), null, new Rect(pixel, y, 1, barHeight));
            }
        }
    }

    private void DrawPlayhead(DrawingContext context, double height)
    {
        double x = XOf(CursorTime, Bounds.Width);
        if (x < -7 || x > Bounds.Width + 7)
        {
            return;
        }

        IBrush accent = Brush("AccentBrush");
        context.DrawRectangle(accent, null, new Rect(x - 1, 0, 2, height));

        StreamGeometry flag = new();
        using (StreamGeometryContext g = flag.Open())
        {
            g.BeginFigure(new Point(x - 7, 0), true);
            g.LineTo(new Point(x + 7, 0));
            g.LineTo(new Point(x + 7, 6));
            g.LineTo(new Point(x, 12));
            g.LineTo(new Point(x - 7, 6));
            g.EndFigure(true);
        }

        context.DrawGeometry(accent, null, flag);
    }

    private double XOf(double time, double width) => (time - ViewStart) / ViewSpan * width;

    private static int LowerBound(double[] times, double value)
    {
        int low = 0;
        int high = times.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (times[mid] < value)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    /// <summary>The smallest "round" tick spacing that is at least <paramref name="target"/> seconds.</summary>
    public static double NiceStep(double target)
    {
        foreach (double step in TickSteps)
        {
            if (step >= target)
            {
                return step;
            }
        }

        return TickSteps[^1];
    }

    /// <summary>Ruler label: <c>m:ss</c> for whole-second ticks, with decimals when ticks are finer.</summary>
    public static string TickLabel(double seconds, double step)
    {
        int minutes = (int)Math.Floor(seconds / 60.0);
        double rest = seconds - minutes * 60.0;
        string text = step >= 1
            ? ((int)Math.Round(rest)).ToString("00", CultureInfo.InvariantCulture)
            : rest.ToString(step >= 0.1 ? "00.0" : (step >= 0.01 ? "00.00" : "00.000"), CultureInfo.InvariantCulture);
        return minutes.ToString(CultureInfo.InvariantCulture) + ":" + text;
    }

    // Theme tokens are looked up by key so the control has no colours of its own.
    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush ? brush : Brushes.Gray;

    private FontFamily Font(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is FontFamily family ? family : FontFamily.Default;

    private IBrush LevelBrush(string key, int level)
    {
        if (!brushCache.TryGetValue((key, level), out IBrush? brush))
        {
            brush = Brush(key) is ISolidColorBrush solid
                ? new ImmutableSolidColorBrush(solid.Color, DensityAlpha[level])
                : Brushes.Gray;
            brushCache[(key, level)] = brush;
        }

        return brush;
    }
}
