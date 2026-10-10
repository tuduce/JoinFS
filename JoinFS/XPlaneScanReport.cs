namespace JoinFS
{
    /// <summary>
    /// Which step of the X-Plane scan decided the result
    /// </summary>
    public enum XPlaneScanOutcome
    {
        Models,
        NoCslFolder,
        NoXsbFiles,
        NoEntries,
        AllBanned,
        ReadErrors,
    }

    /// <summary>
    /// What the X-Plane scan saw in each of its steps. Explains an empty result instead of leaving a
    /// bare "No models found". Not wrapped in a simulator #if so it can be unit tested everywhere.
    /// </summary>
    /// <param name="CslFolderExists">The JoinFS CSL folder inside the install exists</param>
    /// <param name="AircraftFiles">.acf files found in the scanned aircraft folders (0 when only reading existing CSL)</param>
    /// <param name="XsbFiles">xsb_aircraft.txt files found below the CSL folder</param>
    /// <param name="Entries">model entries (LIVERY/ICAO/AIRLINE/MATCHES lines) read from them</param>
    /// <param name="Banned">entries dropped by the ban list</param>
    /// <param name="ReadErrors">xsb files that could not be read</param>
    /// <param name="Models">models in the published list</param>
    internal sealed record XPlaneScanReport(bool CslFolderExists, int AircraftFiles, int XsbFiles, int Entries, int Banned, int ReadErrors, int Models)
    {
        /// <summary>
        /// The step that lost the models when there are none
        /// </summary>
        public XPlaneScanOutcome Outcome
        {
            get
            {
                if (Models > 0)
                {
                    return XPlaneScanOutcome.Models;
                }
                if (CslFolderExists == false)
                {
                    return XPlaneScanOutcome.NoCslFolder;
                }
                if (XsbFiles == 0)
                {
                    return XPlaneScanOutcome.NoXsbFiles;
                }
                if (Entries == 0)
                {
                    return ReadErrors > 0 ? XPlaneScanOutcome.ReadErrors : XPlaneScanOutcome.NoEntries;
                }
                return Banned > 0 ? XPlaneScanOutcome.AllBanned : XPlaneScanOutcome.NoEntries;
            }
        }

        /// <summary>
        /// One line for the monitor log
        /// </summary>
        public string Summary()
        {
            return "Scan: " + AircraftFiles + " aircraft file(s), " + XsbFiles + " CSL file(s), " + Entries + " entries, "
                + Banned + " banned, " + ReadErrors + " unreadable, " + Models + " model(s) - " + Outcome;
        }

        /// <summary>
        /// True when there is no CSL model at all, which the user fixes by generating CSL; the other
        /// outcomes mean models exist but none was accepted
        /// </summary>
        public static bool NothingGenerated(XPlaneScanOutcome outcome)
        {
            return outcome == XPlaneScanOutcome.NoCslFolder || outcome == XPlaneScanOutcome.NoXsbFiles;
        }
    }
}
