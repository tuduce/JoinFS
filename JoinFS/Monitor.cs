using System;
using System.Collections.Generic;
using System.IO;

namespace JoinFS
{
    public class Monitor
    {
        public const string LOG_FILE = "log";

        Main main;

        /// <summary>
        /// Log files
        /// </summary>
        public string logName = "";
        public string previousName = "";

        /// <summary>Base path shared by every generation of the rotated previous-log file (see Fix 4d).</summary>
        string previousBase = "";

        /// <summary>
        /// displayed lines of text
        /// </summary>
        public List<string> lines = [];

        StreamWriter writer;

        /// <summary>
        /// Guards all mutation of <see cref="lines"/> and writes to <see cref="writer"/> - see Fix 4d.
        /// MonitorForm reads <see cref="lines"/> off the UI thread while the work thread appends to it here.
        /// </summary>
        readonly object writeLock = new();

        /// <summary>Number of previous-generation log files to keep (see Fix 4d) - a crash log then survives several restarts.</summary>
        const int LogGenerations = 5;

        /// <summary>
        /// Keep track of repeated lines
        /// </summary>
        int repeatCount = 1;

        /// <summary>
        /// Show network events
        /// </summary>
        public bool network = false;

        /// <summary>
        /// Show variable events
        /// </summary>
        public bool variables = false;

        /// <summary>
        /// Open log file
        /// </summary>
        public void OpenLog()
        {
            // check that log file is currently closed
            if (writer == null)
            {
                // check if file exists
                if (File.Exists(logName))
                {
                    // rotate previous log files through several generations, so a crash log written
                    // shortly before shutdown survives the user's next few launches instead of being
                    // overwritten on the very next start (see Fix 4d).
                    try
                    {
                        string GenName(int gen) => gen <= 1 ? previousName : previousBase + "." + gen + ".txt";

                        // drop the oldest, then shuffle each generation up by one
                        if (File.Exists(GenName(LogGenerations))) File.Delete(GenName(LogGenerations));
                        for (int gen = LogGenerations - 1; gen >= 1; gen--)
                        {
                            if (File.Exists(GenName(gen)))
                            {
                                File.Move(GenName(gen), GenName(gen + 1));
                            }
                        }
                    }
                    catch { /* rotation is best-effort - fall through to the plain move below */ }

                    // rename current log file
                    if (File.Exists(previousName)) File.Delete(previousName);
                    File.Move(logName, previousName);
                }

                try
                {
                    // check for auto log
//                    if (Settings.Default.AutoLog)
                    if (true)
                    {
                            // open file
                            writer = new StreamWriter(logName)
                        {
                            // auto flush
                            AutoFlush = true
                        };
                        // write current lines
                        lock (writeLock)
                        {
                            foreach (var line in lines)
                            {
                                // save line to log file
                                writer.WriteLine(line);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    main.ShowMessage(ex.Message);
                }
            }
        }

        /// <summary>
        /// Close log
        /// </summary>
        public void CloseLog()
        {
            // close log file
            writer?.Close();
            writer = null;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="mainForm"></param>
        public Monitor(Main main)
        {
            this.main = main;

            // get port
            ushort port = main.settingsPortEnabled ? main.settingsPort : Network.DEFAULT_PORT;

            // make file name
            logName = main.storagePath + Path.DirectorySeparatorChar + LOG_FILE + "-" + port + ".txt";
            previousBase = main.storagePath + Path.DirectorySeparatorChar + LOG_FILE + "-" + port + "-previous";
            previousName = previousBase + ".txt";

            // check for auto log
//            if (Settings.Default.AutoLog)
            if (true)
            {
                    // open log
                    OpenLog();
            }
        }

        /// <summary>
        /// Process repeated lines
        /// </summary>
        void ProcessRepeat()
        {
            // check for repeated lines
            if (repeatCount > 1)
            {
                // make repeat line
                string repeatText = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff") + " - " + "[" + repeatCount.ToString() + " times]";
                // add line
                lines.Add(repeatText);
                // check for log file
                // save line to log file
                writer?.WriteLine(repeatText);
#if CONSOLE
                Console.WriteLine(repeatText);
#endif
                repeatCount = 1;
            }
        }

        /// <summary>
        /// Output some text to the event window
        /// </summary>
        /// <param name="text">Output text</param>
        public void Write(String text)
        {
            lock (writeLock)
            {
                // don't display previous line
                if (lines.Count == 0 || lines[lines.Count - 1].Length <= 26 || text.Equals(lines[lines.Count - 1].Substring(26)) == false)
                {
                    ProcessRepeat();

                    // include time
                    string line = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff") + " - " + text;
                    // add line
                    lines.Add(line);
                    // check for log file
                    // save line to log file
                    writer?.WriteLine(line);
#if CONSOLE
                    Console.WriteLine(line);
#endif
                }
                else
                {
                    // increment repeat
                    repeatCount++;
                }
            }
        }

        /// <summary>
        /// Thread-safe copy of the last <paramref name="count"/> log lines, for the crash writer (see Fix 4).
        /// </summary>
        public string[] LinesSnapshot(int count)
        {
            lock (writeLock)
            {
                int start = Math.Max(0, lines.Count - count);
                string[] result = new string[lines.Count - start];
                lines.CopyTo(start, result, 0, result.Length);
                return result;
            }
        }
    }
}
