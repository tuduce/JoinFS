using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace JoinFS.UI.Views.Controls;

/// <summary>
/// Draws an <see cref="IconData"/> in the inherited text colour, like the design's inline
/// <c>stroke="currentColor"</c> SVGs, so an icon follows its button's or nav item's foreground.
/// Size it with Width/Height; it scales uniformly and stays centred.
/// </summary>
public sealed class Icon : Control
{
    public static readonly StyledProperty<IconData?> SourceProperty =
        AvaloniaProperty.Register<Icon, IconData?>(nameof(Source));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    static Icon()
    {
        AffectsRender<Icon>(SourceProperty, ForegroundProperty);
        AffectsMeasure<Icon>(SourceProperty);
    }

    public IconData? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        IconData? source = Source;
        return source is null ? default : new Size(source.ViewWidth, source.ViewHeight);
    }

    public override void Render(DrawingContext context)
    {
        IconData? source = Source;
        IBrush? brush = Foreground;
        if (source is null || brush is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        double scale = Math.Min(Bounds.Width / source.ViewWidth, Bounds.Height / source.ViewHeight);
        double offsetX = (Bounds.Width - source.ViewWidth * scale) / 2;
        double offsetY = (Bounds.Height - source.ViewHeight * scale) / 2;
        Matrix transform = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY);

        using (context.PushTransform(transform))
        {
            if (source.Fill is not null)
                context.DrawGeometry(brush, null, source.Fill);
            if (source.Stroke is not null)
                context.DrawGeometry(null, new Pen(brush, source.StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), source.Stroke);
        }
    }
}
