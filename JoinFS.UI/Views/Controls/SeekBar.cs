using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace JoinFS.UI.Views.Controls;

/// <summary>
/// The Recorder's timeline: a thin track with a round handle. Click to move the playhead, or press and drag.
/// <see cref="Fraction"/> is 0..1 along the bar and binds two-way, so the view model decides what it snaps to.
/// </summary>
public sealed class SeekBar : Control
{
    private const double TrackHeight = 5, HandleSize = 14, HandleBorder = 2.5;

    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(Fraction), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay, coerce: (_, v) => Math.Clamp(v, 0, 1));

    static SeekBar()
    {
        AffectsRender<SeekBar>(FractionProperty);
        FocusableProperty.OverrideDefaultValue<SeekBar>(true);
        CursorProperty.OverrideDefaultValue<SeekBar>(new Cursor(StandardCursorType.Hand));
    }

    public double Fraction
    {
        get => GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width, 22);

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width;
        double centreY = Bounds.Height / 2;
        double x = Fraction * width;

        // The track clips the filled part, so it keeps the rounded ends.
        Rect track = new(0, centreY - TrackHeight / 2, width, TrackHeight);
        context.DrawRectangle(Resource("BorderInputBrush"), null, track, TrackHeight / 2, TrackHeight / 2);
        using (context.PushClip(new RoundedRect(track, TrackHeight / 2)))
            context.DrawRectangle(Resource("AccentBrush"), null, new Rect(0, track.Y, x, TrackHeight));

        Point centre = new(x, centreY);
        double radius = HandleSize / 2;
        context.DrawEllipse(Brushes.White, new Pen(Resource("AccentBrush"), HandleBorder), centre, radius - HandleBorder / 2, radius - HandleBorder / 2);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Pointer.Capture(this);
        Seek(e);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (ReferenceEquals(e.Pointer.Captured, this))
            Seek(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (ReferenceEquals(e.Pointer.Captured, this))
            e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.1 : 0.01;
        switch (e.Key)
        {
            case Key.Left: Fraction -= step; e.Handled = true; break;
            case Key.Right: Fraction += step; e.Handled = true; break;
            case Key.Home: Fraction = 0; e.Handled = true; break;
            case Key.End: Fraction = 1; e.Handled = true; break;
        }
    }

    private void Seek(PointerEventArgs e)
    {
        if (Bounds.Width > 0)
            Fraction = e.GetPosition(this).X / Bounds.Width;
    }

    private IBrush Resource(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush ? brush : Brushes.Gray;
}
