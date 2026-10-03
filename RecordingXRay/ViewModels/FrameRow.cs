using System.Globalization;
using RecordingXRay.Services;

namespace RecordingXRay.ViewModels;

/// <summary>
/// One line in the frame browser. Created on demand while scrolling, never stored per frame. Records compare by value,
/// so a row made twice for the same frame is the same row to the list selection.
/// </summary>
public sealed record FrameRow(int Index, RecordedFrame Frame, double? PreviousTime)
{
    public string IndexText => $"[{Index.ToString(CultureInfo.InvariantCulture)}]";

    public string TimeText => TimeFormat.Seconds(Frame.Time);

    public string TypeName => Frame.Type.ToString();

    public FrameKind Kind => FrameKinds.Of(Frame.Type);

    /// <summary>Time since the previous frame in the same lane, such as <c>+75 ms</c>; a dash for the first frame.</summary>
    public string DeltaText
    {
        get
        {
            if (PreviousTime is not double previous)
            {
                return "—";
            }

            double ms = Math.Round((Frame.Time - previous) * 1000.0);
            return ms >= 100_000
                ? string.Create(CultureInfo.InvariantCulture, $"+{ms / 1000.0:0.0} s")
                : string.Create(CultureInfo.InvariantCulture, $"{(ms < 0 ? "-" : "+")}{Math.Abs(ms):0} ms");
        }
    }
}
