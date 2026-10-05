using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.UI.Views.Controls;

/// <summary>
/// A slippy map: tiles from an <see cref="IMapTileSource"/> with an aircraft marker for each of <see cref="Markers"/>. Drag to move,
/// wheel to zoom at the pointer, double-click to zoom in. Until the user moves it, the map keeps to the aircraft: it shows all of them
/// whenever one comes or goes. The names of the aircraft show when there are few, or for the one under the pointer.
/// </summary>
public sealed class MapView : Control
{
    private const double MarkerHitRadius = 13;
    private const int LabelsWhenAtMost = 15;
    private const double WheelZoomPerNotch = 0.5;

    public static readonly StyledProperty<IReadOnlyList<MapMarker>?> MarkersProperty =
        AvaloniaProperty.Register<MapView, IReadOnlyList<MapMarker>?>(nameof(Markers));

    public static readonly StyledProperty<IMapTileSource?> TileSourceProperty =
        AvaloniaProperty.Register<MapView, IMapTileSource?>(nameof(TileSource));

    // A plane seen from above, nose up, about 20 units across.
    private static readonly Geometry PlaneShape = Geometry.Parse(
        "M0,-10 L1.8,-5 L1.8,-2.5 L10,2.5 L10,4.5 L1.8,2.5 L1.8,7 L4.5,9 L4.5,10 L0,9 L-4.5,10 L-4.5,9 L-1.8,7 L-1.8,2.5 L-10,4.5 L-10,2.5 L-1.8,-2.5 L-1.8,-5 Z");

    private static readonly IBrush Ground = new SolidColorBrush(Color.Parse("#DDE3E8"));
    private static readonly IBrush MarkerFill = new SolidColorBrush(Color.Parse("#0091CE"));
    private static readonly IPen MarkerOutline = new Pen(Brushes.White, 1.6, lineJoin: PenLineJoin.Round);
    private static readonly IBrush LabelBackground = new SolidColorBrush(Color.Parse("#E6FFFFFF"));
    private static readonly IBrush LabelText = new SolidColorBrush(Color.Parse("#1F2933"));
    private static readonly Typeface LabelFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    private readonly MapViewport _viewport = new();
    private MapTileCache? _tiles;

    private bool _autoFit = true;
    private bool _needsFit = true;
    private string _fittedIds = "";

    private bool _dragging;
    private Point _dragFrom;
    private string? _hovered;

    static MapView()
    {
        AffectsRender<MapView>(MarkersProperty);
        ClipToBoundsProperty.OverrideDefaultValue<MapView>(true);
        FocusableProperty.OverrideDefaultValue<MapView>(true);
        CursorProperty.OverrideDefaultValue<MapView>(new Cursor(StandardCursorType.Hand));
    }

    public IReadOnlyList<MapMarker>? Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public IMapTileSource? TileSource
    {
        get => GetValue(TileSourceProperty);
        set => SetValue(TileSourceProperty, value);
    }

    /// <summary>Where the map looks now. Only to see what the user did to it.</summary>
    internal MapViewport Viewport => _viewport;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TileSourceProperty)
        {
            _tiles?.Dispose();
            _tiles = null;
            if (change.NewValue is IMapTileSource source)
            {
                _viewport.MaxZoom = source.MaxZoom + 1;
                _tiles = new MapTileCache(source, InvalidateVisual);
            }
            InvalidateVisual();
        }
        else if (change.Property == MarkersProperty)
        {
            // the map moves to show a new set of aircraft, never because the same ones flew
            string ids = string.Join('|', (Markers ?? []).Select(m => m.Id).Order(StringComparer.Ordinal));
            if (_autoFit && ids != _fittedIds)
                _needsFit = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _tiles?.Dispose();
        _tiles = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_tiles is null && TileSource is { } source)
            _tiles = new MapTileCache(source, InvalidateVisual);
    }

    // ---- what the user can do

    /// <summary>Shows all the aircraft again, and follows them again as they come and go.</summary>
    public void FitToMarkers()
    {
        _autoFit = true;
        _needsFit = true;
        InvalidateVisual();
    }

    public void ZoomIn() => ZoomBy(1);

    public void ZoomOut() => ZoomBy(-1);

    private void ZoomBy(double levels)
    {
        _autoFit = false;
        _viewport.ZoomAt(new Point(_viewport.Width / 2, _viewport.Height / 2), levels);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPoint point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
        {
            _autoFit = false;
            _viewport.ZoomAt(point.Position, 1);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        _dragging = true;
        _dragFrom = point.Position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point position = e.GetPosition(this);

        if (_dragging)
        {
            _autoFit = false;
            _viewport.PanBy(position.X - _dragFrom.X, position.Y - _dragFrom.Y);
            _dragFrom = position;
            InvalidateVisual();
            return;
        }

        string? hovered = MarkerAt(position)?.Id;
        if (hovered != _hovered)
        {
            _hovered = hovered;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hovered is not null)
        {
            _hovered = null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _autoFit = false;
        _viewport.ZoomAt(e.GetPosition(this), e.Delta.Y * WheelZoomPerNotch);
        InvalidateVisual();
        // the page behind the map does not scroll while the pointer is over it
        e.Handled = true;
    }

    private MapMarker? MarkerAt(Point position)
    {
        MapMarker? best = null;
        double bestDistance = MarkerHitRadius * MarkerHitRadius;
        foreach (MapMarker marker in Markers ?? [])
        {
            Point at = _viewport.ToScreen(marker.Latitude, marker.Longitude);
            double distance = (at.X - position.X) * (at.X - position.X) + (at.Y - position.Y) * (at.Y - position.Y);
            if (distance <= bestDistance)
            {
                best = marker;
                bestDistance = distance;
            }
        }
        return best;
    }

    // ---- drawing

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        _viewport.Resize(Bounds.Width, Bounds.Height);
        if (_needsFit)
        {
            IReadOnlyList<MapMarker> markers = Markers ?? [];
            _viewport.Fit(markers.Select(m => (m.Latitude, m.Longitude)).ToList());
            _fittedIds = string.Join('|', markers.Select(m => m.Id).Order(StringComparer.Ordinal));
            _needsFit = false;
        }

        context.FillRectangle(Ground, new Rect(Bounds.Size));
        DrawTiles(context);
        DrawMarkers(context);
    }

    private void DrawTiles(DrawingContext context)
    {
        if (_tiles is null || TileSource is null)
            return;

        // tiles come in whole zoom levels; between two of them the next lower level is stretched
        int level = Math.Clamp((int)Math.Floor(_viewport.Zoom), 0, TileSource.MaxZoom);
        double tileSize = WebMercator.TileSize * Math.Pow(2, _viewport.Zoom - level);
        int count = 1 << level;

        double originX = WebMercator.X(_viewport.CenterLongitude, _viewport.Zoom) - _viewport.Width / 2;
        double originY = WebMercator.Y(_viewport.CenterLatitude, _viewport.Zoom) - _viewport.Height / 2;

        int firstX = (int)Math.Floor(originX / tileSize), lastX = (int)Math.Floor((originX + _viewport.Width) / tileSize);
        int firstY = Math.Max(0, (int)Math.Floor(originY / tileSize)), lastY = Math.Min(count - 1, (int)Math.Floor((originY + _viewport.Height) / tileSize));

        for (int ty = firstY; ty <= lastY; ty++)
        {
            for (int tx = firstX; tx <= lastX; tx++)
            {
                // the world repeats sideways
                int wrappedX = ((tx % count) + count) % count;
                Bitmap? tile = _tiles.Get(level, wrappedX, ty);
                if (tile is null)
                    continue;

                // a hair more than the tile, so the seams between tiles do not show
                Rect target = new(tx * tileSize - originX, ty * tileSize - originY, tileSize + 0.6, tileSize + 0.6);
                context.DrawImage(tile, new Rect(tile.Size), target);
            }
        }

        _tiles.EndFrame();
    }

    private void DrawMarkers(DrawingContext context)
    {
        IReadOnlyList<MapMarker> markers = Markers ?? [];
        bool labelAll = markers.Count <= LabelsWhenAtMost;
        List<(MapMarker Marker, Point At)> labelled = [];

        foreach (MapMarker marker in markers)
        {
            Point at = _viewport.ToScreen(marker.Latitude, marker.Longitude);
            if (at.X < -20 || at.Y < -20 || at.X > _viewport.Width + 20 || at.Y > _viewport.Height + 20)
                continue;

            Matrix place = Matrix.CreateRotation(marker.Heading * Math.PI / 180.0) * Matrix.CreateTranslation(at.X, at.Y);
            using (context.PushTransform(place))
            {
                context.DrawGeometry(MarkerFill, MarkerOutline, PlaneShape);
            }

            if (labelAll || marker.Id == _hovered)
                labelled.Add((marker, at));
        }

        // the hovered one last, on top
        foreach (var (marker, at) in labelled.OrderBy(l => l.Marker.Id == _hovered))
            DrawLabel(context, marker.Callsign, at);
    }

    private void DrawLabel(DrawingContext context, string text, Point at)
    {
        if (text.Length == 0)
            return;

        FormattedText label = new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFace, 11, LabelText);
        Rect box = new(at.X + 11, at.Y - label.Height / 2 - 2, label.Width + 8, label.Height + 4);
        context.DrawRectangle(LabelBackground, null, box, 3, 3);
        context.DrawText(label, new Point(box.X + 4, box.Y + 2));
    }
}
