using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RecordingXRay.Views.Controls;

/// <summary>
/// A small bar for a control position. Centred bars (rudder, elevator, aileron) grow left or right of a centre tick for
/// -1..1; one-sided bars (brakes) fill from the left for 0..1.
/// </summary>
public sealed class ValueBar : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<ValueBar, double>(nameof(Value));
    public static readonly StyledProperty<bool> IsCenteredProperty = AvaloniaProperty.Register<ValueBar, bool>(nameof(IsCentered));
    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<ValueBar, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> FillBrushProperty = AvaloniaProperty.Register<ValueBar, IBrush?>(nameof(FillBrush));
    public static readonly StyledProperty<IBrush?> TickBrushProperty = AvaloniaProperty.Register<ValueBar, IBrush?>(nameof(TickBrush));

    private const double TrackHeight = 6;

    static ValueBar()
    {
        AffectsRender<ValueBar>(ValueProperty, IsCenteredProperty, TrackBrushProperty, FillBrushProperty, TickBrushProperty);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public bool IsCentered { get => GetValue(IsCenteredProperty); set => SetValue(IsCenteredProperty, value); }

    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    public IBrush? FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }

    public IBrush? TickBrush { get => GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width;
        double top = (Bounds.Height - TrackHeight) / 2;
        if (width <= 0)
        {
            return;
        }

        context.DrawRectangle(TrackBrush, null, new Rect(0, top, width, TrackHeight), 3, 3);

        double value = Math.Clamp(Value, IsCentered ? -1 : 0, 1);
        double x;
        double fillWidth;
        if (IsCentered)
        {
            fillWidth = value == 0 ? 0 : Math.Max(2, Math.Abs(value) * width / 2);
            x = value < 0 ? width / 2 - fillWidth : width / 2;
        }
        else
        {
            fillWidth = value == 0 ? 0 : Math.Max(2, value * width);
            x = 0;
        }

        if (fillWidth > 0)
        {
            context.DrawRectangle(FillBrush, null, new Rect(x, top, fillWidth, TrackHeight), 3, 3);
        }

        if (IsCentered)
        {
            context.DrawRectangle(TickBrush, null, new Rect(width / 2 - 1, top - 2, 2, TrackHeight + 4));
        }
    }
}
