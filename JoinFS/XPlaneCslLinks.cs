using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace JoinFS
{
    /// <summary>
    /// Keeps directory links to installed CSL packs inside JoinFS's own CSL folder in step with what the
    /// user enabled, so the scan and the plugin see those packs without any file being copied.
    /// Only reparse points whose name carries <see cref="Prefix"/> are ever created or removed, and a link
    /// is removed without touching what it points to.
    /// </summary>
    internal static class XPlaneCslLinks
    {
        public const string Prefix = "__linked_";

        /// <summary>
        /// The link name for a pack: prefix plus the name reduced to letters, digits, '_' and '-'
        /// </summary>
        public static string LinkName(string packName)
        {
            string name = "";
            foreach (char c in (packName ?? "").Replace('\\', '_').Replace('/', '_'))
            {
                if (c == ' ')
                {
                    name += '_';
                }
                else if (char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-')
                {
                    name += c;
                }
            }
            return Prefix + name;
        }

        /// <summary>
        /// True for a junction or symbolic link (a reparse point)
        /// </summary>
        public static bool IsLink(string path)
        {
            try
            {
                return Directory.Exists(path) && new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Make the links in the JoinFS CSL folder match <paramref name="enabled"/>. Returns how many links
        /// are in place afterwards. Does nothing when the plugin has not created its CSL folder yet.
        /// </summary>
        public static int Sync(string installRoot, IEnumerable<XPlaneCslSource> enabled, Action<string> log)
        {
            string cslFolder = Path.Combine(installRoot, "Resources", "plugins", "JoinFS", "Resources", "CSL");
            if (Directory.Exists(cslFolder) == false)
            {
                return 0;
            }

            Dictionary<string, XPlaneCslSource> wanted = new(StringComparer.OrdinalIgnoreCase);
            foreach (XPlaneCslSource source in enabled)
            {
                if (Directory.Exists(source.Path))
                {
                    wanted[LinkName(source.Name)] = source;
                }
            }

            RemoveUnwanted(cslFolder, wanted, log);
            return CreateMissing(cslFolder, wanted, log);
        }

        static void RemoveUnwanted(string cslFolder, Dictionary<string, XPlaneCslSource> wanted, Action<string> log)
        {
            foreach (string folder in Directory.GetDirectories(cslFolder))
            {
                string name = Path.GetFileName(folder);
                if (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) == false || IsLink(folder) == false)
                {
                    continue;
                }

                if (wanted.TryGetValue(name, out XPlaneCslSource source) && PointsTo(folder, source.Path))
                {
                    continue;
                }

                try
                {
                    // non-recursive: removes the link, never what it points to
                    Directory.Delete(folder, false);
                }
                catch (Exception ex)
                {
                    log("Could not remove CSL link '" + name + "': " + ex.Message);
                }
            }
        }

        static int CreateMissing(string cslFolder, Dictionary<string, XPlaneCslSource> wanted, Action<string> log)
        {
            int linked = 0;
            foreach (var (name, source) in wanted)
            {
                string link = Path.Combine(cslFolder, name);
                if (Directory.Exists(link) == false)
                {
                    try
                    {
                        CreateLink(link, source.Path);
                    }
                    catch (Exception ex)
                    {
                        log("Could not link CSL pack '" + source.Name + "': " + ex.Message);
                        continue;
                    }
                }
                else if (IsLink(link) == false)
                {
                    log("CSL folder '" + name + "' exists and is not a link; leaving it alone");
                    continue;
                }
                linked++;
            }
            return linked;
        }

        /// <summary>
        /// True when the link is known to point at the target; an unreadable link target counts as
        /// matching so a link is never torn down on a guess
        /// </summary>
        static bool PointsTo(string link, string target)
        {
            try
            {
                string actual = new DirectoryInfo(link).LinkTarget;
                if (string.IsNullOrEmpty(actual))
                {
                    return true;
                }
                return string.Equals(Normalize(actual), Normalize(target), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }

        static string Normalize(string path)
        {
            string trimmed = path.StartsWith(@"\??\", StringComparison.Ordinal) ? path.Substring(4) : path;
            return XPlaneInstallLocator.NormalizeFolder(trimmed) ?? trimmed;
        }

        static void CreateLink(string link, string target)
        {
            if (OperatingSystem.IsWindows())
            {
                // a directory junction needs no administrator rights, unlike a symbolic link
                ProcessStartInfo start = new("cmd.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                start.ArgumentList.Add("/c");
                start.ArgumentList.Add("mklink");
                start.ArgumentList.Add("/J");
                start.ArgumentList.Add(link);
                start.ArgumentList.Add(target);

                using Process process = Process.Start(start);
                string output = process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new IOException(output.Trim());
                }
            }
            else
            {
                Directory.CreateSymbolicLink(link, target);
            }
        }
    }
}
