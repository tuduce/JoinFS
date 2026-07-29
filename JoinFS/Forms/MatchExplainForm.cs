using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;

namespace JoinFS
{
    /// <summary>
    /// Explains how Substitution.Match() (or Masquerade()) chose the model currently
    /// rendered for a specific aircraft - a requested-vs-matched attribute comparison
    /// plus the tier-by-tier trace recorded at match time.
    /// </summary>
    public partial class MatchExplainForm : Form
    {
        readonly Main main;
        readonly Sim.Aircraft aircraft;

        public MatchExplainForm(Main main, Sim.Aircraft aircraft)
        {
            this.main = main;
            this.aircraft = aircraft;

            InitializeComponent();

            // change icon
            Icon = main.icon;
            // remove JoinFS from title, add callsign
            Text = Text.Replace("JoinFS: ", "") + ": " + aircraft.flightPlan.callsign;

            RefreshWindow();
        }

        static string AttributeLabel(Substitution.MatchAttribute attribute)
        {
            return attribute switch
            {
                Substitution.MatchAttribute.Title => Resources.Strings.MatchAttr_Title,
                Substitution.MatchAttribute.Livery => Resources.Strings.MatchAttr_Livery,
                Substitution.MatchAttribute.Registration => Resources.Strings.MatchAttr_Registration,
                Substitution.MatchAttribute.IcaoType => Resources.Strings.MatchAttr_IcaoType,
                Substitution.MatchAttribute.IcaoAirline => Resources.Strings.MatchAttr_IcaoAirline,
                Substitution.MatchAttribute.ClassCode => Resources.Strings.MatchAttr_ClassCode,
                Substitution.MatchAttribute.Wtc => Resources.Strings.MatchAttr_Wtc,
                Substitution.MatchAttribute.Typerole => Resources.Strings.MatchAttr_Typerole,
                Substitution.MatchAttribute.Folder => Resources.Strings.MatchAttr_Folder,
                _ => attribute.ToString()
            };
        }

        string ModelSourceDescription()
        {
            string description;
#if FS2024
            description = Resources.Strings.MatchExplain_ModelSource_FS2024;
#elif XPLANE
            description = Resources.Strings.MatchExplain_ModelSource_XPlane;
#else
            description = Resources.Strings.MatchExplain_ModelSource_Other;
#endif
            int banned = main.substitution?.lastBanExclusionCount ?? 0;
            if (banned > 0)
            {
                description += string.Format(Resources.Strings.MatchExplain_BanExclusionNote, banned);
            }
            return description;
        }

        /// <summary>
        /// Explains a corrected ICAO type designator, if any - see Model.icaoResolutionNote, set when a
        /// config-confirmed icao_type_designator wasn't a recognized Doc8643 designator (wrong or entirely
        /// blank) and JoinFS had to find the real one another way (icao_model, a title guess, or a title
        /// guess corroborated by classCode/WTC - see ResolveConfirmedIcaoType). Returns "" when nothing
        /// needed correcting.
        /// </summary>
        string IcaoResolutionExplanation()
        {
            string note = aircraft.subModel?.icaoResolutionNote ?? "";
            if (note.Length == 0) return "";

            int colon = note.IndexOf(':');
            string reason = colon >= 0 ? note[..colon] : note;
            string declaredValue = colon >= 0 ? note[(colon + 1)..] : "";
            string resolvedValue = aircraft.subModel.icaoType;

            // "Blank" variants mean icao_type_designator wasn't merely wrong but entirely absent - those
            // messages take only the resolved value, since there's no invalid declared value to reference
            return reason switch
            {
                "IcaoModelFallback" => string.Format(Resources.Strings.MatchExplain_IcaoResolution_IcaoModelFallback, declaredValue, resolvedValue),
                "IcaoModelOnly" => string.Format(Resources.Strings.MatchExplain_IcaoResolution_IcaoModelOnly, resolvedValue),
                "TitleGuessCorroborated" => string.Format(Resources.Strings.MatchExplain_IcaoResolution_TitleGuessCorroborated, declaredValue, resolvedValue),
                "TitleGuessCorroboratedBlank" => string.Format(Resources.Strings.MatchExplain_IcaoResolution_TitleGuessCorroboratedBlank, resolvedValue),
                "TitleGuess" => string.Format(Resources.Strings.MatchExplain_IcaoResolution_TitleGuess, declaredValue, resolvedValue),
                "TitleGuessBlank" => string.Format(Resources.Strings.MatchExplain_IcaoResolution_TitleGuessBlank, resolvedValue),
                "Unresolved" => string.Format(Resources.Strings.MatchExplain_IcaoResolution_Unresolved, declaredValue, resolvedValue),
                _ => ""
            };
        }

        void RefreshWindow()
        {
            Substitution.MatchTrace trace = aircraft.subTrace;
            if (trace == null)
            {
                Label_Outcome.Text = Resources.Strings.MatchExplain_NoMatchYet;
                return;
            }

            // outcome headline
            string outcomeText = string.Format(Resources.Strings.MatchExplain_ResultPrefix, aircraft.subType);
            if (aircraft.subModel != null)
            {
                outcomeText += string.Format(Resources.Strings.MatchExplain_ResultMatchSuffix, aircraft.subModel.title);
                if (aircraft.subModel.variation.Length > 0)
                {
                    outcomeText += " / '" + aircraft.subModel.variation + "'";
                }
            }
            else
            {
                outcomeText += Resources.Strings.MatchExplain_ResultNoModel;
            }
            Label_Outcome.Text = outcomeText;

            // ICAO-guessed / ICAO-corrected warning - a resolution note (see Model.icaoResolutionNote)
            // takes priority since it explains specifically what happened; falls back to the generic
            // guessed warning for the older upfront-scan title guess path, which doesn't track a note
            string resolutionText = IcaoResolutionExplanation();
            if (resolutionText.Length > 0)
            {
                Label_IcaoGuessed.Text = resolutionText;
                Label_IcaoGuessed.Visible = true;
            }
            else if (aircraft.subModel != null && aircraft.subModel.icaoGuessed)
            {
                Label_IcaoGuessed.Text = Resources.Strings.MatchExplain_IcaoGuessedWarning;
                Label_IcaoGuessed.Visible = true;
            }
            else
            {
                Label_IcaoGuessed.Visible = false;
            }

            // attribute comparison grid - the matched-value cell gets a "(+N)" score-contribution
            // suffix (and a "guessed" note when downweighted) for any attribute that actually
            // contributed to the winning candidate's score, so the scoring reasoning is visible
            // right next to the value it's about, not just in the trace text below
            Grid_Attributes.Rows.Clear();
            foreach (var comparison in trace.attributes)
            {
                string matchedDisplay = comparison.matched;
                if (comparison.scoreContribution > 0)
                {
                    matchedDisplay += " (+" + comparison.scoreContribution + (comparison.wasDownweighted ? ", guessed" : "") + ")";
                }
                int index = Grid_Attributes.Rows.Add(AttributeLabel(comparison.attribute), comparison.requested, matchedDisplay);
                if (comparison.decisive)
                {
                    Grid_Attributes.Rows[index].DefaultCellStyle.BackColor = Properties.Settings.Default.ColourActiveBackground;
                    Grid_Attributes.Rows[index].DefaultCellStyle.ForeColor = Properties.Settings.Default.ColourActiveText;
                }
            }

            // tier-by-tier trace
            List<string> steps = new(trace.steps);
            if (aircraft.subModel != null && aircraft.subModel.classCodeConfirmed)
            {
                steps.Add(Resources.Strings.MatchExplain_ClassCodeConfirmedNote);
            }
            if (trace.topCandidates.Count > 1)
            {
                steps.Add("");
                steps.Add(string.Format(Resources.Strings.MatchExplain_OtherCandidatesHeader, trace.topCandidates.Count));
                foreach (var candidate in trace.topCandidates)
                {
                    string label = "'" + candidate.title + "'" + (candidate.variation.Length > 0 ? " / '" + candidate.variation + "'" : "");
                    string why = candidate.contributions.Count > 0 ? string.Join(" + ", candidate.contributions) : "no positive signals";
                    steps.Add("  " + candidate.totalScore + " pts - " + label + " - " + why);
                }
            }
            Text_Trace.Text = string.Join(Environment.NewLine, steps);

            // footer
            Label_Footer.Text = ModelSourceDescription();
        }

        string BuildMarkdownReport()
        {
            Substitution.MatchTrace trace = aircraft.subTrace;
            StringBuilder sb = new();

            sb.AppendLine("# Match Report - " + aircraft.flightPlan.callsign);
            sb.AppendLine();
            sb.AppendLine("**" + Resources.Strings.MatchExplain_ReportOutcome + "** " + Label_Outcome.Text);
            if (Label_IcaoGuessed.Visible)
            {
                sb.AppendLine();
                sb.AppendLine("**" + Resources.Strings.MatchExplain_ReportNote + "** " + Label_IcaoGuessed.Text);
            }
            sb.AppendLine();

            sb.AppendLine("## " + Resources.Strings.MatchExplain_ReportAttrHeader);
            sb.AppendLine();
            sb.AppendLine("| Attribute | Requested | Matched Model | Score | Decisive |");
            sb.AppendLine("|---|---|---|---|---|");
            if (trace != null)
            {
                foreach (var comparison in trace.attributes)
                {
                    string requested = comparison.requested.Length > 0 ? comparison.requested : "-";
                    string matched = comparison.matched.Length > 0 ? comparison.matched : "-";
                    string label = AttributeLabel(comparison.attribute);
                    string score = comparison.scoreContribution > 0 ? "+" + comparison.scoreContribution + (comparison.wasDownweighted ? " (guessed)" : "") : "-";
                    if (comparison.decisive)
                    {
                        label = "**" + label + "**";
                    }
                    sb.AppendLine($"| {label} | {requested} | {matched} | {score} | {(comparison.decisive ? "**Yes**" : "No")} |");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## " + Resources.Strings.MatchExplain_ReportStepsHeader);
            sb.AppendLine();
            if (trace != null)
            {
                int step = 1;
                foreach (var line in trace.steps)
                {
                    sb.AppendLine($"{step}. {line}");
                    step++;
                }
                if (aircraft.subModel != null && aircraft.subModel.classCodeConfirmed)
                {
                    sb.AppendLine($"{step}. {Resources.Strings.MatchExplain_ClassCodeConfirmedNote}");
                }
            }
            sb.AppendLine();

            if (trace != null && trace.topCandidates.Count > 1)
            {
                sb.AppendLine("## " + Resources.Strings.MatchExplain_ReportOtherCandidatesHeader);
                sb.AppendLine();
                sb.AppendLine("| Score | Title | Variation | Why |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var candidate in trace.topCandidates)
                {
                    string why = candidate.contributions.Count > 0 ? string.Join(" + ", candidate.contributions) : "no positive signals";
                    sb.AppendLine($"| {candidate.totalScore} | {candidate.title} | {candidate.variation} | {why} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("## " + Resources.Strings.MatchExplain_ReportSourceHeader);
            sb.AppendLine();
            sb.AppendLine(ModelSourceDescription());

            return sb.ToString();
        }

        void Button_Copy_Click(object sender, EventArgs e)
        {
            Clipboard.SetText(BuildMarkdownReport());
        }

        void Button_OpenModelsList_Click(object sender, EventArgs e)
        {
            string filename = main.substitution?.MakeModelsFilename();
            if (string.IsNullOrEmpty(filename) || File.Exists(filename) == false)
            {
                MessageBox.Show(Resources.Strings.MatchExplain_NoModelsFile, Main.Name + ": Explain Match");
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filename) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Resources.Strings.MatchExplain_CouldNotOpen, filename, ex.Message), Main.Name + ": Explain Match");
            }
        }

        void Button_ExportBundle_Click(object sender, EventArgs e)
        {
            SaveFileDialog dialog = new()
            {
                Filter = "Zip files (*.zip)|*.zip",
                FilterIndex = 1,
                RestoreDirectory = true,
                FileName = "JoinFS-MatchDebug-" + aircraft.flightPlan.callsign + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip"
            };

            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            try
            {
                using Stream stream = dialog.OpenFile();
                using ZipArchive archive = new(stream, ZipArchiveMode.Create);

                // human-readable report
                var reportEntry = archive.CreateEntry("match-report.md");
                using (StreamWriter writer = new(reportEntry.Open()))
                {
                    writer.Write(BuildMarkdownReport());
                }

                // supporting model/override data behind the report
                AddFileIfExists(archive, main.substitution?.MakeModelsFilename());
                AddFileIfExists(archive, main.substitution?.MakeMatchingFilename());
                AddFileIfExists(archive, main.substitution?.MakeMasqueradingFilename());
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Resources.Strings.MatchExplain_CouldNotCreateBundle, ex.Message), Main.Name + ": Explain Match");
            }
        }

        static void AddFileIfExists(ZipArchive archive, string path)
        {
            if (string.IsNullOrEmpty(path) || File.Exists(path) == false)
            {
                return;
            }

            archive.CreateEntryFromFile(path, Path.GetFileName(path));
        }

        void Button_Close_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
