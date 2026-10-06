using System.Globalization;
using System.Resources;

namespace JoinFS.UI.Localization;

/// <summary>
/// Looks up the text the UI shows. The English text is the key: <c>Loc.T("Open full view")</c> returns the
/// translation in <c>Resources/Strings.&lt;lang&gt;.resx</c>, or the English itself when the language has none, so a missing
/// translation shows English and never an error. The language is the operating system's, as the old forms' was, unless
/// <see cref="Culture"/> is set (the <c>--lang</c> switch does).
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Resources = new("JoinFS.UI.Resources.Strings", typeof(Loc).Assembly);

    /// <summary>The language of the text. Set it before the first window opens.</summary>
    public static CultureInfo Culture { get; set; } = CultureInfo.CurrentUICulture;

    /// <summary>The translation of <paramref name="english"/>.</summary>
    public static string T(string english)
    {
        try
        {
            return Resources.GetString(english, Culture) ?? english;
        }
        catch (MissingManifestResourceException)
        {
            return english;
        }
    }

    /// <summary>The translation of <paramref name="english"/>, a format string, filled with <paramref name="args"/>.</summary>
    public static string F(string english, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(english), args);
}
