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
        /// list of height adjustments. Concurrent: the sim thread applies them while the UI edits
        /// them (HeightForm, AircraftForm).
        /// </summary>
        readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> heightAdjustments = new();

        /// <summary>
        /// get the height adjustment for a model
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public int GetHeightAdjustment(Substitution.Model model)
        {
            // find model
            if (model != null && heightAdjustments.TryGetValue(model.longType, out int value))
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
        public void UpdateHeightAdjustment(Substitution.Model model, int adjustment)
        {
            // check for valid model
            if (model != null)
            {
                // check for no adjustment
                if (adjustment == 0)
                {
                    // check for model
                    heightAdjustments.TryRemove(model.longType, out _);
                }
                else
                {
                    // set adjustment
                    heightAdjustments[model.longType] = adjustment;
                }
            }
        }

        /// <summary>
        /// Load height adjustment
        /// </summary>
        public void LoadHeightAdjustments()
        {
            // check for simulator
            if (Connected)
            {
                try
                {
                    // make filename
                    string filename = main.storagePath + Path.DirectorySeparatorChar + "heights - " + GetSimulatorName() + ".txt";

                    // check for matching file
                    if (File.Exists(filename))
                    {
                        // clear list
                        heightAdjustments.Clear();

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
                                            heightAdjustments[model.longType] = adjustment;
                                        }
                                        else
                                        {
                                            // add adjustment
                                            heightAdjustments[type] = adjustment;
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
                        main.MonitorEvent("Loaded " + heightAdjustments.Count + " height adjustments");
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
        public void SaveHeightAdjustments()
        {
            // check for simulator
            if (Connected)
            {
                try
                {
                    // make filename
                    string filename = main.storagePath + Path.DirectorySeparatorChar + "heights - " + GetSimulatorName() + ".txt";

                    // open file
                    StreamWriter writer = new(filename);
                    if (writer != null)
                    {
                        // for each adjustment
                        foreach (var pair in heightAdjustments)
                        {
                            // write adjustment
                            writer.WriteLine(pair.Key + "=" + pair.Value);
                        }
                        // close writer
                        writer.Close();
                    }

                    // message
                    main.MonitorEvent("Saved " + heightAdjustments.Count + " height adjustments");
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
