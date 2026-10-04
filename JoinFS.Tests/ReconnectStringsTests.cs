using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace JoinFS.Tests;

/// <summary>Every locale ships the strings of the automatic reconnect, with the same placeholders.</summary>
public class ReconnectStringsTests
{
    static readonly string[] Locales = ["", "de", "es", "fr", "it", "ko", "nl", "pt", "ru"];

    static readonly string[] Keys = ["ReconnectGaveUp", "ReconnectCredentialsRejected", "Tip_NetworkReconnecting"];

    static string ResourcesFolder()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JoinFS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "JoinFS", "Resources");
    }

    static string Value(string locale, string key)
    {
        string file = Path.Combine(ResourcesFolder(), locale == "" ? "strings.resx" : $"strings.{locale}.resx");
        XElement? data = XDocument.Load(file).Root!.Elements("data").FirstOrDefault(e => (string?)e.Attribute("name") == key);
        Assert.True(data != null, $"{Path.GetFileName(file)} lacks '{key}'");
        return data!.Element("value")!.Value;
    }

    static string Placeholders(string text) =>
        string.Join(",", Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).OrderBy(p => p));

    public static IEnumerable<object[]> LocaleAndKey() =>
        from locale in Locales from key in Keys select new object[] { locale, key };

    [Theory]
    [MemberData(nameof(LocaleAndKey))]
    public void EveryLocaleHasTheString_WithTheSamePlaceholdersAsEnglish(string locale, string key)
    {
        string translated = Value(locale, key);

        Assert.False(string.IsNullOrWhiteSpace(translated));
        Assert.Equal(Placeholders(Value("", key)), Placeholders(translated));
    }

    [Fact]
    public void GaveUpMessage_ShowsTheMinutes()
    {
        Assert.Equal("{0}", Placeholders(Value("", "ReconnectGaveUp")));
    }
}
