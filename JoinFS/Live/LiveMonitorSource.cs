using System;
using System.Collections.Generic;
using System.IO;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// The Monitor tab on the real log: the last lines of <c>main.monitor</c>, which also writes the log files, the two switches that make
    /// it say more (network, variables), and the two dumps of statistics the old window had in its menu.
    /// </summary>
    class LiveMonitorSource : IMonitorSource
    {
        /// <summary>How many lines the tab shows. The files have all of them.</summary>
        const int SHOWN_LINES = 50;

        readonly Main main;

#if SIMCONNECT
        // the frame count and the time it was last read at, to tell how many frames came since
        int previousFrameCount = 0;
        double previousTime = 0.0;
#endif

        public LiveMonitorSource(Main main)
        {
            this.main = main;
        }

        public IReadOnlyList<string> GetLogLines()
        {
            List<string> shown = [];
            Monitor monitor = main.monitor;
            if (monitor != null)
            {
                string[] lines = monitor.CopyLines(SHOWN_LINES, out int total);

                // as the old window did: say where the rest is
                if (total > SHOWN_LINES)
                {
                    shown.Add("[Click 'View Logs' to see full log files]");
                    shown.Add("...");
                }
                shown.AddRange(lines);
            }
            return shown;
        }

        public int? FramesPerSecond
        {
#if SIMCONNECT
            get
            {
                if (main.sim == null)
                {
                    return null;
                }

                // the frames drawn since the last time, over the time since
                int frames = main.sim.View.FrameCount - previousFrameCount;
                double now = main.ElapsedTime;
                int fps = (int)Math.Round(frames / Math.Max(0.1, now - previousTime));
                previousFrameCount = main.sim.View.FrameCount;
                previousTime = now;
                return fps;
            }
#else
            // X-Plane does not tell
            get => null;
#endif
        }

        public bool ShowNetwork
        {
            get => main.monitor != null && main.monitor.network;
            set
            {
                lock (main.conch)
                {
                    if (main.monitor != null)
                    {
                        main.monitor.network = value;
                    }
                }
            }
        }

        public bool ShowVariables
        {
            get => main.monitor != null && main.monitor.variables;
            set
            {
                lock (main.conch)
                {
                    if (main.monitor != null)
                    {
                        main.monitor.variables = value;
                    }
                }
            }
        }

        public void WriteNodeStatistics() => main.monitor?.WriteNodeStatistics();

        public void WritePacketStatistics() => main.monitor?.WritePacketStatistics();

        public IReadOnlyList<string> LogFiles
        {
            get
            {
                List<string> files = [];
                Monitor monitor = main.monitor;
                if (monitor != null)
                {
                    if (File.Exists(monitor.logName))
                    {
                        files.Add(monitor.logName);
                    }
                    if (File.Exists(monitor.previousName))
                    {
                        files.Add(monitor.previousName);
                    }
                }
                return files;
            }
        }
    }
}
