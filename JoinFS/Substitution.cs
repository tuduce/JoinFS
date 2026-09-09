using System;
using System.Collections.Generic;
using System.Linq;
#if !CONSOLE
using System.Windows.Forms;
#endif
using System.IO;
using System.Globalization;
using JoinFS.Properties;
using System.Threading.Tasks;

namespace JoinFS
{
    public class Substitution
    {
        const string MODELS_FILE = "models.txt";
        const string MATCHING_FILE = "matching.txt";
        const string MASQUERADING_FILE = "masquerading.txt";

        /// <summary>
        /// folders
        /// </summary>
        public string simFolder = "";
        string initialScanFolders = "";
        string initialAddOns = "";
        string initialAdditionals = "";
        public static string[] AddonsFileContents = [ "" ];

        /// <summary>
        /// Reference to the main form
        /// </summary>
        readonly Main main;

        /// <summary>
        /// Constructor
        /// </summary>
        public Substitution(Main main)
        {
            // set main form
            this.main = main;

            // clear default models
            defaultModels.Clear();
            // create default model names
            foreach (var name in typeroleNames)
            {
                // add default model
                defaultModels.Add(name.Key, Resources.Strings.Default + " " + name.Value);
            }

// TODO: cleanup code
//            enrichModelService = new EnrichModelService(
//                jsonlFilePath: main.storagePath + Path.DirectorySeparatorChar + "model-data.jsonl",
//                httpClient: null,
//                main: main);
//#if X64
//            embeddingService = new EmbeddingService(
//                modelPath: "AIModel" + Path.DirectorySeparatorChar + "model.onnx",
//                vocabPath: "AIModel" + Path.DirectorySeparatorChar + "vocab.txt",
//                main: main // Pass main for logging
//            );
//#endif
        }

        /// <summary>
        /// Type role for a model
        /// </summary>
        public const int TypeRole_SingleProp    = 1;
        public const int TypeRole_TwinProp      = 2;
        public const int TypeRole_Airliner      = 3;
        public const int TypeRole_Rotorcraft    = 4;
        public const int TypeRole_Glider        = 5;
        public const int TypeRole_Fighter       = 6;
        public const int TypeRole_Bomber        = 7;
        public const int TypeRole_FourProp      = 8;
        public const int TypeRole_Airship       = 9;
        public const int TypeRole_Balloon       = 10;

        /// <summary>
        /// type roles names
        /// </summary>
        public readonly Dictionary<int, string> typeroleNames = new()
        {
            { TypeRole_SingleProp,      Resources.Strings.SingleProp },
            { TypeRole_TwinProp,        Resources.Strings.TwinProp },
            { TypeRole_Airliner,        Resources.Strings.Airliner },
            { TypeRole_Rotorcraft,      Resources.Strings.Rotorcraft },
            { TypeRole_Glider,          Resources.Strings.Glider },
            { TypeRole_Fighter,         Resources.Strings.Fighter },
            { TypeRole_Bomber,          Resources.Strings.Bomber },
            { TypeRole_FourProp,        Resources.Strings.FourProp },
            { TypeRole_Airship,         Resources.Strings.Airship },
            { TypeRole_Balloon,         Resources.Strings.Balloon },
        };

#if FS2024
        readonly Dictionary<string, (string typeRoleName, string compareType)> typeroleClassifier = [];
#endif
        readonly List<string> modelBanList = [];

        /// <summary>
        /// Count of models excluded by the ban list during the most recent scan/load pass - surfaced in
        /// Explain Match's model-source footer so the effect of bannedModels.txt is visible, not just logged.
        /// </summary>
        public int lastBanExclusionCount = 0;

        /// <summary>
        /// Convert a string to a typerole
        /// </summary>
        /// <param name="typerole"></param>
        /// <returns></returns>
        static int TyperoleFromString(string typerole)
        {
            // check for type role
            if (typerole.Contains("Single", StringComparison.CurrentCulture) && typerole.Contains("Prop", StringComparison.CurrentCulture))
            {
                // Single Prop
                return TypeRole_SingleProp;
            }
            else if (typerole.Contains("Twin", StringComparison.CurrentCulture) && typerole.Contains("Prop", StringComparison.CurrentCulture))
            {
                // Twin Prop
                return TypeRole_TwinProp;
            }
            else if (typerole.Contains("Four", StringComparison.CurrentCulture) && typerole.Contains("Prop", StringComparison.CurrentCulture))
            {
                // Four Prop
                return TypeRole_FourProp;
            }
            else if (typerole.Contains("Regional", StringComparison.CurrentCulture) || typerole.Contains("Airliner", StringComparison.CurrentCulture))
            {
                // Airliner
                return TypeRole_Airliner;
            }
            else if (typerole.Contains("Rotorcraft", StringComparison.CurrentCulture))
            {
                // Rotorcraft
                return TypeRole_Rotorcraft;
            }
            else if (typerole.Contains("Glider", StringComparison.CurrentCulture))
            {
                // Glider
                return TypeRole_Glider;
            }
            else if (typerole.Contains("Balloon", StringComparison.CurrentCulture) || typerole.Contains("Zeppelin", StringComparison.CurrentCulture))
            {
                // Balloon
                return TypeRole_Balloon;
            }
            else if (typerole.Contains("Airship", StringComparison.CurrentCulture) || typerole.Contains("Blimp", StringComparison.CurrentCulture))
            {
                // Airship
                return TypeRole_Airship;
            }
            else if (typerole.Contains("Fighter", StringComparison.CurrentCulture) || typerole.Contains("Jet", StringComparison.CurrentCulture))
            {
                // Fighter
                return TypeRole_Fighter;
            }
            else if (typerole.Contains("Bomber", StringComparison.CurrentCulture) || 
                    typerole.Contains("Airliner", StringComparison.CurrentCulture) || 
                    typerole.Contains("Four Engine", StringComparison.CurrentCulture))
            {
                // Bomber
                return TypeRole_Bomber;
            }
            else
            {
                // default to SingleProp
                return TypeRole_SingleProp;
            }
        }

        /// <summary>
        /// Derive a typerole from an ICAO type designator and its Doc8643 classification code.
        /// Returns 0 when no reliable classification can be made (caller should keep any existing typerole).
        /// </summary>
        static int TyperoleFromIcao(string icaoType, string classCode, string wtc)
        {
            // full official ICAO special-designator set, checked first since it bypasses the (possibly stale) bundled Doc8643 rows
            switch (icaoType)
            {
                case "SHIP": return TypeRole_Airship;
                case "BALL": return TypeRole_Balloon;
                case "GLID":
                case "GLIM": return TypeRole_Glider;
                case "GYRO":
                case "UHEL": return TypeRole_Rotorcraft;
                case "ULAC": return TypeRole_SingleProp;
                    // PARA, FFLO, VFHC, ZZZZ and anything else fall through - no clean existing typerole fits
            }

            // classification code driven typerole, e.g. "H2T", "L2P", "L2J"
            if (classCode.Length != 3)
            {
                return 0;
            }

            char platform = classCode[0];
            char engineType = classCode[2];

            if (platform is 'H' or 'G')
            {
                // helicopter or gyrocopter
                return TypeRole_Rotorcraft;
            }
            else if (classCode is "L1P" or "S1P" or "A1P")
            {
                return TypeRole_SingleProp;
            }
            else if (classCode is "L2P" or "S2P" or "A2P")
            {
                return TypeRole_TwinProp;
            }
            else if (classCode is "L4P" or "L4T")
            {
                return TypeRole_FourProp;
            }
            else if (platform == 'L' && engineType == 'J' && wtc is "M" or "H" or "J")
            {
                return TypeRole_Airliner;
            }

            // no reliable classification (includes Fighter/Bomber, which Doc8643 codes can't distinguish)
            return 0;
        }

        /// <summary>
        /// Best-effort Doc8643-style classCode (e.g. "L2J", "H1T") derived directly from live SimConnect
        /// data - CATEGORY, ENGINE TYPE and NUMBER OF ENGINES - for an aircraft that has actually been
        /// instantiated locally. Unlike the bundled Doc8643-by-icaoType lookup (originally sourced for
        /// X-Plane's CSL matching), this doesn't depend on icaoType being a recognized/correct designator,
        /// so it also correctly classifies add-ons that report a bogus or non-standard ATC MODEL string.
        /// Leaves classCode/wtc empty when the category/engine data can't be classified reliably.
        /// </summary>
        public static void DeriveLiveClassCode(string category, int engineType, int numEngines, out string classCode, out string wtc)
        {
            classCode = "";
            wtc = "";

            char platform;
            if (category == "Airplane") platform = 'L';
            else if (category == "Helicopter") platform = 'H';
            else return; // boats/ground vehicles/other categories aren't Doc8643-classified

            // SimConnect "ENGINE TYPE": 0=Piston, 1=Jet, 2=None, 3=Helo(turbine), 4=Unsupported, 5=Turboprop
            char engine = engineType switch
            {
                0 => 'P',
                1 => 'J',
                3 => 'T',
                5 => 'T',
                _ => '\0'
            };
            if (engine == '\0' || numEngines < 1 || numEngines > 9) return;

            classCode = "" + platform + numEngines + engine;

            // a jet landplane is at least "Medium" wake turbulence in practice - enough to satisfy
            // TyperoleFromIcao's Airliner gate without a live weight-based WTC source
            if (platform == 'L' && engine == 'J')
            {
                wtc = "M";
            }
        }

        /// <summary>
        /// Same platform+engine-count+engine-type -&gt; classCode logic as DeriveLiveClassCode, but reading
        /// the textual Category/icao_engine_type/icao_engine_count values straight out of an aircraft.cfg
        /// [GENERAL] section (per the MSFS SDK's documented allowed values) instead of the numeric live
        /// SimConnect ENGINE TYPE simvar. Used to corroborate/guess an ICAO type designator when
        /// icao_type_designator itself can't be trusted - see ResolveConfirmedIcaoType.
        /// </summary>
        static void DeriveClassCodeFromConfig(string category, string engineTypeText, int engineCount, out string classCode, out string wtc)
        {
            classCode = "";
            wtc = "";

            char platform;
            if (category.Equals("Airplane", StringComparison.OrdinalIgnoreCase)) platform = 'L';
            else if (category.Equals("Helicopter", StringComparison.OrdinalIgnoreCase)) platform = 'H';
            else return;

            char engine = engineTypeText.Trim().ToUpperInvariant() switch
            {
                "PISTON" => 'P',
                "JET" => 'J',
                "TURBOPROP/TURBOSHAFT" => 'T',
                _ => '\0'
            };
            if (engine == '\0' || engineCount < 1 || engineCount > 9) return;

            classCode = "" + platform + engineCount + engine;

            if (platform == 'L' && engine == 'J')
            {
                wtc = "M";
            }
        }

        /// <summary>
        /// type roles names
        /// </summary>
        public readonly Dictionary<int, string> defaultModels = [];

        /// <summary>
        /// Fine-grained defaults, one per (typerole, classCode, wtc) combination that actually has 2+
        /// installed candidates to distinguish between (a single candidate needs no configured default -
        /// the scorer already finds it unambiguously via classCode/WTC-exact signals). Auto-seeded by
        /// ChooseDefaults(); consulted by Match() ahead of the coarse per-typerole default in
        /// defaultModels above. Value is a synthetic key into the same `matches` dictionary as every
        /// other override/default, so matching.txt's format needs no migration.
        /// </summary>
        public readonly Dictionary<(int typerole, string classCode, string wtc), string> fineDefaultModels = [];

        /// <summary>
        /// A model entry
        /// </summary>
        public class Model
        {
            public string title;
            public string manufacturer;
            public string type;
            public string longType;
            public string variation;
            public int index;
            public string folder;
            public int typerole;
            public int smokeCount;
            // TODO: cleanup code
            // public EnrichedAircraftData enrichedData = null;
            public float[] embedding = null;

            /// <summary>
            /// ICAO Doc8643 type designator, e.g. "EC45"/"A20N"
            /// </summary>
            public string icaoType = "";
            /// <summary>
            /// ICAO Doc8643 classification code, e.g. "H2T" - re-derived from icaoType via the bundled
            /// Doc8643 lookup, unless classCodeConfirmed is set (see below)
            /// </summary>
            public string classCode = "";
            /// <summary>
            /// Wake turbulence category, e.g. "L"
            /// </summary>
            public string wtc = "";
            /// <summary>
            /// ICAO airline operator code, e.g. "AEE" - often empty for non-airline aircraft
            /// </summary>
            public string icaoAirline = "";
            /// <summary>
            /// Registration/tail number baked into the livery's aircraft.cfg atc_id, e.g. "D-AJOE" -
            /// often empty (many liveries leave it blank, relying on MSFS's live in-sim customization
            /// instead), in which case matching falls back to a substring search in title/variation
            /// </summary>
            public string atcId = "";
            /// <summary>
            /// True when icaoType came from FS2024's best-effort title guess rather than an exact/live read
            /// </summary>
            public bool icaoGuessed = false;
            /// <summary>
            /// True when icaoAirline came from GuessIcaoAirlineFromText (a free-text airline-name match
            /// somewhere in livery/title) rather than a config-confirmed icao_airline or a recognized live
            /// ATC AIRLINE code. Independent of icaoGuessed - a model can have a confirmed icaoType but a
            /// guessed icaoAirline, or vice versa.
            /// </summary>
            public bool icaoAirlineGuessed = false;
            /// <summary>
            /// True when classCode was derived directly from live SimConnect data (category/engine type/engine
            /// count - see DeriveLiveClassCode), which doesn't depend on icaoType being a recognized Doc8643
            /// designator. Once set, RefreshIcaoDerived leaves classCode/wtc alone instead of re-deriving them
            /// from the (possibly stale or X-Plane-only-relevant) bundled Doc8643-by-icaoType lookup.
            /// </summary>
            public bool classCodeConfirmed = false;
            /// <summary>
            /// True when icaoType/wtc/icaoAirline/atcId were read directly from the model's own real
            /// aircraft.cfg/livery.cfg (located via SimConnect's LIVERY FOLDER the moment it was locally
            /// instantiated) - the most reliable source available for FS2024, on par with the upfront
            /// folder scan other builds already get. Implies classCodeConfirmed.
            /// </summary>
            public bool configConfirmed = false;
            /// <summary>
            /// Set only when a config-confirmed icao_type_designator wasn't a recognized Doc8643 designator
            /// and had to be corrected - see ResolveConfirmedIcaoType. Format "&lt;Reason&gt;:&lt;declaredValue&gt;",
            /// e.g. "IcaoModelFallback:500E" (icao_model held the real code instead) or
            /// "TitleGuessCorroborated:500E" (guessed from title text, corroborated by classCode/WTC).
            /// Empty when icao_type_designator needed no correction. Surfaced in Explain Match.
            /// </summary>
            public string icaoResolutionNote = "";

            public Model(string title, string manufacturer, string type, string variation, int index, string typerole, string smoke, string folder,
                string icaoType = "", string wtc = "", string icaoAirline = "", string classCode = "", bool classCodeConfirmed = false, string atcId = "",
                bool icaoAirlineGuessed = false)
            {
                this.title = title;
                this.manufacturer = manufacturer;
                this.type = type;
                longType = manufacturer + " " + type;
                this.variation = variation;
                this.index = index;
                this.folder = folder;
                this.typerole = TyperoleFromString(typerole);
                // convert smoke count
                this.smokeCount = 0;
                int.TryParse(smoke, NumberStyles.Number, CultureInfo.InvariantCulture, out this.smokeCount);
                this.icaoType = icaoType;
                this.wtc = wtc;
                this.icaoAirline = icaoAirline;
                this.classCode = classCode;
                this.classCodeConfirmed = classCodeConfirmed;
                this.atcId = atcId;
                this.icaoAirlineGuessed = icaoAirlineGuessed;
            }

            /// <summary>
            /// Recompute classCode/wtc and, when available, a more reliable typerole from the ICAO type designator.
            /// Never overrides a Fighter/Bomber classification already made from the title (Doc8643 codes don't
            /// distinguish military variants). Skips classCode/wtc entirely when classCodeConfirmed is set.
            /// </summary>
            public void RefreshIcaoDerived(Dictionary<string, (string classCode, string wtc)> doc8643Lookup)
            {
                if (classCodeConfirmed == false)
                {
                    classCode = "";
                    if (icaoType.Length > 0 && doc8643Lookup.TryGetValue(icaoType, out var entry))
                    {
                        classCode = entry.classCode;
                        if (wtc.Length == 0)
                        {
                            wtc = entry.wtc;
                        }
                    }
                }

                if (typerole != TypeRole_Fighter && typerole != TypeRole_Bomber)
                {
                    int derived = TyperoleFromIcao(icaoType, classCode, wtc);
                    if (derived > 0)
                    {
                        typerole = derived;
                    }
                }
            }
        }

        /// <summary>
        /// List of valid models in the sim
        /// </summary>
        public List<Model> models = [];

        /// <summary>
        /// ICAO Doc8643 reference data: icaoType -> (classCode, wtc), first entry for a designator wins
        /// </summary>
        static readonly Dictionary<string, (string classCode, string wtc)> doc8643Lookup = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// All rows from the bundled Doc8643 dataset, used for FS2024's title-based ICAO type guessing
        /// </summary>
        static readonly List<(string manufacturer, string modelName, string icaoType, string classCode, string wtc)> doc8643Rows = [];

        /// <summary>
        /// Load the bundled ICAO Doc8643 reference dataset (process-lifetime, loaded once)
        /// </summary>
        void LoadDoc8643Index()
        {
            // already loaded
            if (doc8643Lookup.Count > 0) return;

            try
            {
                using var stream = new MemoryStream(Properties.Resources_XPLANE.XPMP2_Doc8643);
                using var reader = new StreamReader(stream);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length != 5) continue;

                    string manufacturer = parts[0];
                    string modelName = parts[1];
                    string icaoType = parts[2];
                    string classCode = parts[3];
                    string wtc = parts[4];

                    if (icaoType.Length == 0 || icaoType == "ZZZZ") continue;

                    doc8643Rows.Add((manufacturer, modelName, icaoType, classCode, wtc));
                    if (doc8643Lookup.ContainsKey(icaoType) == false)
                    {
                        // first-wins; confirmed rows sharing a designator agree in practice
                        doc8643Lookup.Add(icaoType, (classCode, wtc));
                    }
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent("Error parsing Doc8643 dataset: " + ex.Message);
            }
        }

        /// <summary>
        /// True when icaoType is a real, recognized Doc8643 designator - used to sanity-check a config-
        /// confirmed icao_type_designator before trusting it outright. Some add-ons put a made-up value
        /// there instead of a real designator (see ResolveConfirmedIcaoType for a concrete real-world case).
        /// </summary>
        static bool IsRecognizedIcaoType(string icaoType) => icaoType.Length > 0 && doc8643Lookup.ContainsKey(icaoType);

        /// <summary>
        /// ICAO airline operator code -> airline name, e.g. "CFG" -> "condor" (bundled from opennav.com)
        /// </summary>
        static readonly Dictionary<string, string> icaoAirlineNames = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Load the bundled ICAO airline code reference dataset (process-lifetime, loaded once)
        /// </summary>
        void LoadIcaoAirlineIndex()
        {
            // already loaded
            if (icaoAirlineNames.Count > 0) return;

            try
            {
                using var stream = new MemoryStream(Properties.Resources_XPLANE.ICAO_Airlines);
                using var reader = new StreamReader(stream);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length != 3) continue;

                    string code = parts[0];
                    string name = parts[2];

                    if (code.Length != 3 || name.Length == 0) continue;

                    if (icaoAirlineNames.ContainsKey(code) == false)
                    {
                        // first-wins (alphabetically-first code for a shared name is usually the primary operator)
                        icaoAirlineNames.Add(code, name);
                    }
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent("Error parsing ICAO airline dataset: " + ex.Message);
            }
        }

        /// <summary>
        /// True when code is a real, recognized ICAO airline operator code - used to reject bogus/non-standard
        /// values some add-ons report via SimConnect's ATC AIRLINE (e.g. a flight-number-style string).
        /// </summary>
        static bool IsKnownIcaoAirline(string code) => code.Length == 3 && icaoAirlineNames.ContainsKey(code);

        /// <summary>
        /// True when needle appears in haystack as a standalone token - not immediately adjacent to another
        /// letter/digit - to avoid false positives from two unrelated words coincidentally gluing together
        /// at a boundary (e.g. stripping the space out of "...bus A32NX..." creates the substring "sA3", a
        /// real but completely unrelated ICAO designator that just happens to span that seam).
        /// </summary>
        static bool ContainsToken(string haystack, string needle)
        {
            int searchFrom = 0;
            while (true)
            {
                int idx = haystack.IndexOf(needle, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return false;

                bool leftOk = idx == 0 || !char.IsLetterOrDigit(haystack[idx - 1]);
                int rightPos = idx + needle.Length;
                bool rightOk = rightPos >= haystack.Length || !char.IsLetterOrDigit(haystack[rightPos]);
                if (leftOk && rightOk) return true;

                searchFrom = idx + 1;
            }
        }

        /// <summary>
        /// Best-effort guess of an ICAO airline code from livery/title text, by matching the longest known
        /// airline name that appears in the text. Used only when a live ATC AIRLINE value doesn't look like
        /// a real ICAO code, e.g. "FSC739" reported for a Condor-liveried aircraft instead of "CFG".
        /// </summary>
        static string GuessIcaoAirlineFromText(string text)
        {
            (string code, int matchLength) best = ("", 0);

            foreach (var pair in icaoAirlineNames)
            {
                string needle = pair.Value;
                if (needle.Length >= 4 && needle.Length > best.matchLength && ContainsToken(text, needle))
                {
                    best = (pair.Key, needle.Length);
                }
            }

            return best.code;
        }

        /// <summary>
        /// Indexes over models keyed by ICAO type, exact classification code, and loose platform+engine-type category
        /// </summary>
        readonly Dictionary<string, List<Model>> icaoIndex = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<Model>> classCodeIndex = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<Model>> categoryIndex = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<int, List<Model>> typeroleIndex = [];

        /// <summary>
        /// Set when a model's ICAO tag has been learned live and the ICAO indexes need rebuilding
        /// before the next Match()
        /// </summary>
        bool icaoIndexDirty = false;

        /// <summary>
        /// Rebuild the ICAO-based lookup indexes from the current model list
        /// </summary>
        public void MakeIcaoIndex()
        {
            icaoIndex.Clear();
            classCodeIndex.Clear();
            categoryIndex.Clear();
            typeroleIndex.Clear();

            foreach (var model in models)
            {
                if (typeroleIndex.TryGetValue(model.typerole, out var typeroleList) == false)
                {
                    typeroleList = [];
                    typeroleIndex.Add(model.typerole, typeroleList);
                }
                typeroleList.Add(model);

                if (model.icaoType.Length == 0) continue;

                if (icaoIndex.TryGetValue(model.icaoType, out var icaoList) == false)
                {
                    icaoList = [];
                    icaoIndex.Add(model.icaoType, icaoList);
                }
                icaoList.Add(model);

                if (model.classCode.Length == 3)
                {
                    if (classCodeIndex.TryGetValue(model.classCode, out var classList) == false)
                    {
                        classList = [];
                        classCodeIndex.Add(model.classCode, classList);
                    }
                    classList.Add(model);

                    string looseKey = model.classCode[0] + "*" + model.classCode[2];
                    if (categoryIndex.TryGetValue(looseKey, out var categoryList) == false)
                    {
                        categoryList = [];
                        categoryIndex.Add(looseKey, categoryList);
                    }
                    categoryList.Add(model);
                }
            }

            icaoIndexDirty = false;
        }

#if FS2024
        /// <summary>
        /// Best-effort guess of a model's ICAO type designator from its title, by substring-matching
        /// against Doc8643 model names. Used only as a fallback when SimConnect's model enumeration
        /// (title + livery only) gives us no other way to tag a model before it has ever been flown.
        /// Prefers the longest matching model name to reduce false positives.
        ///
        /// Optionally corroborated by an independently-derived classCode/WTC (e.g. from
        /// icao_engine_type/icao_engine_count/Category in the same aircraft.cfg, when
        /// icao_type_designator itself couldn't be trusted - see ResolveConfirmedIcaoType). When a
        /// candidate row's own classCode/WTC agrees with the corroborating value, a much shorter/weaker
        /// text match is trusted that would otherwise be rejected as too ambiguous - two independent
        /// signals agreeing sharply cuts the false-positive risk the length thresholds guard against.
        /// </summary>
        static string GuessIcaoTypeFromTitle(string title, string corroboratingClassCode = "", string corroboratingWtc = "")
        {
            // Only dashes are stripped (common in titles like "A-320" for designator "A320"); spaces are
            // kept so real word boundaries survive - see ContainsToken for why that matters.
            string haystack = title.Replace("-", "");

            // Prefer a direct match against a real ICAO type designator itself (e.g. "A321", "B738").
            // These are unambiguous and far less prone to false positives than the manufacturer's full
            // model name below, which can coincidentally collide with an unrelated word elsewhere in a
            // livery/operator title (e.g. the airline "Condor" colliding with the homebuilt "Druine/
            // Rollason Condor", D6CR/SingleProp, when the real aircraft is an Airbus A321).
            (string icaoType, int matchLength) bestIcao = ("", 0);
            foreach (var icaoType in doc8643Lookup.Keys)
            {
                if (icaoType.Length >= 3 && icaoType.Length > bestIcao.matchLength &&
                    ContainsToken(haystack, icaoType))
                {
                    bestIcao = (icaoType, icaoType.Length);
                }
            }
            if (bestIcao.icaoType.Length > 0)
            {
                return bestIcao.icaoType;
            }

            // Fall back to matching a full manufacturer model name, only when no designator matched.
            // Require a >=6 char needle (up from >=3), and either an unambiguous >=8 char needle or that
            // the row's manufacturer also appears in the title - guards against a short/generic model
            // name (e.g. "Baron", 5 chars) coincidentally matching inside an unrelated title/operator
            // name (e.g. "...BVN_Baron Aviation", a Cessna Caravan livery for an operator named "Baron
            // Aviation", with no "Beechcraft"/"Raytheon" anywhere in the title to justify the match).
            //
            // Exception: a needle with distinctive internal casing (a capital letter after position 0,
            // alongside a lowercase letter - e.g. "XCub", "NXCub") is treated as a coined product name
            // rather than a plain English word, and is exempt from both the >=6 length floor and the
            // manufacturer-must-also-appear requirement. "Baron" (Title Case only - one leading capital)
            // does not qualify, so that case is unaffected. Confirmed real case: Cub Crafters' Doc8643
            // rows for CC19 are "XCub"/"NXCub" (4-5 chars) - too short for the general rule, and their
            // installed titles ("XCub Passengers Big Wheels") never contain "Cub Crafters" to satisfy the
            // manufacturer check either, so without this the type/class code could never be resolved from
            // title text alone.
            (string icaoType, int matchLength) best = ("", 0);
            foreach (var row in doc8643Rows)
            {
                bool classCorroborated = corroboratingClassCode.Length > 0 && row.classCode == corroboratingClassCode
                    && (corroboratingWtc.Length == 0 || row.wtc == corroboratingWtc);

                string needle = row.modelName.Replace("-", "");
                bool distinctiveCasing = needle.Skip(1).Any(char.IsUpper) && needle.Any(char.IsLower);
                int minNeedleLength = classCorroborated || distinctiveCasing ? 3 : 6;
                if (needle.Length < minNeedleLength || needle.Length <= best.matchLength) continue;
                if (ContainsToken(haystack, needle) == false) continue;
                if (classCorroborated == false && distinctiveCasing == false && needle.Length < 8 && ContainsToken(haystack, row.manufacturer) == false) continue;

                best = (row.icaoType, needle.Length);
            }

            return best.icaoType;
        }

        /// <summary>
        /// Resolves the most trustworthy ICAO type designator from a real aircraft.cfg/livery.cfg [GENERAL]
        /// section. Per the MSFS SDK, icao_type_designator is meant to hold the real ICAO code and icao_model
        /// a separate, descriptive-only model name - but some add-ons swap that intent (or simply leave
        /// icao_type_designator blank, also common). Confirmed against a real package: CowanSim's MD500E
        /// ships icao_type_designator="500E" (not a real designator at all) with the actual code "H500"
        /// sitting in icao_model instead. Tries, in order: (1) the declared icao_type_designator, if it's a
        /// real recognized designator; (2) icao_model, if THAT is a real recognized designator instead
        /// (whether icao_type_designator was wrong or simply blank); (3) guessing from the title text,
        /// corroborated by classCode/WTC derived from icao_engine_type/icao_engine_count/Category in the
        /// same file (see DeriveClassCodeFromConfig/GuessIcaoTypeFromTitle) so a much weaker text match can
        /// be trusted when it independently agrees with the aircraft's actual category/engine class; (4)
        /// finally, trusting the raw unrecognized declared value as a last resort when there was one - the
        /// SDK does permit custom values for experimental/unlisted aircraft, and an unrecognized-but-
        /// consistent value can still match exactly between two users of the same add-on even though it
        /// won't resolve a classCode/WTC.
        ///
        /// Returns a resolution note alongside the resolved type, formatted "&lt;Reason&gt;:&lt;declaredValue&gt;"
        /// (empty when the declared value needed no correction) so callers can explain what happened and
        /// why - see LearnIcaoFromLiveObject/Model.icaoResolutionNote and MatchExplainForm's use of it. The
        /// reason gets a "Blank" suffix when icao_type_designator wasn't merely wrong but entirely absent -
        /// there's nothing invalid to report in that case, just nothing declared, so the wording differs.
        /// </summary>
        static (string icaoType, string resolutionNote) ResolveConfirmedIcaoType(string configuredIcaoType, string configuredIcaoModel, string modelTitle, string corroboratingClassCode, string corroboratingWtc)
        {
            if (IsRecognizedIcaoType(configuredIcaoType)) return (configuredIcaoType, "");

            bool declaredButInvalid = configuredIcaoType.Length > 0;

            if (IsRecognizedIcaoType(configuredIcaoModel))
            {
                string reason = declaredButInvalid ? "IcaoModelFallback" : "IcaoModelOnly";
                return (configuredIcaoModel, reason + ":" + configuredIcaoType);
            }

            string guessed = GuessIcaoTypeFromTitle(modelTitle, corroboratingClassCode, corroboratingWtc);
            if (guessed.Length > 0)
            {
                string reason = corroboratingClassCode.Length > 0
                    ? (declaredButInvalid ? "TitleGuessCorroborated" : "TitleGuessCorroboratedBlank")
                    : (declaredButInvalid ? "TitleGuess" : "TitleGuessBlank");
                return (guessed, reason + ":" + configuredIcaoType);
            }

            // nothing better found - if a value was declared (even if invalid), still report it and
            // explain why; if it was simply blank there's nothing wrong to explain, just no data found,
            // so no note is needed at all
            return (configuredIcaoType, declaredButInvalid ? "Unresolved:" + configuredIcaoType : "");
        }
#endif

        /// <summary>
        /// Learn a model's ICAO type/airline/classCode live from SimConnect, the moment it is actually
        /// instantiated in the sim (the user's own aircraft or any locally-drawn AI/multiplayer object).
        /// This is the only way to tag special-designator aircraft (paraplanes, gliders, balloons, ...)
        /// whose Doc8643 rows are generic placeholders with no real product name to guess from, and the
        /// only way to correctly classify an aircraft whose reported ATC MODEL/type doesn't match any
        /// Doc8643 designator at all (e.g. an add-on shipping a non-standard type string).
        /// </summary>
        public string LearnIcaoFromLiveObject(string title, string variation, string icaoType, string icaoAirline, string classCode, string wtc, string atcId = "", bool configConfirmed = false, string icaoResolutionNote = "")
        {
#if FS2024
            Model model = GetModel(title, variation);
#else
            Model model = GetModel(title);
#endif
            if (model == null) return "";

            bool changed = false;

            if (icaoType.Length > 0 && model.icaoType != icaoType)
            {
                model.icaoType = icaoType;
                // A recognized designator read straight from icao_type_designator, or (when that field
                // held something else - see ResolveConfirmedIcaoType) from icao_model instead, is still
                // fully confirmed. Only a title-guess/unresolved fallback is genuinely a guess and should
                // keep the lower-confidence tag (and its ×GuessedSignalMultiplier downweight in scoring).
                model.icaoGuessed = icaoResolutionNote.StartsWith("TitleGuess") || icaoResolutionNote.StartsWith("Unresolved");
                model.icaoResolutionNote = icaoResolutionNote;
                changed = true;
            }

            // A confirmed icao_airline read straight from aircraft.cfg/livery.cfg is trustworthy as-is;
            // a live ATC AIRLINE value is only trustworthy when it's a real, recognized ICAO code - some
            // add-ons report a flight-number/virtual-callsign-style string instead (e.g. "FSC739"). When
            // that happens, fall back to matching a known airline name against the livery/title text - a
            // genuine guess, tagged as such so ScoreCandidate can weight it below a real ICAO-code read.
            bool airlineIsGuess = false;
            string resolvedAirline;
            if (configConfirmed && icaoAirline.Length > 0)
            {
                resolvedAirline = icaoAirline;
            }
            else if (IsKnownIcaoAirline(icaoAirline))
            {
                resolvedAirline = icaoAirline;
            }
            else
            {
                resolvedAirline = GuessIcaoAirlineFromText(variation + " " + title);
                airlineIsGuess = resolvedAirline.Length > 0;
            }
            // also re-trigger when a previous guess for the same code gets corroborated/upgraded to
            // confirmed, so the stale "guessed" tag doesn't linger once a better source agrees
            if (resolvedAirline.Length > 0 && (model.icaoAirline != resolvedAirline || model.icaoAirlineGuessed != airlineIsGuess))
            {
                model.icaoAirline = resolvedAirline;
                model.icaoAirlineGuessed = airlineIsGuess;
                changed = true;
            }

            // registration baked into this specific livery's aircraft.cfg/livery.cfg atc_id - only ever
            // populated via the config-confirmed path (FS2024 has no other source for it)
            if (configConfirmed && atcId.Length > 0 && model.atcId != atcId)
            {
                model.atcId = atcId;
                changed = true;
            }

            // Config-confirmed classification (real aircraft.cfg/livery.cfg data) and live-derived
            // classification (from actual category/engine simvars) are both more reliable than the
            // bundled Doc8643-by-icaoType guess, and don't depend on icaoType being a recognized designator.
            // A config-confirmed read always wins over a merely live-derived one if both are present.
            if (classCode.Length > 0 && ((model.classCodeConfirmed == false && model.configConfirmed == false) || model.classCode != classCode || (configConfirmed && model.configConfirmed == false)))
            {
                model.classCode = classCode;
                model.classCodeConfirmed = true;
                if (configConfirmed) model.configConfirmed = true;
                if (wtc.Length > 0) model.wtc = wtc;
                changed = true;
            }

            if (changed)
            {
                model.RefreshIcaoDerived(doc8643Lookup);
                // avoid rebuilding the full index on every single object spawn - Match() rebuilds lazily
                icaoIndexDirty = true;
                main.ScheduleSubstitutionSave();
            }

            return resolvedAirline;
        }

#if FS2024
        /// <summary>
        /// Lazily-built index of installed SimObjects\Airplanes\&lt;name&gt;/SimObjects\Rotorcraft\&lt;name&gt;
        /// folders, keyed by folder name only (not full-parsed) - resolves cross-package [VARIATION]
        /// base_container references that a plain relative-path lookup can't reach, since MSFS merges
        /// package content into one virtual namespace rather than nesting them physically on disk.
        /// </summary>
        readonly Dictionary<string, string> packageFolderIndex = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>True once a build has actually succeeded (found a valid sim folder) - distinct from
        /// packageFolderIndex being empty, so a failed attempt (sim folder not set yet) can retry on the
        /// next call instead of being stuck empty for the rest of the session.</summary>
        bool packageFolderIndexBuilt = false;
        /// <summary>Guards packageFolderIndex/titleFolderIndex builds against concurrent mutation - these
        /// are plain Dictionaries with no synchronization of their own, and unlike their historical sole
        /// caller (the live LIVERY FOLDER read, always serialized under main.conch on the SimConnect
        /// message thread), the scan-time pre-warm call runs on a background thread deliberately outside
        /// main.conch (to avoid stalling it), so both can now race on the same dictionaries without this.
        /// Intentionally a separate, narrow lock rather than main.conch itself - reusing main.conch here
        /// would serialize the whole app behind a potentially multi-second index build.</summary>
        readonly object packageFolderIndexLock = new();

        void BuildPackageFolderIndexIfNeeded()
        {
            if (packageFolderIndexBuilt) return;
            lock (packageFolderIndexLock)
            {
                if (packageFolderIndexBuilt) return;
                BuildPackageFolderIndexLocked();
            }
        }

        void BuildPackageFolderIndexLocked()
        {
            if (Directory.Exists(simFolder) == false)
            {
                if (packageFolderIndexDiagLogged.Add(simFolder ?? ""))
                {
                    main.MonitorEvent("DIAG: package folder index not built - configured sim folder '" + simFolder + "' does not exist (or isn't set yet). Will retry next time it's needed.");
                }
                return;
            }

            int packageFolderCount = 0;
            try
            {
                // simFolder is the sim's base install folder (e.g. "H:\MSFS2024"), which itself only
                // contains grouping folders like "Community"/"Official2024"/"Official2020" - the actual
                // installed packages (each with its own SimObjects\Airplanes|Rotorcraft) are one level
                // further down inside those. Index both this level and one level down, so a package
                // folder is found whether simFolder points at the base install or directly at a
                // grouping folder like Community.
                foreach (var groupFolder in Directory.GetDirectories(simFolder))
                {
                    packageFolderCount += IndexPackageFolder(groupFolder);

                    try
                    {
                        foreach (var packageFolder in Directory.GetDirectories(groupFolder))
                        {
                            packageFolderCount += IndexPackageFolder(packageFolder);
                        }
                    }
                    catch (Exception ex)
                    {
                        // one inaccessible/unusual group folder (e.g. a locked system-managed cache)
                        // shouldn't abort indexing the rest of the sim install
                        main.MonitorEvent("Error indexing package folder '" + groupFolder + "': " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent("Error building package folder index: " + ex.Message);
                return;
            }

            main.MonitorEvent("DIAG: package folder index built from sim folder '" + simFolder + "' - " + packageFolderCount + " candidate package folder(s) scanned, " + packageFolderIndex.Count + " installed aircraft/rotorcraft folder(s) indexed.");
            packageFolderIndexBuilt = true;
        }

        /// <summary>Indexes a single package folder's SimObjects\Airplanes|Rotorcraft subfolders, if any. Returns 1 (counted as scanned) so the caller can tally how many folders were checked.</summary>
        int IndexPackageFolder(string packageFolder)
        {
            foreach (var kind in new[] { "Airplanes", "Rotorcraft" })
            {
                string simObjectsPath = Path.Combine(packageFolder, "SimObjects", kind);
                if (Directory.Exists(simObjectsPath) == false) continue;

                foreach (var modelFolder in Directory.GetDirectories(simObjectsPath))
                {
                    string name = Path.GetFileName(modelFolder);
                    packageFolderIndex.TryAdd(name, modelFolder);
                }
            }
            return 1;
        }

        /// <summary>Distinct simFolder values already logged as missing, so a permanently-unset folder doesn't spam the log on every spawn.</summary>
        readonly HashSet<string> packageFolderIndexDiagLogged = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Resolve a [VARIATION] base_container reference to an absolute folder, trying the literal
        /// relative path first (covers same-package siblings, e.g. FSLTL's "..\FSLTL_A20N") before
        /// falling back to the package-name index (covers cross-package references, e.g. a standalone
        /// livery package's "../Helicopter_500E" pointing at an entirely separate installed package).
        /// </summary>
        string ResolveBaseContainer(string liveryFolder, string baseContainer)
        {
            try
            {
                string relative = Path.GetFullPath(Path.Combine(liveryFolder, baseContainer));
                if (Directory.Exists(relative)) return relative;
            }
            catch { }

            BuildPackageFolderIndexIfNeeded();
            string leafName = baseContainer.TrimEnd('/', '\\');
            int lastSep = leafName.LastIndexOfAny(['/', '\\']);
            if (lastSep >= 0) leafName = leafName[(lastSep + 1)..];

            return packageFolderIndex.TryGetValue(leafName, out var folder) ? folder : null;
        }

        /// <summary>
        /// Parse one aircraft.cfg/livery.cfg file for [GENERAL] icao_type_designator/icao_model/icao_WTC/
        /// icao_engine_type/icao_engine_count/Category (shared across every [FLTSIM.N] block in the file -
        /// see ResolveConfirmedIcaoType for why all of these are read, not just icao_type_designator),
        /// [VARIATION] base_container, and the specific [FLTSIM.N] block's atc_id/icao_airline for
        /// modelTitle (falling back to the first/only block found, since most livery folders - the common
        /// case - contain exactly one). Merges into the ref parameters rather than overwriting, so a
        /// second call (e.g. resolving base_container) can fill in only what the first call left blank.
        /// icaoResolutionNote is set only when icao_type_designator needed correcting - see
        /// ResolveConfirmedIcaoType.
        /// </summary>
        bool TryParseAircraftConfigFile(string path, string modelTitle, ref string icaoType, ref string wtc,
            ref string icaoAirline, ref string atcId, ref string classCode, ref string icaoResolutionNote, out string baseContainer)
        {
            baseContainer = "";
            if (File.Exists(path) == false) return false;

            try
            {
                string section = "";
                string blockTitle = "", blockAtcId = "", blockIcaoAirline = "";
                string firstAtcId = "", firstIcaoAirline = "";
                string matchedAtcId = "", matchedIcaoAirline = "";
                bool haveFirst = false;
                bool matched = false;
                string fileIcaoType = "", fileIcaoModel = "", fileWtc = "", fileCategory = "", fileEngineType = "";
                int fileEngineCount = 0;

                void FinishBlock()
                {
                    if (blockTitle.Length == 0) return;
                    if (haveFirst == false)
                    {
                        haveFirst = true;
                        firstAtcId = blockAtcId;
                        firstIcaoAirline = blockIcaoAirline;
                    }
                    if (matched == false && blockTitle.Equals(modelTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = true;
                        matchedAtcId = blockAtcId;
                        matchedIcaoAirline = blockIcaoAirline;
                    }
                    blockTitle = ""; blockAtcId = ""; blockIcaoAirline = "";
                }

                foreach (var rawLine in File.ReadLines(path))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(';') || line.StartsWith("//")) continue;

                    if (line[0] == '[')
                    {
                        FinishBlock();
                        int end = line.IndexOf(']');
                        section = end > 0 ? line[1..end].ToUpperInvariant() : "";
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line[..eq].Trim();
                    string value = line[(eq + 1)..].Trim();
                    int semi = value.IndexOf(';');
                    if (semi >= 0) value = value[..semi].Trim();
                    value = value.Trim('"');

                    if (section == "GENERAL")
                    {
                        if (key.Equals("icao_type_designator", StringComparison.OrdinalIgnoreCase)) fileIcaoType = value;
                        else if (key.Equals("icao_model", StringComparison.OrdinalIgnoreCase)) fileIcaoModel = value;
                        else if (key.Equals("icao_WTC", StringComparison.OrdinalIgnoreCase))
                        {
                            int slash = value.IndexOf('/');
                            fileWtc = slash >= 0 ? value[..slash] : value;
                        }
                        else if (key.Equals("icao_engine_type", StringComparison.OrdinalIgnoreCase)) fileEngineType = value;
                        else if (key.Equals("icao_engine_count", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, out fileEngineCount);
                        else if (key.Equals("Category", StringComparison.OrdinalIgnoreCase)) fileCategory = value;
                    }
                    else if (section == "VARIATION")
                    {
                        if (key.Equals("base_container", StringComparison.OrdinalIgnoreCase)) baseContainer = value;
                    }
                    else if (section.StartsWith("FLTSIM"))
                    {
                        if (key.Equals("title", StringComparison.OrdinalIgnoreCase)) blockTitle = value;
                        else if (key.Equals("atc_id", StringComparison.OrdinalIgnoreCase)) blockAtcId = value;
                        else if (key.Equals("icao_airline", StringComparison.OrdinalIgnoreCase)) blockIcaoAirline = value;
                    }
                }
                FinishBlock();

                // resolve even when icao_type_designator itself is blank (a very common case - see the
                // wiki's "left them blank" note) as long as icao_model or the title gives something to
                // try instead; only truly give up when neither field has anything at all
                if (icaoType.Length == 0 && (fileIcaoType.Length > 0 || fileIcaoModel.Length > 0))
                {
                    DeriveClassCodeFromConfig(fileCategory, fileEngineType, fileEngineCount, out string derivedClassCode, out string derivedWtc);
                    string corroboratingWtc = fileWtc.Length > 0 ? fileWtc : derivedWtc;
                    var (resolvedIcaoType, note) = ResolveConfirmedIcaoType(fileIcaoType, fileIcaoModel, modelTitle, derivedClassCode, corroboratingWtc);
                    icaoType = resolvedIcaoType;
                    if (icaoResolutionNote.Length == 0 && note.Length > 0) icaoResolutionNote = note;
                }
                if (wtc.Length == 0 && fileWtc.Length > 0) wtc = fileWtc;
                if (matched)
                {
                    if (atcId.Length == 0) atcId = matchedAtcId;
                    if (icaoAirline.Length == 0) icaoAirline = matchedIcaoAirline;
                }
                else if (haveFirst)
                {
                    // no exact title match (quoting/whitespace mismatch, or caller doesn't know the
                    // title yet) - fall back to the only/first block, correct for the common single-
                    // variation-per-folder case
                    if (atcId.Length == 0) atcId = firstAtcId;
                    if (icaoAirline.Length == 0) icaoAirline = firstIcaoAirline;
                }

                if (classCode.Length == 0 && icaoType.Length > 0 && doc8643Lookup.TryGetValue(icaoType, out var entry))
                {
                    classCode = entry.classCode;
                    if (wtc.Length == 0) wtc = entry.wtc;
                }

                return icaoType.Length > 0;
            }
            catch (Exception ex)
            {
                main.MonitorEvent("Error parsing '" + path + "': " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Best-effort read of icao_type_designator/icao_WTC/icao_airline/atc_id straight from a model's
        /// real aircraft.cfg/livery.cfg, located via the folder SimConnect's LIVERY FOLDER simvar points
        /// at the moment it's actually instantiated locally. This is the primary, most-reliable data
        /// source for FS2024 - the same tier of confidence non-FS2024 builds already get from their
        /// upfront folder scan - ahead of DeriveLiveClassCode (category/engine simvars) and title-
        /// guessing, both of which stay as fallbacks for when this can't find/parse a config file.
        /// icaoResolutionNote is set only when the file's own icao_type_designator wasn't a recognized
        /// designator and had to be corrected - see ResolveConfirmedIcaoType.
        /// </summary>
        public bool TryReadConfigFromLiveryFolder(string liveryFolder, string modelTitle,
            out string icaoType, out string wtc, out string icaoAirline, out string atcId, out string classCode, out string icaoResolutionNote)
        {
            icaoType = ""; wtc = ""; icaoAirline = ""; atcId = ""; classCode = ""; icaoResolutionNote = "";

            string resolvedFolder = null;
            if (string.IsNullOrWhiteSpace(liveryFolder))
            {
                // LIVERY FOLDER is documented as "folder where livery.cfg is stored" - confirmed against
                // a real installed aircraft that it comes back genuinely blank for classic all-liveries-
                // in-one-aircraft.cfg packages that never had a livery.cfg at all (still very common
                // among freeware/community add-ons). Fall back to a lazily-built title->folder index
                // instead of giving up, since the real aircraft.cfg is still on disk somewhere.
                if (liveryFolderDiagLogged.Add("<blank:" + modelTitle + ">"))
                {
                    main.MonitorEvent("DIAG: LIVERY FOLDER was blank/not reported by SimConnect for model '" + modelTitle + "' - trying the title->folder index instead.");
                }
            }
            else
            {
                try
                {
                    resolvedFolder = ResolveLiveryFolder(liveryFolder);
                }
                catch (Exception ex)
                {
                    main.MonitorEvent("Error resolving LIVERY FOLDER '" + liveryFolder + "': " + ex.Message);
                }

                if (resolvedFolder == null && liveryFolderDiagLogged.Add(liveryFolder))
                {
                    main.MonitorEvent("DIAG: could not resolve LIVERY FOLDER '" + liveryFolder + "' to an existing directory (tried as-is, relative to the configured sim folder '" + simFolder + "', and by leaf folder name) - trying the title->folder index instead.");
                }
                else if (resolvedFolder != null && liveryFolderDiagLogged.Add(liveryFolder))
                {
                    main.MonitorEvent("DIAG: LIVERY FOLDER '" + liveryFolder + "' resolved to '" + resolvedFolder + "'.");
                }
            }

            if (resolvedFolder == null)
            {
                BuildTitleFolderIndexIfNeeded();
                if (titleFolderIndex.TryGetValue(modelTitle, out resolvedFolder) == false)
                {
                    return false;
                }
                if (liveryFolderDiagLogged.Add("<titleindex:" + modelTitle + ">"))
                {
                    main.MonitorEvent("DIAG: title->folder index resolved model '" + modelTitle + "' to '" + resolvedFolder + "'.");
                }
            }

            try
            {
                bool found = TryParseAircraftConfigFile(Path.Combine(resolvedFolder, "aircraft.cfg"), modelTitle,
                    ref icaoType, ref wtc, ref icaoAirline, ref atcId, ref classCode, ref icaoResolutionNote, out string baseContainer);
                if (found == false)
                {
                    // TryParseAircraftConfigFile always resets its own out baseContainer to "" as its
                    // first line (even when the file doesn't exist) - don't let a missing/base_container-
                    // less livery.cfg wipe out a real base_container the aircraft.cfg parse already found
                    // (e.g. a pure [VARIATION] overlay with no [GENERAL] section of its own at all, which
                    // legitimately has no icaoType to report but does have the reference to follow)
                    found = TryParseAircraftConfigFile(Path.Combine(resolvedFolder, "livery.cfg"), modelTitle,
                        ref icaoType, ref wtc, ref icaoAirline, ref atcId, ref classCode, ref icaoResolutionNote, out string liveryBaseContainer);
                    if (baseContainer.Length == 0)
                    {
                        baseContainer = liveryBaseContainer;
                    }
                }

                // some real fields (most commonly icao_WTC) only live in the base package's own file -
                // resolve and merge in whatever this file's own read left blank
                if ((icaoType.Length == 0 || wtc.Length == 0) && baseContainer.Length > 0)
                {
                    string baseFolder = ResolveBaseContainer(resolvedFolder, baseContainer);
                    if (baseFolder != null)
                    {
                        TryParseAircraftConfigFile(Path.Combine(baseFolder, "aircraft.cfg"), modelTitle,
                            ref icaoType, ref wtc, ref icaoAirline, ref atcId, ref classCode, ref icaoResolutionNote, out _);
                    }
                }

                return icaoType.Length > 0;
            }
            catch (Exception ex)
            {
                main.MonitorEvent("Error reading config from '" + resolvedFolder + "': " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Lazily-built index of every installed model's title -> containing folder, built by scanning
        /// every aircraft.cfg found under the same installed-package tree BuildPackageFolderIndexIfNeeded()
        /// already walks. Only built (and only needed) the first time LIVERY FOLDER comes back blank or
        /// unresolvable for some model - the common case (a real LIVERY FOLDER value) never touches this.
        /// </summary>
        Dictionary<string, string> titleFolderIndex;

        void BuildTitleFolderIndexIfNeeded()
        {
            if (titleFolderIndex != null) return;
            // same dedicated lock as BuildPackageFolderIndexIfNeeded (see packageFolderIndexLock) - this
            // method also assigns titleFolderIndex before it's fully populated, so without a lock a second
            // thread's `titleFolderIndex != null` check above could observe a still-being-built dictionary
            lock (packageFolderIndexLock)
            {
                if (titleFolderIndex != null) return;
                Dictionary<string, string> newIndex = new(StringComparer.OrdinalIgnoreCase);

                BuildPackageFolderIndexIfNeeded();
                foreach (var folder in packageFolderIndex.Values)
                {
                    string cfgPath = Path.Combine(folder, "aircraft.cfg");
                    if (File.Exists(cfgPath) == false) continue;

                    try
                    {
                        foreach (var title in QuickExtractTitles(cfgPath))
                        {
                            newIndex.TryAdd(title, folder);
                        }
                    }
                    catch (Exception ex)
                    {
                        main.MonitorEvent("Error indexing '" + cfgPath + "': " + ex.Message);
                    }
                }

                main.MonitorEvent("Indexed " + newIndex.Count + " model title(s) from " + packageFolderIndex.Count + " installed aircraft folder(s), for use when LIVERY FOLDER doesn't resolve.");
                // only publish once fully populated
                titleFolderIndex = newIndex;
            }
        }

        /// <summary>
        /// Cheap single-pass extraction of every [FLTSIM.N] "title" value in an aircraft.cfg, without the
        /// full field parsing TryParseAircraftConfigFile does - this only needs to build the title->folder
        /// index, not read every model's classCode/wtc/etc. up front.
        /// </summary>
        static IEnumerable<string> QuickExtractTitles(string path)
        {
            string section = "";
            foreach (var rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith("//")) continue;

                if (line[0] == '[')
                {
                    int end = line.IndexOf(']');
                    section = end > 0 ? line[1..end].ToUpperInvariant() : "";
                    continue;
                }
                if (section.StartsWith("FLTSIM") == false) continue;

                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line[..eq].Trim();
                if (key.Equals("title", StringComparison.OrdinalIgnoreCase) == false) continue;

                string value = line[(eq + 1)..].Trim();
                int semi = value.IndexOf(';');
                if (semi >= 0) value = value[..semi].Trim();
                value = value.Trim('"');
                if (value.Length > 0) yield return value;
            }
        }

        /// <summary>
        /// Distinct raw LIVERY FOLDER values already logged once (success or failure), so the
        /// diagnostic doesn't repeat on every single spawn of the same model within a session.
        /// </summary>
        readonly HashSet<string> liveryFolderDiagLogged = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Resolve SimConnect's LIVERY FOLDER value to an existing directory. The exact format this
        /// simvar returns wasn't verifiable without a live session when this was first written, so this
        /// tries progressively looser interpretations: the raw value as an absolute path, the raw value
        /// joined against the configured sim folder (in case it's package-relative), and finally a lookup
        /// by leaf folder name against the same installed-package index base_container resolution uses
        /// (in case it's neither of the above - e.g. just a bare folder/package name).
        /// </summary>
        string ResolveLiveryFolder(string liveryFolder)
        {
            string trimmed = liveryFolder.Trim();

            try
            {
                if (Directory.Exists(trimmed)) return trimmed;
            }
            catch { }

            try
            {
                string combined = Path.GetFullPath(Path.Combine(simFolder, trimmed));
                if (Directory.Exists(combined)) return combined;
            }
            catch { }

            BuildPackageFolderIndexIfNeeded();
            string leafName = trimmed.TrimEnd('/', '\\');
            int lastSep = leafName.LastIndexOfAny(['/', '\\']);
            if (lastSep >= 0) leafName = leafName[(lastSep + 1)..];

            return leafName.Length > 0 && packageFolderIndex.TryGetValue(leafName, out var folder) ? folder : null;
        }
#endif

        // TODO: cleanup code
        //        public EnrichModelService enrichModelService = null;
        //#if X64
        //        public EmbeddingService embeddingService = null;
        //#endif

        /// <summary>
        /// Does a model exist
        /// </summary>
        /// <returns>Model exists</returns>
        public bool ModelExists(string title)
        {
            return GetModel(title) != null;
        }

        /// <summary>
        /// Does a model exist
        /// </summary>
        /// <returns>Model exists</returns>
        public bool ModelExists(string title, string variation)
        {
            return GetModel(title, variation) != null;
        }

        /// <summary>
        /// Get a model by title
        /// </summary>
        /// <returns>Model exists</returns>
        public Model GetModel(string title)
        {
#if FS2024
            string[] separator = [ "[+]" ];
            string[] parts = title.Split(separator, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                return models.Find(m => m.title.Equals(parts[0]) && m.variation.Equals(parts[1]));
            }
            return models.Find(m => m.title.Equals(parts[0]));
#else
            return models.Find(m => m.title.Equals(title));
#endif
        }

        /// <summary>
        /// Get a model by title and livery
        /// </summary>
        /// <returns>Model exists</returns>
        public Model GetModel(string title, string variation)
        {
            return models.Find(m => m.title.Equals(title) && m.variation.Equals(variation));
        }

        /// <summary>
        /// Get type role for a model
        /// </summary>
        /// <param name="title">Model</param>
        /// <returns>Type role</returns>
        public int GetTypeRole(string title)
        {
            // get model from title
            Model model = GetModel(title);
            // check for existing model
            if (model != null)
            {
                // return type role
                return model.typerole;
            }
            else
            {
                // default to single prop
                return TypeRole_SingleProp;
            }
        }

        /// <summary>
        /// Get smoke count for a model
        /// </summary>
        /// <param name="title">Model</param>
        /// <returns>Smoke count</returns>
        public int GetSmokeCount(string title)
        {
            // get model from title
            Model model = GetModel(title);
            // check for existing model
            if (model != null)
            {
                // return smoke count
                return model.smokeCount;
            }
            else
            {
                // default no smoke
                return 0;
            }
        }

        /// <summary>
        /// Model matches
        /// </summary>
        public readonly Dictionary<string, Model> matches = [];

        /// <summary>
        /// Model masquerades
        /// </summary>
        public readonly Dictionary<string, Model> masquerades = [];

        /// <summary>
        /// Trim white space and quote characters
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        static string TrimComments(string str)
        {
            int index;
            // find comment
            index = str.IndexOf(';');
            if (index >= 0)
            {
                // remove comment
                str = str[..index];
            }
            // find comment
            index = str.IndexOf(@"//");
            if (index >= 0)
            {
                // remove comment
                str = str[..index];
            }
            return str;
        }

        /// <summary>
        /// Trim white space characters
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        static string Trim(string str)
        {
            // trim white space and quotes from beginning and end of string
            return TrimComments(str).TrimStart(' ', '\t', '=').TrimEnd(' ', '\t', '=');
        }

        /// <summary>
        /// Trim white space and quote characters
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        static string TrimQuotes(string str)
        {
            // trim white space and quotes from beginning and end of string
            return TrimComments(str).TrimStart(' ', '"', '\t', '=').TrimEnd(' ', '"', '\t', '=');
        }

        // model details
        bool scanBlock = false;
        string scanTitle = "";
        string scanTyperole = "";
        string scanManufacturer = "";
        string scanType = "";
        string scanVariation = "";
        int scanIndex = 0;
        string scanFolder = "";
        string scanModel = "";
        string scanTexture = "";
        string scanIcaoAirline = "";
        string scanAtcId = "";

        /// <summary>
        /// Submit the current scanned names
        /// </summary>
        void SubmitScan()
        {
            // check for valid block and title
            if (scanBlock && scanTitle.Length > 0)
            {
                // check for non-flyable/broken models (scenery props, static display liveries, known-bad
                // entries) before doing any other work - this is the single shared entry point for every
                // build's scanning (FS2024 SimConnect enumeration, aircraft.cfg/sim.cfg folder scan, and
                // X-Plane CSL scan all funnel through here), so filtering here covers all of them
                if (IsModelBanned(scanTitle, scanVariation))
                {
                    main.MonitorEvent("Model banned, excluded from scan: '" + scanTitle + "'" + (scanVariation.Length > 0 ? " / '" + scanVariation + "'" : ""));
                    lastBanExclusionCount++;
                    scanBlock = false;
                    scanTitle = "";
                    scanTyperole = "";
                    scanManufacturer = "";
                    scanType = "";
                    scanVariation = "";
                    scanIndex = 0;
                    scanModel = "";
                    scanTexture = "";
                    scanFolder = "";
                    scanIcaoAirline = "";
                    scanAtcId = "";
                    return;
                }

                // brief lock around the actual models mutation only - Scan() itself deliberately
                // does NOT hold this lock for its whole (potentially many-seconds) duration, so
                // other conch-protected readers/writers (Match/Save/UI dialogs) only ever wait a
                // few microseconds per model found, not the whole scan
                lock (main.conch)
                {
                // check for quotes
                if (scanTitle.StartsWith('\"'))
                {
                    // trim quotes
                    scanTitle = scanTitle.TrimStart('"').TrimEnd('"');
                }

                // validate manufacturer
                if (scanManufacturer.StartsWith("TT:"))
                {
                    scanManufacturer = "All";
                }
                else if (scanManufacturer.StartsWith("$$:"))
                {
                    scanManufacturer = scanManufacturer.Replace("$$:", "");
                }

                // validate type
                if (scanType.StartsWith("TT:"))
                {
                    scanType = scanTitle;
                }
                else if (scanType.StartsWith("$$:"))
                {
                    scanType = scanType.Replace("$$:", "");
                }

                // validate variation
                if (scanVariation.StartsWith("TT:"))
                {
                    scanVariation = scanTitle;
                }
                else if (scanVariation.StartsWith("$$:"))
                {
                    scanVariation = scanTitle;
                }

                // check for invalid variation
                if (scanVariation.Length == 0)
                {
                    // check for valid texture
                    if (scanTexture.Length > 0)
                    {
                        // use texture
                        scanVariation = scanTexture;
                    }
                    else if (scanModel.Length > 0)
                    {
                        // use model name
                        scanVariation = scanModel;
                    }
                    else
                    {
                        // use folder name
                        scanVariation = scanFolder;
                    }
                }

                // check for invalid type
                if (scanType.Length == 0)
                {
                    // use folder name
                    scanType = scanFolder;
                }

                // check if model is already listed
#if FS2024
                Model model = GetModel(scanTitle, scanVariation);

                // check if the typerole is "MSFS2024"
                // we only get this for MSFS2024
                if (scanTyperole == "MSFS2024")
                {
                    bool found = false;
                    // iterate over typeroleClassifier and test if typeroleClassifier key is a substring of scanTitle
                    foreach (var entry in typeroleClassifier)
                    {
                        if (scanTitle.Contains(entry.Key))
                        {
                            scanTyperole = entry.Value.typeRoleName;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        scanTyperole = "SingleProp";
                    }
                }

                // SimConnect enumeration gives us title/livery only, never real config data - before
                // falling back to guessing the ICAO type from the title text (which can misfire when an
                // airline/livery name coincidentally spells out an unrelated real designator, e.g.
                // "...Smart_Lynx" matching the Lynx helicopter instead of the actual Airbus A320), try
                // reading the real aircraft.cfg/base_container from disk - same logic the live SimConnect
                // LIVERY FOLDER read already uses (TryReadConfigFromLiveryFolder), just resolved by title
                // via titleFolderIndex (BuildTitleFolderIndexIfNeeded, pre-warmed in Scan()) instead of a
                // live SimConnect value.
                string guessedIcaoType = "";
                string diskClassCode = "", diskWtc = "", diskIcaoAirline = "", diskAtcId = "", diskResolutionNote = "";
                bool diskConfirmed = false;
                if (model == null || model.icaoType.Length == 0)
                {
                    diskConfirmed = TryReadConfigFromLiveryFolder("", scanTitle, out guessedIcaoType, out diskWtc, out diskIcaoAirline, out diskAtcId, out diskClassCode, out diskResolutionNote);
                    if (diskConfirmed == false)
                    {
                        guessedIcaoType = GuessIcaoTypeFromTitle(scanTitle);
                    }
                }

                // apply the disk-confirmed or guessed ICAO/class-code result to a model, matching what the
                // live LIVERY FOLDER read's finalize step (LearnIcaoFromLiveObject) already does for a
                // disk-confirmed result, so Explain Match shows this as confirmed rather than guessed
                void ApplyIcaoResult(Model target)
                {
                    if (guessedIcaoType.Length == 0) return;
                    target.icaoType = guessedIcaoType;
                    target.icaoGuessed = !diskConfirmed;
                    if (diskConfirmed)
                    {
                        target.classCode = diskClassCode;
                        target.classCodeConfirmed = true;
                        target.configConfirmed = true;
                        if (diskWtc.Length > 0) target.wtc = diskWtc;
                        if (diskIcaoAirline.Length > 0) { target.icaoAirline = diskIcaoAirline; target.icaoAirlineGuessed = false; }
                        if (diskAtcId.Length > 0) target.atcId = diskAtcId;
                    }
                    target.RefreshIcaoDerived(doc8643Lookup);
                }
#else
                Model model = GetModel(scanTitle);
#endif

                if (model != null)
                {
                    // update the model details
                    model.manufacturer = scanManufacturer;
                    model.type = scanType;
                    model.longType = scanManufacturer + " " + scanType;
                    model.variation = scanVariation;
                    model.index = scanIndex;
                    // don't update typerole for MSFS2024. The first classification was most probably correct
#if !FS2024
                    model.typerole = TyperoleFromString(scanTyperole);
#endif
                    model.folder = scanFolder;
                    // don't let a blank re-scan (e.g. FS2024's SimConnect model-enumeration pass, which has
                    // no icaoAirline data at all - see SubmitModel's 6-arg overload) clobber an icaoAirline
                    // already captured by a real config-file read (this scan's own folder-walk, or a
                    // previous live-learn) - only apply when this scan actually carries a value.
                    if (scanIcaoAirline.Length > 0)
                    {
                        model.icaoAirline = scanIcaoAirline;
                        model.icaoAirlineGuessed = false;
                    }
                    model.atcId = scanAtcId;
#if FS2024
                    if (model.icaoType.Length == 0)
                    {
                        ApplyIcaoResult(model);
                    }
#endif
                }
                else
                {
                    // add the model
                    Model newModel = new(scanTitle, scanManufacturer, scanType, scanVariation, scanIndex, scanTyperole, "0", scanFolder, "", "", scanIcaoAirline, atcId: scanAtcId);
#if FS2024
                    ApplyIcaoResult(newModel);
#endif
                    models.Add(newModel);
                }
                }
            }

            // reset scan details
            scanBlock = false;
            scanTitle = "";
            scanTyperole = "";
            scanManufacturer = "";
            scanType = "";
            scanVariation = "";
            scanIndex = 0;
            scanModel = "";
            scanTexture = "";
            scanFolder = "";
            scanIcaoAirline = "";
            scanAtcId = "";
        }


        /// <summary>
        /// Dynamic update of the model list
        /// </summary>
        /// <param name="title">Name of the Model</param>
        public void SubmitModel(string title)
        {
            // check if not already listed
            if (ModelExists(title) == false)
            {
                SubmitModel(title, "All", title, title, 0, "SingleProp");
            }
        }

        /// <summary>
        /// Dynamic update of the model list
        /// </summary>
        /// <param name="title">Name of the Model</param>
        public void SubmitModel(string title, string manufacturer, string type, string variation, int index, string typerole)
        {
            if (IsModelBanned(title, variation))
            {
                return;
            }

            scanBlock = true;
            scanTitle = title;
            scanManufacturer = manufacturer;
            scanType = type;
            scanVariation = variation;
            scanIndex = index;
            scanTyperole = typerole;
            scanModel = "";
            scanTexture = "";
            scanFolder = "";
            // add model
            SubmitScan();
            // save
            main.ScheduleSubstitutionSave();
        }

        /// <summary>
        /// True if the title or (optionally) variation matches an entry in the non-flyable/broken-model
        /// ban list (bannedModels.txt), using word-boundary matching so short/generic ban entries (e.g.
        /// "Static") can't accidentally match inside an unrelated real title/livery name.
        /// </summary>
        bool IsModelBanned(string title, string variation = "")
        {
            foreach (var ban in modelBanList)
            {
                if (ContainsToken(title, ban) || (variation.Length > 0 && ContainsToken(variation, ban)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Get all the models available in MSFS2024
        /// </summary>
        public void ScanSimForModels()
        {
            main.sim.RequestSimulatorModels();
        }

        /// <summary>
        /// Dynamic update of the model list
        /// </summary>
        /// <param name="title">Name of the Model</param>
        public void RemoveModel(string title)
        {
            // check if not already listed
            Model model = GetModel(title);
            if (model != null)
            {
                // remove model
                models.Remove(model);
                // save
                main.ScheduleSubstitutionSave();
            }
        }

        /// <summary>
        /// Recursive search for files in a folder
        /// </summary>
        static void SearchForFiles(string searchPath, string filename, List<string> paths, int depth)
        {
            try
            {
                // add files
                paths.AddRange(Directory.GetFiles(searchPath, filename, SearchOption.TopDirectoryOnly));
            }
            catch { }

            // check depth
            if (depth < 10)
            {
                try
                {
                    // for each folder
                    foreach (var folder in Directory.GetDirectories(searchPath))
                    {
                        SearchForFiles(folder, filename, paths, depth + 1);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Recursive search for files in a folder
        /// </summary>
        static void SearchForFolders(string searchPath, string searchFolder, List<string> paths, int depth)
        {
            try
            {
                // add files
                paths.AddRange(Directory.GetDirectories(searchPath, searchFolder, SearchOption.TopDirectoryOnly));
            }
            catch { }

            // check depth
            if (depth < 10)
            {
                try
                {
                    // for each folder
                    foreach (var folder in Directory.GetDirectories(searchPath))
                    {
                        SearchForFolders(folder, searchFolder, paths, depth + 1);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Scan simulator folders for models. <paramref name="simulatorNameOverride"/> lets
        /// this run before the simulator is connected (e.g. right after first-install
        /// auto-detection), for builds where scanning is pure disk I/O and doesn't
        /// otherwise depend on a live connection.
        /// </summary>
        public bool Scan(bool interactive, string simulatorNameOverride = null)
        {
            // name to branch on below - the real connected name, unless overridden by a caller
            // that already knows it (because the sim isn't connected yet)
            string simulatorName = simulatorNameOverride ?? (main.sim != null ? main.sim.GetSimulatorName() : "");

            // check if simulator is not connected
#if XPLANE || CONSOLE
            if (main.sim != null)
#else
            if (main.sim != null && (simulatorNameOverride != null || main.sim.Connected))
#endif
            {
                // message
                main.MonitorEvent("Starting model scan...");
                lastBanExclusionCount = 0;

#if !CONSOLE
                // busy
                Cursor.Current = Cursors.WaitCursor;
#endif

                // check for valid sim folder
                if (simFolder.Length == 0)
                {
                    // message
                    main.MonitorEvent("No folder found to scan");
                }
                else
                {
                    // initialize list of scan folders
                    List<string> subFolders = [];

                    // check for initial scan folders
                    if (initialScanFolders.Length > 0)
                    {
                        // get folder list
                        string[] folders = initialScanFolders.Split('|');
                        // for each folder
                        foreach (string folder in folders)
                        {
                            // add to scan folders
                            subFolders.Add(folder);
                        }
                    }
                    else
                    {
#if XPLANE || CONSOLE
                        subFolders.Add("");
#else
                        // add default scan folder
                        subFolders.Add("Airplanes");
                        subFolders.Add("Rotorcraft");
#endif
                    }

                    // create list of folders
                    List<string> scanFolders = [];

                    // we do this for MNSFS2020 only. MSFS2024 delievers the community
                    // models via the simulator as well as the default models
                    if (simulatorName == "Microsoft Flight Simulator 2020")
                    {
                        // add folder to list
                        scanFolders.Add(simFolder);
                    }
#if FS2024
                    else if (simulatorName == "Microsoft Flight Simulator 2024")
                    {
                        // simFolder + "\SimObjects\<folder>" is never a real path in a modern Community/
                        // Official package-based install - there's an extra package-name folder layer in
                        // between (e.g. simFolder\Community\<addon>\SimObjects\Airplanes\...), so this old
                        // flat-layout guess finds nothing here by default. Use packageFolderIndex instead,
                        // which already walks that layout correctly (see BuildPackageFolderIndexIfNeeded)
                        // and gives the exact leaf model folders directly - no recursive search even needed.
                        BuildPackageFolderIndexIfNeeded();
                        scanFolders.AddRange(packageFolderIndex.Values);
                    }
#endif
                    else
                    {
                        // for each folder
                        foreach (var folder in subFolders)
                        {
#if XPLANE || CONSOLE
                            // add folder to list
                            scanFolders.Add(Path.Combine(simFolder, "Aircraft", folder));
#else
                            // add folder to list
                            scanFolders.Add(simFolder + Path.DirectorySeparatorChar + "SimObjects" + Path.DirectorySeparatorChar + folder);
#endif
                        }
                    }

#if !XPLANE
                    // check for initial additionals
                    if (initialAdditionals.Length > 0)
                    {
                        // get folder list
                        string[] folders = initialAdditionals.Split('|');
                        // for each folder
                        foreach (string folder in folders)
                        {
                            // add to scan folders
                            scanFolders.Add(folder);
                        }
                    }
#endif

                    // check for P3D
                    if (simulatorName.Contains("Prepar3D"))
                    {
                        // create path list
                        List<string> simobjectsList = [];

                        // search for all aircraft.cfg in SimObjects
                        SearchForFolders(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prepar3D v4 Add-ons"), "Simobjects", simobjectsList, 0);

                        // for each simobjects folder
                        foreach (var simobjectsFolder in simobjectsList)
                        {
                            // for each sub folder
                            foreach (var subFolder in subFolders)
                            {
                                // add folder to list
                                scanFolders.Add(Path.Combine(simobjectsFolder, subFolder));
                            }
                        }
                    }

                    // clear current models
                    lock (main.conch)
                    {
                        models.Clear();
                    }

#if XPLANE || CONSOLE
                    // create path list
                    List<string> pathList = new List<string>();

                    // if interactive scan then auto-generate CSL
                    if (interactive && main.settingsGenerateCsl)
                    {
                        try
                        {
                            // for each folder
                            foreach (var folder in scanFolders)
                            {
                                // check for folder
                                if (Directory.Exists(folder))
                                {
                                    // search for all aircraft.cfg in SimObjects
                                    SearchForFiles(folder, "*.acf", pathList, 0);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            main.MonitorEvent("Failed to search folder." + ex.Message);
                        }

                        // for each file
                        foreach (var path in pathList)
                        {
                            // get aircraft subfolder
                            string subFolder = Path.GetDirectoryName(path.Substring(simFolder.Length + 1));
                            // split by folder seperator
                            string[] names = path.Split('\\');
                            if (names.Length >= 4)
                            {
                                // generate CSL for default
                                main.sim ?. xplane.GenerateCsl(simFolder, subFolder, path, names[names.Length - 2], "default", true);

                                //// create livery list
                                //List<string> liveryList = new List<string>();
                                //// get livery folder
                                //string liveryFolder = Path.Combine(Path.GetDirectoryName(path), "liveries");
                                //// check for folder
                                //if (Directory.Exists(liveryFolder))
                                //{
                                //    // search for all liveries in SimObjects
                                //    liveryList.AddRange(Directory.GetDirectories(liveryFolder));
                                //    // for each livery
                                //    foreach (var liveryPath in liveryList)
                                //    {
                                //        // generate CSL for livery
                                //        main.sim ?. xplane.GenerateCsl(simFolder, subFolder, path, names[names.Length - 2], Path.GetFileNameWithoutExtension(liveryPath), false);
                                //    }
                                //}
                            }
                        }
                    }

                    // clear paths
                    pathList.Clear();

                    // get CSL folder
                    string cslFolder = Path.Combine(simFolder, "Resources", "plugins", "JoinFS", "Resources", "CSL");

                    // check for folder
                    if (Directory.Exists(cslFolder))
                    {
                        // search for all xsb_aircraft files
                        SearchForFiles(cslFolder, "xsb_aircraft.txt", pathList, 0);
                    }
                    else
                    {
                        // monitor
                        main.MonitorEvent("Unable to locate CSL folder, " + cslFolder);
                    }

                    // for each file
                    foreach (var path in pathList)
                    {
                        string manufacturer = "UNKNOWN";
                        // open file
                        StreamReader reader = null;

                        try
                        {
                            // open file
                            reader = File.OpenText(path);
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                // parse line
                                string[] words = line.TrimStart(' ').Split(' ');
                                // check for valid line
                                if (words.Length > 1)
                                {
                                    // get command
                                    string command = words[0].ToUpper();
                                    // check for EXPORT_NAME
                                    if (command == "EXPORT_NAME")
                                    {
                                        // get manufacturer
                                        manufacturer = words[1];
                                    }
                                    // check for ICAO
                                    else if (command == "MATCHES" || command == "ICAO" || command == "AIRLINE" || command == "LIVERY")
                                    {
                                        // get manufacturer
                                        scanManufacturer = manufacturer;
                                        // get type
                                        scanType = words[1];
                                        // get variation
                                        if (words.Length > 3) scanVariation = words[2] + " " + words[3];
                                        else if (words.Length == 3) scanVariation = words[2];
                                        else scanVariation = "000";
                                        // get title
                                        if (words.Length >= 3 && words[2] == "JFS") scanTitle = scanType + " " + scanVariation;
                                        else scanTitle = scanType + " " + scanManufacturer + " " + scanVariation;
                                        // submit the current scan
                                        scanBlock = true;
                                        SubmitScan();
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // monitor
                            main.MonitorEvent("Failed to read file '" + path + "'. " + ex.Message);
                        }
                        finally
                        {
                            // close file
                            if (reader != null) reader.Close();
                        }
                    }
#else
                    // create path list
                    List<string> pathList = [];

                    try
                    {
                        // for each folder
                        foreach (var folder in scanFolders)
                        {
                            // check for folder
                            if (Directory.Exists(folder))
                            {
                                // search for all aircraft.cfg in SimObjects
                                SearchForFiles(folder, "aircraft.cfg", pathList, 0);
                                // not for MSF
                                if (simulatorName != "Microsoft Flight Simulator 2020")
                                {
                                    // search for all sim.cfg in Rotorcraft
                                    SearchForFiles(folder, "sim.cfg", pathList, 0);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        main.MonitorEvent("Failed to search folder." + ex.Message);
                    }

                    // for each file
                    foreach (var path in pathList)
                    {
                        // get folder name
                        scanFolder = Path.GetFileName(Path.GetDirectoryName(path));

                        // track the maximum smoke entry
                        int smokeCount = 0;
                        int startIndex = models.Count;
                        StreamReader reader = null;

                        // one broken/inaccessible file (permission denied, locked by another
                        // process, a corrupt/truncated config) must not abort scanning every
                        // other installed aircraft - catch it here and move on to the next file,
                        // same convention as the XPLANE branch of this same method above
                        try
                        {
                        // create reader
                        reader = new(path);

                        // [GENERAL] section values - apply once per file to every model found in it
                        string generalIcaoType = "";
                        string generalIcaoModel = "";
                        string generalWtc = "";
                        string generalCategory = "";

                        // for each line the file
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            // check for block
                            if (line.StartsWith('['))
                            {
                                // submit the current scan
                                SubmitScan();
                                // check for model block
                                if (line[1..].StartsWith("fltsim", StringComparison.OrdinalIgnoreCase))
                                {
                                    // within model block
                                    scanBlock = true;
                                }
                            }
                            // check for title string
                            if (line.StartsWith("title", StringComparison.OrdinalIgnoreCase))
                            {
                                // get title string
                                scanTitle = Trim(line[5..]);
                            }
                            else if (line.StartsWith("ui_typerole", StringComparison.OrdinalIgnoreCase))
                            {
                                // get typerole string
                                scanTyperole = TrimQuotes(line[11..]);
                            }
                            else if (line.StartsWith("ui_manufacturer", StringComparison.OrdinalIgnoreCase))
                            {
                                // get manufacturer string
                                scanManufacturer = TrimQuotes(line[15..]);
                            }
                            else if (line.StartsWith("ui_type", StringComparison.OrdinalIgnoreCase))
                            {
                                // get type string
                                scanType = TrimQuotes(line[7..]);
                            }
                            else if (line.StartsWith("ui_variation", StringComparison.OrdinalIgnoreCase))
                            {
                                // get variation string
                                scanVariation = TrimQuotes(line[12..]);
                            }
                            else if (line.StartsWith("model", StringComparison.OrdinalIgnoreCase))
                            {
                                // get model string
                                scanModel = TrimQuotes(line[5..]);
                            }
                            else if (line.StartsWith("texture", StringComparison.OrdinalIgnoreCase))
                            {
                                // get texture string
                                scanTexture = TrimQuotes(line[7..]);
                            }
                            else if (line.StartsWith("icao_type_designator", StringComparison.OrdinalIgnoreCase))
                            {
                                // get ICAO type designator, e.g. "EC45"
                                generalIcaoType = TrimQuotes(line[20..]);
                            }
                            else if (line.StartsWith("icao_model", StringComparison.OrdinalIgnoreCase))
                            {
                                // descriptive-only model name per the MSFS SDK, not meant for matching -
                                // read anyway as a fallback for add-ons that put the real designator here
                                // instead of in icao_type_designator (see ResolveConfirmedIcaoType/
                                // IsRecognizedIcaoType below)
                                generalIcaoModel = TrimQuotes(line[10..]);
                            }
                            else if (line.StartsWith("icao_WTC", StringComparison.OrdinalIgnoreCase))
                            {
                                // get wake turbulence category, e.g. "L" or "L/M"
                                generalWtc = TrimQuotes(line[8..]);
                                // normalize compound values like "L/M" to a single leading letter
                                int slashIndex = generalWtc.IndexOf('/');
                                if (slashIndex >= 0) generalWtc = generalWtc[..slashIndex];
                            }
                            else if (line.StartsWith("icao_airline", StringComparison.OrdinalIgnoreCase))
                            {
                                // get ICAO airline operator code - per-livery, e.g. "AEE"
                                scanIcaoAirline = TrimQuotes(line[12..]);
                            }
                            else if (line.StartsWith("atc_id", StringComparison.OrdinalIgnoreCase) &&
                                (line.Length == 6 || (line[6] != '_' && !char.IsLetterOrDigit(line[6]))))
                            {
                                // get registration/tail number - per-livery, e.g. "D-AJOE" - the length/char
                                // check excludes atc_id_enable/atc_id_color/atc_id_font, which share the prefix
                                scanAtcId = TrimQuotes(line[6..]);
                            }
                            else if (line.StartsWith("category", StringComparison.OrdinalIgnoreCase))
                            {
                                // get [GENERAL] category, e.g. "Airplane"/"Helicopter"/"Boat"/"GroundVehicle"
                                generalCategory = TrimQuotes(line[8..]);
                            }
                            else if (line.StartsWith("smoke.", StringComparison.OrdinalIgnoreCase))
                            {
                                // get smoke line
                                string[] parts = line[6..].Split(' ', '=');
                                if (parts.Length > 0)
                                {
                                    // get smoke value
                                    int.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out int entry);
                                    int count = entry + 1;
                                    // check if value is greater than current smoke count
                                    if (count > smokeCount)
                                    {
                                        // use higher value
                                        smokeCount = count;
                                    }
                                }
                            }
                        }

                        // submit the current scan
                        SubmitScan();

                        // exclude boats and ground vehicles from the models found in this file - keep permissive
                        // for "Airplane"/"Helicopter"/empty (many legitimate aircraft.cfg files omit category)
                        if (generalCategory.Equals("Boat", StringComparison.OrdinalIgnoreCase) || generalCategory.Equals("GroundVehicle", StringComparison.OrdinalIgnoreCase))
                        {
                            if (models.Count > startIndex)
                            {
                                models.RemoveRange(startIndex, models.Count - startIndex);
                            }
                        }
                        else
                        {
#if FS2024
                            // consolidate onto the same shared config-reading logic the SimConnect-
                            // enumeration path (SubmitScan) and the live LIVERY FOLDER read already use,
                            // instead of the hand-rolled generalIcaoType/generalIcaoModel/generalWtc
                            // collected above - that never follows base_container and never marks
                            // classCodeConfirmed/configConfirmed, so even a successfully-found real file
                            // still showed as guessed/unconfirmed downstream (e.g. Explain Match)
                            string fileIcaoType = "", fileWtc = "", fileIcaoAirline = "", fileAtcId = "", fileClassCode = "", fileResolutionNote = "";
                            TryParseAircraftConfigFile(path, "", ref fileIcaoType, ref fileWtc, ref fileIcaoAirline, ref fileAtcId, ref fileClassCode, ref fileResolutionNote, out string fileBaseContainer);
                            if ((fileIcaoType.Length == 0 || fileWtc.Length == 0) && fileBaseContainer.Length > 0)
                            {
                                string baseFolder = ResolveBaseContainer(Path.GetDirectoryName(path), fileBaseContainer);
                                if (baseFolder != null)
                                {
                                    TryParseAircraftConfigFile(Path.Combine(baseFolder, "aircraft.cfg"), "", ref fileIcaoType, ref fileWtc, ref fileIcaoAirline, ref fileAtcId, ref fileClassCode, ref fileResolutionNote, out _);
                                }
                            }
                            bool fileConfirmed = fileIcaoType.Length > 0;
#endif
                            // for each new model
                            for (int index = startIndex; index < models.Count; index++)
                            {
                                // set smoke count
                                models[index].smokeCount = smokeCount;
#if FS2024
                                if (models[index].icaoType.Length == 0 && fileConfirmed)
                                {
                                    models[index].icaoType = fileIcaoType;
                                    models[index].icaoGuessed = false;
                                    models[index].classCode = fileClassCode;
                                    models[index].classCodeConfirmed = true;
                                    models[index].configConfirmed = true;
                                    if (fileWtc.Length > 0) models[index].wtc = fileWtc;
                                    if (fileIcaoAirline.Length > 0) { models[index].icaoAirline = fileIcaoAirline; models[index].icaoAirlineGuessed = false; }
                                    if (fileAtcId.Length > 0) models[index].atcId = fileAtcId;
                                    models[index].RefreshIcaoDerived(doc8643Lookup);
                                }
#else
                                if (generalIcaoType.Length > 0)
                                {
                                    // some add-ons put the real designator in icao_model instead of
                                    // icao_type_designator - prefer whichever field is actually recognized
                                    models[index].icaoType = IsRecognizedIcaoType(generalIcaoType) ? generalIcaoType
                                        : IsRecognizedIcaoType(generalIcaoModel) ? generalIcaoModel
                                        : generalIcaoType;
                                    if (generalWtc.Length > 0) models[index].wtc = generalWtc;
                                    models[index].RefreshIcaoDerived(doc8643Lookup);
                                }
#endif
                            }
                        }
                        }
                        catch (Exception ex)
                        {
                            // one broken/inaccessible file must not abort scanning every other
                            // installed aircraft - log it and move on to the next file, same
                            // convention as the XPLANE branch of this same method above
                            main.MonitorEvent("Failed to read file '" + path + "'. " + ex.Message);
                            // discard any model(s) already added from this file before the
                            // failure - a file that misbehaved partway through can't be trusted
                            // to have parsed correctly up to that point
                            if (models.Count > startIndex)
                            {
                                models.RemoveRange(startIndex, models.Count - startIndex);
                            }
                            // a failure mid-read can leave an in-progress [fltsim.N] block's
                            // fields (scanTitle etc.) sitting in the method-level scan* fields
                            // without having gone through SubmitScan()'s own reset - clear the
                            // flag that gates SubmitScan() using them, so the next file's first
                            // block boundary doesn't fold this broken file's stale/partial data
                            // into a new model
                            scanBlock = false;
                        }
                        finally
                        {
                            if (reader != null) reader.Close();
                        }
                    }

                    // rebuild ICAO indexes now that models[] has been populated by the scan
                    MakeIcaoIndex();

                    if (simulatorName == "Microsoft Flight Simulator 2020")
                    {
                        if ((AddonsFileContents[0] != ""))
                        {
                            try
                            {
                                lock (main.conch)
                                {

                                    string lastaddon = "";
                                    int AddOnnmodels = 0;
                                    foreach (string line in AddonsFileContents)
                                    {
                                        string[] separator = [ "[+]" ];
                                        string[] parts = line.Split(separator, StringSplitOptions.None);
                                        //count addons and split lines
                                        lastaddon = parts[0];
                                        bool ThisAddonSelected = initialAddOns.Contains(lastaddon);

                                        // check that model is not already present
                                        if (ModelExists(parts[1]) == false && ThisAddonSelected)
                                        {
                                            SubmitModel(parts[1], parts[2], parts[3], parts[4], 0, parts[6]);
                                            AddOnnmodels++;
                                        }
                                    }

                                    // message
                                    main.MonitorEvent("Loaded " + AddOnnmodels + " AddOn Models");

                                }
                            }
                            catch (Exception ex)
                            {
                                main.ShowMessage(ex.Message);
                            }
                        }
                        
                        else
                        {
                            main.MonitorEvent("FS2020: No AddOns");
                        }
                    }
                    // check for MSFS2024
                    if (simulatorName == "Microsoft Flight Simulator 2024")
                    {
#if FS2024
                        // pre-warm the title/folder index here so the one-time walk of every installed
                        // package's aircraft.cfg happens before SimConnect enumeration responses start
                        // arriving on the locked main thread (ProcessModelList/SubmitScan), which is where
                        // TryReadConfigFromLiveryFolder would otherwise trigger it lazily and stall that
                        // thread instead. This only actually runs off the main/UI thread when Scan() got
                        // here via the auto-on-connect path (main.ScheduleSubstitutionLoad() -> Task.Run,
                        // Program.cs) - a manual "Scan For Models" (ScanUI() -> Scan(true)) still runs this
                        // (and the rest of Scan()'s own file walk, pre-existing behavior) on the UI thread,
                        // same as it already could before this change for a large install.
                        BuildTitleFolderIndexIfNeeded();
#endif
                        // check for initial addons
                        if (initialAddOns.Length > 0)
                        {
                            // get addons
                            string[] addOns = initialAddOns.Split('|');
                            // for each addon
                            foreach (var addOn in addOns)
                            {
                                if (addOn == "My MSFS 2024")
                                {
                                    ScanSimForModels();
                                }
                            }
                        }
                        else
                        {
#if FS2024
                            // with no addons in the list, we must trigger the model matching and save manually
                            main.EnqueueCommand(() =>
                            {
                                main.substitution?.Match();
                            });
                            // we must show the number of models as well
                            if (interactive)
                            {
                                if (models.Count > 0)
                                {
                                    main.scheduleShowMessage = Resources.Strings.FoundPrefix + " " + models.Count.ToString() + " " + Resources.Strings.FoundSuffix;
                                }
                                else
                                {
                                    main.scheduleShowMessage = "No models found";
                                }
                            }
#endif
                        }
                    }
#endif

#if !FS2024
                    // other sims than FS2024
                    // FS2024 has async loading of models
                    main.EnqueueCommand(() =>
                    {
                        main.substitution?.Match();
                    });
#endif
                    // check for models scanned
                    if (models.Count > 0)
                    {
// TODO: cleanup code
//                        if(main.settingsUseAIFeatures)
//                        {
//                            main.EnqueueCommand(async () =>
//                            {
//                                await main.substitution.enrichModelService.EnrichModelsWithDetailsAsync(models);
//                                main.MonitorEvent("Model data enriched");
//#if X64
//                                await main.substitution.embeddingService.GenerateEmbeddingsFromModelsAsync(models);
//                                main.MonitorEvent("Model data enriched");
//#endif
//                            });
//                        }
                        main.MonitorEvent("Scan found " + models.Count + ((models.Count == 1) ? " model" : " models") + " in the community folder(s)");
                    }
                    else
                    {
                        main.MonitorEvent("Scan found no models");
                    }

                }

#if !CONSOLE
                // no longer busy
                Cursor.Current = Cursors.Default;
#endif
                // finished
                return true;
            }

            // unable to do scan
            return false;
        }

#if !SERVER && !CONSOLE
        /// <summary>Guards against a second manual "Scan For Models" click firing an overlapping
        /// background Scan() while one triggered by ScanUI() is already in flight.</summary>
        volatile bool manualScanRunning = false;
#endif

        /// <summary>
        /// Scan simulator folders for models
        /// </summary>
        public bool ScanUI()
        {
#if !SERVER && !CONSOLE

#if XPLANE
            if (main.sim != null)
            {
                // show dialog for choosing match model
                ScanForm_XPLANE scanForm = new ScanForm_XPLANE(main, simFolder, initialScanFolders);
#elif SIMCONNECT
            // check if simulator is not connected
            if (main.sim != null && main.sim.Connected)
            {
                // show dialog for choosing match model
                ScanForm scanForm = new(main, simFolder, initialScanFolders, initialAddOns, initialAdditionals);
#endif

                // open dialog
                switch (scanForm.ShowDialog())
                {
                    case System.Windows.Forms.DialogResult.OK:
                        {
                            // get simfolder
                            simFolder = scanForm.GetFolder();

                            // saved scan folders
                            initialScanFolders = "";
                            initialAddOns = "";
                            initialAdditionals = "";

                            // for each scan folder
                            foreach (string folder in scanForm.scanFolders)
                            {
                                // check if folder exists
                                if (scanForm.folderList.Contains(folder))
                                {
                                    // if not first folder
                                    if (initialScanFolders.Length > 0)
                                    {
                                        // add seperator
                                        initialScanFolders += '|';
                                    }
                                    // add folder
                                    initialScanFolders += folder;
                                }
                            }

#if !XPLANE
                            // for each addon
                            for (int index = 0; index < scanForm.addOns.Count && index < scanForm.addOnsSelected.Length; index++)
                            {
                                // check if add is selected
                                if (scanForm.addOnsSelected[index])
                                {
                                    // if not first addon
                                    if (initialAddOns.Length > 0)
                                    {
                                        // add seperator
                                        initialAddOns += '|';
                                    }
                                    // add addon
                                    initialAddOns += scanForm.addOns[index];
                                }
                            }

                            // for each additional folder
                            foreach (var folder in scanForm.GetAdditionals())
                            {
                                // check for folder
                                if (folder.Length > 0)
                                {
                                    // if not first folder
                                    if (initialAdditionals.Length > 0)
                                    {
                                        // add seperator
                                        initialAdditionals += '|';
                                    }
                                    // add folder to list
                                    initialAdditionals += folder;
                                }
                            }
#endif

                            // save folders
                            SaveFolders();

#if XPLANE
                            // warning message
                            if (Settings.Default.GenerateCsl == false || MessageBox.Show(Resources.Strings.GenerateCslWarning, Main.Name, MessageBoxButtons.OKCancel) == DialogResult.OK)
#else
                            if (true)
#endif
                            {
                                // Scan() itself can take many seconds (a full aircraft.cfg directory walk,
                                // plus for FS2024 the per-model disk-config reads added above) - run it and
                                // its own follow-up off the UI thread so a manual "Scan For Models" doesn't
                                // freeze the app, matching why the auto-on-connect scan already does this
                                // (see Program.cs's scheduleSubstitutionLoad dispatch). manualScanRunning
                                // guards against a second click firing an overlapping scan while one is
                                // already in flight.
                                if (manualScanRunning == false)
                                {
                                    manualScanRunning = true;
                                    Task.Run(() =>
                                    {
                                        try
                                        {
                                            // do model scan
                                            Scan(true);

                                            // reload matches
#if FS2024
                                            main.sim.requestModelListIsVerbose = true;
#else
                                            LoadMatches();
                                            LoadMasquerades();

                                            // check for models scanned
                                            if (models.Count > 0)
                                            {
                                                main.scheduleShowMessage = Resources.Strings.FoundPrefix + " " + models.Count.ToString() + " " + Resources.Strings.FoundSuffix;
                                            }
                                            else
                                            {
                                                main.scheduleShowMessage = "No models found";
                                            }
#endif
                                        }
                                        catch (Exception ex)
                                        {
                                            main.MonitorEvent("Error during manual model scan: " + ex);
                                        }
                                        finally
                                        {
                                            manualScanRunning = false;
                                        }
                                    });
                                }
                            }
                        }
                        break;
                }

                return true;
            }
            else
            {
                // no simulator connected
                main.ShowMessage(Resources.Strings.ScanWarning);
            }

#endif // !SERVER
            return false;
        }

        /// <summary>
        /// List of model prefixes
        /// </summary>
        readonly Dictionary<string, string> prefixList = [];

        /// <summary>
        /// Make a list of model prefix strings
        /// </summary>
        void MakePrefixList()
        {
            // clear list
            prefixList.Clear();

            // for each model

                foreach (var model in models)
                {
                // for each prefix length
                for (int length = 4; length <= model.title.Length; length++)
                {
                    // make key
                    string key = model.title[..length];
                    // check if prefix not yet listed
                    if (prefixList.ContainsKey(key) == false)
                    {
                        // add prefix entry to list
                        prefixList.Add(model.title[..length], model.title);
                    }
                }
            }
        }

        /// <summary>
        /// Make the filename from the simulator name and version
        /// </summary>
        /// <returns></returns>
        public string MakeModelsFilename()
        {
            return main.storagePath + Path.DirectorySeparatorChar + "models - " + (main.sim != null ? main.sim.GetSimulatorName() : "null") + ".txt";
        }

        /// <summary>
        /// Make the filename from the simulator name and version
        /// </summary>
        /// <returns></returns>
        public string MakeMatchingFilename()
        {
            return main.storagePath + Path.DirectorySeparatorChar + "matching - " + (main.sim != null ? main.sim.GetSimulatorName() : "null") + ".txt";
        }

        /// <summary>
        /// Make the filename from the simulator name and version
        /// </summary>
        /// <returns></returns>
        public string MakeMasqueradingFilename()
        {
            return main.storagePath + Path.DirectorySeparatorChar + "masquerading - " + (main.sim != null ? main.sim.GetSimulatorName() : "null") + ".txt";
        }

        /// <summary>
        /// Load models from file
        /// </summary>
        void LoadModels()
        {
            // check for simulator
#if XPLANE || CONSOLE
            if (main.sim != null)
#else
            if (main.sim != null && main.sim.Connected)
#endif
            {
                try
                {
                    // check for existing 2020 file and legacy file
                    if (File.Exists(main.storagePath + Path.DirectorySeparatorChar + "models - Microsoft Flight Simulator 2020.txt") == false && File.Exists(main.storagePath + Path.DirectorySeparatorChar + "models - KittyHawk.txt"))
                    {
                        // use legacy file
                        File.Move(main.storagePath + Path.DirectorySeparatorChar + "models - KittyHawk.txt", main.storagePath + Path.DirectorySeparatorChar + "models - Microsoft Flight Simulator 2020.txt");
                    }

                    // make filename
                    string filename = MakeModelsFilename();

                    // check for models file
                    if (File.Exists(filename))
                    {
                        // read all models from file
                        string[] lines = File.ReadAllLines(filename);
                        string[] separator = [ "|" ];
                        // for all lines
                        lock (main.conch)
                        {
                        foreach (string line in lines)
                        {
                            // ignore comments (comment lines begin with a #)
                            if (line.StartsWith('#'))
                            {
                                // check if line contains the "new separator" command
                                if (line.Contains("new separator"))
                                {
                                    separator = ["[+]"];
                                }
                                continue;
                            }
                            // ignore empty lines
                            if (line.Trim().Length == 0) continue;

                            // split line
                            string[] parts = line.Split(separator, StringSplitOptions.None);
                            // check if model is banned
                            if (IsModelBanned(parts[0], parts.Length > 3 ? parts[3] : ""))
                            {
                                // skip banned model
                                lastBanExclusionCount++;
                                continue;
                            }
                            // check that model is not already present
#if FS2024
                            if (ModelExists(parts[0], parts[3]) == false)
#else
                            if (ModelExists(parts[0]) == false)
#endif
                            {
                                // check for correct parts
                                if (parts.Length == 4)
                                {
                                    // add model
                                    models.Add(new Model(parts[0], parts[1], parts[2], parts[3], 0, "SingleProp", "1", ""));
                                }
                                else if (parts.Length == 5)
                                {
                                    // add model
                                    models.Add(new Model(parts[0], parts[1], parts[2], parts[3], 0, parts[4], "1", ""));
                                }
                                else if (parts.Length == 6)
                                {
                                    // add model
                                    models.Add(new Model(parts[0], parts[1], parts[2], parts[3], 0, parts[4], parts[5], ""));
                                }
                                else if (parts.Length == 7)
                                {
                                    // add model
                                    models.Add(new Model(parts[0], parts[1], parts[2], parts[3], 0, parts[4], parts[5], parts[6]));
                                }
                                else if (parts.Length >= 8)
                                {
                                    int.TryParse(parts[4], NumberStyles.Number, CultureInfo.InvariantCulture, out int index);
                                    // read new ICAO fields when present, tolerating files saved by an older build
                                    string icaoType = parts.Length > 8 ? parts[8] : "";
                                    string wtc = parts.Length > 9 ? parts[9] : "";
                                    string icaoAirline = parts.Length > 10 ? parts[10] : "";
                                    string classCode = parts.Length > 11 ? parts[11] : "";
                                    bool classCodeConfirmed = parts.Length > 12 && bool.TryParse(parts[12], out bool confirmed) && confirmed;
                                    // add model
                                    Model model = new(parts[0], parts[1], parts[2], parts[3], index, parts[5], parts[6], parts[7], icaoType, wtc, icaoAirline, classCode, classCodeConfirmed);
                                    model.RefreshIcaoDerived(doc8643Lookup);
                                    models.Add(model);
                                }
                            }
                        }
                        }

                        // message
                        main.MonitorEvent("Loaded " + models.Count + ((models.Count == 1) ? " model" : " models"));
                    }
                }
                catch (Exception ex)
                {
                    main.ShowMessage(ex.Message);
                }

                // make prefix list
                MakePrefixList();
                // make ICAO indexes
                MakeIcaoIndex();
            }
            else
            {
                // error
                main.MonitorEvent("Unable to load models because a simulator is not connected.");
            }
        }

        /// <summary>
        /// Save mode list
        /// </summary>
        void SaveModels()
        {
            // check for simulator
#if XPLANE || CONSOLE
            if (main.sim != null)
#else
            if (main.sim != null && main.sim.Connected)
#endif
            {
                try
                {
                    // make filename
                    string filename = MakeModelsFilename();

                    // open models file
                    StreamWriter writer = new(filename);
                    // write new separator command
                    writer.WriteLine("# new separator - DO NOT DELETE THIS LINE");
                    // for all models
                    foreach (var model in models)
                    {
                        // get typerole name
                        string typeroleName = typeroleNames.TryGetValue(model.typerole, out string value) ? value : "SingleProp";
                        // write model
                        writer.WriteLine(model.title + "[+]" + model.manufacturer + "[+]" + model.type + "[+]" + model.variation + "[+]" + model.index + "[+]" + typeroleName + "[+]" + model.smokeCount + "[+]" + model.folder + "[+]" + model.icaoType + "[+]" + model.wtc + "[+]" + model.icaoAirline + "[+]" + (model.classCodeConfirmed ? model.classCode : "") + "[+]" + model.classCodeConfirmed);
                    }
                    writer.Close();

                    // message
                    main.MonitorEvent("Saved " + models.Count + ((models.Count == 1) ? " model" : " models"));
                }
                catch (Exception ex)
                {
                    main.ShowMessage(ex.Message);
                }

                // make prefix list
                MakePrefixList();
                // make ICAO indexes
                MakeIcaoIndex();
            }
            else
            {
                // error
                main.MonitorEvent("Unable to save models because a simulator is not connected.");
            }
        }

        /// <summary>
        /// Load list of model matches
        /// </summary>
        void LoadMatches()
        {
#if XPLANE || CONSOLE
            if (main.sim != null)
#else
            // check for simulator
            if (main.sim != null && main.sim.Connected)
#endif
            {
                try
                {
                    // make filename
                    string filename = MakeMatchingFilename();

                    // check for existing file and legacy file
                    if (File.Exists(filename) == false && File.Exists(main.storagePath + Path.DirectorySeparatorChar + "matching.txt"))
                    {
                        // use legacy file
                        File.Move(main.storagePath + Path.DirectorySeparatorChar + "matching.txt", filename);
                    }

                    // check for existing 2020 file and legacy file
                    if (File.Exists(main.storagePath + Path.DirectorySeparatorChar + "matching - Microsoft Flight Simulator 2020.txt") == false && File.Exists(main.storagePath + Path.DirectorySeparatorChar + "matching - KittyHawk.txt"))
                    {
                        // use legacy file
                        File.Move(main.storagePath + Path.DirectorySeparatorChar + "matching - KittyHawk.txt", main.storagePath + Path.DirectorySeparatorChar + "matching - Microsoft Flight Simulator 2020.txt");
                    }

                    // check for matching file
                    if (File.Exists(filename))
                    {
                        // clear list
                        matches.Clear();

                        // open file
                        StreamReader reader = File.OpenText(filename);
                        string line;
                        string[] separator = [ "|" ];
                        while ((line = reader.ReadLine()) != null)
                        {
                            // ignore comments (comment lines begin with a #)
                            if (line.StartsWith('#'))
                            {
                                // check if line contains the "new separator" command
                                if (line.Contains("new separator"))
                                {
                                    separator = ["[+]"];
                                }
                                continue;
                            }
                            // ignore empty lines
                            if (line.Trim().Length == 0) continue;

                            // parse line
                            string[] parts = line.Split('=');
                            // check for two parts
                            if (parts.Length == 2)
                            {
                                Model model = null;
                                string[] subParts = parts[1].Split(separator, StringSplitOptions.None);
                                if (subParts.Length == 2)
                                {
                                    // find model
                                    model = GetModel(subParts[0], subParts[1]);
                                }
                                else
                                {
                                    // find model
                                    model = GetModel(parts[1]);
                                }

                                if (model != null)
                                {
                                    // add model match
                                    matches[parts[0]] = model;
                                }
                                else
                                {
                                    main.ShowMessage(Resources.Strings.NoSubstituteModel + ": " + line);
                                }
                            }
                            else
                            {
                                main.ShowMessage(Resources.Strings.InvalidSubstitution + ": " + line);
                            }
                        }
                        reader.Close();

                        main.MonitorEvent("Loaded " + matches.Count + " match substitutions");
                    }
                }
                catch (Exception ex)
                {
                    main.ShowMessage(ex.Message);
                }

                // remove all objects
                main.sim.ScheduleRemoveObjects();
            }
            else
            {
                // error
                main.MonitorEvent("Unable to load substitutions because a simulator is not connected.");
            }

            // refresh
#if !SERVER && !CONSOLE
            main.matchingForm ?. refresher.Schedule();
            main.aircraftForm ?. refresher.Schedule(3);
#endif

            // choose defaults
            ChooseDefaults();
        }

        /// <summary>
        /// Save match list
        /// </summary>
        void SaveMatches()
        {
            // check for simulator
#if XPLANE || CONSOLE
            if (main.sim != null)
#else
            if (main.sim != null && main.sim.Connected)
#endif
            {
                try
                {
                    // make filename
                    string filename = MakeMatchingFilename();

                    // open file
                    StreamWriter writer = new(filename);
                    if (writer != null)
                    {
                        // write new separator command
                        writer.WriteLine("# new separator - DO NOT DELETE THIS LINE");
                        // for each model match
                        foreach (var pair in matches)
                        {
                            // write model match
                            writer.WriteLine(pair.Key + "=" + pair.Value.title + "[+]" + pair.Value.variation);
                        }
                        writer.Close();
                    }

                    main.MonitorEvent("Saved " + matches.Count + " match substitutions");
                }
                catch (Exception ex)
                {
                    main.ShowMessage(ex.Message);
                }
            }
            else
            {
                // error
                main.MonitorEvent("Unable to save substitutions because a simulator is not connected.");
            }

#if !SERVER && !CONSOLE
            // refresh
            main.matchingForm ?. refresher.Schedule();
#endif
        }

        /// <summary>
        /// Load list of model matches
        /// </summary>
        void LoadMasquerades()
        {
            // check for simulator
#if XPLANE || CONSOLE
            if (main.sim != null)
#else
            if (main.sim != null && main.sim.Connected)
#endif
            {
                try
                {
                    // make filename
                    string filename = MakeMasqueradingFilename();

                    // check for matching file
                    if (File.Exists(filename))
                    {
                        // clear list
                        masquerades.Clear();

                        // open file
                        StreamReader reader = File.OpenText(filename);
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            // parse line
                            string[] parts = line.Split('=');
                            // check for three parts
                            if (parts.Length == 2)
                            {
                                // get info
                                string modelTitle = parts[0].TrimStart(' ').TrimEnd(' ');
                                string subTitle = parts[1].TrimStart(' ').TrimEnd(' ');
                                
                                // find sub model
                                Model model = GetModel(subTitle);
                                if (model != null)
                                {
                                    // add model match
                                    masquerades[modelTitle] = model;
                                }
                                else
                                {
                                    main.ShowMessage(Resources.Strings.NoSubstituteModel + ": " + line);
                                }
                            }
                            else
                            {
                                main.ShowMessage(Resources.Strings.InvalidSubstitution + ": " + line);
                            }
                        }
                        reader.Close();

                        main.MonitorEvent("Loaded " + masquerades.Count + " masquerade substitutions");
                    }
                }
                catch (Exception ex)
                {
                    main.ShowMessage(ex.Message);
                }
            }
            else
            {
                // error
                main.MonitorEvent("Unable to load model masquerading because a simulator is not connected.");
            }

#if !SERVER && !CONSOLE
            // refresh
            main.aircraftForm ?. refresher.Schedule(3);
#endif
        }

        /// <summary>
        /// Save match list
        /// </summary>
        void SaveMasquerades()
        {
            // check for simulator
#if XPLANE || CONSOLE
            if (main.sim != null)
#else
            if (main.sim != null && main.sim.Connected)
#endif
            {
                try
                {
                    // make filename
                    string filename = MakeMasqueradingFilename();

                    // open file
                    StreamWriter writer = new(filename);
                    if (writer != null)
                    {
                        // for each masquerade
                        foreach (var pair in masquerades)
                        {
                            // write model match
                            writer.WriteLine(pair.Key + "=" + pair.Value.title);
                        }
                        writer.Close();
                    }

                    main.MonitorEvent("Saved " + masquerades.Count + " masquerade substitutions");
                }
                catch (Exception ex)
                {
                    main.ShowMessage(ex.Message);
                }
            }
            else
            {
                // error
                main.MonitorEvent("Unable to save model masquerading because a simulator is not connected.");
            }
        }

        /// <summary>
        /// Save scan folders
        /// </summary>
        public void SaveFolders()
        {
            // check for sim
#if XPLANE || CONSOLE
            if (main.sim != null)
#else
            if (main.sim != null && main.sim.Connected)
#endif
            {
                WriteFoldersFile(main.sim.GetSimulatorName());
            }
        }

        /// <summary>
        /// Write the current folder settings to "folders - &lt;simulatorName&gt;.txt",
        /// independent of whether the simulator is connected yet.
        /// </summary>
        void WriteFoldersFile(string simulatorName)
        {
            try
            {
                // folders file
                string foldersFile = Path.Combine(main.storagePath, "folders - " + simulatorName + ".txt");
                // open models file
                StreamWriter writer = new(foldersFile);
                // write folders
                writer.WriteLine(simFolder);
                writer.WriteLine(initialScanFolders);
                writer.WriteLine(initialAddOns);
                writer.WriteLine(initialAdditionals);
                // close file
                writer.Close();
            }
            catch (Exception ex)
            {
                main.ShowMessage(ex.Message);
            }
        }

        /// <summary>
        /// On first install, try to resolve the simulator folder from the simulator's own
        /// recorded install location (UserCfg.opt / registry / x-plane_install_*.txt)
        /// instead of requiring the user to browse for it. Safe to call before the
        /// simulator is connected. Returns true if a folders file now exists (either it
        /// already did, or detection succeeded and it was just written); false means the
        /// caller should prompt the user to pick the folder manually.
        /// </summary>
        public bool EnsureFoldersConfigured(string fallbackSimulatorName, out string resolvedSimulatorName)
        {
            resolvedSimulatorName = fallbackSimulatorName;
            string detected = null;

#if P3D
            detected = SimPathDetector.TryDetect(out string detectedVersion);
            if (detected != null)
            {
                resolvedSimulatorName = detectedVersion;
            }
#elif FS2020 || FS2024 || FSX || XPLANE
            detected = SimPathDetector.TryDetect();
#endif

            // folders file
            string foldersFile = Path.Combine(main.storagePath, "folders - " + resolvedSimulatorName + ".txt");
            // check if already configured with a real folder (whether from a previous run,
            // or manually) - a file can exist but still have a blank first line, e.g. from an
            // earlier ScanForm session the user opened and left empty, or an older version
            // that created the file before a folder was ever chosen. Treat that the same as
            // "not configured" so detection still gets a chance to fill it in.
            if (File.Exists(foldersFile) && FoldersFileHasFolder(foldersFile))
            {
                return true;
            }

            // check if detection succeeded
            if (detected != null)
            {
                // use the detected folder, with no add-on/additional-folder selection yet
                simFolder = detected;
                initialScanFolders = "";
                initialAddOns = DefaultAddOns();
                initialAdditionals = "";
                WriteFoldersFile(resolvedSimulatorName);
                return true;
            }

            // caller should prompt the user
            return false;
        }

        /// <summary>
        /// True if a "folders - &lt;sim&gt;.txt" file exists and its first line (simFolder)
        /// is non-blank.
        /// </summary>
        static bool FoldersFileHasFolder(string foldersFile)
        {
            try
            {
                using StreamReader reader = new(foldersFile);
                string firstLine = reader.ReadLine();
                return string.IsNullOrWhiteSpace(firstLine) == false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Save a folder the user picked manually in the first-run setup dialog. Unlike
        /// <see cref="EnsureFoldersConfigured"/>, this always overwrites, and does not
        /// require the simulator to be connected.
        /// </summary>
        public void SaveManualFolder(string simulatorName, string folder)
        {
            simFolder = folder;
            initialScanFolders = "";
            initialAddOns = DefaultAddOns();
            initialAdditionals = "";
            WriteFoldersFile(simulatorName);
        }

        /// <summary>
        /// "My MSFS 2024" isn't a folder at all - it's the checkbox name Scan() looks for
        /// (Substitution.cs, "check for MSFS2024" branch) to decide whether to ask the
        /// running sim directly for its aircraft/livery list via SimConnect
        /// (ScanSimForModels -&gt; main.sim.RequestSimulatorModels()). Without it selected,
        /// FS2024's community models are never fetched even with a correct folder saved -
        /// so auto-detection and manual folder entry both need to turn it on by default,
        /// the same as if the user had ticked it in Scan For Models.
        /// </summary>
        static string DefaultAddOns()
        {
#if FS2024
            return "My MSFS 2024";
#else
            return "";
#endif
        }

        /// <summary>
        /// Load scan folders
        /// </summary>
        public void LoadFolders()
        {
            // check for sim
#if XPLANE || CONSOLE
            if (main.sim != null)
#else
            if (main.sim != null && main.sim.Connected)
#endif
            {
                // folders file
                string foldersFile = Path.Combine(main.storagePath, "folders - " + main.sim.GetSimulatorName() + ".txt");
                // check if folder exists
                if (File.Exists(foldersFile))
                {
                    // open file
                    StreamReader reader = new(foldersFile);
                    // read folders
                    simFolder = reader.ReadLine();
                    initialScanFolders = reader.ReadLine();
                    initialAddOns = reader.ReadLine();
                    initialAdditionals = reader.ReadLine();
                    // close file
                    reader.Close();
                }
                else
                {
#if !CONSOLE
                    // read old settings
                    simFolder = OldSettings.ReadString("SimFolder - " + main.sim.GetSimulatorName(), OldSettings.ReadString("SimFolder"));
                    initialScanFolders = OldSettings.ReadString("ScanFolders - " + main.sim.GetSimulatorName(), OldSettings.ReadString("ScanFolders"));
                    initialAddOns = OldSettings.ReadString("AddOns - " + main.sim.GetSimulatorName(), "Asobo Standard");
                    initialAdditionals = OldSettings.ReadString("ScanAdditionals - " + main.sim.GetSimulatorName(), OldSettings.ReadString("ScanAdditionals"));
#endif

                    // save folders
                    SaveFolders();
                }
            }
        }

#if FS2024
        public async Task LoadTypeClassifiersAsync()
        {
            // check for sim
            if (main.sim != null && main.sim.Connected)
            {
                // type classifiers file
                string typeClassifiersFile = Path.Combine(main.storagePath, "typeclassifiers - " + main.sim.GetSimulatorName() + ".txt");
                // download the file if it does not exist
                if (File.Exists(typeClassifiersFile) == false)
                {
                    // download the file from the repository (via CDN, with fork fallback)
                    await GitHubData.DownloadToFileAsync("JoinFS/util/model2type.txt", typeClassifiersFile, main.MonitorEvent);
                }
                // check if file exists
                if (File.Exists(typeClassifiersFile))
                {
                    // open file
                    StreamReader reader = new(typeClassifiersFile);
                    // read classifiers
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        // parse line
                        string[] parts = line.Split('|');
                        // check for three parts
                        if (parts.Length == 3)
                        {
                            // add classifier if doesn't exist
                            if (typeroleClassifier.ContainsKey(parts[1]) == false)
                            {
                                typeroleClassifier.Add(parts[1], (parts[2], parts[0]));
                            }
                        }
                    }
                    // close file
                    reader.Close();
                }
            }
        }
#endif

#if FS2020
        public async Task LoadAddonsListAsync()
        {
            // check for sim
            if (main.sim != null && main.sim.Connected)
            {
                // type classifiers file
                string AddonsFile = Path.Combine(main.storagePath, "Addons_FS2020.txt");
                string AddonsFile_Web = Path.Combine(main.storagePath, "Addons_FS2020_Web.txt");
                // Always refresh the AddOns file from the repository (via CDN, with fork fallback).
                await GitHubData.DownloadToFileAsync("JoinFS/util/Addons_FS2020.txt", AddonsFile_Web, main.MonitorEvent);
                // check if file exists
                if (File.Exists(AddonsFile_Web))
                {
                    File.Copy(AddonsFile_Web, AddonsFile, true);
                }
                if (File.Exists(AddonsFile))
                {
                    // read AddOns
                    AddonsFileContents = File.ReadAllLines(AddonsFile);
                }
                else
                {
                    main.MonitorEvent("Error: FS2020 AddOns List file not found");
                }
            }
        }
#endif


        public async Task LoadModelBanListAsync()
        {
            // check for sim
#if XPLANE || CONSOLE
            if (main.sim != null)
#else
            if (main.sim != null && main.sim.Connected)
#endif
            {
                // ban list file
                string banListFile = Path.Combine(main.storagePath, "bannedModels - " + main.sim.GetSimulatorName() + ".txt");
                // download the file if it does not exist
                if (File.Exists(banListFile) == false)
                {
                    // download the file from the repository (via CDN, with fork fallback)
                    await GitHubData.DownloadToFileAsync("JoinFS/util/bannedModels.txt", banListFile, main.MonitorEvent);
                }
                // check if file exists
                if (File.Exists(banListFile))
                {
                    // open file
                    StreamReader reader = new(banListFile);
                    // read ban list
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line != "" && !line.StartsWith('#'))
                        {
                            // add to ban list
                            modelBanList.Add(line);
                        }
                    }
                    // close file
                    reader.Close();
                }
            }
        }

        /// <summary>
        /// Load model matching
        /// </summary>
        /// <param name="simulatorName">Name of the simulator</param>
        /// <param name="simulatorVersion">Version of simulator</param>
        public void Load()
        {
            // check for simulator
#if XPLANE || CONSOLE
            if (main.sim != null)
#else
            if (main.sim != null && main.sim.Connected)
#endif
            {
                // load folders
                LoadFolders();
                // load ICAO Doc8643 reference data (needed by LoadModels() to derive classCode/typerole)
                LoadDoc8643Index();
                // load ICAO airline code reference data (used to validate/correct live ATC AIRLINE values)
                LoadIcaoAirlineIndex();
                // load models from file
                LoadModels();

#if FS2024
                LoadTypeClassifiersAsync().GetAwaiter().GetResult();
#endif
#if FS2020
                LoadAddonsListAsync().GetAwaiter().GetResult();
#endif

                LoadModelBanListAsync().GetAwaiter().GetResult();

                // check for scan setting
                if (main.settingsScan)
                {
                    // scan for models
                    Scan(false);
                }

                
            }
        }

        public void Match()
        {
            // check for simulator
#if XPLANE || CONSOLE
            if (main.sim != null)
#else
            if (main.sim != null && main.sim.Connected)
#endif
            {
                // check for no models
                if (models.Count == 0)
                {
                    // scan for models request
                    main.scheduleScanForModels = true;
                }

                // load matching
                LoadMatches();
                // load masquerades
                LoadMasquerades();

                // now we can save
                main.ScheduleSubstitutionSave();
            }
        }

        /// <summary>
        /// Save model matching
        /// </summary>
        public bool Save()
        {
#if FS2024
            if (main.sim != null && main.sim.requestModelListInProgress)
            {
                main.MonitorEvent("Trying to save matches while request from sim active");
                return false;
            }
#endif
            SaveModels();
            SaveMatches();
            SaveMasquerades();
            return true;
        }

        /// <summary>
        /// Clear all model matching
        /// </summary>
        public void Clear()
        {
            // clear all lists
            models.Clear();
            prefixList.Clear();
            matches.Clear();
            masquerades.Clear();

#if !SERVER && !CONSOLE
            main.matchingForm ?. refresher.Schedule();
            main.aircraftForm ?. refresher.Schedule();
#endif
        }

        /// <summary>
        /// Choose a default model
        /// </summary>
        public void ChooseDefaults()
        {
            try
            {
                // check for models
                if (models.Count > 0)
                {
                    // changed flag
                    bool changed = false;

                    // for each default model
                    foreach (var defaultModel in defaultModels)
                    {
                        // check for missing default
                        if (matches.ContainsKey(defaultModel.Value) == false && typeroleNames.ContainsKey(defaultModel.Key))
                        {
                            // find replace with model
                            Model model = models.Find(m => m.typerole.Equals(defaultModel.Key));
                            // check if typerole not found
                            // use first model
                            model ??= models[0];

                            // update model match
                            matches[defaultModel.Value] = model;
                            // changed
                            changed = true;
                            // monitor
                            main.MonitorEvent(defaultModel.Value + " set to '" + model.title + "'");
                        }
                    }

                    // fine-grained defaults - one per (typerole, classCode, wtc) combo, but only where
                    // there's an actual choice to make among 2+ installed candidates
                    Dictionary<(int typerole, string classCode, string wtc), List<Model>> groups = [];
                    foreach (var candidate in models)
                    {
                        if (candidate.classCode.Length != 3 || candidate.wtc.Length == 0) continue;

                        var key = (candidate.typerole, candidate.classCode, candidate.wtc);
                        if (groups.TryGetValue(key, out var list) == false)
                        {
                            list = [];
                            groups.Add(key, list);
                        }
                        list.Add(candidate);
                    }
                    foreach (var group in groups)
                    {
                        if (group.Value.Count < 2) continue;

                        if (fineDefaultModels.TryGetValue(group.Key, out string fineKey) == false)
                        {
                            string typeroleName = typeroleNames.TryGetValue(group.Key.typerole, out var tn) ? tn : group.Key.typerole.ToString();
                            fineKey = Resources.Strings.Default + " " + typeroleName + " " + group.Key.classCode + " " + group.Key.wtc;
                            fineDefaultModels.Add(group.Key, fineKey);
                        }

                        if (matches.ContainsKey(fineKey) == false)
                        {
                            Model model = group.Value[0];
                            matches[fineKey] = model;
                            changed = true;
                            main.MonitorEvent(fineKey + " set to '" + model.title + "' (" + group.Value.Count + " candidates)");
                        }
                    }

                    // check if changed
                    if (changed)
                    {
                        // save matches
                        main.ScheduleSubstitutionSave();
                    }
                }
            }
            catch (Exception ex)
            {
                main.ShowMessage(ex.Message);
            }
        }

        /// <summary>
        /// Apply a masquerade to all objects
        /// </summary>
        void ApplyMasquerade(string replaceModel, Model model)
        {
            // check for sim
            if (main.sim != null)
            {
                // for all objects in the sim
                foreach (var obj in main.sim.objectList)
                {
                    // check if replace model
                    if (obj.Injected == false && obj.ownerModel.Equals(replaceModel))
                    {
                        // set the substitute
                        obj.subModel = model;
                        obj.subType = (model != null) ? Type.Substitute : Type.Original;
                    }
                }
            }

#if !SERVER && !CONSOLE
            // refresh
            main.aircraftForm ?. refresher.Schedule();
#endif
        }

        /// <summary>
        /// Edit an existing match
        /// </summary>
        /// <param name="modelTitle"></param>
        /// <param name="typerole"></param>
        /// <returns></returns>
#if FS2024
        public bool EditMatch(string modelTitle, string modelVariation, int typerole)
#else
        public bool EditMatch(string modelTitle, int typerole)
#endif
        {
#if !SERVER && !CONSOLE
            // check for some models
            if (models.Count > 0)
            {
                try
                {
                    // show dialog for choosing match model
#if FS2024
                    SubstitutionForm substitutionForm = new(main, modelTitle, modelVariation, typerole);
#else
                    SubstitutionForm substitutionForm = new SubstitutionForm(main, modelTitle, typerole);
#endif
                    switch (substitutionForm.ShowDialog())
                    {
                        case System.Windows.Forms.DialogResult.OK:
                            lock (main.conch)
                            {
                                // find replace with model
#if FS2024
                                Model model = GetModel(substitutionForm.GetWithModel(), substitutionForm.GetWithVariation());
#else
                                Model model = GetModel(substitutionForm.GetWithModel());
#endif
                                if (model != null)
                                {
                                    // update model match
                                    matches[substitutionForm.GetReplaceModel()] = model;
                                    main.ScheduleSubstitutionSave();
                                    // remove aircraft using the selected model
                                    main.sim ?. ScheduleRemoveModel(substitutionForm.GetReplaceModel());
                                    // refresh
                                    main.aircraftForm ?. refresher.Schedule(2);
                                }
                            }
                            return true;

                        case System.Windows.Forms.DialogResult.No:
                            lock (main.conch)
                            {
                                // remove this model match
                                matches.Remove(modelTitle);
                                main.ScheduleSubstitutionSave();
                                // remove aircraft using the selected model
                                main.sim ?. ScheduleRemoveModel(modelTitle);
                                // refresh
                                main.aircraftForm ?. refresher.Schedule(2);
                            }
                            return true;
                    }
                }
                catch (Exception ex)
                {
                    main.ShowMessage(ex.Message);
                }

                return false;
            }
            else
            {
                MessageBox.Show(Resources.Strings.MatchingForm_EmptyModelList, Main.Name + ": Model Matching");
            }
#endif
                                return false;
        }

        /// <summary>
        /// Edit an existing masquerade
        /// </summary>
        /// <param name="modelTitle"></param>
        /// <param name="typerole"></param>
        /// <returns></returns>
#if FS2024
        public bool EditMasquerade(string modelTitle, string modelVariation, int typerole)
#else
        public bool EditMasquerade(string modelTitle, int typerole)
#endif
        {
#if !SERVER && !CONSOLE
            // check for some models
            if (models.Count > 0)
            {
                try
                {
                    // show dialog for choosing match model
#if FS2024
                    SubstitutionForm substitutionForm = new(main, modelTitle, modelVariation, typerole);
#else
                    SubstitutionForm substitutionForm = new SubstitutionForm(main, modelTitle, typerole);
#endif
                    switch (substitutionForm.ShowDialog())
                    {
                        case System.Windows.Forms.DialogResult.OK:
                            lock (main.conch)
                            {
                                // find replace with model
#if FS2024
                                Model model = GetModel(substitutionForm.GetWithModel(), substitutionForm.GetWithVariation());
#else
                                Model model = GetModel(substitutionForm.GetWithModel());
#endif
                                if (model != null)
                                {
                                    // update model masquerade
                                    masquerades[modelTitle] = model;
                                    main.ScheduleSubstitutionSave();
                                    // apply
                                    ApplyMasquerade(modelTitle, model);
                                }
                            }
                            return true;

                        case System.Windows.Forms.DialogResult.No:
                            lock (main.conch)
                            {
                                // remove this model match
                                masquerades.Remove(modelTitle);
                                main.ScheduleSubstitutionSave();
                                // apply
                                ApplyMasquerade(modelTitle, null);
                            }
                            return true;
                    }
                }
                catch (Exception ex)
                {
                    main.ShowMessage(ex.Message);
                }

                return false;
            }
            else
            {
                MessageBox.Show(Resources.Strings.MatchingForm_EmptyModelList, Main.Name + ": Model Masquerading");
            }
#endif
                                return false;
        }

        /// <summary>
        /// Type of match
        /// </summary>
        public enum Type
        {
            Original,
            Substitute,
            Auto,
            Default,
            AI,
            /// <summary>Same ICAO type designator - livery chosen by operator preference or first available</summary>
            Icao,
            /// <summary>Same Doc8643 category/classCode - livery chosen the same way</summary>
            Category
        }

        /// <summary>
        /// Attribute compared between the remote aircraft's request and the matched model, for diagnostics/UI display
        /// </summary>
        public enum MatchAttribute
        {
            Title,
            Livery,
            Registration,
            IcaoType,
            IcaoAirline,
            ClassCode,
            Wtc,
            Typerole,
            Folder
        }

        /// <summary>
        /// Step-by-step account of how Match() arrived at its result, for diagnostics/UI display (Explain Match).
        /// Plain data only - no logic - so it can be serialized directly for export.
        /// </summary>
        public class MatchTrace
        {
            public class AttributeComparison
            {
                public MatchAttribute attribute;
                public string requested = "";
                public string matched = "";
                /// <summary>True when this attribute is what actually drove the winning tier's decision</summary>
                public bool decisive = false;
                /// <summary>Points this attribute contributed to the winning candidate's score</summary>
                public int scoreContribution = 0;
                /// <summary>True when the contribution was reduced because the matched model's tag was guessed, not confirmed</summary>
                public bool wasDownweighted = false;
            }

            /// <summary>One scored candidate, for the "other candidates considered" transparency panel</summary>
            public class Candidate
            {
                public string title = "";
                public string variation = "";
                public int totalScore = 0;
                public List<string> contributions = [];
            }

            /// <summary>Requested-vs-matched value for every MatchAttribute, in enum declaration order</summary>
            public List<AttributeComparison> attributes = [];
            /// <summary>Ordered, human-readable account of what each tier tried and found</summary>
            public List<string> steps = [];
            /// <summary>Top scored candidates from the unified scorer, winner first - empty for fast-path/Default results</summary>
            public List<Candidate> topCandidates = [];
        }

        /// <summary>
        /// Strip everything but letters/digits, for comparing registrations/callsigns against installed
        /// titles/variations regardless of how dashes/underscores/spaces are used in either one
        /// (e.g. "D-AJOE" vs. a livery folder named "DAJOE_Eurowings_Europapark").
        /// </summary>
        static string AlnumOnly(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            Span<char> buf = stackalloc char[s.Length];
            int n = 0;
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c)) buf[n++] = c;
            }
            return new string(buf[..n]);
        }

        /// <summary>
        /// Minimum total score a candidate must reach to be accepted by the unified scorer - below this,
        /// Match() falls through to the configured typerole Default instead of trusting a weak/coincidental
        /// signal (e.g. typerole-only or a short title-prefix match alone).
        /// </summary>
        const int MinMatchScore = 20;

        /// <summary>
        /// Multiplier applied to a guessed (not confirmed) candidate's ICAO-type/class-code/WTC score
        /// contributions - a title-text guess is materially less trustworthy than a confirmed tag, and
        /// should not let a mistagged installed model outscore a real, differently-typed classCode/WTC
        /// match (verified against a real mistagged-model case during design: 0.4 was not aggressive
        /// enough, 0.2 gives a clear margin).
        /// </summary>
        const double GuessedSignalMultiplier = 0.2;

        /// <summary>
        /// Score one candidate against the remote aircraft's reported/derived attributes. Every signal
        /// is independent and additive - unlike the old tiered fall-through, a candidate doesn't need an
        /// exact ICAO type match to win; sharing class code + WTC (e.g. a Diamond DA62 against a
        /// Beechcraft Baron remote - both L2P/L) can outscore a same-class-but-wrong-WTC candidate (e.g.
        /// a Douglas DC-3, L2P/M), which is what actually fixes the reported "wrong substitute" bug. The
        /// one exception is a disagreeing typerole without an exact ICAO type match, which is penalized
        /// heavily rather than just withholding a bonus - see the typerole block below.
        /// Returns the total score, a per-MatchAttribute breakdown (for Explain Match's attribute grid),
        /// and a human-readable contribution list (for the trace/"other candidates considered" panel).
        /// </summary>
        static void ScoreCandidate(Model candidate, string remoteIcaoType, string remoteClassCode, string remoteWtc,
            string remoteIcaoAirline, string remoteRegistration, string remoteRegistrationAlnum, string remoteLivery,
            int remoteTyperole, string remoteTitle,
            out int score, out Dictionary<MatchAttribute, int> attributeScores, out List<string> contributions)
        {
            int total = 0;
            Dictionary<MatchAttribute, int> attrScores = [];
            List<string> details = [];
            double guessFactor = candidate.icaoGuessed ? GuessedSignalMultiplier : 1.0;
            // independent of guessFactor - icaoType and icaoAirline provenance are unrelated, a candidate
            // can have a confirmed icaoType but a text-guessed icaoAirline or vice versa
            double airlineGuessFactor = candidate.icaoAirlineGuessed ? GuessedSignalMultiplier : 1.0;

            void Add(MatchAttribute attr, double points, string detail)
            {
                int applied = (int)Math.Round(points);
                if (applied == 0) return;
                total += applied;
                attrScores[attr] = attrScores.GetValueOrDefault(attr) + applied;
                details.Add(detail + " (" + (applied > 0 ? "+" : "") + applied + ")");
            }

            bool exactIcaoTypeMatch = remoteIcaoType.Length > 0 && candidate.icaoType.Length > 0 &&
                candidate.icaoType.Equals(remoteIcaoType, StringComparison.OrdinalIgnoreCase);

            if (exactIcaoTypeMatch)
            {
                Add(MatchAttribute.IcaoType, 200 * guessFactor, "ICAO type '" + remoteIcaoType + "'" + (guessFactor < 1 ? " (guessed)" : ""));
            }

            if (remoteIcaoAirline.Length > 0 && candidate.icaoAirline.Length > 0 &&
                candidate.icaoAirline.Equals(remoteIcaoAirline, StringComparison.OrdinalIgnoreCase))
            {
                Add(MatchAttribute.IcaoAirline, 100 * airlineGuessFactor, "ICAO airline '" + remoteIcaoAirline + "'" + (airlineGuessFactor < 1 ? " (guessed)" : ""));
            }

            // classCode/WTC are not downweighted by guessFactor even when candidate.icaoType came from a
            // title guess: once a title guess has identified a real, recognized Doc8643 designator, that
            // designator's classCode/WTC mapping is itself deterministic, not a second independent guess -
            // the uncertainty is only in "is this the right aircraft," already captured by the IcaoType
            // match's own downweight above. Downweighting both compounded to the point that a correctly
            // title-guessed match (e.g. "XCub" -> CC19/L1P) could score below an unrelated candidate that
            // merely happened to share the same class code by coincidence (e.g. a Cirrus SR22, also L1P).
            if (remoteClassCode.Length == 3 && candidate.classCode.Length == 3 && candidate.classCode == remoteClassCode)
            {
                Add(MatchAttribute.ClassCode, 60, "class code '" + remoteClassCode + "'");
                if (candidate.classCode[1] == remoteClassCode[1])
                {
                    Add(MatchAttribute.ClassCode, 20, "engine count");
                }
                if (candidate.classCode[2] == remoteClassCode[2])
                {
                    Add(MatchAttribute.ClassCode, 20, "engine type");
                }
            }

            if (remoteWtc.Length > 0 && candidate.wtc.Length > 0 && candidate.wtc == remoteWtc)
            {
                Add(MatchAttribute.Wtc, 40, "WTC '" + remoteWtc + "'");
            }

            // registration - prefer an exact match against the candidate's own scanned atc_id when it
            // has one (much more reliable - the actual tail number baked into that specific livery);
            // only fall back to a substring search in title/variation when it has no scanned atc_id.
            // Deliberately low-weight (well below even a guessed icaoAirline match) - a pilot can change
            // their tail number mid-session, so registration should only ever act as a tie-breaker among
            // candidates that already agree on type/airline (e.g. picking the exact-tail livery over a
            // generic one for the same airline), never an independently decisive signal on its own.
            if (remoteRegistrationAlnum.Length > 0)
            {
                string candidateAtcIdAlnum = AlnumOnly(candidate.atcId);
                if (candidateAtcIdAlnum.Length > 0)
                {
                    if (candidateAtcIdAlnum.Equals(remoteRegistrationAlnum, StringComparison.OrdinalIgnoreCase))
                    {
                        Add(MatchAttribute.Registration, 15, "registration '" + remoteRegistration + "' (exact atc_id match)");
                    }
                }
                else if (AlnumOnly(candidate.variation).Contains(remoteRegistrationAlnum, StringComparison.OrdinalIgnoreCase) ||
                         AlnumOnly(candidate.title).Contains(remoteRegistrationAlnum, StringComparison.OrdinalIgnoreCase))
                {
                    Add(MatchAttribute.Registration, 5, "registration '" + remoteRegistration + "' (title/variation match)");
                }
            }

            if (remoteTyperole > 0 && candidate.typerole > 0)
            {
                if (candidate.typerole == remoteTyperole)
                {
                    Add(MatchAttribute.Typerole, 15, "same typerole");
                }
                else if (exactIcaoTypeMatch == false)
                {
                    // a disagreeing typerole (e.g. a light business jet tagged Fighter/GA-bucket vs. an
                    // installed Airliner) is real evidence these are different-size aircraft, even when
                    // classCode+WTC+ICAO airline happen to coincide - WTC buckets are wide enough (Medium
                    // spans ~7,000-136,000kg) that a Citation CJ4 and a 737 can share L2J/M and even the
                    // same painted airline, which let a wrong-category match win outright (reported: a CJ4
                    // remote scored onto an installed 737 substitute purely on classCode+WTC+airline, with
                    // no CJ4 model installed to compete). The penalty equals the combined max of those three
                    // coincidental signals (100 airline + 100 classCode/engine + 40 WTC = 240) so it cancels
                    // them out entirely and drops the candidate below MinMatchScore, falling through to the
                    // configured typerole Default instead of forcing a wrong-sized guess - unless a real
                    // identity signal (exact ICAO type, matched above and exempted from this penalty, or
                    // registration/title-prefix) still carries it.
                    Add(MatchAttribute.Typerole, -240, "typerole mismatch");
                }
            }

            // weakest signal - multi-word overlap between the remote's title/livery and the candidate's
            // title/variation. Every distinct significant word (>=3 chars - short enough to still catch
            // "Cub" for the well-known Piper Cub family) shared by both sides adds points - not just a
            // leading prefix or the first word found, so e.g. "XCub Kenmore Livery" against an installed
            // "XCub Passengers Large Wheels (Kenmore)" credits both "XCub" and "Kenmore" instead of only
            // the shared "XCub " prefix. Word-boundary matched (ContainsToken), case-insensitive. Replaces
            // the old separate "first livery word only" and "title-prefix only" mechanisms, both too
            // narrow to catch words that match out of position.
            {
                HashSet<string> remoteWords = new(StringComparer.OrdinalIgnoreCase);
                foreach (var word in (remoteTitle + " " + remoteLivery).Split([' ', '-', '_', '(', ')', '.', ','], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (word.Length >= 3) remoteWords.Add(word);
                }
                string candidateText = candidate.title + " " + candidate.variation;
                List<string> matchedWords = [];
                foreach (var word in remoteWords)
                {
                    if (ContainsToken(candidateText, word))
                    {
                        matchedWords.Add(word);
                    }
                }
                if (matchedWords.Count > 0)
                {
                    Add(MatchAttribute.Title, Math.Min(matchedWords.Count * 8, 40),
                        matchedWords.Count + " title/livery word(s) matched (" + string.Join(", ", matchedWords) + ")");
                }
            }

            score = total;
            attributeScores = attrScores;
            contributions = details;
        }

        /// <summary>
        /// Match a model
        /// </summary>
        /// <param name="model">Model to check</param>
        /// <returns>Matched model</returns>
#if FS2024
        public async Task<(Model model, Type type, MatchTrace trace)> Match(string title, string livery, string icaoType, string icaoAirline, string classCode, string wtc, bool classCodeConfirmed, int typerole, string registration = "")
        // in MSFS2024 aircraft livery is the model variation
#else
        public async Task<(Model model, Type type, MatchTrace trace)> Match(string title, string icaoType, string icaoAirline, string classCode, string wtc, bool classCodeConfirmed, int typerole, string registration = "")
#endif
        {
            Model model;
            Type type;
            MatchTrace trace = new();

            // the remote's classCode/wtc: prefer whatever the sender's own JoinFS already resolved and
            // sent directly (classCodeConfirmed - config-confirmed or live-derived, never a guess) over
            // re-deriving from icaoType via the local bundled Doc8643 table, which fails whenever icaoType
            // is a bogus/non-standard string that isn't a real Doc8643 designator. Older peers that don't
            // send classCode/wtc at all (classCode.Length == 0) transparently fall back to re-derivation,
            // same behavior as before this was added.
            string remoteClassCode = classCode, remoteWtc = wtc;
            if (remoteClassCode.Length == 0 && icaoType.Length > 0 && doc8643Lookup.TryGetValue(icaoType, out var remoteDocEntryFallback))
            {
                remoteClassCode = remoteDocEntryFallback.classCode;
                remoteWtc = remoteDocEntryFallback.wtc;
            }

            // build the requested-vs-matched attribute comparison for Explain Match. When attributeScores
            // is supplied (the unified scorer's per-attribute breakdown for the winning candidate), score
            // contributions/decisive/downweighted flags come from it; otherwise (fast-path Substitute/
            // Original results, or the Default/last-resort fallback) decisiveAttrs marks attributes the
            // old boolean way, with no score to show.
            void Finalize(Model matched, Dictionary<MatchAttribute, int> attributeScores = null, params MatchAttribute[] decisiveAttrs)
            {
                string requestedTyperoleName = typeroleNames.TryGetValue(typerole, out var tn) ? tn : typerole.ToString();
                string matchedTyperoleName = matched != null && typeroleNames.TryGetValue(matched.typerole, out var mtn) ? mtn : "";
                bool matchedIsGuessed = matched != null && matched.icaoGuessed;
                bool matchedAirlineIsGuessed = matched != null && matched.icaoAirlineGuessed;

                void Add(MatchAttribute attr, string requested, string matchedValue)
                {
                    int contribution = attributeScores != null ? attributeScores.GetValueOrDefault(attr) : 0;
                    trace.attributes.Add(new MatchTrace.AttributeComparison
                    {
                        attribute = attr,
                        requested = requested,
                        matched = matchedValue,
                        decisive = attributeScores != null ? contribution > 0 : Array.IndexOf(decisiveAttrs, attr) >= 0,
                        scoreContribution = contribution,
                        // ClassCode/Wtc are no longer downweighted for a guessed icaoType - see ScoreCandidate
                        wasDownweighted = contribution > 0 && (
                            (matchedIsGuessed && attr == MatchAttribute.IcaoType) ||
                            (matchedAirlineIsGuessed && attr == MatchAttribute.IcaoAirline))
                    });
                }

                Add(MatchAttribute.Title, title, matched?.title ?? "");
#if FS2024
                Add(MatchAttribute.Livery, livery, matched?.variation ?? "");
#else
                Add(MatchAttribute.Livery, "", matched?.variation ?? "");
#endif
                // requested side is sourced from SimConnect's "ATC ID" (tail number) via the
                // aircraft's flight plan; matched side is the winning model's scanned aircraft.cfg
                // atc_id (Model.atcId), when PreferByOperator had one to match against
                Add(MatchAttribute.Registration, registration, matched?.atcId ?? "");
                Add(MatchAttribute.IcaoType, icaoType, matched?.icaoType ?? "");
                Add(MatchAttribute.IcaoAirline, icaoAirline, matched?.icaoAirline ?? "");
                Add(MatchAttribute.ClassCode, remoteClassCode, matched?.classCode ?? "");
                Add(MatchAttribute.Wtc, remoteWtc, matched?.wtc ?? "");
                Add(MatchAttribute.Typerole, requestedTyperoleName, matchedTyperoleName);
                Add(MatchAttribute.Folder, "", matched?.folder ?? "");
            }

            // rebuild ICAO indexes lazily if a live-learned tag changed them since the last rebuild
            if (icaoIndexDirty)
            {
                MakeIcaoIndex();
            }

            // check for existing model match
            if (matches.TryGetValue(title, out Model value))
            {
                // check for original model
#if FS2024
                model = GetModel(value.title, value.variation);
#else
                model = GetModel(matches[title].title);
#endif
                if (model != null)
                {
                    // use matched model
                    type = Type.Substitute;
                    trace.steps.Add($"Substitute: found a user-defined override for title '{title}' -> '{model.title}' (Settings ▸ Model Matching), and the target model is currently installed.");
#if FS2024
                    Finalize(model, null, MatchAttribute.Title, MatchAttribute.Livery);
#else
                    Finalize(model, null, MatchAttribute.Title);
#endif
                    return (model, type, trace);
                }
                else
                {
                    trace.steps.Add($"Substitute: a user-defined override exists for title '{title}' -> '{value.title}', but that target model is not currently installed/scanned. Falling through to the next matching tier.");
                }
            }
            else
            {
                trace.steps.Add("Substitute: no user-defined override configured for this title.");
            }

            // check for original model
#if FS2024
            model = GetModel(title, livery);
#else
            model = GetModel(title);
#endif
            if (model != null)
            {
                // use the specified model
                type = Type.Original;
#if FS2024
                trace.steps.Add($"Original: an installed model exactly matches the requested title '{title}' and livery '{livery}'.");
                Finalize(model, null, MatchAttribute.Title, MatchAttribute.Livery);
#else
                trace.steps.Add($"Original: an installed model exactly matches the requested title '{title}'.");
                Finalize(model, null, MatchAttribute.Title);
#endif
                return (model, type, trace);
            }
            else
            {
#if FS2024
                // surface other installed liveries for this base title, if any - helps spot "right title, wrong livery name"
                var siblingLiveries = models.FindAll(m => m.title.Equals(title, StringComparison.Ordinal));
                if (siblingLiveries.Count > 0)
                {
                    trace.steps.Add($"Original: title '{title}' is installed, but not with livery/variation '{livery}'. Installed liveries for this title: {string.Join(", ", siblingLiveries.ConvertAll(m => "'" + m.variation + "'"))}.");
                }
                else
                {
                    trace.steps.Add($"Original: no installed model has title '{title}'.");
                }
#else
                trace.steps.Add($"Original: no installed model has title '{title}'.");
#endif
            }

            // Unified scoring pass - replaces the old separate ICAO/Category/Auto tiers with one weighted
            // scorer across every plausible candidate (same ICAO type, same classCode, same loose
            // platform+engine category, or same typerole). A candidate no longer needs an exact ICAO type
            // match to win: sharing classCode+WTC with a differently-typed remote can outscore a same-
            // classCode-but-wrong-WTC candidate, which is what actually fixes cases like a Beechcraft
            // Baron remote preferring an installed Diamond DA62 (both L2P/L) over an installed Douglas
            // DC-3 (L2P/M) - previously an unbroken tie, resolved only by install order.
            {
#if FS2024
                string remoteLivery = livery;
#else
                string remoteLivery = "";
#endif
                // remoteClassCode/remoteWtc resolved once, outer scope - see comment above Finalize()
                string remoteRegistrationAlnum = AlnumOnly(registration);

                HashSet<Model> candidatePool = [];
                if (icaoType.Length > 0 && icaoIndex.TryGetValue(icaoType, out var icaoCandidates))
                {
                    candidatePool.UnionWith(icaoCandidates);
                }
                if (remoteClassCode.Length == 3)
                {
                    if (classCodeIndex.TryGetValue(remoteClassCode, out var classCandidates))
                    {
                        candidatePool.UnionWith(classCandidates);
                    }
                    string looseKey = remoteClassCode[0] + "*" + remoteClassCode[2];
                    if (categoryIndex.TryGetValue(looseKey, out var looseCandidates))
                    {
                        candidatePool.UnionWith(looseCandidates);
                    }
                }
                if (typeroleIndex.TryGetValue(typerole, out var typeroleCandidates))
                {
                    candidatePool.UnionWith(typeroleCandidates);
                }

                string poolSource = "installed models sharing ICAO type/class code/category/typerole";
                if (candidatePool.Count == 0 && models.Count > 0)
                {
                    // rare - nothing shares any of the four index dimensions with the remote at all
                    candidatePool.UnionWith(models);
                    poolSource = "all installed models (none shared ICAO type/class code/category/typerole)";
                }

                if (candidatePool.Count > 0)
                {
                    List<(Model model, int score, Dictionary<MatchAttribute, int> attributeScores, List<string> contributions)> scored = [];
                    foreach (var candidate in candidatePool)
                    {
                        ScoreCandidate(candidate, icaoType, remoteClassCode, remoteWtc, icaoAirline, registration,
                            remoteRegistrationAlnum, remoteLivery, typerole, title,
                            out int score, out var attributeScores, out var contributions);
                        scored.Add((candidate, score, attributeScores, contributions));
                    }
                    scored.Sort((a, b) => b.score.CompareTo(a.score));

                    var winner = scored[0];
                    var topScored = scored.Count > 5 ? scored.GetRange(0, 5) : scored;
                    trace.topCandidates = topScored.ConvertAll(s => new MatchTrace.Candidate
                    {
                        title = s.model.title,
                        variation = s.model.variation,
                        totalScore = s.score,
                        contributions = s.contributions
                    });

                    if (winner.score >= MinMatchScore)
                    {
                        model = winner.model;
                        type = model.icaoType.Length > 0 && icaoType.Length > 0 && model.icaoType.Equals(icaoType, StringComparison.OrdinalIgnoreCase) ? Type.Icao
                             : model.classCode.Length == 3 && model.classCode == remoteClassCode ? Type.Category
                             : Type.Auto;
                        string contributionText = winner.contributions.Count > 0 ? string.Join(" + ", winner.contributions) : "no positive signals";
                        trace.steps.Add($"Scoring: {candidatePool.Count} candidate(s) considered ({poolSource}). Winner '{model.title}' / '{model.variation}' scored {winner.score} - {contributionText}.");
                        Finalize(model, winner.attributeScores);
                        return (model, type, trace);
                    }

                    trace.steps.Add($"Scoring: {candidatePool.Count} candidate(s) considered ({poolSource}), but the best score ({winner.score}, '{winner.model.title}') is below the minimum match threshold ({MinMatchScore}). Falling through to the configured typerole default.");
                }
                else
                {
                    trace.steps.Add("Scoring: no installed models at all to consider.");
                }
            }

            // check for a fine-grained (typerole, classCode, wtc) default first - falls back to the
            // coarse per-typerole default below when none is configured for this exact combination
            string requestedTyperoleName = typeroleNames.TryGetValue(typerole, out var requestedTyperoleNameValue) ? requestedTyperoleNameValue : typerole.ToString();
            if (remoteClassCode.Length == 3 && remoteWtc.Length > 0 &&
                fineDefaultModels.TryGetValue((typerole, remoteClassCode, remoteWtc), out string fineDefault) &&
                matches.TryGetValue(fineDefault, out var fineMatch))
            {
                model = GetModel(fineMatch.title);
                if (model != null)
                {
                    type = Type.Default;
                    trace.steps.Add($"Default: using the fine-grained default model for '{requestedTyperoleName}' / class '{remoteClassCode}' / WTC '{remoteWtc}' -> '{model.title}'.");
                    Finalize(model, null, MatchAttribute.Typerole, MatchAttribute.ClassCode, MatchAttribute.Wtc);
                    return (model, type, trace);
                }
            }

            // check for default typerole
            if (defaultModels.TryGetValue(typerole, out string defaultModel))
            {
                // check for match
                if (matches.TryGetValue(defaultModel, out var match))
                {
                    // check for original model
                    model = GetModel(match.title);
                    if (model != null)
                    {
                        // use default model
                        type = Type.Default;
                        trace.steps.Add($"Default: using the configured default model for typerole '{requestedTyperoleName}' -> '{model.title}'.");
                        Finalize(model, null, MatchAttribute.Typerole);
                        return (model, type, trace);
                    }
                    else
                    {
                        trace.steps.Add($"Default: a default model is configured for typerole '{requestedTyperoleName}' ('{match.title}'), but that model is not currently installed/scanned.");
                    }
                }
                else
                {
                    trace.steps.Add($"Default: typerole '{requestedTyperoleName}' has a default model name configured ('{defaultModel}') but no matching override entry was found for it.");
                }
            }
            else
            {
                trace.steps.Add($"Default: no default model is configured for typerole '{requestedTyperoleName}'.");
            }

            // check for any models
            if (models.Count > 0)
            {
                // use first model
                model = models[0];
                type = Type.Default;
                trace.steps.Add($"Last resort: falling back to the first installed model in the scan list ('{model.title}') out of {models.Count} installed model(s) - nothing else matched.");
                Finalize(model);
                return (model, type, trace);
            }

            // use last resort
            model = null;
            type = Type.Default;
            trace.steps.Add("Last resort: no models are installed/scanned at all. Nothing can be rendered until a model scan finds at least one installed aircraft.");
            Finalize(model);
            return (model, type, trace);
        }

        /// <summary>
        /// Masquerade a model
        /// </summary>
        /// <param name="model">Model to check</param>
        public void Masquerade(string title, out Model masquerade, out Type type, out MatchTrace trace)
        {
            trace = new MatchTrace();

            // check for existing model masquerade
            if (masquerades.TryGetValue(title, out Model value))
            {
                // use matched model
                masquerade = value;
                type = Type.Substitute;
                trace.steps.Add($"Substitute: explicit masquerade override for title '{title}' -> '{value.title}' (not a computed Match() result).");
                trace.attributes.Add(new MatchTrace.AttributeComparison { attribute = MatchAttribute.Title, requested = title, matched = value.title, decisive = true });
            }
            else
            {
                // use the original
                masquerade = null;
                type = Type.Original;
                trace.steps.Add($"Original: no masquerade override configured for title '{title}'; the aircraft's own model is used as-is.");
                trace.attributes.Add(new MatchTrace.AttributeComparison { attribute = MatchAttribute.Title, requested = title, matched = title, decisive = true });
            }
        }

    }
}
