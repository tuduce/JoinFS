using System.Globalization;

namespace RecordingXRay.Services;

/// <summary>Number formatting for the inspector. Always culture-invariant.</summary>
public static class NumberFormat
{
    /// <summary>Full-precision shortest round-trip text, for example <c>0.19611156</c>.</summary>
    public static string Raw(double value) => value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Raw(double)"/>
    public static string Raw(float value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Radians as degrees with a fixed number of decimals, for example <c>55.4293°</c>. Never shows <c>-0.00°</c>.</summary>
    public static string Degrees(double radians, int decimals)
    {
        double degrees = radians * 180.0 / Math.PI;
        string text = degrees.ToString("F" + decimals, CultureInfo.InvariantCulture);
        if (text.StartsWith('-') && text.Trim('-', '0', '.').Length == 0)
        {
            text = text[1..];
        }

        return text + "°";
    }

    /// <summary>A heading in radians as degrees in 0..360.</summary>
    public static double HeadingDegrees(double radians)
    {
        double degrees = radians * 180.0 / Math.PI % 360.0;
        return degrees < 0 ? degrees + 360.0 : degrees;
    }

    /// <summary>Heading text such as <c>164.70°</c>.</summary>
    public static string Heading(double radians) =>
        HeadingDegrees(radians).ToString("F2", CultureInfo.InvariantCulture) + "°";

    /// <summary>
    /// Recorded control values are fixed point, <c>raw / 16384</c> (docs/recording-protocol.md 4.2). Returns the
    /// fraction clamped to -1..1, or 0..1 for one-sided controls such as brakes.
    /// </summary>
    public static double ControlFraction(short raw, bool centered)
    {
        double fraction = raw / 16384.0;
        return centered ? Math.Clamp(fraction, -1.0, 1.0) : Math.Clamp(fraction, 0.0, 1.0);
    }
}
