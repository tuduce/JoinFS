using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using JoinFS.Properties;
using JoinFS.Net;




#if SIMCONNECT
#if P3D
//using LockheedMartin.Prepar3D.SimConnect;
using Microsoft.FlightSimulator.SimConnect;
#else
using Microsoft.FlightSimulator.SimConnect;
#endif
#endif

namespace JoinFS
{
    public partial class Sim
    {
        /// <summary>
        /// List of user defined variable sets for different models
        /// </summary>
        public Dictionary<string, List<string>> modelVariables = [];

        /// <summary>
        /// Make the filename from the simulator name and version
        /// </summary>
        /// <returns></returns>
        string MakeModelVariablesFilename()
        {
            return main.storagePath + Path.DirectorySeparatorChar + "variables.txt";
        }

        /// <summary>
        /// Load model variables
        /// </summary>
        void LoadModelVariables_Old(string line)
        {
            // split line
            string[] parts = line.Split('|');
            // check that model is not already present
            if (modelVariables.ContainsKey(parts[0]) == false)
            {
                // add new entry
                modelVariables[parts[0]] = [];
                // for each variable file
                for (int index = 1; index < parts.Length; index++)
                {
                    // add variable filename
                    modelVariables[parts[0]].Add(parts[index]);
                }
            }
        }


        /// <summary>
        /// Load model variables
        /// </summary>
        void LoadModelVariables()
        {
            // clear existing data
            modelVariables.Clear();

            try
            {
                // make filename
                string filename = MakeModelVariablesFilename();
                // check for models file
                if (File.Exists(filename))
                {
                    // read all models from file
                    string[] lines = File.ReadAllLines(filename);
                    // for all lines
                    foreach (string line in lines)
                    {
                        // split line
                        string[] separator = ["[+]"];
                        string[] parts = line.Split(separator, StringSplitOptions.None);
                        // check for parts
                        if (parts.Length > 1)
                        {
                            // check that model is not already present
                            if (modelVariables.ContainsKey(parts[0]) == false)
                            {
                                // add new entry
                                modelVariables[parts[0]] = [];
                                // for each variable file
                                for (int index = 1; index < parts.Length; index++)
                                {
                                    // add variable filename
                                    modelVariables[parts[0]].Add(parts[index]);
                                }
                            }
                        }
                        else
                        {
                            // load old format
                            LoadModelVariables_Old(line);
                        }
                    }
                }

                // message
                main.MonitorEvent("Loaded " + modelVariables.Count + " variable set(s)");
            }
            catch (Exception ex)
            {
                main.ShowMessage(ex.Message);
            }
        }

        /// <summary>
        /// Save model variables
        /// </summary>
        public void SaveModelVaribles()
        {
            // open models file
            StreamWriter writer = null;

            try
            {
                // make filename
                string filename = MakeModelVariablesFilename();

                // open models file
                writer = new StreamWriter(filename);
                // for all models
                foreach (var model in modelVariables)
                {
                    // check that model is not using default variables
                    if (UsingDefaultVariables(model.Key) == false)
                    {
                        // write model
                        writer.Write(model.Key);
                        // for each variable file
                        foreach (var variableFilename in model.Value)
                        {
                            // write filename
                            writer.Write("[+]" + variableFilename);
                        }
                        // finish line
                        writer.WriteLine();
                    }
                }
                // close file
                writer.Close();

                // message
                main.MonitorEvent("Saved " + modelVariables.Count + " variable set(s)");
            }
            catch (Exception ex)
            {
                // monitor
                main.ShowMessage(ex.Message);
                // close writer
                writer?.Close();
            }
        }

        /// <summary>
        /// Is a model using default variables
        /// </summary>
        bool UsingDefaultVariables(string title)
        {
            // check for model variables
            if (modelVariables.TryGetValue(title, out List<string> modelFiles))
            {
                // get default files
                List<string> defaultFiles = GetModelDefaultVariables(title);
                // check if number of files is the same
                if (modelFiles.Count == defaultFiles.Count)
                {
                    // for each file
                    for (int index = 0; index < modelFiles.Count; index++)
                    {
                        // check if file is different
                        if (modelFiles[index] != defaultFiles[index])
                        {
                            // not using the default
                            return false;
                        }
                    }

                    // using default
                    return true;
                }
                else
                {
                    // not using default
                    return false;
                }
            }
            else
            {
                // using default
                return true;
            }
        }

        /// <summary>
        /// Get list of variables for a particular model
        /// </summary>
        /// <param name="title"></param>
        /// <returns></returns>
        public List<string> GetModelDefaultVariables(string title)
        {
            // create list
            List<string> list = [];

            // get model
            Substitution.Model model = main.substitution ?. GetModel(title);
            if (model != null)
            {
                // check model type
                switch (model.typerole)
                {
                    case Substitution.TypeRole_SingleProp:
                        list.Add("Plane.txt");
                        list.Add("SingleProp.txt");
                        break;

                    case Substitution.TypeRole_TwinProp:
                        list.Add("Plane.txt");
                        list.Add("TwinProp.txt");
                        break;

                    case Substitution.TypeRole_Airliner:
                        list.Add("Plane.txt");
                        list.Add("QuadTurbine.txt");
                        break;

                    case Substitution.TypeRole_Rotorcraft:
                        list.Add("Rotorcraft.txt");
                        list.Add("SingleTurbine.txt");
                        break;

                    case Substitution.TypeRole_Glider:
                        list.Add("Plane.txt");
                        break;

                    case Substitution.TypeRole_Fighter:
                        list.Add("Plane.txt");
                        list.Add("TwinTurbine.txt");
                        break;

                    case Substitution.TypeRole_Bomber:
                        list.Add("Plane.txt");
                        list.Add("QuadTurbine.txt");
                        break;

                    case Substitution.TypeRole_FourProp:
                        list.Add("Plane.txt");
                        list.Add("QuadProp.txt");
                        break;
                }
            }
            else
            {
                // use default
                list.Add("Plane.txt");
                list.Add("SingleProp.txt");
            }

            // return new list
            return list;
        }

        /// <summary>
        /// Get list of variables for a particular model
        /// </summary>
        /// <param name="title"></param>
        /// <returns></returns>
        public List<string> GetModelVariables(string title)
        {
            // check for existing variables
            if (modelVariables.TryGetValue(title, out List<string> value))
            {
                // return variable file list
                return value;
            }
            else
            {
                // return default variables
                return GetModelDefaultVariables(title);
            }
        }

        /// <summary>
        /// Load model variables for a particular object
        /// </summary>
        void CreateModelVariables(Obj obj)
        {
            // variable lists
            List<string> files = [];
            // check if sim is connected
            if (Connected)
            {
                // get files from model
                files = GetModelVariables(obj.ModelTitle);
            }
            else if (obj is Plane)
            {
                // add plane variables as default
                files.Add("Plane.txt");
            }
            else if (obj is Helicopter)
            {
                // add rotorcraft variables as default
                files.Add("Rotorcraft.txt");
            }
            // stop requests
            obj.variableSet ?. StopRequests();
            // reload variables
            obj.variableSet = new VariableMgr.Set(main, obj.simId, obj.Injected, files);
        }
    }
}
