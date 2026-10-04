using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace JoinFS.UI.Views.Controls;

/// <summary>
/// The tiger-moth logo, drawn from the two geometries in Styles/Moth.axaml (64 x 64).
/// One colour (the inherited text colour) by default, like the title bar's 18px glyph; give
/// <see cref="BodyBrush"/> and <see cref="WingsBrush"/> for the two-tone sidebar and About versions.
/// </summary>
public sealed class TigerMoth : Control
{
    private const double ArtSize = 64;

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<TigerMoth>();

    public static readonly StyledProperty<IBrush?> BodyBrushProperty =
        AvaloniaProperty.Register<TigerMoth, IBrush?>(nameof(BodyBrush));

    public static readonly StyledProperty<IBrush?> WingsBrushProperty =
        AvaloniaProperty.Register<TigerMoth, IBrush?>(nameof(WingsBrush));

    static TigerMoth() => AffectsRender<TigerMoth>(ForegroundProperty, BodyBrushProperty, WingsBrushProperty);

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Fuselage and tail. Falls back to <see cref="Foreground"/>.</summary>
    public IBrush? BodyBrush
    {
        get => GetValue(BodyBrushProperty);
        set => SetValue(BodyBrushProperty, value);
    }

    /// <summary>Wings and propeller. Falls back to <see cref="Foreground"/>.</summary>
    public IBrush? WingsBrush
    {
        get => GetValue(WingsBrushProperty);
        set => SetValue(WingsBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(ArtSize, ArtSize);

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        double scale = Math.Min(Bounds.Width, Bounds.Height) / ArtSize;
        double offsetX = (Bounds.Width - ArtSize * scale) / 2;
        double offsetY = (Bounds.Height - ArtSize * scale) / 2;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            Draw(context, "MothBodyGeometry", BodyBrush ?? Foreground);
            Draw(context, "MothWingsGeometry", WingsBrush ?? Foreground);
        }
    }

    private void Draw(DrawingContext context, string key, IBrush? brush)
    {
        if (brush is not null && this.TryFindResource(key, ActualThemeVariant, out object? resource) && resource is Geometry geometry)
            context.DrawGeometry(brush, null, geometry);
    }
}
