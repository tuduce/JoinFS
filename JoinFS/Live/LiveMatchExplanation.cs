using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using JoinFS.UI.Models;

namespace JoinFS.Live
{
    /// <summary>
    /// How Substitution.Match() (or Masquerade()) chose the model of an aircraft, as the new UI shows it. The words and the report are
    /// those of the old MatchExplainForm, which this replaces: a requested-against-matched table, the tier-by-tier trace, the other
    /// candidates, and a Markdown report of all of it.
    /// </summary>
    static class LiveMatchExplanation
    {
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

        static string ModelSourceDescription(Main main)
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
        /// Explains a corrected ICAO type designator, if any: a config-confirmed icao_type_designator that was not a recognised designator
        /// (wrong or blank), which JoinFS had to find another way (icao_model, a title guess, or a guess corroborated by class code and WTC).
        /// Empty when nothing needed correcting.
        /// </summary>
        /// <summary>Which matching engine produced the trace, in the user's language; empty when none was involved (as the old dialog says it)</summary>
        internal static string EngineDescription(Substitution.MatchTrace trace)
        {
            return trace?.engine switch
            {
                MatchingEngine.New => Resources.Strings.MatchExplain_EngineNew,
                MatchingEngine.Classic => Resources.Strings.MatchExplain_EngineClassic,
                _ => ""
            };
        }

        static string IcaoResolutionExplanation(Substitution.Model matched)
        {
            string note = matched?.icaoResolutionNote ?? "";
            if (note.Length == 0)
            {
                return "";
            }

            int colon = note.IndexOf(':');
            string reason = colon >= 0 ? note[..colon] : note;
            string declaredValue = colon >= 0 ? note[(colon + 1)..] : "";
            string resolvedValue = matched.icaoType;

            // "Blank" variants mean icao_type_designator was entirely absent: those messages take only the resolved value
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

        public static MatchExplanation Build(Main main, Sim.Aircraft aircraft, Substitution.Model matched, Substitution.Type matchType, Substitution.MatchTrace trace)
        {
            string callsign = aircraft.flightPlan.callsign;
            string source = ModelSourceDescription(main);

            if (trace == null)
            {
                string none = Resources.Strings.MatchExplain_NoMatchYet;
                return new MatchExplanation(callsign, none, null, [], [], source, BuildReport(callsign, none, null, null, null, matched, source));
            }

            // outcome headline
            string outcome = string.Format(Resources.Strings.MatchExplain_ResultPrefix, matchType);
            if (matched != null)
            {
                outcome += string.Format(Resources.Strings.MatchExplain_ResultMatchSuffix, matched.title);
                if (matched.variation.Length > 0)
                {
                    outcome += " / '" + matched.variation + "'";
                }
            }
            else
            {
                outcome += Resources.Strings.MatchExplain_ResultNoModel;
            }

            // a resolution note explains specifically what happened, so it comes before the generic "guessed" warning
            string note = IcaoResolutionExplanation(matched);
            if (note.Length == 0 && matched != null && matched.icaoGuessed)
            {
                note = Resources.Strings.MatchExplain_IcaoGuessedWarning;
            }

            // the matched-value cell gets a "(+N)" score suffix (and "guessed" when downweighted) for an attribute that scored
            List<ExplainRow> rows = [];
            foreach (var comparison in trace.attributes)
            {
                string matchedDisplay = comparison.matched;
                if (comparison.scoreContribution > 0)
                {
                    matchedDisplay += " (+" + comparison.scoreContribution + (comparison.wasDownweighted ? ", guessed" : "") + ")";
                }
                rows.Add(new ExplainRow(AttributeLabel(comparison.attribute), comparison.requested, matchedDisplay, comparison.decisive));
            }

            // tier-by-tier trace, then the other candidates
            List<string> steps = new(trace.steps);
            string engineText = EngineDescription(trace);
            if (engineText.Length > 0)
            {
                steps.Insert(0, "");
                steps.Insert(0, engineText);
            }
            if (matched != null && matched.classCodeConfirmed)
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

            return new MatchExplanation(callsign, outcome, note.Length > 0 ? note : null, rows, steps, source,
                BuildReport(callsign, outcome, note, trace, rows, matched, source));
        }

        static string BuildReport(string callsign, string outcome, string note, Substitution.MatchTrace trace, List<ExplainRow> rows, Substitution.Model matched, string source)
        {
            StringBuilder sb = new();

            sb.AppendLine("# Match Report - " + callsign);
            sb.AppendLine();
            sb.AppendLine("**" + Resources.Strings.MatchExplain_ReportOutcome + "** " + outcome);
            if (!string.IsNullOrEmpty(note))
            {
                sb.AppendLine();
                sb.AppendLine("**" + Resources.Strings.MatchExplain_ReportNote + "** " + note);
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
                    string matchedText = comparison.matched.Length > 0 ? comparison.matched : "-";
                    string label = AttributeLabel(comparison.attribute);
                    string score = comparison.scoreContribution > 0 ? "+" + comparison.scoreContribution + (comparison.wasDownweighted ? " (guessed)" : "") : "-";
                    if (comparison.decisive)
                    {
                        label = "**" + label + "**";
                    }
                    sb.AppendLine($"| {label} | {requested} | {matchedText} | {score} | {(comparison.decisive ? "**Yes**" : "No")} |");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## " + Resources.Strings.MatchExplain_ReportStepsHeader);
            sb.AppendLine();
            string engineSentence = EngineDescription(trace);
            if (engineSentence.Length > 0)
            {
                sb.AppendLine(engineSentence);
                sb.AppendLine();
            }
            if (trace != null)
            {
                int step = 1;
                foreach (var line in trace.steps)
                {
                    sb.AppendLine($"{step}. {line}");
                    step++;
                }
                if (matched != null && matched.classCodeConfirmed)
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
            sb.AppendLine(source);

            return sb.ToString();
        }

        /// <summary>
        /// A zip with the report and the files it was made from: the models, the matching and the masquerading
        /// </summary>
        public static void WriteBundle(Main main, string zipPath, string report)
        {
            using Stream stream = File.Create(zipPath);
            using ZipArchive archive = new(stream, ZipArchiveMode.Create);

            // human-readable report
            var reportEntry = archive.CreateEntry("match-report.md");
            using (StreamWriter writer = new(reportEntry.Open()))
            {
                writer.Write(report);
            }

            // supporting model/override data behind the report
            AddFileIfExists(archive, main.substitution?.MakeModelsFilename());
            AddFileIfExists(archive, main.substitution?.MakeMatchingFilename());
            AddFileIfExists(archive, main.substitution?.MakeMasqueradingFilename());
        }

        static void AddFileIfExists(ZipArchive archive, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            archive.CreateEntryFromFile(path, Path.GetFileName(path));
        }
    }
}
