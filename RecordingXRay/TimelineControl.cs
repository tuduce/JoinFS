namespace RecordingXRay;

/// <summary>
/// A custom WinForms control that renders a horizontal timeline with:
/// - colour-coded frame ticks (variable frames vs. position/other frames),
/// - a draggable scrubber for the current frame,
/// - draggable range-start and range-end handles,
/// - a right-click context menu for precise range placement.
/// </summary>
public sealed class TimelineControl : Control
{
    // ── Layout ───────────────────────────────────────────────────────────────────

    private const int PaddingH = 16;
    private const int LabelTop = 3;
    private const int TrackTop = 24;
    private const int TrackHeight = 18;
    private const int HandleHW = 5;   // half-width of diamond handle
    private const int HandleHH = 7;   // half-height of diamond handle
    private const int HitRadius = 9;

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

    private enum DragTarget { None, Scrubber, RangeStart, RangeEnd }
    private DragTarget _dragTarget = DragTarget.None;

    // ── Events ───────────────────────────────────────────────────────────────────

    public event EventHandler<int>? CurrentFrameChanged;
    public event EventHandler? RangeChanged;

    // ── Constructor ──────────────────────────────────────────────────────────────

    public TimelineControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(30, 41, 59);
        Height = 68;
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

    public bool HasRange => _rangeStart >= 0 && _rangeEnd >= 0 && _rangeEnd >= _rangeStart;

    public int FrameCount => _frames.Count;

    public void SetFrames(IEnumerable<(int Index, double Time, bool IsVariable)> frames, double totalDuration)
    {
        _frames = [.. frames];
        _totalDuration = Math.Max(totalDuration, 0.001);
        _currentFrameIndex = -1;
        _rangeStart = -1;
        _rangeEnd = -1;
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

        for (int i = 0; i <= 4; i++)
        {
            double t = _totalDuration * i / 4;
            int x = TimeToX(tl, tw, t);
            string label = $"{t:0.0}s";
            SizeF sz = g.MeasureString(label, f);
            g.DrawString(label, f, b, x - sz.Width / 2f, LabelTop);
        }
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

    private int TimeToX(int tl, int tw, double time)
        => tl + (int)(Math.Clamp(time / _totalDuration, 0, 1) * tw);

    private int FrameToX(int tl, int tw, int fi)
    {
        if (fi < 0 || fi >= _frames.Count) return tl;
        return TimeToX(tl, tw, _frames[fi].Time);
    }

    /// <summary>Binary search for the frame whose time is closest to the x pixel.</summary>
    private int XToFrame(int tl, int tw, int x)
    {
        if (_frames.Count == 0) return -1;

        double target = Math.Clamp((double)(x - tl) / tw, 0, 1) * _totalDuration;

        // Find insertion point.
        int lo = 0, hi = _frames.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_frames[mid].Time < target) lo = mid + 1;
            else hi = mid;
        }

        // Compare neighbours to find the closest frame.
        int candidate = Math.Clamp(lo, 0, _frames.Count - 1);
        if (candidate > 0 &&
            Math.Abs(_frames[candidate - 1].Time - target) <= Math.Abs(_frames[candidate].Time - target))
            candidate--;

        return candidate;
    }

    // ── Mouse interaction ────────────────────────────────────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_frames.Count == 0) return;

        int tl = PaddingH, tw = Width - 2 * PaddingH;

        if (e.Button == MouseButtons.Right)
        {
            ShowContextMenu(e.Location, tl, tw);
            return;
        }

        if (e.Button != MouseButtons.Left) return;

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
        if (e.Button != MouseButtons.Left || _frames.Count == 0 || _dragTarget == DragTarget.None)
            return;

        int tl = PaddingH, tw = Width - 2 * PaddingH;

        switch (_dragTarget)
        {
            case DragTarget.Scrubber:
                SeekToX(e.X, tl, tw);
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
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragTarget = DragTarget.None;
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
