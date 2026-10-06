using System;
using System.Collections.Generic;
using System.IO;
using JoinFS.UI.Localization;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// Where Scan For Models looks, from <c>main.substitution</c>, which remembers the folders of the last scan, and what starts the scan.
    /// What the old ScanForm worked out for each simulator is worked out here: the Packages folder and the add-ons of Microsoft Flight Simulator,
    /// the SimObjects subfolders of the others.
    /// </summary>
    class LiveModelScanSource : IModelScanSource
    {
        const string MSFS_2020 = "Microsoft Flight Simulator 2020";
        const string MSFS_2024 = "Microsoft Flight Simulator 2024";

        // The internal key the scan matches against and that is kept in the folders file; only what is shown is friendlier.
        const string MY_MSFS_2024 = "My MSFS 2024";

        readonly Main main;

        public LiveModelScanSource(Main main)
        {
            this.main = main;
        }

        public string SimulatorName => main.sim?.View?.SimulatorName ?? "";

        bool IsMsfs => SimulatorName == MSFS_2020 || SimulatorName == MSFS_2024;

        public string SimFolder => main.substitution?.simFolder ?? "";

        public string FolderPrompt => IsMsfs
            ? Loc.T("Please specify the 'Flight Simulator Packages' folder:")
            : Loc.T("Please specify the root folder for the simulator:");

        // a modern MSFS Packages folder nests SimObjects separately per installed package, so there is no single folder to list
        public bool ListsSubfolders => !IsMsfs;

        public IReadOnlyList<string> InitialSubfolders
        {
            get
            {
                string[] scanned = main.substitution?.ScannedSubfolders ?? [];
                // nothing chosen yet: the folders with aircraft in them
                return scanned.Length > 0 ? scanned : ["Airplanes", "Rotorcraft"];
            }
        }

        public IReadOnlyList<string> ListSubfolders(string simFolder)
        {
            List<string> folders = [];
            try
            {
                foreach (string path in Directory.GetDirectories(Path.Combine(simFolder, "SimObjects")))
                {
                    folders.Add(Path.GetFileName(path));
                }
            }
            catch (Exception)
            {
                // no such folder: there is nothing to list
            }
            return folders;
        }

        public IReadOnlyList<ScanAddOn> AddOns
        {
            get
            {
                List<ScanAddOn> addOns = [];
                HashSet<string> selected = [.. main.substitution?.ScannedAddOns ?? []];

                if (SimulatorName == MSFS_2024)
                {
                    addOns.Add(new ScanAddOn(MY_MSFS_2024, Loc.T("FS2024 models via SimConnect"), selected.Contains(MY_MSFS_2024)));
                }
                else if (SimulatorName == MSFS_2020)
                {
                    // the add-ons of the addons file, one for each run of lines of the same add-on
                    lock (main.conch)
                    {
                        if (Substitution.AddonsFileContents[0] != "")
                        {
                            string current = "";
                            foreach (string line in Substitution.AddonsFileContents)
                            {
                                string addOn = line.Split(["[+]"], StringSplitOptions.None)[0];
                                if (addOn != current)
                                {
                                    addOns.Add(new ScanAddOn(addOn, addOn, selected.Contains(addOn)));
                                }
                                current = addOn;
                            }
                        }
                    }
                }
                return addOns;
            }
        }

        public IReadOnlyList<string> AdditionalFolders => main.substitution?.ScannedAdditionals ?? [];

        public bool Scan(string simFolder, IReadOnlyList<string> subfolders, IReadOnlyList<string> addOns, IReadOnlyList<string> additionalFolders)
        {
            Substitution substitution = main.substitution;
            return substitution != null && substitution.ScanFolders(simFolder, subfolders, addOns, additionalFolders);
        }
    }

    /// <summary>
    /// The scan of the XPLANE build: X-Plane's folder and the aircraft folders under it. The same folders the old ScanForm_XPLANE kept.
    /// </summary>
    class LiveXPlaneScanSource : IXPlaneScanSource
    {
        readonly Main main;

        public LiveXPlaneScanSource(Main main)
        {
            this.main = main;
        }

        public string SimFolder => main.substitution?.simFolder ?? "";

        public IReadOnlyList<string> InitialScanFolders => main.substitution?.ScannedSubfolders ?? [];

        public IReadOnlyList<string> ListAircraftFolders(string xplaneFolder)
        {
            List<string> folders = [];
            try
            {
                foreach (string path in Directory.GetDirectories(Path.Combine(xplaneFolder, "Aircraft")))
                {
                    folders.Add(Path.GetFileName(path));
                }
            }
            catch (Exception)
            {
                // no such folder: there is nothing to list
            }
            return folders;
        }

        public bool Scan(string xplaneFolder, IReadOnlyList<string> aircraftFolders)
        {
            Substitution substitution = main.substitution;
            // X-Plane has no add-ons and no other folders
            return substitution != null && substitution.ScanFolders(xplaneFolder, aircraftFolders, [], []);
        }
    }
}
