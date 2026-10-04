using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using RecordingXRay.Services;
using RecordingXRay.ViewModels;

namespace RecordingXRay.Views.Controls;

/// <summary>
/// Draws the map (graticule, trails, aircraft) from a <see cref="MapViewModel"/> and turns pointer and key input into
/// view changes: drag to pan, wheel to zoom, click an aircraft to select it. Arrow keys pan, + / - zoom, Home fits all.
/// </summary>
public sealed class MapControl : Control
{
    public static readonly StyledProperty<MapViewModel?> MapProperty = AvaloniaProperty.Register<MapControl, MapViewModel?>(nameof(Map));

    private const double HitRadius = 16;
    private const double DragThreshold = 4;

    // Standard OpenStreetMap tiles are light; dimmed they sit quietly under the trails on the dark theme.
    private const double TileOpacity = 0.5;

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);

    private readonly Dictionary<(string Key, double Alpha), IBrush> brushCache = [];
    private Point pressedAt;
    private Point lastPointer;
    private bool pressed;
    private bool dragging;
    private LaneViewModel? pressedLane;

    static MapControl()
    {
        FocusableProperty.OverrideDefaultValue<MapControl>(true);
        ClipToBoundsProperty.OverrideDefaultValue<MapControl>(true);
    }

    public MapViewModel? Map { get => GetValue(MapProperty); set => SetValue(MapProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MapProperty)
        {
            if (change.OldValue is MapViewModel old)
            {
                old.Invalidated -= InvalidateVisual;
            }

            if (change.NewValue is MapViewModel map)
            {
                map.Invalidated += InvalidateVisual;
                map.SetViewport(Bounds.Width, Bounds.Height);
            }

            InvalidateVisual();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Map?.SetViewport(e.NewSize.Width, e.NewSize.Height);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        brushCache.Clear();
    }

    // ---- Drawing ----

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(Brush("MapBackgroundBrush"), null, new Rect(Bounds.Size));
        if (Map is not { HasTracks: true } map || Bounds.Width <= 0)
        {
            return;
        }

        DrawTiles(context, map);
        DrawGraticule(context, map);
        if (map.ShowTrails)
        {
            DrawTrails(context, map);
        }

        DrawMarkers(context, map);
    }

    // Basemap tiles. A tile that has not arrived yet is shown from a coarser tile already in memory, if there is one.
    private void DrawTiles(DrawingContext context, MapViewModel map)
    {
        if (!map.ShowBasemap || map.Tiles is not { } tiles)
        {
            return;
        }

        IReadOnlyList<TilePlacement> placements = TileMath.Visible(map.CenterX, map.CenterY, map.Scale, Bounds.Width, Bounds.Height);
        tiles.SetWanted(placements.Select(p => p.Key).ToArray());

        using (context.PushOpacity(TileOpacity))
        {
            foreach (TilePlacement placement in placements)
            {
                // Whole pixels, so neighbouring tiles meet without hairline gaps.
                double left = Math.Round(placement.X);
                double top = Math.Round(placement.Y);
                Rect destination = new(left, top, Math.Round(placement.X + placement.Size) - left, Math.Round(placement.Y + placement.Size) - top);

                if (tiles.TryGet(placement.Key) is { } bitmap)
                {
                    context.DrawImage(bitmap, new Rect(bitmap.Size), destination);
                    continue;
                }

                tiles.Request(placement.Key);
                TileKey ancestor = placement.Key;
                for (int level = 1; level <= 4 && ancestor.Zoom > 0; level++)
                {
                    ancestor = ancestor.Parent;
                    if (tiles.TryGet(ancestor) is { } coarse && TileMath.SourceRect(placement.Key, ancestor, coarse.Size) is { } source)
                    {
                        context.DrawImage(coarse, source, destination);
                        break;
                    }
                }
            }
        }
    }

    private void DrawGraticule(DrawingContext context, MapViewModel map)
    {
        (IReadOnlyList<GraticuleLine> longitudes, IReadOnlyList<GraticuleLine> latitudes) = map.Graticule();
        // Fainter over a basemap, where the lines would otherwise be heavy.
        bool overBasemap = map.ShowBasemap && map.Tiles is not null;
        Pen pen = new(overBasemap ? Alpha("MapGridBrush", 0.5) : Brush("MapGridBrush"), 1);
        IBrush labelBrush = Brush("MapLabelBrush");
        Typeface mono = new(Font("MonoFont"));

        foreach (GraticuleLine line in longitudes)
        {
            double x = Math.Round(line.Pixel) + 0.5;
            context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
            context.DrawText(Text(line.Label, mono, 10, labelBrush), new Point(x + 4, 3));
        }

        foreach (GraticuleLine line in latitudes)
        {
            double y = Math.Round(line.Pixel) + 0.5;
            context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
            context.DrawText(Text(line.Label, mono, 10, labelBrush), new Point(6, y - 14));
        }
    }

    private void DrawTrails(DrawingContext context, MapViewModel map)
    {
        // Selected aircraft last, so its trail is on top.
        IEnumerable<MapTrack> ordered = map.Tracks
            .Where(track => !ReferenceEquals(track.Lane, map.SelectedLane))
            .Concat(map.Tracks.Where(track => ReferenceEquals(track.Lane, map.SelectedLane)));

        foreach (MapTrack track in ordered)
        {
            bool selected = ReferenceEquals(track.Lane, map.SelectedLane);
            (List<(double X, double Y)> past, List<(double X, double Y)> future) = track.Split(map.Time);

            Pen futurePen = selected
                ? new Pen(Alpha("AccentBrush", 0.45), 2.2, new ImmutableDashStyle([6 / 2.2, 5 / 2.2], 0), PenLineCap.Round, PenLineJoin.Round)
                : new Pen(Alpha("MapFutureBrush", 0.32), 1.6, new ImmutableDashStyle([4 / 1.6, 5 / 1.6], 0), PenLineCap.Round, PenLineJoin.Round);
            Pen pastPen = selected
                ? new Pen(Brush("AccentBrush"), 3.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
                : new Pen(Alpha("TextBrush", 0.85), 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

            // Future first so the past line covers the join.
            Draw(context, map, future, futurePen);
            Draw(context, map, past, pastPen);
        }
    }

    // Draws a path, skipping points less than a pixel from the previous one.
    private static void Draw(DrawingContext context, MapViewModel map, List<(double X, double Y)> points, Pen pen)
    {
        if (points.Count < 2)
        {
            return;
        }

        StreamGeometry geometry = new();
        using (StreamGeometryContext g = geometry.Open())
        {
            Point last = new(map.ToScreenX(points[0].X), map.ToScreenY(points[0].Y));
            g.BeginFigure(last, false);
            for (int i = 1; i < points.Count; i++)
            {
                Point point = new(map.ToScreenX(points[i].X), map.ToScreenY(points[i].Y));
                if (i == points.Count - 1 || Math.Abs(point.X - last.X) >= 1 || Math.Abs(point.Y - last.Y) >= 1)
                {
                    g.LineTo(point);
                    last = point;
                }
            }

            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawMarkers(DrawingContext context, MapViewModel map)
    {
        IBrush accent = Brush("AccentBrush");
        IBrush background = Brush("MapBackgroundBrush");
        Typeface ui = new(Font("UiFont"), FontStyle.Normal, FontWeight.SemiBold);
        Typeface uiBold = new(Font("UiFont"), FontStyle.Normal, FontWeight.Bold);

        // Labels are placed selected first, then the others, each avoiding those already placed.
        List<Rect> placed = [];
        Dictionary<MapMarker, (Rect Box, FormattedText Text)> labels = [];
        foreach (MapMarker marker in map.Markers.Reverse())
        {
            if (!marker.IsSelected && !map.ShowLabels)
            {
                continue;
            }

            Point p = ScreenOf(map, marker);
            FormattedText text = marker.IsSelected
                ? Text(marker.Lane.Name, uiBold, 11.5, Brush("AccentTextBrush"))
                : Text(marker.Lane.Name, ui, 11, Brush("Text2Brush"));
            double width = text.Width + (marker.IsSelected ? 22 : 0);
            double height = marker.IsSelected ? 20 : text.Height;
            double radius = marker.IsSelected ? 7 : 5;

            Rect[] candidates =
            [
                new(p.X - width / 2, p.Y + radius + 4, width, height),
                new(p.X - width / 2, p.Y - radius - 4 - height, width, height),
                new(p.X + radius + 5, p.Y - height / 2, width, height),
                new(p.X - radius - 5 - width, p.Y - height / 2, width, height),
            ];
            Rect box = candidates.FirstOrDefault(c => !placed.Any(other => other.Inflate(2).Intersects(c)), candidates[0]);
            placed.Add(box);
            labels[marker] = (box, text);
        }

        foreach (MapMarker marker in map.Markers)
        {
            Point p = ScreenOf(map, marker);
            if (p.X < -40 || p.Y < -40 || p.X > Bounds.Width + 40 || p.Y > Bounds.Height + 40)
            {
                continue;
            }

            using (context.PushOpacity(marker.HasData ? 1.0 : 0.5))
            {
                if (marker.IsSelected)
                {
                    context.DrawEllipse(Alpha("AccentBrush", 0.2), null, p, 15, 15);
                    DrawArrow(context, accent, p, marker.Sample.Heading);
                }

                double radius = marker.IsSelected ? 7 : 5;
                context.DrawEllipse(marker.IsSelected ? accent : Brush("Text2Brush"), new Pen(background, 2), p, radius, radius);

                if (labels.TryGetValue(marker, out var label))
                {
                    if (marker.IsSelected)
                    {
                        context.DrawRectangle(accent, null, label.Box, 10, 10);
                        context.DrawText(label.Text, new Point(label.Box.X + 11, label.Box.Y + (label.Box.Height - label.Text.Height) / 2));
                    }
                    else
                    {
                        context.DrawText(label.Text, label.Box.TopLeft);
                    }
                }
            }
        }
    }

    // A small triangle pointing along the heading (radians, clockwise from north; the map is north-up).
    private static void DrawArrow(DrawingContext context, IBrush brush, Point center, double heading)
    {
        Matrix turn = Matrix.CreateRotation(heading) * Matrix.CreateTranslation(center.X, center.Y);
        using (context.PushTransform(turn))
        {
            StreamGeometry arrow = new();
            using (StreamGeometryContext g = arrow.Open())
            {
                g.BeginFigure(new Point(0, -17), true);
                g.LineTo(new Point(5, -8));
                g.LineTo(new Point(-5, -8));
                g.EndFigure(true);
            }

            context.DrawGeometry(brush, null, arrow);
        }
    }

    private static Point ScreenOf(MapViewModel map, MapMarker marker) =>
        new(map.ToScreenX(marker.Sample.X), map.ToScreenY(marker.Sample.Y));

    // ---- Input ----

    // The aircraft under a point (nearest within the hit radius; the selected one wins ties because it is drawn on top).
    private LaneViewModel? LaneAt(Point point)
    {
        if (Map is not { } map)
        {
            return null;
        }

        LaneViewModel? best = null;
        double bestDistance = HitRadius;
        foreach (MapMarker marker in map.Markers)
        {
            Point p = ScreenOf(map, marker);
            double distance = Math.Sqrt((p.X - point.X) * (p.X - point.X) + (p.Y - point.Y) * (p.Y - point.Y));
            if (distance <= bestDistance)
            {
                best = marker.Lane;
                bestDistance = distance;
            }
        }

        return best;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        pressed = true;
        dragging = false;
        pressedAt = lastPointer = e.GetPosition(this);
        pressedLane = LaneAt(pressedAt);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point point = e.GetPosition(this);

        if (!pressed)
        {
            Cursor = LaneAt(point) is null ? ArrowCursor : HandCursor;
            return;
        }

        if (!dragging && (Math.Abs(point.X - pressedAt.X) >= DragThreshold || Math.Abs(point.Y - pressedAt.Y) >= DragThreshold))
        {
            dragging = true; // from here on it is a pan, never a click
            lastPointer = pressedAt;
        }

        if (dragging)
        {
            Map?.PanBy(point.X - lastPointer.X, point.Y - lastPointer.Y);
            lastPointer = point;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!pressed)
        {
            return;
        }

        // Read these first: giving up the capture raises PointerCaptureLost, which resets the drag state.
        bool wasDragging = dragging;
        LaneViewModel? lane = pressedLane;
        pressed = false;
        pressedLane = null;
        e.Pointer.Capture(null);

        // A press and release without a drag, on the same aircraft, is a click.
        if (!wasDragging && lane is not null && LaneAt(e.GetPosition(this)) is { } under && ReferenceEquals(under, lane))
        {
            Map?.Pick(lane);
        }

        dragging = false;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        pressed = false;
        dragging = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Map is { } map && e.Delta.Y != 0)
        {
            Point point = e.GetPosition(this);
            map.ZoomWheel(e.Delta.Y, point.X, point.Y);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Map is not { } map)
        {
            return;
        }

        const double Step = 60;
        switch (e.Key)
        {
            case Key.Add or Key.OemPlus:
                map.ZoomInCommand.Execute(null);
                break;
            case Key.Subtract or Key.OemMinus:
                map.ZoomOutCommand.Execute(null);
                break;
            case Key.Left:
                map.PanBy(Step, 0);
                break;
            case Key.Right:
                map.PanBy(-Step, 0);
                break;
            case Key.Up:
                map.PanBy(0, Step);
                break;
            case Key.Down:
                map.PanBy(0, -Step);
                break;
            case Key.Home:
                map.FitAll();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // ---- Resources (colours and fonts come from the theme tokens) ----

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush ? brush : Brushes.Gray;

    private FontFamily Font(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is FontFamily family ? family : FontFamily.Default;

    private IBrush Alpha(string key, double alpha)
    {
        if (!brushCache.TryGetValue((key, alpha), out IBrush? brush))
        {
            brush = Brush(key) is ISolidColorBrush solid ? new ImmutableSolidColorBrush(solid.Color, alpha) : Brushes.Gray;
            brushCache[(key, alpha)] = brush;
        }

        return brush;
    }

    private static FormattedText Text(string text, Typeface typeface, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush);
}
