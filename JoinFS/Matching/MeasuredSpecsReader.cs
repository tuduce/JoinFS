using System;
using System.Collections.Concurrent;
using System.IO;

namespace JoinFS.Matching
{
    /// <summary>
    /// Reads the measured physical data of a model from the files in its folder: <c>aircraft.cfg</c> ([GENERAL] category and engines),
    /// <c>flight_model.cfg</c> (wing span, weight, speeds, gear contact points) and <c>engines.cfg</c> for MSFS; for FSX/P3D, where there is no
    /// separate flight model file, the <c>aircraft.cfg</c> itself. A livery package without its own flight model inherits it from its
    /// <c>base_container</c>. Results are cached per folder: hundreds of liveries share one aircraft folder.
    /// Never throws: a missing, locked or malformed file just means "nothing measured".
    /// </summary>
    public sealed class MeasuredSpecsReader
    {
        readonly Func<string, string> findFolderByName;
        readonly ConcurrentDictionary<string, AircraftSpecs> cache = new(StringComparer.OrdinalIgnoreCase);

        /// <param name="findFolderByName">Finds an installed aircraft folder by its leaf name (the sim's package index); used for a base container in another package</param>
        public MeasuredSpecsReader(Func<string, string> findFolderByName = null)
        {
            this.findFolderByName = findFolderByName;
        }

        /// <summary>The measured specs for the model folder, or null when nothing could be measured. Shared between callers - do not change them.</summary>
        public AircraftSpecs ReadFolder(string aircraftFolder)
        {
            if (string.IsNullOrWhiteSpace(aircraftFolder)) return null;
            return cache.GetOrAdd(aircraftFolder, Read);
        }

        AircraftSpecs Read(string folder)
        {
            try
            {
                string aircraftCfg = ReadFile(Path.Combine(folder, "aircraft.cfg"));
                string baseFolder = ResolveBaseContainer(folder, aircraftCfg);

                string flightModelFolder = File.Exists(Path.Combine(folder, "flight_model.cfg")) ? folder : baseFolder;
                string flightModel = flightModelFolder.Length > 0 ? ReadFile(Path.Combine(flightModelFolder, "flight_model.cfg")) : "";
                string engines = ReadFile(Path.Combine(flightModelFolder.Length > 0 ? flightModelFolder : folder, "engines.cfg"));

                // the base container defines [GENERAL] (category, engines); the livery's own file adds its [FLTSIM] entries
                string general = baseFolder.Length > 0 ? ReadFile(Path.Combine(baseFolder, "aircraft.cfg")) + "\n" + aircraftCfg : aircraftCfg;
                // FSX/P3D: no flight_model.cfg anywhere - the geometry, weights and speeds are sections of aircraft.cfg itself
                if (flightModel.Length == 0) flightModel = general;

                AircraftSpecs specs = CfgSpecExtractor.FromMsfsCfg(general, flightModel, engines);
                return specs.KnownFeatureCount > 0 ? specs : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        /// <summary>The folder named by [VARIATION] base_container: relative to the livery folder, else found by name in the package index; "" when none.</summary>
        string ResolveBaseContainer(string folder, string aircraftCfg)
        {
            string baseContainer = IniFile.Parse(aircraftCfg).Get("VARIATION", "base_container");
            if (baseContainer.Length == 0) return "";

            string relative = baseContainer.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            string direct = Path.GetFullPath(Path.Combine(folder, relative));
            if (File.Exists(Path.Combine(direct, "aircraft.cfg")) || File.Exists(Path.Combine(direct, "flight_model.cfg"))) return direct;

            string leaf = Path.GetFileName(relative.TrimEnd(Path.DirectorySeparatorChar));
            string found = leaf.Length > 0 ? findFolderByName?.Invoke(leaf) : null;
            return !string.IsNullOrEmpty(found) && Directory.Exists(found) ? found : "";
        }

        static string ReadFile(string path) => File.Exists(path) ? File.ReadAllText(path) : "";
    }
}
