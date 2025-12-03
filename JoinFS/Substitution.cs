using System;
using System.Collections.Generic;
#if !CONSOLE
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;
#endif
using System.IO;
using System.Globalization;
using JoinFS.Properties;
using System.Net.NetworkInformation;
using System.Drawing;
using System.Net.Sockets;
using static System.Net.Mime.MediaTypeNames;
using System.Security.Policy;
using System.Net;
using JoinFS.Helpers;
using JoinFS.DataModel;
using System.Net.Http;
using System.Threading.Tasks;

namespace JoinFS
{
    public class Substitution
    {
        const string MODELS_FILE = "models.txt";
        const string MATCHING_FILE = "matching.txt";
        const string MASQUERADING_FILE = "masquerading.txt";
        const string CALLSIGNS_FILE = "callsigns.txt";

        /// <summary>
        /// folders
        /// </summary>
        public string simFolder = "";
        string initialScanFolders = "";
        string initialAddOns = "";
        string initialAdditionals = "";
        public static string[] AddonsFileContents = { "" };

        /// <summary>
        /// Reference to the main form
        /// </summary>
        Main main;

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

            enrichModelService = new EnrichModelService(main.storagePath + Path.DirectorySeparatorChar + "model-data.jsonl");
#if X64
            embeddingService = new EmbeddingService(
                modelPath: "AIModel" + Path.DirectorySeparatorChar + "model.onnx",
                vocabPath: "AIModel" + Path.DirectorySeparatorChar + "vocab.txt"
            );
#endif
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

        /// <summary>
        /// type roles names
        /// </summary>
        public readonly Dictionary<int, string> typeroleNames = new Dictionary<int, string>()
        {
            { TypeRole_SingleProp,      Resources.Strings.SingleProp },
            { TypeRole_TwinProp,        Resources.Strings.TwinProp },
            { TypeRole_Airliner,        Resources.Strings.Airliner },
            { TypeRole_Rotorcraft,      Resources.Strings.Rotorcraft },
            { TypeRole_Glider,          Resources.Strings.Glider },
            { TypeRole_Fighter,         Resources.Strings.Fighter },
            { TypeRole_Bomber,          Resources.Strings.Bomber },
            { TypeRole_FourProp,        Resources.Strings.FourProp },
        };

#if FS2024
        Dictionary<string, (string typeRoleName, string compareType)> typeroleClassifier = new Dictionary<string, (string typeRoleName, string compareType)>();
#endif
        List<string> modelBanList = new List<string>();

        /// <summary>
        /// Convert a string to a typerole
        /// </summary>
        /// <param name="typerole"></param>
        /// <returns></returns>
        static int TyperoleFromString(string typerole)
        {
            // check for type role
            if (typerole.IndexOf("Single") >= 0 && typerole.IndexOf("Prop") >= 0)
            {
                // Single Prop
                return TypeRole_SingleProp;
            }
            else if (typerole.IndexOf("Twin") >= 0 && typerole.IndexOf("Prop") >= 0)
            {
                // Twin Prop
                return TypeRole_TwinProp;
            }
            else if (typerole.IndexOf("Four") >= 0 && typerole.IndexOf("Prop") >= 0)
            {
                // Twin Prop
                return TypeRole_FourProp;
            }
            else if (typerole.IndexOf("Regional") >= 0 || typerole.IndexOf("Airliner") >= 0)
            {
                // Airliner
                return TypeRole_Airliner;
            }
            else if (typerole.IndexOf("Rotorcraft") >= 0)
            {
                // Rotorcraft
                return TypeRole_Rotorcraft;
            }
            else if (typerole.IndexOf("Glider") >= 0)
            {
                // Glider
                return TypeRole_Glider;
            }
            else if (typerole.IndexOf("Fighter") >= 0 || typerole.IndexOf("Jet") >= 0)
            {
                // Fighter
                return TypeRole_Fighter;
            }
            else if (typerole.IndexOf("Bomber") >= 0 || typerole.IndexOf("Airliner") >= 0 || typerole.IndexOf("Four Engine") >= 0)
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
        /// type roles names
        /// </summary>
        public readonly Dictionary<int, string> defaultModels = new Dictionary<int, string>();

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
            public EnrichedAircraftData enrichedData = null;
            public float[] embedding = null;

            public Model(string title, string manufacturer, string type, string variation, int index, string typerole, string smoke, string folder)
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
            }
        }

        /// <summary>
        /// List of valid models in the sim
        /// </summary>
        public List<Model> models = new List<Model>();
        public EnrichModelService enrichModelService = null;
#if X64
        public EmbeddingService embeddingService = null;
#endif

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
            string[] separator = { "[+]" };
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
        public readonly Dictionary<string, Model> matches = new Dictionary<string, Model>();

        /// <summary>
        /// Model masquerades
        /// </summary>
        public readonly Dictionary<string, Model> masquerades = new Dictionary<string, Model>();

        /// <summary>
        /// Model callsigns
        /// </summary>
        public readonly Dictionary<string, string> callsigns = new Dictionary<string, string>();

        /// <summary>
        /// Trim white space and quote characters
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        string TrimComments(string str)
        {
            int index;
            // find comment
            index = str.IndexOf(';');
            if (index >= 0)
            {
                // remove comment
                str = str.Substring(0, index);
            }
            // find comment
            index = str.IndexOf(@"//");
            if (index >= 0)
            {
                // remove comment
                str = str.Substring(0, index);
            }
            return str;
        }

        /// <summary>
        /// Trim white space characters
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        string Trim(string str)
        {
            // trim white space and quotes from beginning and end of string
            return TrimComments(str).TrimStart(' ', '\t', '=').TrimEnd(' ', '\t', '=');
        }

        /// <summary>
        /// Trim white space and quote characters
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        string TrimQuotes(string str)
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

        /// <summary>
        /// Submit the current scanned names
        /// </summary>
        void SubmitScan()
        {
            // check for valid block and title
            if (scanBlock && scanTitle.Length > 0)
            {
                // check for quotes
                if (scanTitle.StartsWith("\""))
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
                    // don't update typerole. The first classification was most probably correct
                    // model.typerole = TyperoleFromString(scanTyperole);
                    model.folder = scanFolder;
                }
                else
                {
                    // add the model
                    models.Add(new Model(scanTitle, scanManufacturer, scanType, scanVariation, scanIndex, scanTyperole, "0", scanFolder));
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
            if (isModelBanned(title))
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

        bool isModelBanned(string model)
        {
            foreach (var ban in modelBanList)
            {
                if (model.Contains(ban))
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
        /// Scan simulator folders for models
        /// </summary>
        public bool Scan(bool interactive)
        {
            // check if simulator is not connected
#if XPLANE || CONSOLE
            if (main.sim != null)
#else
            if (main.sim != null && main.sim.Connected)
#endif
            {
                // message
                main.MonitorEvent("Starting model scan...");

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
                    List<string> subFolders = new List<string>();

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
                    List<string> scanFolders = new List<string>();

                    // we do this for MNSFS2020 only. MSFS2024 delievers the community
                    // models via the simulator as well as the default models
                    if (main.sim.GetSimulatorName() == "Microsoft Flight Simulator 2020")
                    {
                        // add folder to list
                        scanFolders.Add(simFolder);
                    }
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
                    if (main.sim.GetSimulatorName().Contains("Prepar3D"))
                    {
                        // create path list
                        List<string> simobjectsList = new List<string>();

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
                    models.Clear();

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
                    List<string> pathList = new List<string>();

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
                                if (main.sim.GetSimulatorName() != "Microsoft Flight Simulator 2020")
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

                        // create reader
                        StreamReader reader = new StreamReader(path);

                        // track the maximum smoke entry
                        int smokeCount = 0;
                        int startIndex = models.Count;

                        // for each line the file
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            // check for block
                            if (line.StartsWith("["))
                            {
                                // submit the current scan
                                SubmitScan();
                                // check for model block
                                if (line.Substring(1).StartsWith("fltsim", StringComparison.OrdinalIgnoreCase))
                                {
                                    // within model block
                                    scanBlock = true;
                                }
                            }
                            // check for title string
                            if (line.StartsWith("title", StringComparison.OrdinalIgnoreCase))
                            {
                                // get title string
                                scanTitle = Trim(line.Substring(5));
                            }
                            else if (line.StartsWith("ui_typerole", StringComparison.OrdinalIgnoreCase))
                            {
                                // get typerole string
                                scanTyperole = TrimQuotes(line.Substring(11));
                            }
                            else if (line.StartsWith("ui_manufacturer", StringComparison.OrdinalIgnoreCase))
                            {
                                // get manufacturer string
                                scanManufacturer = TrimQuotes(line.Substring(15));
                            }
                            else if (line.StartsWith("ui_type", StringComparison.OrdinalIgnoreCase))
                            {
                                // get type string
                                scanType = TrimQuotes(line.Substring(7));
                            }
                            else if (line.StartsWith("ui_variation", StringComparison.OrdinalIgnoreCase))
                            {
                                // get variation string
                                scanVariation = TrimQuotes(line.Substring(12));
                            }
                            else if (line.StartsWith("model", StringComparison.OrdinalIgnoreCase))
                            {
                                // get model string
                                scanModel = TrimQuotes(line.Substring(5));
                            }
                            else if (line.StartsWith("texture", StringComparison.OrdinalIgnoreCase))
                            {
                                // get texture string
                                scanTexture = TrimQuotes(line.Substring(7));
                            }
                            else if (line.StartsWith("smoke.", StringComparison.OrdinalIgnoreCase))
                            {
                                // get smoke line
                                string[] parts = line.Substring(6).Split(' ', '=');
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

                        // for each new model
                        for (int index = startIndex; index < models.Count; index++)
                        {
                            // set smoke count
                            models[index].smokeCount = smokeCount;
                        }
                    }

                    if (main.sim.GetSimulatorName() == "Microsoft Flight Simulator 2020")
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
                                        string[] separator = { "[+]" };
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
                    if (main.sim.GetSimulatorName() == "Microsoft Flight Simulator 2024")
                    {
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
                            main.ScheduleSubstitutionMatch();
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
                    main.ScheduleSubstitutionMatch();
#endif
                    // check for models scanned
                    if (models.Count > 0)
                    {
                        enrichModelService.EnrichModelsWithDetails(models);
#if X64
                        embeddingService.GenerateEmbeddingsFromModels(models);
#endif
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
                ScanForm scanForm = new ScanForm(main, simFolder, initialScanFolders, initialAddOns, initialAdditionals);
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
                            if (Settings.Default.GenerateCsl == false || MessageBox.Show(Resources.Strings.GenerateCslWarning, Main.name, MessageBoxButtons.OKCancel) == DialogResult.OK)
#else
                            if (true)
#endif
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
        Dictionary<string, string> prefixList = new Dictionary<string,string>();

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
                    string key = model.title.Substring(0, length);
                    // check if prefix not yet listed
                    if (prefixList.ContainsKey(key) == false)
                    {
                        // add prefix entry to list
                        prefixList.Add(model.title.Substring(0, length), model.title);
                    }
                }
            }
        }

        /// <summary>
        /// Make the filename from the simulator name and version
        /// </summary>
        /// <returns></returns>
        string MakeModelsFilename()
        {
            return main.storagePath + Path.DirectorySeparatorChar + "models - " + (main.sim != null ? main.sim.GetSimulatorName() : "null") + ".txt";
        }

        /// <summary>
        /// Make the filename from the simulator name and version
        /// </summary>
        /// <returns></returns>
        string MakeMatchingFilename()
        {
            return main.storagePath + Path.DirectorySeparatorChar + "matching - " + (main.sim != null ? main.sim.GetSimulatorName() : "null") + ".txt";
        }

        /// <summary>
        /// Make the filename from the simulator name and version
        /// </summary>
        /// <returns></returns>
        string MakeMasqueradingFilename()
        {
            return main.storagePath + Path.DirectorySeparatorChar + "masquerading - " + (main.sim != null ? main.sim.GetSimulatorName() : "null") + ".txt";
        }

        /// <summary>
        /// Make the filename from the simulator name and version
        /// </summary>
        /// <returns></returns>
        string MakeCallsignsFilename()
        {
            return main.storagePath + Path.DirectorySeparatorChar + "callsigns - " + (main.sim != null ? main.sim.GetSimulatorName() : "null") + ".txt";
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
                        string[] separator = { "|" };
                        // for all lines
                        foreach (string line in lines)
                        {
                            // ignore comments (comment lines begin with a #)
                            if (line.StartsWith("#"))
                            {
                                // check if line contains the "new separator" command
                                if (line.Contains("new separator"))
                                {
                                    separator = new string[] { "[+]" };
                                }
                                continue;
                            }
                            // ignore empty lines
                            if (line.Trim().Length == 0) continue;

                            // split line
                            string[] parts = line.Split(separator, StringSplitOptions.None);
                            // check if model is banned
                            if (isModelBanned(parts[0]))
                            {
                                // skip banned model
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
                                else if (parts.Length == 8)
                                {
                                    int.TryParse(parts[4], NumberStyles.Number, CultureInfo.InvariantCulture, out int index);
                                    // add model
                                    models.Add(new Model(parts[0], parts[1], parts[2], parts[3], index, parts[5], parts[6], parts[7]));
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
                    StreamWriter writer = new StreamWriter(filename);
                    // write new separator command
                    writer.WriteLine("# new separator - DO NOT DELETE THIS LINE");
                    // for all models
                    foreach (var model in models)
                    {
                        // get typerole name
                        string typeroleName = typeroleNames.ContainsKey(model.typerole) ? typeroleNames[model.typerole] : "SingleProp";
                        // write model
                        writer.WriteLine(model.title + "[+]" + model.manufacturer + "[+]" + model.type + "[+]" + model.variation + "[+]" + model.index + "[+]" + typeroleName + "[+]" + model.smokeCount + "[+]" + model.folder);
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
                        string[] separator = { "|" };
                        while ((line = reader.ReadLine()) != null)
                        {
                            // ignore comments (comment lines begin with a #)
                            if (line.StartsWith("#"))
                            {
                                // check if line contains the "new separator" command
                                if (line.Contains("new separator"))
                                {
                                    separator = new string[] { "[+]" };
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
                    StreamWriter writer = new StreamWriter(filename);
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
                    StreamWriter writer = new StreamWriter(filename);
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
        /// Load list of callsigns
        /// </summary>
        void LoadCallsigns()
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
                    string filename = MakeCallsignsFilename();

                    // check for matching file
                    if (File.Exists(filename))
                    {
                        // clear list
                        callsigns.Clear();

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
                                string callsign = parts[1].TrimStart(' ').TrimEnd(' ');

                                // check for callsign
                                if (modelTitle.Length > 0 && callsign.Length > 0)
                                {
                                    // add callsign
                                    callsigns[modelTitle] = callsign;
                                }
                            }
                            else
                            {
                                main.ShowMessage(Resources.Strings.InvalidSubstitution + ": " + line);
                            }
                        }
                        reader.Close();

                        // message
                        main.MonitorEvent("Loaded " + callsigns.Count + " callsign substitutions");
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
                main.MonitorEvent("Unable to load callsigns because a simulator is not connected.");
            }

#if !SERVER && !CONSOLE
            // refresh
            main.aircraftForm ?. refresher.Schedule(3);
#endif
        }

        /// <summary>
        /// Save callsigns
        /// </summary>
        void SaveCallsigns()
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
                    string filename = MakeCallsignsFilename();

                    // open file
                    StreamWriter writer = new StreamWriter(filename);
                    if (writer != null)
                    {
                        // for each callsign
                        foreach (var pair in callsigns)
                        {
                            // write callsign
                            writer.WriteLine(pair.Key + "=" + pair.Value);
                        }
                        writer.Close();
                    }

                    // message
                    main.MonitorEvent("Saved " + callsigns.Count + " callsign substitutions");
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
                try
                {
                    // folders file
                    string foldersFile = Path.Combine(main.storagePath, "folders - " + main.sim.GetSimulatorName() + ".txt");
                    // open models file
                    StreamWriter writer = new StreamWriter(foldersFile);
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
                    StreamReader reader = new StreamReader(foldersFile);
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
                    // download the file from a web server
                    string url = "https://raw.githubusercontent.com/tuduce/JoinFS/refs/heads/main/JoinFS/util/model2type.txt";
                    try
                    {
                        // download the file
                        //using (WebClient client = new WebClient())
                        //{
                        //    client.DownloadFile(url, typeClassifiersFile);
                        //}
                        using (HttpClient httpClient = new HttpClient())
                        {
                            var response = await httpClient.GetAsync(url);
                            response.EnsureSuccessStatusCode();
                            using (var fs = new FileStream(typeClassifiersFile, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                await response.Content.CopyToAsync(fs);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        main.MonitorEvent("Error downloading type classifiers: " + ex.Message);
                    }
                }
                // check if file exists
                if (File.Exists(typeClassifiersFile))
                {
                    // open file
                    StreamReader reader = new StreamReader(typeClassifiersFile);
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
                // Always download the AddOns file from a web server.
                string url = "https://raw.githubusercontent.com/tuduce/JoinFS/refs/heads/main/JoinFS/util/Addons_FS2020.txt";

                try
                {
                    // download the file
                    //using (WebClient client = new WebClient())
                    //{
                    //    client.DownloadFile(url, AddonsFile_Web);
                    //}
                    using (HttpClient httpClient = new HttpClient())
                    {
                        var response = await httpClient.GetAsync(url);
                        response.EnsureSuccessStatusCode();
                        using (var fs = new FileStream(AddonsFile_Web, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await response.Content.CopyToAsync(fs);
                        }
                    }
                }
                catch (Exception ex)
                {
                    main.MonitorEvent("Error downloading FS2020 AddOns List: " + ex.Message);
                }
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
                    // download the file from a web server
                    string url = "https://raw.githubusercontent.com/tuduce/JoinFS/refs/heads/main/JoinFS/util/bannedModels.txt";
                    try
                    {
                        // download the file
                        // TODO: remove obsolete code after tests
                        //using (WebClient client = new WebClient())
                        //{
                        //    client.DownloadFile(url, banListFile);
                        //}
                        using (HttpClient httpClient = new HttpClient())
                        {
                            var response = await httpClient.GetAsync(url);
                            response.EnsureSuccessStatusCode();
                            using (var fs = new FileStream(banListFile, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                await response.Content.CopyToAsync(fs);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        main.MonitorEvent("Error downloading type classifiers: " + ex.Message);
                    }
                }
                // check if file exists
                if (File.Exists(banListFile))
                {
                    // open file
                    StreamReader reader = new StreamReader(banListFile);
                    // read ban list
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line != "" && !line.StartsWith("#"))
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
                // load callsigns
                LoadCallsigns();

                // now we can save
                // TODO: does it make sense to save just after loading?
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
            SaveCallsigns();
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
            callsigns.Clear();

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
                            if (model == null)
                            {
                                // use first model
                                model = models[0];
                            }

                            // update model match
                            matches[defaultModel.Value] = model;
                            // changed
                            changed = true;
                            // monitor
                            main.MonitorEvent(defaultModel.Value + " set to '" + model.title + "'");
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
        /// Apply a callsign to a model
        /// </summary>
        void ApplyCallsign(string modelTitle, string callsign)
        {
            // check for sim
            if (main.sim != null)
            {
                // for all objects in the sim
                foreach (var obj in main.sim.objectList)
                {
                    // check if replace model
                    if (obj is Sim.Aircraft && obj.Injected == false && obj.ownerModel.Equals(modelTitle))
                    {
                        // get aircraft
                        Sim.Aircraft aircraft = obj as Sim.Aircraft;
                        // check for valid callsign
                        if (callsign.Length > 0)
                        {
                            // set the substitute
                            aircraft.flightPlan.callsign = callsign;
                        }
                        else
                        {
                            // use original
                            aircraft.flightPlan.callsign = aircraft.originalCallsign;
                        }
                    }
                }
            }

            // refresh
#if !SERVER && !CONSOLE
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
                    SubstitutionForm substitutionForm = new SubstitutionForm(main, modelTitle, modelVariation, typerole);
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
                MessageBox.Show("The model list is empty. Use 'File|Scan For Models...' to generate a list.", Main.name + ": Model Matching");
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
                    SubstitutionForm substitutionForm = new SubstitutionForm(main, modelTitle, modelVariation, typerole);
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
                MessageBox.Show("The model list is empty. Use 'File|Scan For Models...' to generate a list.", Main.name + ": Model Masquerading");
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
        public bool EditCallsign(string modelTitle, string originalCallsign)
        {
#if !SERVER && !CONSOLE
            // check for some models
            if (models.Count > 0)
            {
                try
                {
                    // show dialog for changing callsign
                    CallsignForm callsignForm = new CallsignForm(main, modelTitle, originalCallsign);
                    switch (callsignForm.ShowDialog())
                    {
                        case System.Windows.Forms.DialogResult.OK:
                            lock (main.conch)
                            {
                                // check for empty callsign
                                if (callsignForm.callsign.Length > 0)
                                {
                                    // update callsign
                                    callsigns[modelTitle] = callsignForm.callsign;
                                }
                                else
                                {
                                    // remove callsign
                                    callsigns.Remove(modelTitle);
                                }
                                // save
                                main.ScheduleSubstitutionSave();
                                // apply
                                ApplyCallsign(modelTitle, callsignForm.callsign);
                            }
                            return true;

                        case System.Windows.Forms.DialogResult.No:
                            lock (main.conch)
                            {
                                // remove callsign
                                callsigns.Remove(modelTitle);
                                // save
                                main.ScheduleSubstitutionSave();
                                // apply
                                ApplyCallsign(modelTitle, "");
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
                MessageBox.Show("The model list is empty. Use 'File|Scan For Models...' to generate a list.", Main.name + ": Model Masquerading");
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
            AI
        }

        /// <summary>
        /// Match a model
        /// </summary>
        /// <param name="model">Model to check</param>
        /// <returns>Matched model</returns>
#if FS2024
        public void Match(string title, string livery, int typerole, out Model model, out Type type)
        // in MSFS2024 aircraft livery is the model variation
#else
        public void Match(string title, int typerole, out Model model, out Type type)
#endif
        {
            // check for existing model match
            if (matches.ContainsKey(title))
            {
                // check for original model
#if FS2024
                model = GetModel(matches[title].title, matches[title].variation);
#else
                model = GetModel(matches[title].title);
#endif
                if (model != null)
                {
                    // use matched model
                    type = Type.Substitute;
                    return;
                }
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
                return;
            }

            // automatic matching using the embedding logic
            // 1. Make a model out of the title and livery
            var tempModel = new Model(
                title, // title
                "", // manufacturer
                "", // type
#if FS2024
                livery, // variation
#else
                "", // variation
#endif
                -1, // index
                typeroleNames[typerole], // typerole
                "", // smoke
                "" // folder
            );
            // 2. Get the model enrichment data
            enrichModelService.EnrichModel(tempModel);
            // 3. Get the embedding
            // 4. Compare against all known models (cosine similarity)
#if X64
            model = embeddingService.FindBestMatchingModel(tempModel, models, 0.2f);
            if (model != null)
            {
                // use automatic match
                type = Type.AI;
                return;
            }
#endif

            // for each prefix
            for (int length = title.Length; length >= 4; length--)
            {
                // make key
                string key = title.Substring(0, length);
                // check if prefix exists
                if (prefixList.ContainsKey(key))
                {
                    // check for original model
                    model = GetModel(prefixList[key]);
                    if (model != null)
                    {
                        // use automatic match
                        type = Type.Auto;
                        return;
                    }
                }
            }

            // check for default typerole
            if (defaultModels.ContainsKey(typerole))
            {
                // get model key
                string key = defaultModels[typerole];
                // check for match
                if (matches.ContainsKey(key))
                {
                    // check for original model
                    model = GetModel(matches[key].title);
                    if (model != null)
                    {
                        // use default model
                        type = Type.Default;
                        return;
                    }
                }
            }

            // check for any models
            if (models.Count > 0)
            {
                // use first model
                model = models[0];
                type = Type.Default;
                return;
            }

            // use last resort
            model = null;
            type = Type.Default;
        }

        /// <summary>
        /// Masquerade a model
        /// </summary>
        /// <param name="model">Model to check</param>
        public void Masquerade(string title, out Model masquerade, out Type type)
        {
            // check for existing model masquerade
            if (masquerades.ContainsKey(title))
            {
                // use matched model
                masquerade = masquerades[title];
                type = Type.Substitute;
            }
            else
            {
                // use the original
                masquerade = null;
                type = Type.Original;
            }
        }

        /// <summary>
        /// Callsign
        /// </summary>
        /// <param name="model">Model to check</param>
        public string Callsign(string modelTitle, string original)
        {
            // check for existing callsign
            if (callsigns.ContainsKey(modelTitle))
            {
                // return modified callsign
                return callsigns[modelTitle];
            }
            else
            {
                // use original
                return original;
            }
        }
    }
}
