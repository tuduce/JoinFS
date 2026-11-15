using System;
using System.Collections.Generic;

namespace JoinFS
{
    /// <summary>
    /// What to do when JoinFS needs the X-Plane plugin (the first run, a plugin of another version answering, a scan of a folder without
    /// it): install it by itself into a known X-Plane folder, once per folder and run, and offer the install card only when no folder is
    /// known or that install failed. Not wrapped in a simulator #if so it can be unit tested in every configuration. Thread-safe: the sim
    /// thread, the UI and the scan all ask.
    /// </summary>
    internal sealed class XPlanePluginAutoInstall
    {
        public enum Action
        {
            /// <summary>Install into the folder now, then call <see cref="Done"/>.</summary>
            Install,
            /// <summary>Offer the install card.</summary>
            Ask,
            /// <summary>An install into the folder is running or has succeeded this run (X-Plane only needs a restart).</summary>
            Nothing,
        }

        enum State { Installing, Installed, Failed }

        readonly object gate = new();
        readonly Dictionary<string, State> folders = new(StringComparer.OrdinalIgnoreCase);

        /// <param name="folder">A valid X-Plane folder, or null when none is known</param>
        public Action Decide(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return Action.Ask;
            }

            lock (gate)
            {
                if (folders.TryGetValue(folder, out State state) == false)
                {
                    folders[folder] = State.Installing;
                    return Action.Install;
                }
                return state == State.Failed ? Action.Ask : Action.Nothing;
            }
        }

        /// <summary>The install that <see cref="Decide"/> started has ended.</summary>
        public void Done(string folder, bool installed)
        {
            lock (gate)
            {
                folders[folder] = installed ? State.Installed : State.Failed;
            }
        }
    }
}
