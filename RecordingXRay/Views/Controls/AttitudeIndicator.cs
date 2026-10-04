using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RecordingXRay.Views.Controls;

/// <summary>
/// Artificial horizon. Pitch and bank are in radians with the sim's sign convention: positive pitch is nose down and
/// positive bank is left wing down, so a nose-up attitude moves the horizon down and a left bank turns it clockwise.
/// </summary>
public sealed class AttitudeIndicator : Control
{
    public static readonly StyledProperty<double> PitchProperty = AvaloniaProperty.Register<AttitudeIndicator, double>(nameof(Pitch));
    public static readonly StyledProperty<double> BankProperty = AvaloniaProperty.Register<AttitudeIndicator, double>(nameof(Bank));
    public static readonly StyledProperty<IBrush?> SkyBrushProperty = AvaloniaProperty.Register<AttitudeIndicator, IBrush?>(nameof(SkyBrush));
    public static readonly StyledProperty<IBrush?> GroundBrushProperty = AvaloniaProperty.Register<AttitudeIndicator, IBrush?>(nameof(GroundBrush));
    public static readonly StyledProperty<IBrush?> RingBrushProperty = AvaloniaProperty.Register<AttitudeIndicator, IBrush?>(nameof(RingBrush));
    public static readonly StyledProperty<IBrush?> LineBrushProperty = AvaloniaProperty.Register<AttitudeIndicator, IBrush?>(nameof(LineBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty = AvaloniaProperty.Register<AttitudeIndicator, IBrush?>(nameof(AccentBrush));

    // Pixels per degree of pitch at the 168 unit design size.
    private const double PitchScale = 2.2;
    private const double DesignSize = 168;

    static AttitudeIndicator()
    {
        AffectsRender<AttitudeIndicator>(PitchProperty, BankProperty, SkyBrushProperty, GroundBrushProperty, RingBrushProperty, LineBrushProperty, AccentBrushProperty);
    }

    public double Pitch { get => GetValue(PitchProperty); set => SetValue(PitchProperty, value); }

    public double Bank { get => GetValue(BankProperty); set => SetValue(BankProperty, value); }

    public IBrush? SkyBrush { get => GetValue(SkyBrushProperty); set => SetValue(SkyBrushProperty, value); }

    public IBrush? GroundBrush { get => GetValue(GroundBrushProperty); set => SetValue(GroundBrushProperty, value); }

    public IBrush? RingBrush { get => GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }

    public IBrush? LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }

    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        double s = size / DesignSize;
        Point center = new(Bounds.Width / 2, Bounds.Height / 2);
        double radius = 80 * s;
        double pitchDegrees = Pitch * 180.0 / Math.PI;
        double shift = -pitchDegrees * PitchScale * s;

        using (context.PushGeometryClip(new EllipseGeometry(new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2))))
        {
            Matrix horizon = Matrix.CreateTranslation(0, shift) * Matrix.CreateRotation(Bank) * Matrix.CreateTranslation(center.X, center.Y);
            using (context.PushTransform(horizon))
            {
                double extent = 800 * s;
                context.DrawRectangle(SkyBrush, null, new Rect(-extent, -extent, extent * 2, extent));
                context.DrawRectangle(GroundBrush, null, new Rect(-extent, 0, extent * 2, extent));

                Pen line = new(LineBrush, 1.5 * s);
                context.DrawLine(line, new Point(-extent, 0), new Point(extent, 0));

                Pen ladder = new(LineBrush, 1.2 * s);
                foreach (int sign in new[] { -1, 1 })
                {
                    context.DrawLine(ladder, new Point(-20 * s, sign * 22 * s), new Point(20 * s, sign * 22 * s));
                    context.DrawLine(ladder, new Point(-10 * s, sign * 44 * s), new Point(10 * s, sign * 44 * s));
                }
            }
        }

        context.DrawEllipse(null, new Pen(RingBrush, 3 * s), center, radius, radius);

        // The aircraft symbol stays fixed in the middle.
        Pen wings = new(AccentBrush, 3 * s, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        foreach (int sign in new[] { -1, 1 })
        {
            StreamGeometry wing = new();
            using (StreamGeometryContext g = wing.Open())
            {
                g.BeginFigure(new Point(center.X + sign * 40 * s, center.Y), false);
                g.LineTo(new Point(center.X + sign * 14 * s, center.Y));
                g.LineTo(new Point(center.X + sign * 10 * s, center.Y + 6 * s));
                g.EndFigure(false);
            }

            context.DrawGeometry(null, wings, wing);
        }

        context.DrawEllipse(AccentBrush, null, center, 2.5 * s, 2.5 * s);
    }
}
