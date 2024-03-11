using System;
using System.Collections.Generic;
#if !CONSOLE
using System.Windows.Forms;
#endif
using System.IO;
using System.Globalization;
using JoinFS.Properties;

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
                defaultModels.Add(name.Key, Resources.strings.Default + " " + name.Value);
            }
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
            { TypeRole_SingleProp,      Resources.strings.SingleProp },
            { TypeRole_TwinProp,        Resources.strings.TwinProp },
            { TypeRole_Airliner,        Resources.strings.Airliner },
            { TypeRole_Rotorcraft,      Resources.strings.Rotorcraft },
            { TypeRole_Glider,          Resources.strings.Glider },
            { TypeRole_Fighter,         Resources.strings.Fighter },
            { TypeRole_Bomber,          Resources.strings.Bomber },
            { TypeRole_FourProp,        Resources.strings.FourProp },
        };

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

        /// <summary>
        /// Does a model exist
        /// </summary>
        /// <returns>Model exists</returns>
        public bool ModelExists(string title)
        {
            return GetModel(title) != null;
        }

        /// <summary>
        /// Get a model by title
        /// </summary>
        /// <returns>Model exists</returns>
        public Model GetModel(string title)
        {
            return models.Find(m => m.title.Equals(title));
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
                Model model = GetModel(scanTitle);
                if (model != null)
                {
                    // update the model details
                    model.manufacturer = scanManufacturer;
                    model.type = scanType;
                    model.longType = scanManufacturer + " " + scanType;
                    model.variation = scanVariation;
                    model.index = scanIndex;
                    model.typerole = TyperoleFromString(scanTyperole);
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

                    // MSF
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

                    // check for MSFS2020
                    if (main.sim.GetSimulatorName() == "Microsoft Flight Simulator 2020")
                    {
                        // check for initial addons
                        if (initialAddOns.Length > 0)
                        {
                            // get addons
                            string[] addOns = initialAddOns.Split('|');
                            // for each addon
                            foreach (var addOn in addOns)
                            {
                                if (addOn == "Asobo Standard")
                                {
                                    SubmitModel("Airbus A320neo", "Asobo", "Airbus A320", "neo", 0, "Airliner");
                                    SubmitModel("Boeing 787-10 Asobo", "Asobo", "Boeing 787-10", "_default", 0, "Airliner");
                                    SubmitModel("Beechcraft King Air 350i Asobo", "Asobo", "Beechcraft King Air 350i", "_default", 0, "TwinProp");
                                    SubmitModel("Cessna 208B Grand Caravan EX", "Asobo", "Cessna 208B Grand Caravan", "_default", 0, "SingleProp");
                                    SubmitModel("TBM 930 Asobo", "Asobo", "TBM 930", "_default", 0, "SingleProp");
                                    SubmitModel("Cessna CJ4 Citation Asobo", "Asobo", "Cessna CJ4 Citation", "_default", 0, "Airliner");
                                    SubmitModel("Pitts Special S2S Rufus", "Asobo", "Pitts Special S2S", "Rufus", 0, "SingleProp");
                                    SubmitModel("Pitts Special S2S Sam", "Asobo", "Pitts Special S2S", "Sam", 0, "SingleProp");
                                    SubmitModel("Pitts Asobo", "Asobo", "Pitts", "_default", 0, "SingleProp");
                                    SubmitModel("Bonanza G36 Asobo", "Asobo", "Bonanza G36", "_default", 0, "SingleProp");
                                    SubmitModel("Cessna 152 Asobo", "Asobo", "Cessna 152", "_default", 0, "SingleProp");
                                    SubmitModel("Cessna Skyhawk G1000 Asobo", "Asobo", "Cessna Skyhawk", "G1000", 0, "SingleProp");
                                    SubmitModel("Asobo XCub", "Asobo", "XCub", "_default", 0, "SingleProp");
                                    SubmitModel("DA40-NG Asobo", "Asobo", "DA40", "NG", 0, "SingleProp");
                                    SubmitModel("DA62 Asobo", "Asobo", "DA62", "_default", 0, "TwinProp");
                                    SubmitModel("Extra 330 Asobo", "Asobo", "Extra 330", "_default", 0, "SingleProp");
                                    SubmitModel("FlightDesignCT Asobo", "Asobo", "FlightDesignCT", "_default", 0, "SingleProp");
                                    SubmitModel("Icon A5 Asobo", "Asobo", "Icon A5", "_default", 0, "SingleProp");
                                    SubmitModel("VL3 Asobo", "Asobo", "VL3", "_default", 0, "SingleProp");
                                    SubmitModel("Mudry Cap 10 C", "Asobo", "Mudry Cap 10 C", "_default", 0, "SingleProp");
                                    SubmitModel("DR400 Asobo", "Asobo", "DR400", "_default", 0, "SingleProp");
                                    SubmitModel("Asobo Savage Cub", "Asobo", "Savage Cub", "_default", 0, "SingleProp");
                                }
                                else if (addOn == "Asobo Deluxe")
                                {
                                    SubmitModel("DA40 TDI Asobo", "Asobo", "DA40", "TDI", 0, "SingleProp");
                                    SubmitModel("DV20 Asobo", "Asobo", "DV20", "_default", 0, "SingleProp");
                                    SubmitModel("Asobo Baron G58", "Asobo", "Baron G58", "_default", 0, "TwinProp");
                                    SubmitModel("Cessna Skyhawk Asobo", "Asobo", "Cessna Skyhawk", "_default", 0, "SingleProp");
                                    SubmitModel("Cessna 152 Aero Asobo", "Asobo", "Cessna 152", "Aero", 0, "SingleProp");
                                }
                                else if (addOn == "Asobo Premium")
                                {
                                    SubmitModel("Boeing 747-8i Asobo", "Asobo", "Boeing 747-8i", "_default", 0, "Airliner");
                                    SubmitModel("SR22 Asobo", "Asobo", "SR22", "_default", 0, "SingleProp");
                                    SubmitModel("Pipistrel Alpha Electro Asobo", "Asobo", "Pipistrel Alpha Electro", "_default", 0, "SingleProp");
                                    SubmitModel("Cessna Longitude Asobo", "Asobo", "Cessna Longitude", "_default", 0, "Airliner");
                                    SubmitModel("Savage Shock Ultra Asobo", "Asobo", "Savage Shock Ultra", "_default", 0, "SingleProp");
                                }
                                else if (addOn == "Carenado YMF-5")
                                {
                                    SubmitModel("Carenado YMF-5 N56ECC", "Carenado", "Carenado YMF-5", "N56ECC", 0, "SingleProp");
                                    SubmitModel("Carenado YMF-5 N240ET", "Carenado", "Carenado YMF-5", "N240ET", 0, "SingleProp");
                                    SubmitModel("Carenado YMF-5 G-YMFT", "Carenado", "Carenado YMF-5", "G-YMFT", 0, "SingleProp");
                                    SubmitModel("Carenado YMF-5 D-EDOF", "Carenado", "Carenado YMF-5", "D-EDOF", 0, "SingleProp");
                                    SubmitModel("Carenado YMF-5 N92320", "Carenado", "Carenado YMF-5", "N92320", 0, "SingleProp");
                                }
                                else if (addOn == "Carenado PA44 Seminole")
                                {
                                    SubmitModel("Carenado PA44 Seminole G-BGCP", "Carenado", "Carenado PA44 Seminole", "G-BGCP", 0, "TwinProp");
                                    SubmitModel("Carenado PA44 Seminole N638YA", "Carenado", "Carenado PA44 Seminole", "N638YA", 0, "TwinProp");
                                    SubmitModel("Carenado PA44 Seminole N463PA", "Carenado", "Carenado PA44 Seminole", "N463PA", 0, "TwinProp");
                                    SubmitModel("Carenado PA44 Seminole N428DG", "Carenado", "Carenado PA44 Seminole", "N428DG", 0, "TwinProp");
                                    SubmitModel("Carenado PA44 Seminole N806AM", "Carenado", "Carenado PA44 Seminole", "N806AM", 0, "TwinProp");
                                }
                                else if (addOn == "Carenado M20R Ovation")
                                {
                                    SubmitModel("Carenado M20R Ovation D-ERWO", "Carenado", "Carenado M20R Ovation", "D-ERWO", 0, "SingleProp");
                                    SubmitModel("Carenado M20R Ovation N541JS", "Carenado", "Carenado M20R Ovation", "N541JS", 0, "SingleProp");
                                    SubmitModel("Carenado M20R Ovation N48MQ", "Carenado", "Carenado M20R Ovation", "N48MQ", 0, "SingleProp");
                                    SubmitModel("Carenado M20R Ovation N247VD", "Carenado", "Carenado M20R Ovation", "N247VD", 0, "SingleProp");
                                }
                                else if (addOn == "Carenado CT182T Skylane")
                                {
                                    SubmitModel("Carenado CT182T Skylane G-ANRW", "Carenado", "Carenado CT182T Skylane", "G-ANRW", 0, "SingleProp");
                                    SubmitModel("Carenado CT182T Skylane N5996K", "Carenado", "Carenado CT182T Skylane", "N5996K", 0, "SingleProp");
                                    SubmitModel("Carenado CT182T Skylane N2250R", "Carenado", "Carenado CT182T Skylane", "N2250R", 0, "SingleProp");
                                    SubmitModel("Carenado CT182T Skylane N2963N", "Carenado", "Carenado CT182T Skylane", "N2963N", 0, "SingleProp");
                                }
                                else if (addOn == "Carenado PA34T Seneca V")
                                {
                                    SubmitModel("Carenado PA34T Seneca V N95465", "Carenado", "Carenado PA34T Seneca V", "N95465", 0, "TwinProp");
                                    SubmitModel("Carenado PA34T Seneca V N58688", "Carenado", "Carenado PA34T Seneca V", "N58688", 0, "TwinProp");
                                    SubmitModel("Carenado PA34T Seneca V N48765", "Carenado", "Carenado PA34T Seneca V", "N48765", 0, "TwinProp");
                                    SubmitModel("Carenado PA34T Seneca V PT-SRD", "Carenado", "Carenado PA34T Seneca V", "PT-SRD", 0, "TwinProp");
                                    SubmitModel("Carenado PA34T Seneca V N58630", "Carenado", "Carenado PA34T Seneca V", "N58630", 0, "TwinProp");
                                    SubmitModel("Carenado PA34T Seneca V WHITE", "Carenado", "Carenado PA34T Seneca V", "WHITE", 0, "TwinProp");
                                }
                                else if (addOn == "Carenado PA28R Arrow III")
                                {
                                    SubmitModel("Carenado PA28R Arrow III N842TU", "Carenado", "Carenado PA28R Arrow III", "N842TU", 0, "SingleProp");
                                    SubmitModel("Carenado PA28R Arrow III N19030", "Carenado", "Carenado PA28R Arrow III", "N19030", 0, "SingleProp");
                                    SubmitModel("Carenado PA28R Arrow III D-ESBT", "Carenado", "Carenado PA28R Arrow III", "D-ESBT", 0, "SingleProp");
                                    SubmitModel("Carenado PA28R Arrow III G-GMCL", "Carenado", "Carenado PA28R Arrow III", "G-GMCL", 0, "SingleProp");
                                    SubmitModel("Carenado PA28R Arrow III N3029K", "Carenado", "Carenado PA28R Arrow III", "N3029K", 0, "SingleProp");
                                }
                                else if (addOn == "Carenado C170")
                                {
                                    SubmitModel("Carenado C170 CC-KMC", "Carenado", "Carenado C170", "CC-KMC", 0, "SingleProp");
                                    SubmitModel("Carenado C170 N24MJ", "Carenado", "Carenado C170", "N24MJ", 0, "SingleProp");
                                    SubmitModel("Carenado C170 N250FP", "Carenado", "Carenado C170", "N250FP", 0, "SingleProp");
                                    SubmitModel("Carenado C170 N78HL", "Carenado", "Carenado C170", "N78HL", 0, "SingleProp");
                                    SubmitModel("Carenado C170 N2472P", "Carenado", "Carenado C170", "N2472P", 0, "SingleProp");
                                    SubmitModel("Carenado C170 C-GEXX", "Carenado", "Carenado C170", "C-GEXX", 0, "SingleProp");
                                    SubmitModel("Carenado C170 WHITE", "Carenado", "Carenado C170", "WHITE", 0, "SingleProp");
                                }
                                else if (addOn == "Carenado C170B Tundra")
                                {
                                    SubmitModel("Carenado C170B CC-KMC Tundra", "Carenado", "Carenado C170B Tundra", "CC-KMC", 0, "SingleProp");
                                    SubmitModel("Carenado C170B N24MJ Tundra", "Carenado", "Carenado C170B Tundra", "N24MJ", 0, "SingleProp");
                                    SubmitModel("Carenado C170B N250FP Tundra", "Carenado", "Carenado C170B Tundra", "N250FP", 0, "SingleProp");
                                    SubmitModel("Carenado C170B N78HL Tundra", "Carenado", "Carenado C170B Tundra", "N78HL", 0, "SingleProp");
                                    SubmitModel("Carenado C170B N2472P Tundra", "Carenado", "Carenado C170B Tundra", "N2472P", 0, "SingleProp");
                                    SubmitModel("Carenado C170B C-GEXX Tundra", "Carenado", "Carenado C170B Tundra", "C-GEXX", 0, "SingleProp");
                                    SubmitModel("Carenado C170B WHITE Tundra", "Carenado", "Carenado C170B Tundra", "WHITE", 0, "SingleProp");
                                }
                                else if (addOn == "MilViz T-45C Goshawk")
                                {
                                    SubmitModel("T-45C Goshawk TW-1", "MilViz", "MilViz T-45C Goshawk", "TW-1", 0, "Fighter");
                                    SubmitModel("T-45C Goshawk TW-2", "MilViz", "MilViz T-45C Goshawk", "TW-2", 0, "Fighter");
                                    SubmitModel("T-45C Goshawk TW-1 CoNA", "MilViz", "MilViz T-45C Goshawk", "TW-1 CoNA", 0, "Fighter");
                                    SubmitModel("T-45C Goshawk TW-2 CoNA", "MilViz", "MilViz T-45C Goshawk", "TW-2 CoNA", 0, "Fighter");
                                    SubmitModel("T-45C Goshawk VX-23", "MilViz", "MilViz T-45C Goshawk", "VX-23", 0, "Fighter");
                                    SubmitModel("T-45C Goshawk VT-21 Redhawks", "MilViz", "MilViz T-45C Goshawk", "VT-21 Redhawks", 0, "Fighter");
                                    SubmitModel("T-45C Goshawk VT-22 Golden Hawks", "MilViz", "MilViz T-45C Goshawk", "VT-22 Golden Hawks", 0, "Fighter");
                                    SubmitModel("T-45C Goshawk VT-7 Eagles", "MilViz", "MilViz T-45C Goshawk", "VT-7 Eagles", 0, "Fighter");
                                    SubmitModel("T-45C Goshawk VT-9 Tigers", "MilViz", "MilViz T-45C Goshawk", "VT-9 Tigers", 0, "Fighter");
                                    SubmitModel("T-45C Goshawk VT-86 Sabrehawks", "MilViz", "MilViz T-45C Goshawk", "VT-86 Sabrehawks", 0, "Fighter");
                                    SubmitModel("T-45C Goshawk VT-86 Special Color", "MilViz", "MilViz T-45C Goshawk", "VT-86 Special Color", 0, "Fighter");
                                }
                                else if (addOn == "Aerosoft A333")
                                {
                                    SubmitModel("Aerosoft A333 professional AIR CANADA C-GFAF", "Aerosoft", "Aerosoft A333 professional", "AIR CANADA C-GFAF", 0, "Airliner");
                                    SubmitModel("Aerosoft A333 professional AIRBUS F-WWCB", "Aerosoft", "Aerosoft A333 professional", "AIRBUS F-WWCB", 0, "Airliner");
                                    SubmitModel("Aerosoft A333 professional CATHAY PACIFIC B-LBJ", "Aerosoft", "Aerosoft A333 professional", "CATHAY PACIFIC B-LBJ", 0, "Airliner");
                                    SubmitModel("Aerosoft A333 professional CORSAIR INTERNATIONAL F-HSKY", "Aerosoft", "Aerosoft A333 professional", "CORSAIR INTERNATIONAL F-HSKY", 0, "Airliner");
                                    SubmitModel("Aerosoft A333 professional DRAGON AIR B-LAB", "Aerosoft", "Aerosoft A333 professional", "DRAGON AIR B-LAB", 0, "Airliner");
                                    SubmitModel("Aerosoft A333 professional EDELWEISS AIR HB-JHQ", "Aerosoft", "Aerosoft A333 professional", "EDELWEISS AIR HB-JHQ", 0, "Airliner");
                                    SubmitModel("Aerosoft A333 professional EUROWINGS OO-SFB", "Aerosoft", "Aerosoft A333 professional", "EUROWINGS OO-SFB", 0, "Airliner");
                                    SubmitModel("Aerosoft A333 professional LION AIR PK-LEF", "Aerosoft", "Aerosoft A333 professional", "LION AIR PK-LEF", 0, "Airliner");
                                    SubmitModel("Aerosoft A333 professional LUFTHANSA D-AIKO", "Aerosoft", "Aerosoft A333 professional", "LUFTHANSA D-AIKO", 0, "Airliner");
                                    SubmitModel("Aerosoft A333 professional SAUDI ARABIAN AIRLINES HZ-AQI", "Aerosoft", "Aerosoft A333 professional", "SAUDI ARABIAN AIRLINES HZ-AQI", 0, "Airliner");
                                    SubmitModel("Aerosoft A333 professional SINGAPORE AIRLINES 9V-SSB", "Aerosoft", "Aerosoft A333 professional", "SINGAPORE AIRLINES 9V-SSB", 0, "Airliner");
                                    SubmitModel("Aerosoft A333 professional SWISS HB-JHK", "Aerosoft", "Aerosoft A333 professional", "SWISS HB-JHK", 0, "Airliner");
                                    SubmitModel("Aerosoft A333 professional VIRGIN ATLANTIC G-VKSS", "Aerosoft", "Aerosoft A333 professional", "VIRGIN ATLANTIC G-VKSS", 0, "Airliner");
                                }
                                else if (addOn == "Aerosoft A321")
                                {
                                    SubmitModel("Aerosoft A321 professional BRITISH AIRWAYS G-EUXG", "Aerosoft", "Aerosoft A321 professional", "BRITISH AIRWAYS G-EUXG", 0, "Airliner");
                                    SubmitModel("Aerosoft A321 professional JETBLUE N976JT", "Aerosoft", "Aerosoft A321 professional", "JETBLUE N976JT", 0, "Airliner");
                                    SubmitModel("Aerosoft A321 professional LUFTHANSA D-AISP", "Aerosoft", "Aerosoft A321 professional", "LUFTHANSA D-AISP", 0, "Airliner");
                                    SubmitModel("Aerosoft A321 professional MIDDLE EAST AIRLINES F-ONEO", "Aerosoft", "Aerosoft A321 professional", "MIDDLE EAST AIRLINES F-ONEO", 0, "Airliner");
                                    SubmitModel("Aerosoft A321 professional MONARCH GMARA", "Aerosoft", "Aerosoft A321 professional", "MONARCH GMARA", 0, "Airliner");
                                    SubmitModel("Aerosoft A321 professional QATAR AIRWAYS", "Aerosoft", "Aerosoft A321 professional", "QATAR AIRWAYS", 0, "Airliner");
                                    SubmitModel("Aerosoft A321 professional AIR BERLIN D-ASLB", "Aerosoft", "Aerosoft A321 professional", "AIR BERLIN D-ASLB", 0, "Airliner");
                                    SubmitModel("Aerosoft A321 professional AIRBUS INDUSTRIES F-WWIA", "Aerosoft", "Aerosoft A321 professional", "AIRBUS INDUSTRIES F-WWIA", 0, "Airliner");
                                    SubmitModel("Aerosoft A321 professional DELTA N301DN", "Aerosoft", "Aerosoft A321 professional", "DELTA N301DN", 0, "Airliner");
                                    SubmitModel("Aerosoft A321 professional FINNAIR OH-NEO", "Aerosoft", "Aerosoft A321 professional", "FINNAIR OH-NEO", 0, "Airliner");
                                    SubmitModel("Aerosoft A321 professional ISRAELI AIRLINES 4X-NEO", "Aerosoft", "Aerosoft A321 professional", "ISRAELI AIRLINES 4X-NEO", 0, "Airliner");
                                }
                                else if (addOn == "Aerosoft A320")
                                {
                                    SubmitModel("Aerosoft A320 professional BRITISH AIRWAYS G-EUUU", "Aerosoft", "Aerosoft A320 professional", "BRITISH AIRWAYS G-EUUU", 0, "Airliner");
                                    SubmitModel("Aerosoft A320 professional JETBLUE N828JB", "Aerosoft", "Aerosoft A320 professional", "JETBLUE N828JB", 0, "Airliner");
                                    SubmitModel("Aerosoft A320 professional UNITED AIRLINES N405UA", "Aerosoft", "Aerosoft A320 professional", "UNITED AIRLINES N405UA", 0, "Airliner");
                                    SubmitModel("Aerosoft A320 professional VUELING EC-LUO", "Aerosoft", "Aerosoft A320 professional", "VUELING EC-LUO", 0, "Airliner");
                                    SubmitModel("Aerosoft A320 professional WIZZ AIR UR-WUC", "Aerosoft", "Aerosoft A320 professional", "WIZZ AIR UR-WUC", 0, "Airliner");
                                    SubmitModel("Aerosoft A320 professional AER LINGUS EI-DEK", "Aerosoft", "Aerosoft A320 professional", "AER LINGUS EI-DEK", 0, "Airliner");
                                    SubmitModel("Aerosoft A320 professional AIR BERLIN D-ABMS", "Aerosoft", "Aerosoft A320 professional", "AIR BERLIN D-ABMS", 0, "Airliner");
                                    SubmitModel("Aerosoft A320 professional AIRBUS INDUSTRIES F-WWBA", "Aerosoft", "Aerosoft A320 professional", "AIRBUS INDUSTRIES F-WWBA", 0, "Airliner");
                                    SubmitModel("Aerosoft A320 professional EASYJET G-EZTB", "Aerosoft", "Aerosoft A320 professional", "EASYJET G-EZTB", 0, "Airliner");
                                    SubmitModel("Aerosoft A320 professional EUROWINGS D-AIZV", "Aerosoft", "Aerosoft A320 professional", "EUROWINGS D-AIZV", 0, "Airliner");
                                    SubmitModel("Aerosoft A320 professional LUFTHANSA D-AIZQ", "Aerosoft", "Aerosoft A320 professional", "LUFTHANSA D-AIZQ", 0, "Airliner");
                                    SubmitModel("Aerosoft A320 professional NIKI OE-LEA", "Aerosoft", "Aerosoft A320 professional", "NIKI OE-LEA", 0, "Airliner");
                                }
                                else if (addOn == "Aerosoft A319")
                                {
                                    SubmitModel("Aerosoft A319 professional AVIANCA N703AV", "Aerosoft", "Aerosoft A319 professional", "AVIANCA N703AV", 0, "Airliner");
                                    SubmitModel("Aerosoft A319 professional BRITISH AIRWAYS G-DBCF", "Aerosoft", "Aerosoft A319 professional", "BRITISH AIRWAYS G-DBCF", 0, "Airliner");
                                    SubmitModel("Aerosoft A319 professional EUROWINGS D-AGWZ", "Aerosoft", "Aerosoft A319 professional", "EUROWINGS D-AGWZ", 0, "Airliner");
                                    SubmitModel("Aerosoft A319 professional GERMANWINGS D-AGWZ", "Aerosoft", "Aerosoft A319 professional", "GERMANWINGS D-AGWZ", 0, "Airliner");
                                    SubmitModel("Aerosoft A319 professional TAM PR-MAN", "Aerosoft", "Aerosoft A319 professional", "TAM PR-MAN", 0, "Airliner");
                                    SubmitModel("Aerosoft A319 professional AEROFLOT VQ-BCP", "Aerosoft", "Aerosoft A319 professional", "AEROFLOT VQ-BCP", 0, "Airliner");
                                    SubmitModel("Aerosoft A319 professional AIR BERLIN D-ABGN", "Aerosoft", "Aerosoft A319 professional", "AIR BERLIN D-ABGN", 0, "Airliner");
                                    SubmitModel("Aerosoft A319 professional AMERICAN AIRLINES N8001N", "Aerosoft", "Aerosoft A319 professional", "AMERICAN AIRLINES N8001N", 0, "Airliner");
                                    SubmitModel("Aerosoft A319 professional DELTA N319NB", "Aerosoft", "Aerosoft A319 professional", "DELTA N319NB", 0, "Airliner");
                                    SubmitModel("Aerosoft A319 professional EASYJET G-EZAX", "Aerosoft", "Aerosoft A319 professional", "EASYJET G-EZAX", 0, "Airliner");
                                    SubmitModel("Aerosoft A319 professional LUFTHANSA D-AIBA", "Aerosoft", "Aerosoft A319 professional", "LUFTHANSA D-AIBA", 0, "Airliner");
                                    SubmitModel("Aerosoft A319 professional LUFTHANSA NEWFLEET D-AIBA", "Aerosoft", "Aerosoft A319 professional", "LUFTHANSA NEWFLEET D-AIBA", 0, "Airliner");
                                }
                                else if (addOn == "Aerosoft A318")
                                {
                                    SubmitModel("Aerosoft A318 professional AIR FRANCE F-GUGA", "Aerosoft", "Aerosoft A318 professional", "AIR FRANCE F-GUGA", 0, "Airliner");
                                    SubmitModel("Aerosoft A318 professional AL JABER AVIATION A6-AJC", "Aerosoft", "Aerosoft A318 professional", "AL JABER AVIATION A6-AJC", 0, "Airliner");
                                    SubmitModel("Aerosoft A318 professional BRITISH AIRWAYS G-EUNA", "Aerosoft", "Aerosoft A318 professional", "BRITISH AIRWAYS G-EUNA", 0, "Airliner");
                                }
                                else if (addOn == "Aermacchi MB-339")
                                {
                                    SubmitModel("Aermacchi MB-339A Factory Livery", "Aermacchi", "Aermacchi MB-339A", "Factory Livery", 0, "SingleTurbine");
                                    SubmitModel("Aermacchi MB-339A Aeronautica Militare Ghost Grey", "Aermacchi", "Aermacchi MB-339A", "Aeronautica Militare Ghost Grey", 0, "SingleTurbine");
                                    SubmitModel("Aermacchi MB-339A Aeronautica Militare Camo", "Aermacchi", "Aermacchi MB-339A", "Aeronautica Militare Camo", 0, "SingleTurbine");
                                    SubmitModel("Aermacchi MB-339A United Arab Emirates", "Aermacchi", "Aermacchi MB-339A", "United Arab Emirates", 0, "SingleTurbine");
                                    SubmitModel("Aermacchi MB-339A Royal Malaysian Air Force", "Aermacchi", "Aermacchi MB-339A", "Royal Malaysian Air Force", 0, "SingleTurbine");
                                    SubmitModel("Aermacchi MB-339A Armada", "Aermacchi", "Aermacchi MB-339A", "Armada", 0, "SingleTurbine");
                                    SubmitModel("Aermacchi MB-339A MLU Aeronautica Militare Ghost Grey", "Aermacchi", "Aermacchi MB-339A", "MLU Aeronautica Militare Ghost Grey", 0, "SingleTurbine");
                                    SubmitModel("Aermacchi MB-339PAN Frecce Tricolori", "Aermacchi", "Aermacchi MB-339PAN", "Frecce Tricolori", 0, "SingleTurbine");
                                }
                                else if (addOn == "Carenado C337H Skymaster II")
                                {
                                    SubmitModel("Carenado C337H Skymaster II N842TU", "Carenado", "Carenado C337H Skymaster II", "N842TU", 0, "TwinProp");
                                    SubmitModel("Carenado C337H Skymaster II N79252", "Carenado", "Carenado C337H Skymaster II", "N79252", 0, "TwinProp");
                                    SubmitModel("Carenado C337H Skymaster II N5JD", "Carenado", "Carenado C337H Skymaster II", "N5JD", 0, "TwinProp");
                                    SubmitModel("Carenado C337H Skymaster II N6276K", "Carenado", "Carenado C337H Skymaster II", "N6276K", 0, "TwinProp");
                                    SubmitModel("Carenado C337H Skymaster II VH-SBZ", "Carenado", "Carenado C337H Skymaster II", "VH-SBZ", 0, "TwinProp");
                                    SubmitModel("Carenado C337H Skymaster II N3271S", "Carenado", "Carenado C337H Skymaster II", "N3271S", 0, "TwinProp");
                                }
                                else if (addOn == "Junkers Ju52/3m")
                                {
                                    SubmitModel("Junkers Ju52/3m 1939", "Junkers", "Junkers Ju52/3m", "1939", 0, "TwinProp");
                                    SubmitModel("Junkers Ju52/3m Floats", "Junkers", "Junkers Ju52/3m", "Floats", 0, "TwinProp");
                                    SubmitModel("Junkers Ju52/3m Modern", "Junkers", "Junkers Ju52/3m", "Modern", 0, "TwinProp");
                                    SubmitModel("Junkers Ju52/3m Skis", "Junkers", "Junkers Ju52/3m", "Skis", 0, "TwinProp");
                                }
                                else if (addOn == "Junkers Ju 52")
                                {
                                    SubmitModel("Junkers Ju 52 Wheels Livery 01", "Junkers", "Junkers Ju 52", "Wheels Livery 01", 0, "TwinProp");
                                    SubmitModel("Junkers Ju 52 Wheels Livery 02", "Junkers", "Junkers Ju 52", "Wheels Livery 02", 0, "TwinProp");
                                    SubmitModel("Junkers Ju 52 Wheels Livery 03", "Junkers", "Junkers Ju 52", "Wheels Livery 03", 0, "TwinProp");
                                    SubmitModel("Junkers Ju 52 Wheels Livery 04", "Junkers", "Junkers Ju 52", "Wheels Livery 04", 0, "TwinProp");
                                    SubmitModel("Junkers Ju 52 Floats Livery 01", "Junkers", "Junkers Ju 52", "Floats Livery 01", 0, "TwinProp");
                                    SubmitModel("Junkers Ju 52 Floats Livery 02", "Junkers", "Junkers Ju 52", "Floats Livery 02", 0, "TwinProp");
                                    SubmitModel("Junkers Ju 52 Livery Aviators Club", "Junkers", "Junkers Ju 52", "Livery Aviators Club", 0, "TwinProp");
                                    SubmitModel("Junkers Ju 52 Livery Xbox Aviators Club", "Junkers", "Junkers Ju 52", "Livery Xbox Aviators Club", 0, "TwinProp");
                                    SubmitModel("Junkers Ju 52 Skis Livery 01", "Junkers", "Junkers Ju 52", "Skis Livery 01", 0, "TwinProp");
                                    SubmitModel("Junkers Ju 52 Skis Livery 02", "Junkers", "Junkers Ju 52", "Skis Livery 02", 0, "TwinProp");
                                }
                                else if (addOn == "Aerosoft DHC-6 Series 100")
                                {
                                    SubmitModel("Aerosoft DHC-6 Series 100 Floats Pax", "Aerosoft", "Aerosoft DHC-6 Series 100", "Floats Pax", 0, "TwinProp");
                                    SubmitModel("Aerosoft DHC-6 Series 100 Wheels Cargo", "Aerosoft", "Aerosoft DHC-6 Series 100", "Wheels Cargo", 0, "TwinProp");
                                    SubmitModel("Aerosoft DHC-6 Series 100 Wheels Pax", "Aerosoft", "Aerosoft DHC-6 Series 100", "Wheels Pax", 0, "TwinProp");
                                }
                                else if (addOn == "Aerosoft DHC-6 Series 300")
                                {
                                    SubmitModel("Aerosoft DHC-6 Series 300 Amphibian Pax", "Aerosoft", "Aerosoft DHC-6 Series 300", "Amphibian Pax", 0, "TwinProp");
                                    SubmitModel("Aerosoft DHC-6 Series 300 Floats Pax", "Aerosoft", "Aerosoft DHC-6 Series 300", "Floats Pax", 0, "TwinProp");
                                    SubmitModel("Aerosoft DHC-6 Series 300 Floats Pax (Short Nose)", "Aerosoft", "Aerosoft DHC-6 Series 300", "Floats Pax (Short Nose)", 0, "TwinProp");
                                    SubmitModel("Aerosoft DHC-6 Series 300 Ski Cargo", "Aerosoft", "Aerosoft DHC-6 Series 300", "Ski Cargo", 0, "TwinProp");
                                    SubmitModel("Aerosoft DHC-6 Series 300 Tundra Cargo", "Aerosoft", "Aerosoft DHC-6 Series 300", "Tundra Cargo", 0, "TwinProp");
                                    SubmitModel("Aerosoft DHC-6 Series 300 Tundra Pax", "Aerosoft", "Aerosoft DHC-6 Series 300", "Tundra Pax", 0, "TwinProp");
                                    SubmitModel("Aerosoft DHC-6 Series 300 Wheels Cargo 3-Blade", "Aerosoft", "Aerosoft DHC-6 Series 300", "Wheels Cargo 3-Blade", 0, "TwinProp");
                                    SubmitModel("Aerosoft DHC-6 Series 300 Wheels Cargo 4-Blade", "Aerosoft", "Aerosoft DHC-6 Series 300", "Wheels Cargo 4-Blade", 0, "TwinProp");
                                    SubmitModel("Aerosoft DHC-6 Series 300 Wheels Pax", "Aerosoft", "Aerosoft DHC-6 Series 300", "Wheels Pax", 0, "TwinProp");
                                    SubmitModel("Aerosoft DHC-6 Series 300 Wheels Skydiver", "Aerosoft", "Aerosoft DHC-6 Series 300", "Wheels Skydiver", 0, "TwinProp");
                                }
                                else if (addOn == "Asobo L-39")
                                {
                                    SubmitModel("Asobo Aero L-39 Albatros Reno", "Asobo", "Aero L-39 Albatros", "Reno", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros Reno Livery 01", "Asobo", "Aero L-39 Albatros", "Livery 01", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros Reno Livery 02", "Asobo", "Aero L-39 Albatros", "Livery 02", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros Reno Livery 03", "Asobo", "Aero L-39 Albatros", "Livery 03", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros Reno Livery 04", "Asobo", "Aero L-39 Albatros", "Livery 04", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros Reno Livery 05", "Asobo", "Aero L-39 Albatros", "Livery 05", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros Reno Livery 06", "Asobo", "Aero L-39 Albatros", "Livery 06", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros Reno Livery 07", "Asobo", "Aero L-39 Albatros", "Livery 07", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros American Patriot", "Asobo", "Aero L-39 Albatros", "American Patriot", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros American Spirit", "Asobo", "Aero L-39 Albatros", "American Spirit", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros Athena", "Asobo", "Aero L-39 Albatros", "Athena", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros Blue Ice", "Asobo", "Aero L-39 Albatros", "Blue Ice", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros Drop Bear", "Asobo", "Aero L-39 Albatros", "Drop Bear", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros Pipsqueak", "Asobo", "Aero L-39 Albatros", "Pipsqueak", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros Red Thunder", "Asobo", "Aero L-39 Albatros", "Red Thunder", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros Robin1", "Asobo", "Aero L-39 Albatros", "Robin1", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros Sarance", "Asobo", "Aero L-39 Albatros", "Sarance", 0, "Fighter");
                                    SubmitModel("Asobo Aero L-39 Albatros Tumbling Goose", "Asobo", "Aero L-39 Albatros", "Tumbling Goose", 0, "Fighter");
                                }
                                else if (addOn == "Asobo P51-D")
                                {
                                    SubmitModel("P51-D Mustang Asobo Reno", "Asobo", "P51-D Mustang Reno", "Reno", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Livery 01", "Asobo", "P51-D Mustang Reno", "Livery 01", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Livery 02", "Asobo", "P51-D Mustang Reno", "Livery 02", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Livery 03", "Asobo", "P51-D Mustang Reno", "Livery 03", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Livery 04", "Asobo", "P51-D Mustang Reno", "Livery 04", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Livery 05", "Asobo", "P51-D Mustang Reno", "Livery 05", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Livery 06", "Asobo", "P51-D Mustang Reno", "Livery 06", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Livery 07", "Asobo", "P51-D Mustang Reno", "Livery 07", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Goldfinger", "Asobo", "P51-D Mustang Reno", "Goldfinger", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Lady B", "Asobo", "P51-D Mustang Reno", "Lady B", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Bunny", "Asobo", "P51-D Mustang Reno", "Bunny", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Man O War", "Asobo", "P51-D Mustang Reno", "Man O War", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Miss America", "Asobo", "P51-D Mustang Reno", "Miss America", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Mrs Virginia", "Asobo", "P51-D Mustang Reno", "Mrs Virginia", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Spam Can", "Asobo", "P51-D Mustang Reno", "Spam Can", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Strega", "Asobo", "P51-D Mustang Reno", "Strega", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Voodoo", "Asobo", "P51-D Mustang Reno", "Voodoo", 0, "SingleProp");
                                    SubmitModel("P51-D Mustang Asobo Reno Wee Willy II", "Asobo", "P51-D Mustang Reno", "Wee Willy II", 0, "SingleProp");
                                }
                                else if (addOn == "Asobo T-6")
                                {
                                    SubmitModel("North American T-6 Texan Asobo Reno", "Asobo", "North American T-6 Texan Reno", "Reno", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Livery 01", "Asobo", "North American T-6 Texan Reno", "Livery 01", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Livery 02", "Asobo", "North American T-6 Texan Reno", "Livery 02", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Livery 03", "Asobo", "North American T-6 Texan Reno", "Livery 03", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Livery 04", "Asobo", "North American T-6 Texan Reno", "Livery 04", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Livery 05", "Asobo", "North American T-6 Texan Reno", "Livery 05", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Livery 06", "Asobo", "North American T-6 Texan Reno", "Livery 06", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Livery 07", "Asobo", "North American T-6 Texan Reno", "Livery 07", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Almost Perfect", "Asobo", "North American T-6 Texan Reno", "Almost Perfect", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Baby Boomer", "Asobo", "North American T-6 Texan Reno", "Baby Boomer", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Barons Revenge", "Asobo", "North American T-6 Texan Reno", "Barons Revenge", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Big Red", "Asobo", "North American T-6 Texan Reno", "Big Red", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Eros", "Asobo", "North American T-6 Texan Reno", "Eros", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Gotcha", "Asobo", "North American T-6 Texan Reno", "Gotcha", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Gunslinger", "Asobo", "North American T-6 Texan Reno", "Gunslinger", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Radial Velocity", "Asobo", "North American T-6 Texan Reno", "Radial Velocity", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Six Cat", "Asobo", "North American T-6 Texan Reno", "Six Cat", 0, "SingleProp");
                                    SubmitModel("North American T-6 Texan Asobo Reno Undecided", "Asobo", "North American T-6 Texan Reno", "Undecided", 0, "SingleProp");
                                }
                                else if (addOn == "Asobo Pitts")
                                {
                                    SubmitModel("Pitts Special S1 Reno Asobo", "Asobo", "Pitts Special S1 Reno", "Asobo", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Asobo Livery 01", "Asobo", "Pitts Special S1 Reno", "Livery 01", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Asobo Livery 02", "Asobo", "Pitts Special S1 Reno", "Livery 02", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Asobo Livery 03", "Asobo", "Pitts Special S1 Reno", "Livery 03", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Asobo Livery 04", "Asobo", "Pitts Special S1 Reno", "Livery 04", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Asobo Livery 05", "Asobo", "Pitts Special S1 Reno", "Livery 05", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Asobo Livery 06", "Asobo", "Pitts Special S1 Reno", "Livery 06", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Asobo Livery 07", "Asobo", "Pitts Special S1 Reno", "Livery 07", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Livery Xbox Aviators Club Asobo", "Asobo", "Pitts Special S1 Reno", "Xbox Aviators Club", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Livery Aviators Club Asobo", "Asobo", "Pitts Special S1 Reno", "Aviators Club", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Black Hawk Asobo", "Asobo", "Pitts Special S1 Reno", "Black Hawk", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Miss Diane Asobo", "Asobo", "Pitts Special S1 Reno", "Miss Diane", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Nice and Ez Asobo", "Asobo", "Pitts Special S1 Reno", "Nice and Ez", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Panther Asobo", "Asobo", "Pitts Special S1 Reno", "Panther", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Racer X Asobo", "Asobo", "Pitts Special S1 Reno", "Racer X", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Second Hand Asobo", "Asobo", "Pitts Special S1 Reno", "Second Hand", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Smokin Hot Asobo", "Asobo", "Pitts Special S1 Reno", "Smokin Hot", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Tango Tango Asobo", "Asobo", "Pitts Special S1 Reno", "Tango Tango", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno The Yellow Bomber Asobo", "Asobo", "Pitts Special S1 Reno", "The Yellow Bomber", 0, "SingleProp");
                                    SubmitModel("Pitts Special S1 Reno Toto Asobo", "Asobo", "Pitts Special S1 Reno", "Toto", 0, "SingleProp");
                                }
                                else if (addOn == "Lockheed C-130E")
                                {
                                    SubmitModel("Lockheed C-130E USAF 640544", "Lockheed", "Lockheed C-130E", "USAF 640544", 0, "SingleProp");
                                    SubmitModel("Lockheed C-130E Brazilian Air Force 2459", "Lockheed", "Lockheed C-130E", "Brazilian Air Force 2459", 0, "SingleProp");
                                    SubmitModel("Lockheed C-130E RAAF A97181", "Lockheed", "Lockheed C-130E", "RAAF A97181", 0, "SingleProp");
                                    SubmitModel("Lockheed C-130E RCAF 130307", "Lockheed", "Lockheed C-130E", "RCAF 130307", 0, "SingleProp");
                                    SubmitModel("Lockheed C-130E USAF 637777", "Lockheed", "Lockheed C-130E", "USAF 637777", 0, "SingleProp");
                                    SubmitModel("Lockheed C-130E RAF 887", "Lockheed", "Lockheed C-130E", "RAF 887", 0, "SingleProp");
                                }
                                else if (addOn == "Beechcraft D18S")
                                {
                                    SubmitModel("Beechcraft D18S N85ET", "Carenado", "Beechcraft D18S", "N85ET", 0, "TwinProp");
                                    SubmitModel("Beechcraft D18S Navy", "Carenado", "Beechcraft D18S", "Navy", 0, "TwinProp");
                                    SubmitModel("Beechcraft D18S N846DS", "Carenado", "Beechcraft D18S", "N846DS", 0, "TwinProp");
                                    SubmitModel("Beechcraft D18S N522B", "Carenado", "Beechcraft D18S", "N522B", 0, "TwinProp");
                                    SubmitModel("Beechcraft D18S C-GJMC", "Carenado", "Beechcraft D18S", "C-GJMC", 0, "TwinProp");
                                    SubmitModel("Beechcraft D18S N83KT", "Carenado", "Beechcraft D18S", "N83KT", 0, "TwinProp");
                                    SubmitModel("Beechcraft D18S N438Y", "Carenado", "Beechcraft D18S", "N438Y", 0, "TwinProp");
                                    SubmitModel("Beechcraft D18S N99ST", "Carenado", "Beechcraft D18S", "N99ST", 0, "TwinProp");
                                    SubmitModel("Beechcraft D18S Aviators Club Livery", "Carenado", "Beechcraft D18S", "Aviators Club Livery", 0, "TwinProp");
                                    SubmitModel("Beechcraft D18S Xbox Aviators Club Livery", "Carenado", "Beechcraft D18S", "Xbox Aviators Club Livery", 0, "TwinProp");
                                }
                                else if (addOn == "Beechcraft V35B")
                                {
                                    SubmitModel("Beechcraft V35B Bonanza G-BSVH", "Carenado", "Beechcraft V35B Bonanza", "G-BSVH", 0, "SingleProp");
                                    SubmitModel("Beechcraft V35B Bonanza N9609T", "Carenado", "Beechcraft V35B Bonanza", "N9609T", 0, "SingleProp");
                                    SubmitModel("Beechcraft V35B Bonanza N829K", "Carenado", "Beechcraft V35B Bonanza", "N829K", 0, "SingleProp");
                                    SubmitModel("Beechcraft V35B Bonanza N295K", "Carenado", "Beechcraft V35B Bonanza", "N295K", 0, "SingleProp");
                                    SubmitModel("Beechcraft V35B Bonanza G-BGGH", "Carenado", "Beechcraft V35B Bonanza", "G-BGGH", 0, "SingleProp");
                                    SubmitModel("Beechcraft V35B Bonanza N96652", "Carenado", "Beechcraft V35B Bonanza", "N96652", 0, "SingleProp");
                                    SubmitModel("Beechcraft V35B Bonanza N828L", "Carenado", "Beechcraft V35B Bonanza", "N828L", 0, "SingleProp");
                                    SubmitModel("Beechcraft V35B Bonanza N298P", "Carenado", "Beechcraft V35B Bonanza", "N298P", 0, "SingleProp");
                                    SubmitModel("Beechcraft V35B Bonanza White", "Carenado", "Beechcraft V35B Bonanza", "White", 0, "SingleProp");
                                    SubmitModel("Beechcraft V35B Aviators Club Livery", "Carenado", "Beechcraft V35B Bonanza", "Aviators Club Livery", 0, "SingleProp");
                                    SubmitModel("Beechcraft V35B Bonanza Xbox Aviators Club Livery", "Carenado", "Beechcraft V35B Bonanza", "Xbox Aviators Club Livery", 0, "SingleProp");
                                }
                            }
                        }
                    }
#endif

                    // save models to file
                    main.ScheduleSubstitutionSave();

                    // check for models scanned
                    if (models.Count > 0)
                    {
                        main.MonitorEvent("Scan found " + models.Count + ((models.Count == 1) ? " model" : " models"));
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
                            if (Settings.Default.GenerateCsl == false || MessageBox.Show(Resources.strings.GenerateCslWarning, Main.name, MessageBoxButtons.OKCancel) == DialogResult.OK)
#else
                            if (true)
#endif
                            {
                                // do model scan
                                Scan(true);

                                // reload matches
                                LoadMatches();
                                LoadMasquerades();

                                // check for models scanned
                                if (models.Count > 0)
                                {
                                    main.scheduleShowMessage = Resources.strings.FoundPrefix + " " + models.Count.ToString() + " " + Resources.strings.FoundSuffix;
                                }
                                else
                                {
                                    main.scheduleShowMessage = "No models found";
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
                main.ShowMessage(Resources.strings.ScanWarning);
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
                        // for all lines
                        foreach (string line in lines)
                        {
                            // split line
                            string[] parts = line.Split('|');
                            // check that model is not already present
                            if (ModelExists(parts[0]) == false)
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
                    // for all models
                    foreach (var model in models)
                    {
                        // get typerole name
                        string typeroleName = typeroleNames.ContainsKey(model.typerole) ? typeroleNames[model.typerole] : "SingleProp";
                        // write model
                        writer.WriteLine(model.title + '|' + model.manufacturer + '|' + model.type + '|' + model.variation + '|' + model.index + '|' + typeroleName + '|' + model.smokeCount + '|' + model.folder);
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
                        while ((line = reader.ReadLine()) != null)
                        {
                            // parse line
                            string[] parts = line.Split('=');
                            // check for two parts
                            if (parts.Length == 2)
                            {
                                // find model
                                Model model = GetModel(parts[1]);
                                if (model != null)
                                {
                                    // add model match
                                    matches[parts[0]] = model;
                                }
                                else
                                {
                                    main.ShowMessage(Resources.strings.NoSubstituteModel + ": " + line);
                                }
                            }
                            else
                            {
                                main.ShowMessage(Resources.strings.InvalidSubstitution + ": " + line);
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
                        // for each model match
                        foreach (var pair in matches)
                        {
                            // write model match
                            writer.WriteLine(pair.Key + "=" + pair.Value.title);
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
                                    main.ShowMessage(Resources.strings.NoSubstituteModel + ": " + line);
                                }
                            }
                            else
                            {
                                main.ShowMessage(Resources.strings.InvalidSubstitution + ": " + line);
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
                                main.ShowMessage(Resources.strings.InvalidSubstitution + ": " + line);
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

                // check for scan setting
                if (main.settingsScan)
                {
                    // scan for models
                    Scan(false);
                }

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
            }
        }

        /// <summary>
        /// Save model matching
        /// </summary>
        public void Save()
        {
            SaveModels();
            SaveMatches();
            SaveMasquerades();
            SaveCallsigns();
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
        public bool EditMatch(string modelTitle, int typerole)
        {
#if !SERVER && !CONSOLE
            // check for some models
            if (models.Count > 0)
            {
                try
                {
                    // show dialog for choosing match model
                    SubstitutionForm substitutionForm = new SubstitutionForm(main, modelTitle, typerole);
                    switch (substitutionForm.ShowDialog())
                    {
                        case System.Windows.Forms.DialogResult.OK:
                            lock (main.conch)
                            {
                                // find replace with model
                                Model model = GetModel(substitutionForm.GetWithModel());
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
        public bool EditMasquerade(string modelTitle, int typerole)
        {
#if !SERVER && !CONSOLE
            // check for some models
            if (models.Count > 0)
            {
                try
                {
                    // show dialog for choosing match model
                    SubstitutionForm substitutionForm = new SubstitutionForm(main, modelTitle, typerole);
                    switch (substitutionForm.ShowDialog())
                    {
                        case System.Windows.Forms.DialogResult.OK:
                            lock (main.conch)
                            {
                                // find replace with model
                                Model model = GetModel(substitutionForm.GetWithModel());
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
        }

        /// <summary>
        /// Match a model
        /// </summary>
        /// <param name="model">Model to check</param>
        /// <returns>Matched model</returns>
        public void Match(string title, int typerole, out Model model, out Type type)
        {
            // check for existing model match
            if (matches.ContainsKey(title))
            {
                // check for original model
                model = GetModel(matches[title].title);
                if (model != null)
                {
                    // use matched model
                    type = Type.Substitute;
                    return;
                }
            }

            // check for original model
            model = GetModel(title);
            if (model != null)
            {
                // use the specified model
                type = Type.Original;
                return;
            }

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
