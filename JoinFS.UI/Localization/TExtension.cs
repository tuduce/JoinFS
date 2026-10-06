using Avalonia.Markup.Xaml;

namespace JoinFS.UI.Localization;

/// <summary>
/// XAML's <c>{l:T 'Open full view'}</c>: the translation of the English text, see <see cref="Loc"/>.
/// <c>Upper=True</c> shows it in capitals, for headings, so they share the translation of the plain word.
/// </summary>
public sealed class TExtension(string english) : MarkupExtension
{
    public string English { get; set; } = english;

    public bool Upper { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        string text = Loc.T(English);
        return Upper ? text.ToUpper(Loc.Culture) : text;
    }
}
