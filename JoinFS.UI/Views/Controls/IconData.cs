using Avalonia.Media;

namespace JoinFS.UI.Views.Controls;

/// <summary>
/// One icon from the design: an optional stroked outline and an optional filled shape, drawn in the
/// same colour on a <see cref="ViewWidth"/> x <see cref="ViewHeight"/> grid (18 x 18 unless stated).
/// Declared as a resource in Styles/Icons.axaml.
/// </summary>
public sealed class IconData
{
    public Geometry? Stroke { get; set; }
    public Geometry? Fill { get; set; }
    public double StrokeWidth { get; set; } = 1.5;
    public double ViewWidth { get; set; } = 18;
    public double ViewHeight { get; set; } = 18;
}
