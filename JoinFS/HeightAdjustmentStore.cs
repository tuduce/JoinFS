using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using JoinFS.Properties;

namespace JoinFS
{
    /// <summary>
    /// Per-model, per-simulator height adjustments (HeightForm), persisted to a "heights - &lt;sim
    /// name&gt;.txt" file. Concurrent: the sim thread applies adjustments while the UI edits them
    /// directly, off the sim thread (HeightForm, AircraftForm) - so this uses a ConcurrentDictionary
    /// rather than the usual Main.SimCommand/PostToSim marshalling.
    /// </summary>
    public sealed class HeightAdjustmentStore
    {
        readonly Main main;
        readonly Func<bool> connected;
        readonly Func<string> simulatorName;
        readonly ConcurrentDictionary<string, int> adjustments = new();

        public HeightAdjustmentStore(Main main, Func<bool> connected, Func<string> simulatorName)
        {
            this.main = main;
            this.connected = connected;
            this.simulatorName = simulatorName;
        }

        /// <summary>
        /// get the height adjustment for a model
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public int Get(Substitution.Model model)
        {
            // find model
            if (model != null && adjustments.TryGetValue(model.longType, out int value))
            {
                // return adjustment in metres
                return value;
            }

            // no adjustment
            return 0;
        }

        /// <summary>
        /// update a height adjustment
        /// </summary>
        /// <param name="model"></param>
        /// <param name="adjustment"></param>
        public void Update(Substitution.Model model, int adjustment)
        {
            // check for valid model
            if (model != null)
            {
                // check for no adjustment
                if (adjustment == 0)
                {
                    // check for model
                    adjustments.TryRemove(model.longType, out _);
                }
                else
                {
                    // set adjustment
                    adjustments[model.longType] = adjustment;
                }
            }
        }

        string MakeFilename() => main.storagePath + Path.DirectorySeparatorChar + "heights - " + simulatorName() + ".txt";

        /// <summary>
        /// Load height adjustment
        /// </summary>
        public void Load()
        {
            // check for simulator
            if (connected())
            {
                try
                {
                    // make filename
                    string filename = MakeFilename();

                    // check for matching file
                    if (File.Exists(filename))
                    {
                        // clear list
                        adjustments.Clear();

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
                                // get model
                                string type = parts[0].TrimStart(' ').TrimEnd(' ');
                                // get adjustment
                                if (int.TryParse(parts[1].TrimStart(' ').TrimEnd(' '), NumberStyles.Number, CultureInfo.InvariantCulture, out int adjustment))
                                {
                                    // validate
                                    if (type.Length > 0)
                                    {
                                        // check for title
                                        Substitution.Model model = main.substitution ?. GetModel(type);
                                        // check if model found
                                        if (model != null)
                                        {
                                            // add adjustment
                                            adjustments[model.longType] = adjustment;
                                        }
                                        else
                                        {
                                            // add adjustment
                                            adjustments[type] = adjustment;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // monitor
                                main.ShowMessage(Resources.Strings.InvalidHeight + ": " + line);
                            }
                        }
                        // close reader
                        reader.Close();

                        // message
                        main.MonitorEvent("Loaded " + adjustments.Count + " height adjustments");
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
                main.MonitorEvent("Unable to load height adjustments because a simulator is not connected.");
            }

#if !SERVER && !CONSOLE
            // refresh
            main.aircraftForm ?. refresher.Schedule();
#endif
        }

        /// <summary>
        /// Save height adjustments
        /// </summary>
        public void Save()
        {
            // check for simulator
            if (connected())
            {
                try
                {
                    // make filename
                    string filename = MakeFilename();

                    // open file
                    StreamWriter writer = new(filename);
                    if (writer != null)
                    {
                        // for each adjustment
                        foreach (var pair in adjustments)
                        {
                            // write adjustment
                            writer.WriteLine(pair.Key + "=" + pair.Value);
                        }
                        // close writer
                        writer.Close();
                    }

                    // message
                    main.MonitorEvent("Saved " + adjustments.Count + " height adjustments");
                }
                catch (Exception ex)
                {
                    main.ShowMessage(ex.Message);
                }
            }
            else
            {
                // error
                main.MonitorEvent("Unable to save height adjustments because a simulator is not connected.");
            }
        }
    }
}
