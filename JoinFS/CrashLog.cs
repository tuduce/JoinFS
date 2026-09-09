using System;
using System.IO;
using System.Text;

namespace JoinFS
{
    /// <summary>
    /// Standalone crash writer - see Fix 4 (CTD diagnostics).
    ///
    /// Deliberately self-contained: it writes with File.AppendAllText straight to a dedicated file and
    /// never touches Monitor's StreamWriter or takes lock(conch). The existing fatal handlers route
    /// through MonitorEvent, which takes lock(conch); the work thread holds conch for all of DoWork, so
    /// a fault raised on the work thread would deadlock a handler that tried to log through Monitor.
    /// This path has no such dependency and is safe to call from any thread at any time.
    /// </summary>
    public static class CrashLog
    {
        /// <summary>
        /// Best-effort directory for crash/diagnostic files. Prefers the running instance's storage path
        /// (%LOCALAPPDATA%\JoinFS-&lt;variant&gt;), falls back to %LOCALAPPDATA%\JoinFS, then the temp dir.
        /// </summary>
        public static string Directory(Main main)
        {
            try
            {
                if (main != null && string.IsNullOrEmpty(main.storagePath) == false && main.storagePath != ".")
                {
                    return main.storagePath;
                }
            }
            catch { }

            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JoinFS");
                System.IO.Directory.CreateDirectory(dir);
                return dir;
            }
            catch { }

            return Path.GetTempPath();
        }

        static ushort Port(Main main)
        {
            try
            {
                if (main != null && main.settingsPortEnabled)
                {
                    return main.settingsPort;
                }
            }
            catch { }
            return Network.DEFAULT_PORT;
        }

        /// <summary>Full path of the crash file for this instance.</summary>
        public static string CrashFilePath(Main main)
        {
            return Path.Combine(Directory(main), "crash-" + Port(main) + ".txt");
        }

        /// <summary>Full path of the first-chance trace file for this instance (see -tracediagnostics).</summary>
        public static string FirstChanceFilePath(Main main)
        {
            return Path.Combine(Directory(main), "firstchance-" + Port(main) + ".txt");
        }

        /// <summary>Marker file recording the crash file's write time that was last surfaced to the user.</summary>
        public static string HandledMarkerPath(Main main)
        {
            return Path.Combine(Directory(main), "crash-" + Port(main) + ".handled");
        }

        /// <summary>
        /// Append a full crash record. Never throws.
        /// </summary>
        public static void Write(string context, Exception ex, Main main, bool isTerminating = false)
        {
            try
            {
                StringBuilder sb = new();
                sb.AppendLine("========================================");
                sb.AppendLine("UTC        : " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "Z");
                sb.AppendLine("Context    : " + context);
                sb.AppendLine("Terminating: " + isTerminating);

                try { sb.AppendLine("Version    : JoinFS " + Main.Version); } catch { }

                try
                {
                    if (main?.sim != null)
                    {
                        sb.AppendLine("Simulator  : " + main.sim.GetSimulatorName() + " " + main.sim.GetSimulatorVersion());
                        sb.AppendLine("Objects    : " + main.sim.objectList.Count);
                    }
                }
                catch { }

                sb.AppendLine();
                sb.AppendLine(ex != null ? ex.ToString() : "(no exception object)");

                try
                {
                    string[] recent = main?.monitor?.LinesSnapshot(50);
                    if (recent != null && recent.Length > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("--- last " + recent.Length + " log lines ---");
                        foreach (string line in recent)
                        {
                            sb.AppendLine(line);
                        }
                    }
                }
                catch { }

                sb.AppendLine();

                File.AppendAllText(CrashFilePath(main), sb.ToString());
            }
            catch
            {
                // last resort - a crash writer that throws is worse than useless
            }
        }

        /// <summary>
        /// Append one compact first-chance line (see -tracediagnostics). Never throws.
        /// </summary>
        public static void WriteFirstChance(string message, Main main)
        {
            try
            {
                File.AppendAllText(FirstChanceFilePath(main),
                    DateTime.UtcNow.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
