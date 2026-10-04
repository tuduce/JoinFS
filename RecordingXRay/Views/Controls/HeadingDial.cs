using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RecordingXRay.Services;

namespace RecordingXRay.Views.Controls;

/// <summary>A compass card that turns under a fixed pointer. Heading is in radians, clockwise from north.</summary>
public sealed class HeadingDial : Control
{
    public static readonly StyledProperty<double> HeadingProperty = AvaloniaProperty.Register<HeadingDial, double>(nameof(Heading));
    public static readonly StyledProperty<IBrush?> FaceBrushProperty = AvaloniaProperty.Register<HeadingDial, IBrush?>(nameof(FaceBrush));
    public static readonly StyledProperty<IBrush?> RingBrushProperty = AvaloniaProperty.Register<HeadingDial, IBrush?>(nameof(RingBrush));
    public static readonly StyledProperty<IBrush?> TickBrushProperty = AvaloniaProperty.Register<HeadingDial, IBrush?>(nameof(TickBrush));
    public static readonly StyledProperty<IBrush?> LetterBrushProperty = AvaloniaProperty.Register<HeadingDial, IBrush?>(nameof(LetterBrush));
    public static readonly StyledProperty<IBrush?> ValueBrushProperty = AvaloniaProperty.Register<HeadingDial, IBrush?>(nameof(ValueBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty = AvaloniaProperty.Register<HeadingDial, IBrush?>(nameof(AccentBrush));
    public static readonly StyledProperty<FontFamily> UiFontProperty = AvaloniaProperty.Register<HeadingDial, FontFamily>(nameof(UiFont), FontFamily.Default);
    public static readonly StyledProperty<FontFamily> MonoFontProperty = AvaloniaProperty.Register<HeadingDial, FontFamily>(nameof(MonoFont), FontFamily.Default);

    private const double DesignSize = 168;

    static HeadingDial()
    {
        AffectsRender<HeadingDial>(HeadingProperty, FaceBrushProperty, RingBrushProperty, TickBrushProperty, LetterBrushProperty,
            ValueBrushProperty, AccentBrushProperty, UiFontProperty, MonoFontProperty);
    }

    public double Heading { get => GetValue(HeadingProperty); set => SetValue(HeadingProperty, value); }

    public IBrush? FaceBrush { get => GetValue(FaceBrushProperty); set => SetValue(FaceBrushProperty, value); }

    public IBrush? RingBrush { get => GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }

    public IBrush? TickBrush { get => GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }

    public IBrush? LetterBrush { get => GetValue(LetterBrushProperty); set => SetValue(LetterBrushProperty, value); }

    public IBrush? ValueBrush { get => GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }

    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    public FontFamily UiFont { get => GetValue(UiFontProperty); set => SetValue(UiFontProperty, value); }

    public FontFamily MonoFont { get => GetValue(MonoFontProperty); set => SetValue(MonoFontProperty, value); }

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
        double headingDegrees = NumberFormat.HeadingDegrees(Heading);

        context.DrawEllipse(FaceBrush, new Pen(RingBrush, 3 * s), center, radius, radius);

        // Ticks every 30 degrees and the four letters, turned so the current heading is at the top.
        Pen tick = new(TickBrush, 1.5 * s);
        string[] letters = ["N", "E", "S", "W"];
        for (int i = 0; i < 12; i++)
        {
            double angle = (i * 30 - headingDegrees) * Math.PI / 180.0;
            Matrix turn = Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateRotation(angle) * Matrix.CreateTranslation(center.X, center.Y);
            using (context.PushTransform(turn))
            {
                context.DrawLine(tick, new Point(center.X, center.Y - 76 * s), new Point(center.X, center.Y - 68 * s));
                if (i % 3 == 0)
                {
                    FormattedText letter = Text(letters[i / 3], UiFont, 13 * s, FontWeight.SemiBold, i == 0 ? AccentBrush : LetterBrush);
                    context.DrawText(letter, new Point(center.X - letter.Width / 2, center.Y - 48 * s - letter.Height / 2));
                }
            }
        }

        // Fixed pointer at the top.
        StreamGeometry pointer = new();
        using (StreamGeometryContext g = pointer.Open())
        {
            g.BeginFigure(new Point(center.X, center.Y - 82 * s), true);
            g.LineTo(new Point(center.X - 6 * s, center.Y - 71 * s));
            g.LineTo(new Point(center.X + 6 * s, center.Y - 71 * s));
            g.EndFigure(true);
        }

        context.DrawGeometry(AccentBrush, null, pointer);

        FormattedText value = Text(headingDegrees.ToString("0.0", CultureInfo.InvariantCulture) + "°", MonoFont, 18 * s, FontWeight.SemiBold, ValueBrush);
        context.DrawText(value, new Point(center.X - value.Width / 2, center.Y - value.Height / 2));
    }

    private static FormattedText Text(string text, FontFamily family, double size, FontWeight weight, IBrush? brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyle.Normal, weight), size, brush);
}
