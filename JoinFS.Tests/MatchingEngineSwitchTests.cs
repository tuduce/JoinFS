using System.Xml.Linq;
using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// The new engine is the default; the classic one is chosen only with the command line parameter --classicmatching (no setting, no UI).
    /// Every text the user can see for this - the option's help and the Explain Match sentences - exists in all languages.
    /// </summary>
    public class MatchingEngineSwitchTests
    {
        static string RepositoryFile(string relative)
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "JoinFS", relative))) dir = Path.GetDirectoryName(dir)!;
            return Path.Combine(dir ?? "", "JoinFS", relative);
        }

        static Sub Create(MatchingEngine engine)
        {
            var substitution = new Sub(null!) { engine = engine };
            substitution.LoadDoc8643Index();
            substitution.models = [new Sub.Model("Boeing 747-400", "Boeing", "747", "Default", 0, "Airliner", "0", "", "B744", "H", "", "L4J", true)];
            substitution.RebuildTitleIndex();
            substitution.MakeIcaoIndex();
            return substitution;
        }

        static MatchRequest Request() => new("Remote", "", "B748", "", "", "", false, Sub.TypeRole_Airliner, "") { LiveryAware = true };

        // ---- which engine ran -------------------------------------------------------------------------------------------

        [Fact]
        public void A_trace_says_the_new_engine_produced_it()
        {
            var (_, _, trace) = Create(MatchingEngine.New).Resolve(Request());
            Assert.Equal(MatchingEngine.New, trace.engine);
        }

        [Fact]
        public void A_trace_says_the_classic_engine_produced_it()
        {
            var (_, _, trace) = Create(MatchingEngine.Classic).Resolve(Request());
            Assert.Equal(MatchingEngine.Classic, trace.engine);
        }

        [Fact]
        public void A_trace_nobody_resolved_names_no_engine()
        {
            Assert.Null(new Sub.MatchTrace().engine);
        }

        [Fact]
        public void The_new_engine_is_the_default()
        {
            Assert.Equal(MatchingEngine.New, new Sub(null!).engine);
        }

        // ---- no setting ------------------------------------------------------------------------------------------------

        [Fact]
        public void There_is_no_persisted_setting_for_the_engine()
        {
            foreach (var file in new[] { "Properties/Settings.settings", "Properties/Settings.Designer.cs", "App.config" })
            {
                Assert.DoesNotContain("ModelMatchingEngine", File.ReadAllText(RepositoryFile(file)));
            }
        }

        // ---- the texts exist in every language -------------------------------------------------------------------------

        static readonly string[] Languages = ["", "de", "es", "fr", "it", "ko", "nl", "pt", "ru"];
        static readonly string[] Keys = ["Tip_ClassicMatching", "MatchExplain_EngineNew", "MatchExplain_EngineClassic"];

        static string TextOf(string language, string key)
        {
            var doc = XDocument.Load(RepositoryFile("Resources/strings" + (language.Length > 0 ? "." + language : "") + ".resx"));
            var data = doc.Root!.Elements("data").FirstOrDefault(e => (string?)e.Attribute("name") == key);
            return data?.Element("value")?.Value ?? "";
        }

        public static IEnumerable<object[]> LanguageAndKey() => Languages.SelectMany(l => Keys.Select(k => new object[] { l, k }));

        [Theory]
        [MemberData(nameof(LanguageAndKey))]
        public void Every_text_is_present_in_every_language(string language, string key)
        {
            Assert.False(string.IsNullOrWhiteSpace(TextOf(language, key)), $"strings{(language.Length > 0 ? "." + language : "")}.resx has no '{key}'");
        }

        [Theory]
        [MemberData(nameof(LanguageAndKey))]
        public void Translations_differ_from_the_English_text_and_name_the_parameter(string language, string key)
        {
            if (language.Length == 0) return;
            string text = TextOf(language, key);
            Assert.NotEqual(TextOf("", key), text);
            if (key != "Tip_ClassicMatching") Assert.Contains("--classicmatching", text);
        }

        [Fact]
        public void The_parameter_is_listed_in_the_help_and_the_options_dialog_and_handled()
        {
            Assert.Contains("case \"-classicmatching\":", File.ReadAllText(RepositoryFile("Program.cs")));
            Assert.Contains("--classicmatching", File.ReadAllText(RepositoryFile("Program.cs")));
            Assert.Contains("--classicmatching", File.ReadAllText(RepositoryFile("Forms/OptionsForm.cs")));
        }
    }
}
