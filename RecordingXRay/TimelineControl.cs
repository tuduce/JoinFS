namespace RecordingXRay;

/// <summary>
/// A custom WinForms control that renders a horizontal timeline with:
/// - colour-coded frame ticks (variable frames vs. position/other frames),
/// - a draggable scrubber for the current frame,
/// - draggable range-start and range-end handles,
/// - a right-click context menu for precise range placement,
/// - mouse-wheel zoom (centred on cursor) and zoom-bar buttons.
/// </summary>
public sealed class TimelineControl : Control
{
    // ── Layout ───────────────────────────────────────────────────────────────────

    private const int PaddingH  = 16;
    private const int LabelTop  = 3;
    private const int TrackTop  = 24;
    private const int TrackHeight = 18;
    private const int HandleHW  = 5;   // half-width of diamond handle
    private const int HandleHH  = 7;   // half-height of diamond handle
    private const int HitRadius = 9;
    private const int ZoomBarH  = 22;  // height of the bottom zoom button strip
    private const double ZoomMin = 1.0;
    private const double ZoomMax = 200.0;
    private const double ZoomStep = 1.25;

    // ── Colors (dark theme) ──────────────────────────────────────────────────────

    private static readonly Color CTrack       = Color.FromArgb(51, 65, 85);
    private static readonly Color CTickVar     = Color.FromArgb(99, 102, 241);
    private static readonly Color CTickPos     = Color.FromArgb(71, 85, 105);
    private static readonly Color CScrubber    = Color.FromArgb(59, 130, 246);
    private static readonly Color CRangeHandle = Color.FromArgb(16, 185, 129);
    private static readonly Color CRangeFill   = Color.FromArgb(28, 16, 185, 129);
    private static readonly Color CLabel       = Color.FromArgb(148, 163, 184);
    private static readonly Color CMenuBack    = Color.FromArgb(30, 41, 59);
    private static readonly Color CMenuFore    = Color.FromArgb(226, 232, 240);

    // ── State ────────────────────────────────────────────────────────────────────

    private List<(int Index, double Time, bool IsVariable)> _frames = [];
    private double _totalDuration = 1.0;
    private int _currentFrameIndex = -1;
    private int _rangeStart = -1;
    private int _rangeEnd = -1;

    private enum DragTarget { None, Scrubber, RangeStart, RangeEnd, Pan }
    private DragTarget _dragTarget = DragTarget.None;
    private int _panStartX;
    private double _panStartOffset;

    // Zoom: _zoomFactor ≥ 1; _viewOffset is the left edge of the viewport in time.
    private double _zoomFactor = 1.0;
    private double _viewOffset = 0.0;  // in seconds

    // ── Events ───────────────────────────────────────────────────────────────────

    public event EventHandler<int>? CurrentFrameChanged;
    public event EventHandler? RangeChanged;

    // ── Constructor ──────────────────────────────────────────────────────────────

    public TimelineControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(30, 41, 59);
        Height = 68 + ZoomBarH;
        Cursor = Cursors.Hand;
    }

    // ── Public API ───────────────────────────────────────────────────────────────

    public int CurrentFrameIndex
    {
        get => _currentFrameIndex;
        set { if (_currentFrameIndex != value) { _currentFrameIndex = value; Invalidate(); } }
    }

    public int RangeStartIndex
    {
        get => _rangeStart;
        set { _rangeStart = value; Invalidate(); }
    }

    public int RangeEndIndex
    {
        get => _rangeEnd;
        set { _rangeEnd = value; Invalidate(); }
    }

    public double ZoomFactor
    {
        get => _zoomFactor;
        set { _zoomFactor = Math.Clamp(value, ZoomMin, ZoomMax); ClampViewOffset(); Invalidate(); }
    }

    public bool HasRange => _rangeStart >= 0 && _rangeEnd >= 0 && _rangeEnd >= _rangeStart;

    public int FrameCount => _frames.Count;

    public void SetFrames(IEnumerable<(int Index, double Time, bool IsVariable)> frames, double totalDuration)
    {
        _frames = [.. frames];
        _totalDuration = Math.Max(totalDuration, 0.001);
        _currentFrameIndex = -1;
        _rangeStart = -1;
        _rangeEnd = -1;
        _zoomFactor = 1.0;
        _viewOffset = 0.0;
        Invalidate();
    }

    public void ClearRange()
    {
        _rangeStart = -1;
        _rangeEnd = -1;
        Invalidate();
        RangeChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Painting ─────────────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);

        int tl = PaddingH;
        int tw = Width - 2 * PaddingH;
        if (tw <= 0) return;

        DrawTrack(g, tl, tw);
        DrawRangeFill(g, tl, tw);
        DrawTicks(g, tl, tw);
        DrawTimeLabels(g, tl, tw);
        DrawRangeHandles(g, tl, tw);
        DrawScrubber(g, tl, tw);
        DrawZoomBar(g, tl, tw);
    }

    private void DrawTrack(Graphics g, int tl, int tw)
    {
        using SolidBrush b = new(CTrack);
        g.FillRectangle(b, tl, TrackTop, tw, TrackHeight);
    }

    private void DrawRangeFill(Graphics g, int tl, int tw)
    {
        if (!HasRange) return;
        int x1 = FrameToX(tl, tw, _rangeStart);
        int x2 = FrameToX(tl, tw, _rangeEnd);
        using SolidBrush b = new(CRangeFill);
        g.FillRectangle(b, Math.Min(x1, x2), TrackTop, Math.Abs(x2 - x1) + 1, TrackHeight);
    }

    private void DrawTicks(Graphics g, int tl, int tw)
    {
        if (_frames.Count == 0) return;

        // Throttle tick density to at most 1 tick per 2 pixels.
        int stride = Math.Max(1, _frames.Count / (tw / 2));

        using Pen varPen = new(CTickVar, 1);
        using Pen posPen = new(CTickPos, 1);

        for (int i = 0; i < _frames.Count; i += stride)
        {
            var (_, time, isVar) = _frames[i];
            int x = TimeToX(tl, tw, time);
            if (isVar)
                g.DrawLine(varPen, x, TrackTop - 3, x, TrackTop + TrackHeight + 2);
            else
                g.DrawLine(posPen, x, TrackTop, x, TrackTop + TrackHeight);
        }
    }

    private void DrawTimeLabels(Graphics g, int tl, int tw)
    {
        using Font f = new("Segoe UI", 7.5f);
        using SolidBrush b = new(CLabel);

        double visibleDuration = _totalDuration / _zoomFactor;
        int labelCount = Math.Max(2, tw / 80);
        for (int i = 0; i <= labelCount; i++)
        {
            double t = _viewOffset + visibleDuration * i / labelCount;
            if (t > _totalDuration) break;
            int x = TimeToX(tl, tw, t);
            string label = $"{t:0.00}s";
            SizeF sz = g.MeasureString(label, f);
            if (x - sz.Width / 2f >= tl && x + sz.Width / 2f <= tl + tw)
                g.DrawString(label, f, b, x - sz.Width / 2f, LabelTop);
        }
    }

    private void DrawZoomBar(Graphics g, int tl, int tw)
    {
        int barTop = Height - ZoomBarH;

        // Background strip
        using (SolidBrush bg = new(Color.FromArgb(15, 23, 42)))
            g.FillRectangle(bg, 0, barTop, Width, ZoomBarH);

        using Font f = new("Segoe UI", 8f);
        using SolidBrush lb = new(CLabel);

        // Zoom percentage label
        string pct = $"{_zoomFactor:0.0}\u00d7  [{_viewOffset:0.00}s–{_viewOffset + _totalDuration / _zoomFactor:0.00}s]";
        g.DrawString(pct, f, lb, tl, barTop + 4);

        // "-" and "+" buttons
        int btnW = 22, btnH = 16;
        int btnY = barTop + (ZoomBarH - btnH) / 2;
        int plusX  = tl + tw - btnW;
        int minusX = plusX - btnW - 4;

        DrawZoomButton(g, minusX, btnY, btnW, btnH, "−");
        DrawZoomButton(g, plusX,  btnY, btnW, btnH, "+");

        // Scroll track (minimap)
        if (_zoomFactor > 1.0)
        {
            int trackX = tl + 80, trackRight = minusX - 8;
            int trackW = trackRight - trackX;
            if (trackW > 20)
            {
                using Pen tp = new(Color.FromArgb(71, 85, 105), 1);
                g.DrawRectangle(tp, trackX, btnY, trackW, btnH);

                double thumbStart = _viewOffset / _totalDuration;
                double thumbEnd   = (_viewOffset + _totalDuration / _zoomFactor) / _totalDuration;
                int tx = trackX + (int)(thumbStart * trackW);
                int tw2 = Math.Max(4, (int)((thumbEnd - thumbStart) * trackW));
                using SolidBrush tb = new(Color.FromArgb(80, 59, 130, 246));
                g.FillRectangle(tb, tx, btnY + 1, tw2, btnH - 1);
            }
        }
    }

    private void DrawZoomButton(Graphics g, int x, int y, int w, int h, string label)
    {
        using SolidBrush bg = new(Color.FromArgb(51, 65, 85));
        using Pen border = new(Color.FromArgb(71, 85, 105), 1);
        using SolidBrush fg = new(CLabel);
        using Font f = new("Segoe UI", 9f, FontStyle.Bold);
        g.FillRectangle(bg, x, y, w, h);
        g.DrawRectangle(border, x, y, w - 1, h - 1);
        SizeF sz = g.MeasureString(label, f);
        g.DrawString(label, f, fg, x + (w - sz.Width) / 2f, y + (h - sz.Height) / 2f);
    }

    private void DrawRangeHandles(Graphics g, int tl, int tw)
    {
        if (_rangeStart >= 0)
            DrawDiamond(g, FrameToX(tl, tw, _rangeStart), TrackTop + TrackHeight / 2, CRangeHandle);

        if (_rangeEnd >= 0)
            DrawDiamond(g, FrameToX(tl, tw, _rangeEnd), TrackTop + TrackHeight / 2, CRangeHandle);
    }

    private void DrawScrubber(Graphics g, int tl, int tw)
    {
        if (_currentFrameIndex < 0 || _currentFrameIndex >= _frames.Count) return;

        int x = FrameToX(tl, tw, _currentFrameIndex);
        using Pen p = new(CScrubber, 2);
        g.DrawLine(p, x, 2, x, Height - 2);
        DrawDiamond(g, x, TrackTop + TrackHeight / 2, CScrubber);
    }

    private static void DrawDiamond(Graphics g, int cx, int cy, Color color)
    {
        Point[] pts =
        [
            new(cx, cy - HandleHH),
            new(cx + HandleHW, cy),
            new(cx, cy + HandleHH),
            new(cx - HandleHW, cy),
        ];
        using SolidBrush b = new(color);
        g.FillPolygon(b, pts);
    }

    // ── Coordinate helpers ───────────────────────────────────────────────────────

    /// <summary>Converts a time value to an x pixel, accounting for zoom/offset.</summary>
    private int TimeToX(int tl, int tw, double time)
    {
        double visible = _totalDuration / _zoomFactor;
        double frac = (time - _viewOffset) / visible;
        return tl + (int)(Math.Clamp(frac, 0, 1) * tw);
    }

    private int FrameToX(int tl, int tw, int fi)
    {
        if (fi < 0 || fi >= _frames.Count) return tl;
        return TimeToX(tl, tw, _frames[fi].Time);
    }

    /// <summary>Binary search for the frame closest to the x pixel within the current viewport.</summary>
    private int XToFrame(int tl, int tw, int x)
    {
        if (_frames.Count == 0) return -1;

        double visible = _totalDuration / _zoomFactor;
        double target = _viewOffset + Math.Clamp((double)(x - tl) / tw, 0, 1) * visible;

        int lo = 0, hi = _frames.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_frames[mid].Time < target) lo = mid + 1;
            else hi = mid;
        }

        int candidate = Math.Clamp(lo, 0, _frames.Count - 1);
        if (candidate > 0 &&
            Math.Abs(_frames[candidate - 1].Time - target) <= Math.Abs(_frames[candidate].Time - target))
            candidate--;

        return candidate;
    }

    private void ClampViewOffset()
    {
        double visible = _totalDuration / _zoomFactor;
        _viewOffset = Math.Clamp(_viewOffset, 0, Math.Max(0, _totalDuration - visible));
    }

    /// <summary>
    /// Zooms by <paramref name="factor"/> keeping the time under pixel <paramref name="pivotX"/> fixed.
    /// </summary>
    private void ZoomAt(int tl, int tw, int pivotX, double factor)
    {
        double visible = _totalDuration / _zoomFactor;
        double pivotTime = _viewOffset + Math.Clamp((double)(pivotX - tl) / tw, 0, 1) * visible;

        _zoomFactor = Math.Clamp(_zoomFactor * factor, ZoomMin, ZoomMax);
        double newVisible = _totalDuration / _zoomFactor;

        // Keep pivotTime at the same screen position.
        double frac = Math.Clamp((double)(pivotX - tl) / tw, 0, 1);
        _viewOffset = pivotTime - frac * newVisible;
        ClampViewOffset();
        Invalidate();
    }

    // ── Mouse interaction ────────────────────────────────────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_frames.Count == 0) return;

        int tl = PaddingH, tw = Width - 2 * PaddingH;

        // Middle button: start pan
        if (e.Button == MouseButtons.Middle)
        {
            _dragTarget = DragTarget.Pan;
            _panStartX = e.X;
            _panStartOffset = _viewOffset;
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            ShowContextMenu(e.Location, tl, tw);
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        // Check zoom bar button hit.
        if (e.Y >= Height - ZoomBarH)
        {
            int btnW = 22, btnH = 16;
            int plusX  = tl + tw - btnW;
            int minusX = plusX - btnW - 4;
            if (e.X >= plusX && e.X <= plusX + btnW)
                ZoomAt(tl, tw, tl + tw / 2, ZoomStep);
            else if (e.X >= minusX && e.X <= minusX + btnW)
                ZoomAt(tl, tw, tl + tw / 2, 1.0 / ZoomStep);
            return;
        }

        // Hit-test handles before falling back to scrubber seek.
        if (_rangeStart >= 0 && Math.Abs(e.X - FrameToX(tl, tw, _rangeStart)) <= HitRadius)
            _dragTarget = DragTarget.RangeStart;
        else if (_rangeEnd >= 0 && Math.Abs(e.X - FrameToX(tl, tw, _rangeEnd)) <= HitRadius)
            _dragTarget = DragTarget.RangeEnd;
        else
        {
            _dragTarget = DragTarget.Scrubber;
            SeekToX(e.X, tl, tw);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_frames.Count == 0 || _dragTarget == DragTarget.None) return;

        int tl = PaddingH, tw = Width - 2 * PaddingH;

        switch (_dragTarget)
        {
            case DragTarget.Scrubber:
                if (e.Button == MouseButtons.Left) SeekToX(e.X, tl, tw);
                break;

            case DragTarget.RangeStart:
            {
                _rangeStart = XToFrame(tl, tw, e.X);
                if (_rangeEnd >= 0 && _rangeEnd < _rangeStart) _rangeEnd = _rangeStart;
                Invalidate();
                RangeChanged?.Invoke(this, EventArgs.Empty);
                break;
            }

            case DragTarget.RangeEnd:
            {
                _rangeEnd = XToFrame(tl, tw, e.X);
                if (_rangeStart >= 0 && _rangeStart > _rangeEnd) _rangeStart = _rangeEnd;
                Invalidate();
                RangeChanged?.Invoke(this, EventArgs.Empty);
                break;
            }

            case DragTarget.Pan:
            {
                double visible = _totalDuration / _zoomFactor;
                double delta = -(double)(e.X - _panStartX) / tw * visible;
                _viewOffset = Math.Clamp(_panStartOffset + delta, 0, Math.Max(0, _totalDuration - visible));
                Invalidate();
                break;
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragTarget = DragTarget.None;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_frames.Count == 0) return;

        int tl = PaddingH, tw = Width - 2 * PaddingH;
        double factor = e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep;
        // Hold Ctrl for finer steps.
        if ((ModifierKeys & Keys.Control) != 0)
            factor = e.Delta > 0 ? 1.05 : 1.0 / 1.05;
        ZoomAt(tl, tw, e.X, factor);
    }

    private void SeekToX(int x, int tl, int tw)
    {
        int fi = XToFrame(tl, tw, x);
        if (fi == _currentFrameIndex) return;
        _currentFrameIndex = fi;
        Invalidate();
        CurrentFrameChanged?.Invoke(this, fi);
    }

    // ── Context menu ─────────────────────────────────────────────────────────────

    private void ShowContextMenu(Point location, int tl, int tw)
    {
        int fi = XToFrame(tl, tw, location.X);
        if (fi < 0) return;

        double t = _frames[fi].Time;

        ContextMenuStrip menu = new();
        menu.BackColor = CMenuBack;
        menu.ForeColor = CMenuFore;
        menu.RenderMode = ToolStripRenderMode.System;

        ToolStripMenuItem setStart = new($"Set Range Start here  (frame {fi}, {t:0.00}s)");
        setStart.Click += (_, _) =>
        {
            _rangeStart = fi;
            if (_rangeEnd >= 0 && _rangeEnd < _rangeStart) _rangeEnd = _rangeStart;
            Invalidate();
            RangeChanged?.Invoke(this, EventArgs.Empty);
        };

        ToolStripMenuItem setEnd = new($"Set Range End here  (frame {fi}, {t:0.00}s)");
        setEnd.Click += (_, _) =>
        {
            _rangeEnd = fi;
            if (_rangeStart >= 0 && _rangeStart > _rangeEnd) _rangeStart = _rangeEnd;
            Invalidate();
            RangeChanged?.Invoke(this, EventArgs.Empty);
        };

        ToolStripMenuItem clear = new("Clear Range");
        clear.Enabled = HasRange;
        clear.Click += (_, _) => ClearRange();

        menu.Items.Add(setStart);
        menu.Items.Add(setEnd);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(clear);
        menu.Show(this, location);
    }
}
