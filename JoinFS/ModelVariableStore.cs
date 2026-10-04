using System;
using System.Collections.Generic;
using System.IO;

namespace JoinFS
{
    /// <summary>
    /// Per-model list of user-selected variable-set files ("variables.txt"), with a type-role-based
    /// default list for models that have none. VariablesForm mutates this through Main.SimCommand,
    /// which is why AddFiles/RemoveFileAt (not the raw list) are the mutation API - the sim thread is
    /// still the one calling them, this just keeps the "ensure default, then edit, then persist"
    /// sequence in one place instead of at each Forms call site.
    /// </summary>
    public sealed class ModelVariableStore
    {
        readonly Main main;
        readonly Dictionary<string, List<string>> files = [];

        public ModelVariableStore(Main main)
        {
            this.main = main;
        }

        string MakeFilename() => main.storagePath + Path.DirectorySeparatorChar + "variables.txt";

        /// <summary>
        /// Load model variables (old format: "model|file1|file2" on one line)
        /// </summary>
        void LoadOld(string line)
        {
            // split line
            string[] parts = line.Split('|');
            // check that model is not already present
            if (files.ContainsKey(parts[0]) == false)
            {
                // add new entry
                files[parts[0]] = [];
                // for each variable file
                for (int index = 1; index < parts.Length; index++)
                {
                    // add variable filename
                    files[parts[0]].Add(parts[index]);
                }
            }
        }

        /// <summary>
        /// Load model variables
        /// </summary>
        public void Load()
        {
            // clear existing data
            files.Clear();

            try
            {
                // make filename
                string filename = MakeFilename();
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
                            if (files.ContainsKey(parts[0]) == false)
                            {
                                // add new entry
                                files[parts[0]] = [];
                                // for each variable file
                                for (int index = 1; index < parts.Length; index++)
                                {
                                    // add variable filename
                                    files[parts[0]].Add(parts[index]);
                                }
                            }
                        }
                        else
                        {
                            // load old format
                            LoadOld(line);
                        }
                    }
                }

                // message
                main.MonitorEvent("Loaded " + files.Count + " variable set(s)");
            }
            catch (Exception ex)
            {
                main.ShowMessage(ex.Message);
            }
        }

        /// <summary>
        /// Save model variables
        /// </summary>
        public void Save()
        {
            // open models file
            StreamWriter writer = null;

            try
            {
                // make filename
                string filename = MakeFilename();

                // open models file
                writer = new StreamWriter(filename);
                // for all models
                foreach (var model in files)
                {
                    // check that model is not using default variables
                    if (UsingDefault(model.Key) == false)
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
                main.MonitorEvent("Saved " + files.Count + " variable set(s)");
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
        bool UsingDefault(string title)
        {
            // check for model variables
            if (files.TryGetValue(title, out List<string> modelFiles))
            {
                // get default files
                List<string> defaultFiles = GetDefault(title);
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
        /// The models that have a list of their own: those not using the default of their kind (sim thread)
        /// </summary>
        public List<(string model, List<string> files)> GetAssignments()
        {
            List<(string model, List<string> files)> list = [];
            foreach (var model in files)
            {
                if (UsingDefault(model.Key) == false)
                {
                    list.Add((model.Key, [.. model.Value]));
                }
            }
            return list;
        }

        /// <summary>
        /// Get list of variables for a particular model
        /// </summary>
        /// <param name="title"></param>
        /// <returns></returns>
        public List<string> GetDefault(string title)
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
        public List<string> Get(string title)
        {
            // check for existing variables
            if (files.TryGetValue(title, out List<string> value))
            {
                // return variable file list
                return value;
            }
            else
            {
                // return default variables
                return GetDefault(title);
            }
        }

        /// <summary>
        /// Add files to a model's set - creating it from the type-role default first if it has none
        /// yet - and persist. Called from VariablesForm through Main.SimCommand.
        /// </summary>
        public void AddFiles(string title, IEnumerable<string> added)
        {
            if (files.TryGetValue(title, out List<string> list) == false)
            {
                list = GetDefault(title);
                files[title] = list;
            }
            list.AddRange(added);
            Save();
        }

        /// <summary>
        /// Remove one file from a model's set - creating it from the type-role default first if it
        /// has none yet - and persist. Called from VariablesForm through Main.SimCommand.
        /// </summary>
        public void RemoveFileAt(string title, int index)
        {
            if (files.TryGetValue(title, out List<string> list) == false)
            {
                list = GetDefault(title);
                files[title] = list;
            }
            if (index < list.Count)
            {
                list.RemoveAt(index);
            }
            Save();
        }
    }
}
