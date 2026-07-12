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
                Substitution.MatchAttribute.Title => "Title",
                Substitution.MatchAttribute.Livery => "Livery / Variation",
                Substitution.MatchAttribute.IcaoType => "ICAO Type",
                Substitution.MatchAttribute.IcaoAirline => "ICAO Airline",
                Substitution.MatchAttribute.ClassCode => "Class Code",
                Substitution.MatchAttribute.Wtc => "WTC",
                Substitution.MatchAttribute.Typerole => "Typerole",
                Substitution.MatchAttribute.Folder => "Folder",
                _ => attribute.ToString()
            };
        }

        static string ModelSourceDescription()
        {
#if FS2024
            return "Model source (this build): SimConnect live aircraft/livery enumeration, requested via File ▸ Scan For Models ▸ Scan (or automatically at connect if \"Scan at launch\" is enabled).";
#elif XPLANE
            return "Model source (this build): X-Plane CSL definitions parsed from xsb_aircraft.txt during a folder scan (File ▸ Scan For Models ▸ Scan).";
#else
            return "Model source (this build): folder scan of aircraft.cfg/sim.cfg under the simulator's aircraft folders (File ▸ Scan For Models ▸ Scan).";
#endif
        }

        void RefreshWindow()
        {
            Substitution.MatchTrace trace = aircraft.subTrace;
            if (trace == null)
            {
                Label_Outcome.Text = "No match has been computed yet for this aircraft.";
                return;
            }

            // outcome headline
            string outcomeText = "Result: " + aircraft.subType;
            if (aircraft.subModel != null)
            {
                outcomeText += " match -> '" + aircraft.subModel.title + "'";
                if (aircraft.subModel.variation.Length > 0)
                {
                    outcomeText += " / '" + aircraft.subModel.variation + "'";
                }
            }
            else
            {
                outcomeText += " - no model could be chosen (nothing installed/scanned).";
            }
            Label_Outcome.Text = outcomeText;

            // ICAO-guessed warning
            if (aircraft.subModel != null && aircraft.subModel.icaoGuessed)
            {
                Label_IcaoGuessed.Text = "Note: the matched model's ICAO type designator was inferred by JoinFS from its title text, not confirmed via live simulator data. Verify this is correct if the match looks wrong.";
                Label_IcaoGuessed.Visible = true;
            }
            else
            {
                Label_IcaoGuessed.Visible = false;
            }

            // attribute comparison grid
            Grid_Attributes.Rows.Clear();
            foreach (var comparison in trace.attributes)
            {
                int index = Grid_Attributes.Rows.Add(AttributeLabel(comparison.attribute), comparison.requested, comparison.matched);
                if (comparison.decisive)
                {
                    Grid_Attributes.Rows[index].DefaultCellStyle.BackColor = Properties.Settings.Default.ColourActiveBackground;
                    Grid_Attributes.Rows[index].DefaultCellStyle.ForeColor = Properties.Settings.Default.ColourActiveText;
                }
            }

            // tier-by-tier trace
            Text_Trace.Text = string.Join(Environment.NewLine, trace.steps);

            // footer
            Label_Footer.Text = ModelSourceDescription();
        }

        string BuildMarkdownReport()
        {
            Substitution.MatchTrace trace = aircraft.subTrace;
            StringBuilder sb = new();

            sb.AppendLine("# Match Report - " + aircraft.flightPlan.callsign);
            sb.AppendLine();
            sb.AppendLine("**Outcome:** " + Label_Outcome.Text);
            if (Label_IcaoGuessed.Visible)
            {
                sb.AppendLine();
                sb.AppendLine("**Note:** " + Label_IcaoGuessed.Text);
            }
            sb.AppendLine();

            sb.AppendLine("## Attribute comparison");
            sb.AppendLine();
            sb.AppendLine("| Attribute | Requested | Matched Model | Decisive |");
            sb.AppendLine("|---|---|---|---|");
            if (trace != null)
            {
                foreach (var comparison in trace.attributes)
                {
                    string requested = comparison.requested.Length > 0 ? comparison.requested : "-";
                    string matched = comparison.matched.Length > 0 ? comparison.matched : "-";
                    string label = AttributeLabel(comparison.attribute);
                    if (comparison.decisive)
                    {
                        label = "**" + label + "**";
                    }
                    sb.AppendLine($"| {label} | {requested} | {matched} | {(comparison.decisive ? "**Yes**" : "No")} |");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Matching steps");
            sb.AppendLine();
            if (trace != null)
            {
                int step = 1;
                foreach (var line in trace.steps)
                {
                    sb.AppendLine($"{step}. {line}");
                    step++;
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Model source");
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
                MessageBox.Show("No known-models file was found yet. Run File ▸ Scan For Models ▸ Scan first.", Main.Name + ": Explain Match");
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filename) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open '" + filename + "': " + ex.Message, Main.Name + ": Explain Match");
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
                MessageBox.Show("Could not create debug bundle: " + ex.Message, Main.Name + ": Explain Match");
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
