using System;
using System.IO;
using System.Linq;
#if FSX || P3D
using Microsoft.Win32;
#endif

// lets JoinFS.Tests exercise the parsing/validation helpers directly with temp
// files/folders, without depending on real %APPDATA%/%LOCALAPPDATA% contents
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("JoinFS.Tests")]

namespace JoinFS
{
    /// <summary>
    /// Detects the simulator's aircraft/content folder from the records the simulator
    /// itself keeps on disk (UserCfg.opt, registry, x-plane_install_*.txt), so first-run
    /// setup doesn't require the user to browse for it manually.
    /// </summary>
    public static class SimPathDetector
    {
#if FS2020
        /// <summary>
        /// Detect the MSFS2020 Packages folder (parent of Official/Community).
        /// </summary>
        public static string TryDetect()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return TryDetectMsfsPackagesPath(
                Path.Combine(appData, "Microsoft Flight Simulator", "UserCfg.opt"),
                Path.Combine(localAppData, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"));
        }
#endif

#if FS2024
        /// <summary>
        /// Detect the MSFS2024 Packages folder (parent of Official/Community).
        /// </summary>
        public static string TryDetect()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return TryDetectMsfsPackagesPath(
                Path.Combine(appData, "Microsoft Flight Simulator 2024", "UserCfg.opt"),
                Path.Combine(localAppData, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"));
        }
#endif

#if FS2020 || FS2024
        internal static string TryDetectMsfsPackagesPath(string steamUserCfg, string storeUserCfg)
        {
            foreach (string userCfgPath in new[] { steamUserCfg, storeUserCfg })
            {
                string packagesPath = ReadInstalledPackagesPath(userCfgPath);
                if (packagesPath != null && IsValidMsfsPackagesFolder(packagesPath))
                {
                    return packagesPath;
                }
            }
            return null;
        }

        /// <summary>
        /// Parse the InstalledPackagesPath line out of a UserCfg.opt file.
        /// </summary>
        internal static string ReadInstalledPackagesPath(string userCfgPath)
        {
            if (!File.Exists(userCfgPath))
            {
                return null;
            }

            try
            {
                foreach (string line in File.ReadLines(userCfgPath))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("InstalledPackagesPath", StringComparison.OrdinalIgnoreCase))
                    {
                        int firstQuote = trimmed.IndexOf('"');
                        int lastQuote = trimmed.LastIndexOf('"');
                        if (firstQuote >= 0 && lastQuote > firstQuote)
                        {
                            return trimmed.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// A Packages folder normally contains "Official"/"Community" exactly, but a
        /// folder shared between MSFS2020 and MSFS2024 (common with the MS Store/Xbox
        /// versions) instead has version-suffixed names like "Official2024"/
        /// "Community2024" alongside "Official2020"/"Community" - so match by prefix,
        /// not exact name.
        /// </summary>
        internal static bool IsValidMsfsPackagesFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return false;
                }

                foreach (string subFolder in Directory.GetDirectories(path))
                {
                    string name = Path.GetFileName(subFolder);
                    if (name.StartsWith("Official", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("Community", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
#endif

#if FSX
        /// <summary>
        /// Detect the FSX install folder (parent of SimObjects).
        /// </summary>
        public static string TryDetect()
        {
            const string keyPath = @"SOFTWARE\Microsoft\Microsoft Games\Flight Simulator\10.0";

            string path = ReadRegistryPath(RegistryHive.LocalMachine, keyPath, "SetupPath")
                ?? ReadRegistryPath(RegistryHive.CurrentUser, keyPath, "AppPath");

            return IsValidSimObjectsFolder(path) ? path : null;
        }
#endif

#if P3D
        /// <summary>
        /// Detect the Prepar3D install folder (parent of SimObjects), trying v5 then v4.
        /// Returns the matching simulator name via <paramref name="simulatorName"/> since
        /// JoinFS's folders file is keyed by the exact version string.
        /// </summary>
        public static string TryDetect(out string simulatorName)
        {
            foreach (string version in new[] { "Prepar3D v5", "Prepar3D v4" })
            {
                string keyPath = @"SOFTWARE\Lockheed Martin\" + version;

                string path = ReadRegistryPath(RegistryHive.LocalMachine, keyPath, "SetupPath")
                    ?? ReadRegistryPath(RegistryHive.CurrentUser, keyPath, "AppPath");

                if (IsValidSimObjectsFolder(path))
                {
                    simulatorName = version;
                    return path;
                }
            }

            simulatorName = null;
            return null;
        }
#endif

#if FSX || P3D
        /// <summary>
        /// FSX/P3D are always 32-bit installs, so always probe the 32-bit registry view
        /// regardless of whether this process itself is running as x86 or x64.
        /// </summary>
        static string ReadRegistryPath(RegistryHive hive, string keyPath, string valueName)
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry32);
                using RegistryKey key = baseKey.OpenSubKey(keyPath);
                return key?.GetValue(valueName) as string;
            }
            catch
            {
                return null;
            }
        }

        static bool IsValidSimObjectsFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                return Directory.Exists(Path.Combine(path, "SimObjects", "Airplanes"));
            }
            catch
            {
                return false;
            }
        }
#endif

#if XPLANE
        /// <summary>
        /// Detect the X-Plane root install folder (parent of Aircraft), preferring XP12
        /// over XP11 and skipping stale entries that no longer point at a real install.
        /// </summary>
        public static string TryDetect()
        {
            return XPlaneInstallLocator.FindInstalls().FirstOrDefault()?.Path;
        }
#endif
    }
}
