using System.Collections;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using JoinFS.UI.Localization;
using JoinFS.UI.Views;

// The language is one static, which the tests below change; so no test runs beside another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace JoinFS.UI.Tests;

internal static class TestLanguage
{
    /// <summary>
    /// Every test starts in English, whatever the language of the machine it runs on. To see the screens in another language, run
    /// the render tests only: <c>JOINFS_UI_LANG=de JOINFS_UI_SCREENSHOTS=folder dotnet test --filter RenderTests</c> (the other tests expect English).
    /// </summary>
    [ModuleInitializer]
    internal static void English()
    {
        string? language = Environment.GetEnvironmentVariable("JOINFS_UI_LANG");
        Loc.Culture = string.IsNullOrEmpty(language) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(language);
    }
}

public class LocalizationTests
{
    /// <summary>The languages of the old forms.</summary>
    private static readonly string[] Languages = ["de", "es", "fr", "it", "ko", "nl", "pt", "ru"];

    private static readonly ResourceManager Resources = new("JoinFS.UI.Resources.Strings", typeof(Loc).Assembly);

    private static Dictionary<string, string> Strings(CultureInfo culture, bool parents)
    {
        ResourceSet? set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: parents);
        Dictionary<string, string> strings = [];
        if (set is null)
            return strings;
        foreach (DictionaryEntry entry in set)
            strings[(string)entry.Key] = (string?)entry.Value ?? "";
        return strings;
    }

    private static string RepositoryFolder()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !File.Exists(Path.Combine(folder, "JoinFS.sln")))
            folder = Path.GetDirectoryName(folder);
        return folder ?? throw new InvalidOperationException("JoinFS.sln not found above " + AppContext.BaseDirectory);
    }

    private static readonly Regex Placeholder = new(@"\{\d\}");

    [Fact]
    public void EveryLanguageTranslatesEveryText()
    {
        Dictionary<string, string> english = Strings(CultureInfo.InvariantCulture, parents: true);
        Assert.NotEmpty(english);

        List<string> problems = [];
        foreach (string language in Languages)
        {
            Dictionary<string, string> translated = Strings(CultureInfo.GetCultureInfo(language), parents: false);
            foreach (string key in english.Keys)
            {
                if (!translated.TryGetValue(key, out string? text) || string.IsNullOrWhiteSpace(text))
                {
                    problems.Add($"{language}: no translation of \"{key}\"");
                    continue;
                }
                string[] want = Placeholder.Matches(key).Select(m => m.Value).Order().ToArray();
                string[] have = Placeholder.Matches(text).Select(m => m.Value).Order().ToArray();
                if (!want.SequenceEqual(have))
                    problems.Add($"{language}: \"{key}\" has the placeholders {string.Join("", have)}, not {string.Join("", want)}");
            }
            foreach (string key in translated.Keys.Where(k => !english.ContainsKey(k)))
                problems.Add($"{language}: \"{key}\" is not an English text");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Take(40)));
    }

    /// <summary>A text that is looked up but not in the resources would show in English in every language and nobody would notice.</summary>
    [Fact]
    public void EveryTextTheUiLooksUpIsInTheResources()
    {
        string root = RepositoryFolder();
        Dictionary<string, string> english = Strings(CultureInfo.InvariantCulture, parents: true);
        List<string> used = [];

        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "JoinFS.UI"), "*.axaml", SearchOption.AllDirectories).Where(NotBuildOutput))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\{l:T '((?:[^'\\]|\\.)*)'"))
                used.Add(System.Net.WebUtility.HtmlDecode(Regex.Replace(match.Groups[1].Value, @"\\(.)", "$1")));
        }

        IEnumerable<string> code = Directory.EnumerateFiles(Path.Combine(root, "JoinFS.UI"), "*.cs", SearchOption.AllDirectories).Where(NotBuildOutput)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "JoinFS", "Live"), "*.cs"));
        foreach (string file in code)
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"Loc\.[TF]\(\s*""((?:[^""\\]|\\.)*)"""))
                used.Add(Regex.Replace(match.Groups[1].Value, @"\\(.)", "$1"));
        }

        Assert.True(used.Count > 200, "found only " + used.Count + " texts; has the pattern changed?");
        string[] missing = used.Where(text => !english.ContainsKey(text)).Distinct().ToArray();
        Assert.True(missing.Length == 0, "not in Strings.resx: " + string.Join(" | ", missing));
    }

    private static bool NotBuildOutput(string path) =>
        !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar);

    [Fact]
    public void ATextWithoutTranslationShowsAsItIs()
    {
        Assert.Equal("Not a text of the UI", Loc.T("Not a text of the UI"));
        Assert.Equal("Version 26.6.0", Loc.F("Version {0}", "26.6.0"));
    }

    [Fact]
    public void TheLanguageIsChosenByTheCulture()
    {
        CultureInfo before = Loc.Culture;
        try
        {
            Loc.Culture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("Vollansicht öffnen", Loc.T("Open full view"));
            Assert.Equal("Version 26.6.0", Loc.F("Version {0}", "26.6.0"));
            Assert.Equal("Aktivitätskreis: 50 nm", Loc.F("Circle of activity: {0} nm", 50));

            // a regional variant uses the language's texts
            Loc.Culture = CultureInfo.GetCultureInfo("pt-BR");
            Assert.Equal("Abrir visualização completa", Loc.T("Open full view"));

            // a language the UI has no texts for shows English
            Loc.Culture = CultureInfo.GetCultureInfo("ja");
            Assert.Equal("Open full view", Loc.T("Open full view"));
        }
        finally
        {
            Loc.Culture = before;
        }
    }

    [Fact]
    public void HeadingsCanBeShownInCapitals()
    {
        CultureInfo before = Loc.Culture;
        try
        {
            Loc.Culture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("EINSTELLUNGEN", new TExtension("Settings") { Upper = true }.ProvideValue(null!));
        }
        finally
        {
            Loc.Culture = before;
        }
    }

    [AvaloniaFact]
    public void TheCollapsedWindowIsShownInTheChosenLanguage()
    {
        CultureInfo before = Loc.Culture;
        try
        {
            Loc.Culture = CultureInfo.GetCultureInfo("ru");
            Rig rig = new();
            MainWindow window = new() { DataContext = rig.Main };
            window.Show();

            string[] texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
            Assert.Contains("Симулятор", texts);
            Assert.Contains("Сеть", texts);
            Assert.Contains("Открыть полный вид", texts);
            Assert.Contains("Отключено", texts);
            Assert.DoesNotContain("Open full view", texts);
        }
        finally
        {
            Loc.Culture = before;
        }
    }
}
