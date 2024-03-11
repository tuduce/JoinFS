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

        /// <summary>
        /// displayed lines of text
        /// </summary>
        public List<string> lines = new List<string>();

        StreamWriter writer;

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
                    // rename current log file
                    File.Delete(previousName);
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
                        foreach (var line in lines)
                        {
                            // save line to log file
                            writer.WriteLine(line);
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
            if (writer != null)
            {
                writer.Close();
                writer = null;
            }
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
            previousName = main.storagePath + Path.DirectorySeparatorChar + LOG_FILE + "-" + port + "-previous.txt";

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
                if (writer != null)
                {
                    // save line to log file
                    writer.WriteLine(repeatText);
                }
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
            // don't display previous line
            if (lines.Count == 0 || lines[lines.Count - 1].Length <= 26 || text.Equals(lines[lines.Count - 1].Substring(26)) == false)
            {
                ProcessRepeat();

                // include time
                string line = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff") + " - " + text;
                // add line
                lines.Add(line);
                // check for log file
                if (writer != null)
                {
                    // save line to log file
                    writer.WriteLine(line);
                }
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
}
