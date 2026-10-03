using System.Globalization;

namespace RecordingXRay.Services;

/// <summary>Formatting for recording times (seconds since the recording started).</summary>
public static class TimeFormat
{
    /// <summary>
    /// <c>m:ss.fff</c> (<c>11:51.165</c>), or <c>mm:ss.fff</c> (<c>00:00.026</c>) when <paramref name="padMinutes"/> is set.
    /// Minutes are not capped at 59.
    /// </summary>
    public static string Clock(double seconds, bool padMinutes = false)
    {
        long totalMs = double.IsFinite(seconds) ? (long)Math.Round(Math.Max(seconds, 0) * 1000.0) : 0;
        long minutes = totalMs / 60000;
        long secs = totalMs % 60000 / 1000;
        long millis = totalMs % 1000;

        string minutesText = padMinutes
            ? minutes.ToString("00", CultureInfo.InvariantCulture)
            : minutes.ToString(CultureInfo.InvariantCulture);

        return string.Create(CultureInfo.InvariantCulture, $"{minutesText}:{secs:00}.{millis:000}");
    }

    /// <summary>Seconds with three decimals and a unit, for example <c>711.165 s</c>.</summary>
    public static string Seconds(double seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{seconds:0.000} s");
}
