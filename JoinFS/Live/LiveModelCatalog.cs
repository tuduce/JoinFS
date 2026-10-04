using System.Collections.Generic;
using System.Threading.Tasks;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// The models of the simulator and how one stands in for another, from <c>main.substitution</c>: its match table is the Model
    /// Matching tab, its model list is what the Substitute picker chooses from. Changes go through the same operations the old dialogs
    /// used, so the files are saved and the aircraft that use the model are replaced as before.
    /// </summary>
    class LiveModelCatalog : IModelCatalog
    {
        const string SEPARATOR = " [+] ";

        readonly Main main;

        public LiveModelCatalog(Main main)
        {
            this.main = main;
        }

        public bool HasModels => main.substitution != null && main.substitution.models.Count > 0;

        public string ScanStatus
        {
            get
            {
                Substitution substitution = main.substitution;
                if (substitution == null)
                {
                    return "";
                }
                if (substitution.ScanRunning)
                {
                    return "Scanning for models...";
                }
                int count = substitution.models.Count;
                return count == 0
                    ? "No models are known yet. Scan for models from Settings, Simulator."
                    : count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + (count == 1 ? " model known." : " models known.");
            }
        }

        /// <summary>
        /// The model as the old list showed its substitute: the title, and in FS2024 the livery after it
        /// </summary>
        static string Describe(Substitution.Model model)
        {
#if FS2024
            return model.variation.Length > 0 ? model.title + SEPARATOR + model.variation : model.title;
#else
            return model.title;
#endif
        }

        public IReadOnlyList<ModelRule> GetRules()
        {
            Substitution substitution = main.substitution;
            if (substitution == null)
            {
                return [];
            }

            List<ModelRule> rules = [];
            lock (main.conch)
            {
                // the matches, as the old list ordered them: each default first, then the others by name
                Dictionary<string, Substitution.Model> matches = substitution.matches;
                HashSet<string> defaults = [.. substitution.defaultModels.Values];

                foreach (string original in substitution.defaultModels.Values)
                {
                    if (matches.TryGetValue(original, out Substitution.Model match))
                    {
                        rules.Add(new ModelRule(original, Describe(match), IsDefault: true));
                    }
                }

                List<string> others = [];
                foreach (string original in matches.Keys)
                {
                    if (!defaults.Contains(original))
                    {
                        others.Add(original);
                    }
                }
                others.Sort();
                foreach (string original in others)
                {
                    rules.Add(new ModelRule(original, Describe(matches[original]), IsDefault: false));
                }
            }
            return rules;
        }

        public IReadOnlyList<string> GetTypes(string filter) =>
            main.substitution?.TypesMatching(filter) ?? [];

        public IReadOnlyList<string> GetVariations(string type) =>
            main.substitution?.VariationsOf(type) ?? [];

        public string GetReplacement(string type, string variation)
        {
            Substitution.Model model = main.substitution?.FindModel(type, variation);
            return model == null ? "" : Describe(model);
        }

        public string GetTitle(string type, string variation) =>
            main.substitution?.FindModel(type, variation)?.title ?? "";

        public ModelChoice FindChoice(string title)
        {
            Substitution.Model model = main.substitution?.GetModel(title);
            return model == null ? null : new ModelChoice(model.type, model.variation);
        }

        public async Task<ModelChoice> GetCurrentAsync(ModelTarget target)
        {
            Substitution substitution = main.substitution;
            if (substitution == null)
            {
                return null;
            }

            // as the old dialog did: run the matching for the model, so the picker starts on what would stand in for it now
            int typerole = target.TypeRole != 0 ? target.TypeRole : substitution.GetTypeRole(target.Model);
#if FS2024
            string livery = target.Livery;
            if (livery.Length == 0 && substitution.matches.TryGetValue(target.Model, out Substitution.Model current))
            {
                livery = current.variation;
            }
            (Substitution.Model model, _, _) = await substitution.Match(target.Model, livery, "", "", "", "", false, typerole);
#else
            (Substitution.Model model, _, _) = await substitution.Match(target.Model, "", "", "", "", false, typerole);
#endif
            return model == null ? null : new ModelChoice(model.type, model.variation);
        }

        public void SetSubstitute(ModelTarget target, string type, string variation)
        {
            Substitution substitution = main.substitution;
            Substitution.Model model = substitution?.FindModel(type, variation);
            if (model == null)
            {
                return;
            }

            if (target.IsMasquerade)
            {
                substitution.SetMasquerade(target.Model, model);
            }
            else
            {
                substitution.SetMatch(target.Model, model);
            }
        }

        public void ClearSubstitute(ModelTarget target)
        {
            Substitution substitution = main.substitution;
            if (substitution == null)
            {
                return;
            }

            if (target.IsMasquerade)
            {
                substitution.ClearMasquerade(target.Model);
            }
            else
            {
                substitution.ClearMatch(target.Model);
            }
        }

        public string KnownModelsFile()
        {
            string filename = main.substitution?.MakeModelsFilename();
            return !string.IsNullOrEmpty(filename) && System.IO.File.Exists(filename) ? filename : null;
        }

        public void WriteDebugBundle(string zipPath, string report) => LiveMatchExplanation.WriteBundle(main, zipPath, report);
    }
}
