using System.Globalization;

namespace RecordingXRay.Services;

public static class TimeParse
{
    /// <summary>
    /// Parses a time the user typed: <c>123.4</c>, <c>123.4 s</c> or <c>2:03.4</c> (minutes:seconds). Returns false
    /// for anything else, including negative times.
    /// </summary>
    public static bool TryParse(string? text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (text.EndsWith('s') || text.EndsWith('S'))
        {
            text = text[..^1].TrimEnd();
        }

        double minutes = 0;
        int colon = text.IndexOf(':');
        if (colon >= 0)
        {
            if (!double.TryParse(text[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
            {
                return false;
            }

            text = text[(colon + 1)..];
        }

        if (!double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double secs) || secs < 0)
        {
            return false;
        }

        seconds = minutes * 60.0 + secs;
        return true;
    }
}
