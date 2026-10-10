using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JoinFS
{
    /// <summary>
    /// A CSL pack installed by another plugin or by the user (X-CSL, Bluebell, IVAO_CSL ...)
    /// </summary>
    /// <param name="Name">Folder name relative to Resources\plugins, e.g. "IVAO_CSL\CSL"</param>
    /// <param name="Path">Full path of the folder that holds the CSL packages</param>
    /// <param name="Packages">Packages XPMP2 can reach when the folder is linked into JoinFS's CSL folder</param>
    internal sealed record XPlaneCslSource(string Name, string Path, int Packages);

    /// <summary>
    /// Finds CSL packs installed in the same X-Plane that JoinFS can make available by linking them into
    /// its own CSL folder instead of copying them. Not wrapped in a simulator #if so it can be unit tested.
    /// </summary>
    internal static class XPlaneCslSources
    {
        /// <summary>
        /// XPMP2 searches this many folder levels below JoinFS\Resources. The CSL folder is level 1 and a
        /// link in it level 2, so a package folder may sit at most this many levels below the link.
        /// </summary>
        const int MaxLevelsBelowLink = 3;

        /// <summary>How deep inside a plugin folder we look for packages</summary>
        const int MaxSearchDepth = 5;

        const string PackageFile = "xsb_aircraft.txt";

        /// <summary>
        /// Every pack below <c>Resources\plugins</c> (JoinFS itself excluded), sorted by name
        /// </summary>
        public static IReadOnlyList<XPlaneCslSource> Discover(string installRoot)
        {
            string plugins = Path.Combine(installRoot ?? "", "Resources", "plugins");
            if (Directory.Exists(plugins) == false)
            {
                return [];
            }

            List<XPlaneCslSource> sources = [];
            foreach (string pluginFolder in SafeDirectories(plugins))
            {
                if (string.Equals(Path.GetFileName(pluginFolder), "JoinFS", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                List<string> packages = [];
                FindPackages(pluginFolder, 0, packages);
                if (packages.Count == 0)
                {
                    continue;
                }

                string common = CommonAncestor(packages);
                int reachable = packages.Count(package => LevelsBelow(common, package) <= MaxLevelsBelowLink);
                if (reachable == 0)
                {
                    continue;
                }

                string name = Path.GetRelativePath(plugins, common);
                sources.Add(new XPlaneCslSource(name, common, reachable));
            }

            return sources.OrderBy(source => source.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Folders that hold an xsb_aircraft.txt; like XPMP2 the search stops descending where one is found
        /// </summary>
        static void FindPackages(string folder, int depth, List<string> packages)
        {
            if (File.Exists(Path.Combine(folder, PackageFile)))
            {
                packages.Add(folder);
                return;
            }
            if (depth >= MaxSearchDepth)
            {
                return;
            }
            foreach (string child in SafeDirectories(folder))
            {
                FindPackages(child, depth + 1, packages);
            }
        }

        static string CommonAncestor(List<string> folders)
        {
            if (folders.Count == 1)
            {
                return folders[0];
            }

            string[] common = folders[0].Split(Path.DirectorySeparatorChar);
            foreach (string folder in folders.Skip(1))
            {
                string[] parts = folder.Split(Path.DirectorySeparatorChar);
                int length = 0;
                while (length < common.Length && length < parts.Length
                    && string.Equals(common[length], parts[length], StringComparison.OrdinalIgnoreCase))
                {
                    length++;
                }
                common = common.Take(length).ToArray();
            }
            return string.Join(Path.DirectorySeparatorChar, common);
        }

        static int LevelsBelow(string ancestor, string folder)
        {
            string relative = Path.GetRelativePath(ancestor, folder);
            return relative == "." ? 0 : relative.Split(Path.DirectorySeparatorChar).Length;
        }

        static string[] SafeDirectories(string folder)
        {
            try
            {
                return Directory.GetDirectories(folder);
            }
            catch
            {
                return [];
            }
        }
    }
}
