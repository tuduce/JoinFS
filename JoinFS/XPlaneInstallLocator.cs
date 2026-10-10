using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace JoinFS
{
    /// <summary>
    /// One X-Plane installation found on this machine.
    /// </summary>
    /// <param name="Path">Root folder (the one containing Aircraft and Resources)</param>
    /// <param name="Version">Major version (11 or 12)</param>
    internal sealed record XPlaneInstall(string Path, int Version);

    /// <summary>
    /// Finds X-Plane installations from the install registry Laminar's installer/updater
    /// maintains (x-plane_install_11.txt / x-plane_install_12.txt) and from Steam libraries.
    /// Not wrapped in a simulator #if so it can be unit tested in every configuration.
    /// </summary>
    internal static class XPlaneInstallLocator
    {
        static readonly int[] SupportedVersions = { 12, 11 };

        static readonly Dictionary<int, string> SteamFolderNames = new()
        {
            { 12, "X-Plane 12" },
            { 11, "X-Plane 11" },
        };

        static readonly Regex SteamPathEntry = new("^\\s*\"path\"\\s+\"(?<path>[^\"]*)\"", RegexOptions.Compiled);

        /// <summary>
        /// Every valid installation, XP12 before XP11, without duplicates.
        /// </summary>
        /// <param name="registryDirectory">Folder holding x-plane_install_*.txt</param>
        /// <param name="steamRoot">Steam install folder, or null when Steam is not installed</param>
        public static IReadOnlyList<XPlaneInstall> FindInstalls(string registryDirectory, string steamRoot)
        {
            List<XPlaneInstall> installs = new();

            foreach (int version in SupportedVersions)
            {
                AddValid(installs, version, ReadRegistryInstalls(registryDirectory, version));
                AddValid(installs, version, FindSteamInstalls(steamRoot, version));
            }

            return installs;
        }

        /// <summary>
        /// FindInstalls using this machine's registry folder and Steam location.
        /// </summary>
        public static IReadOnlyList<XPlaneInstall> FindInstalls()
        {
            return FindInstalls(DefaultRegistryDirectory(), DefaultSteamRoot());
        }

        /// <summary>
        /// A usable root has both Aircraft (what we scan) and Resources\plugins (where we
        /// install). X-Plane.exe is deliberately not checked: its name varies per OS.
        /// </summary>
        public static bool IsValidInstall(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                return Directory.Exists(Path.Combine(path, "Aircraft"))
                    && Directory.Exists(Path.Combine(path, "Resources", "plugins"));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// All non-blank lines of an install registry file (entries may be stale).
        /// </summary>
        public static IReadOnlyList<string> ReadInstallRegistry(string registryFile)
        {
            return ReadLinesOrEmpty(registryFile)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
        }

        /// <summary>
        /// The library "path" entries of Steam's libraryfolders.vdf.
        /// </summary>
        public static IReadOnlyList<string> ReadSteamLibraryFolders(string libraryFoldersVdf)
        {
            List<string> libraries = new();

            foreach (string line in ReadLinesOrEmpty(libraryFoldersVdf))
            {
                Match match = SteamPathEntry.Match(line);
                if (match.Success)
                {
                    libraries.Add(match.Groups["path"].Value.Replace("\\\\", "\\"));
                }
            }

            return libraries;
        }

        static IEnumerable<string> ReadRegistryInstalls(string registryDirectory, int version)
        {
            if (string.IsNullOrWhiteSpace(registryDirectory))
            {
                return Enumerable.Empty<string>();
            }

            return ReadInstallRegistry(Path.Combine(registryDirectory, "x-plane_install_" + version + ".txt"));
        }

        static IEnumerable<string> FindSteamInstalls(string steamRoot, int version)
        {
            if (string.IsNullOrWhiteSpace(steamRoot))
            {
                return Enumerable.Empty<string>();
            }

            string libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            return ReadSteamLibraryFolders(libraryFile)
                .Prepend(steamRoot)
                .Select(library => Path.Combine(library, "steamapps", "common", SteamFolderNames[version]));
        }

        static void AddValid(List<XPlaneInstall> installs, int version, IEnumerable<string> candidates)
        {
            foreach (string candidate in candidates)
            {
                // the registry holds e.g. "c:\X-Plane 12/"; callers slice paths by the folder's
                // length, so only ever hand out the clean form
                string folder = NormalizeFolder(candidate);
                if (IsValidInstall(folder) && IsNew(installs, folder))
                {
                    installs.Add(new XPlaneInstall(folder, version));
                }
            }
        }

        static bool IsNew(List<XPlaneInstall> installs, string candidate)
        {
            string key = candidate.ToUpperInvariant();
            return installs.All(known => known.Path.ToUpperInvariant() != key);
        }

        /// <summary>
        /// Canonical form of a folder: one kind of separator, no trailing separator (a drive
        /// root keeps its backslash). Blank input is returned unchanged.
        /// </summary>
        public static string NormalizeFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return folder;
            }

            try
            {
                string full = Path.GetFullPath(folder.Trim());
                string root = Path.GetPathRoot(full) ?? "";
                return full.Length > root.Length
                    ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : full;
            }
            catch
            {
                return folder;
            }
        }

        static IEnumerable<string> ReadLinesOrEmpty(string file)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(file) || File.Exists(file) == false)
                {
                    return Enumerable.Empty<string>();
                }
                return File.ReadAllLines(file);
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>
        /// Where Laminar's installer keeps x-plane_install_*.txt on this OS.
        /// </summary>
        static string DefaultRegistryDirectory()
        {
            if (OperatingSystem.IsWindows())
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return OperatingSystem.IsMacOS()
                ? Path.Combine(home, "Library", "Preferences")
                : Path.Combine(home, ".x-plane");
        }

        static string DefaultSteamRoot()
        {
            if (OperatingSystem.IsWindows() == false)
            {
                return null;
            }

            try
            {
                using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                return key?.GetValue("SteamPath") as string;
            }
            catch
            {
                return null;
            }
        }
    }
}
