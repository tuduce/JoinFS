using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JoinFS.Properties;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// What the two X-Plane cards share: the installations on this computer (the ones X-Plane's own install registry and Steam list) and
    /// whether a folder is one, as the old install, setup and scan dialogs checked it.
    /// </summary>
    static class LiveXPlaneInstalls
    {
        public static IReadOnlyList<XPlaneInstallInfo> Installs =>
            [.. XPlaneInstallLocator.FindInstalls().Select(install => new XPlaneInstallInfo(install.Version.ToString(), install.Path))];

        public static bool IsValid(string folder) => XPlaneInstallLocator.IsValidInstall(XPlaneInstallLocator.NormalizeFolder(folder));

        /// <summary>The CSL packs the user switched off, saved '|' separated.</summary>
        public static IEnumerable<string> UnlinkedPacks() =>
            (Settings.Default.UnlinkedCslSources ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);

        public static void SaveUnlinkedPacks(IEnumerable<string> names)
        {
            Settings.Default.UnlinkedCslSources = string.Join("|", names);
            Settings.Default.Save();
        }
    }

    /// <summary>
    /// Installs the JoinFS plugin into an X-Plane folder (the XPLANE build) and, as the old dialog did, then scans that folder so the number of
    /// aircraft found shows without any further step. Other builds have no X-Plane card, so nothing here is asked of them.
    /// </summary>
    class LiveXPlanePluginInstaller(Main main) : IXPlanePluginInstaller
    {
        public IReadOnlyList<XPlaneInstallInfo> Installs => LiveXPlaneInstalls.Installs;

        public bool IsValidInstall(string folder) => LiveXPlaneInstalls.IsValid(folder);

        public string SavedFolder
        {
            get
            {
#if XPLANE
                return main.sim?.xplane?.SuggestedInstallFolder() ?? "";
#else
                return "";
#endif
            }
        }

        public bool TakeInstallRequest()
        {
            if (!main.scheduleAskPlugin)
            {
                return false;
            }
            main.scheduleAskPlugin = false;
            return true;
        }

        public Task InstallAsync(string folder, CancellationToken cancellationToken)
        {
#if XPLANE
            folder = XPlaneInstallLocator.NormalizeFolder(folder);
            if (!XPlaneInstallLocator.IsValidInstall(folder))
            {
                throw new InvalidOperationException(Resources.Strings.InvalidXPlaneFolder);
            }

            // The outcome, good or bad, reaches the user as a message, as it did in the old dialog.
            return Task.Run(() => main.sim?.xplane?.InstallPluginAndScan(folder), cancellationToken);
#else
            throw new NotSupportedException("The plugin is installed from the XPLANE build only.");
#endif
        }
    }
}
