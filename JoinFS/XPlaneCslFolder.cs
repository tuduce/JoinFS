using System;
using System.IO;
using System.Linq;

namespace JoinFS
{
    /// <summary>
    /// Inspects the JoinFS plugin and CSL folders inside an X-Plane install. Not wrapped in a
    /// simulator #if so it can be unit tested in every configuration.
    /// </summary>
    internal static class XPlaneCslFolder
    {
        /// <summary>
        /// The one package the plugin installer ships, which does not count as generated
        /// </summary>
        const string ShippedPackage = "BB_GA";

        const string ModelFileName = "xsb_aircraft.txt";

        static string PluginFolder(string simFolder)
        {
            return Path.Combine(simFolder, "Resources", "plugins", "JoinFS");
        }

        /// <summary>
        /// The folder holding an .acf file, relative to the X-Plane install (e.g.
        /// "Aircraft\Laminar Research\Cessna 172SP"). Resolved from the real paths instead of
        /// cutting characters, so separators, a trailing slash or letter case in the install
        /// folder cannot shift the result. Null when the .acf is outside the install or
        /// directly in its root.
        /// </summary>
        public static string AircraftFolder(string simFolder, string acfFile)
        {
            if (string.IsNullOrWhiteSpace(simFolder) || string.IsNullOrWhiteSpace(acfFile))
            {
                return null;
            }

            try
            {
                string relative = Path.GetRelativePath(simFolder, Path.GetDirectoryName(acfFile));
                bool outside = relative == "." || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(relative);
                return outside ? null : relative;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// True once the plugin installer created its binaries folder in this install
        /// </summary>
        public static bool IsPluginInstalled(string simFolder)
        {
            return string.IsNullOrWhiteSpace(simFolder) == false
                && Directory.Exists(Path.Combine(PluginFolder(simFolder), "64"));
        }

        /// <summary>
        /// True when the plugin is installed but no CSL package other than the shipped default
        /// exists, i.e. the user would have no models until a generating scan runs
        /// </summary>
        public static bool NeedsGeneration(string simFolder)
        {
            if (IsPluginInstalled(simFolder) == false)
            {
                return false;
            }

            string cslFolder = Path.Combine(PluginFolder(simFolder), "Resources", "CSL");
            if (Directory.Exists(cslFolder) == false)
            {
                return true;
            }

            try
            {
                return Directory.EnumerateDirectories(cslFolder)
                    .Where(package => string.Equals(Path.GetFileName(package), ShippedPackage, StringComparison.OrdinalIgnoreCase) == false)
                    .Any(package => File.Exists(Path.Combine(package, ModelFileName))) == false;
            }
            catch
            {
                return false;
            }
        }
    }
}
