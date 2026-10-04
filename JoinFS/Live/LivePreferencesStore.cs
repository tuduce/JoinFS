using System;
using System.Drawing;
using System.Threading;
using JoinFS.Properties;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// The Settings tab's options, kept in the same Settings.Default the Settings dialog of the forms uses, so the old and new UI see
    /// each other's changes. Saving does what the dialog's OK button does: it updates the app's own copies (main.settings*) and
    /// carries out what a change means for the running app (a new port is opened, hub mode starts or stops the hub).
    /// The tab saves at every change, so the file is written once things have been quiet for a moment.
    /// </summary>
    class LivePreferencesStore : IPreferencesStore
    {
        /// <summary>How long after the last change Settings.Default is written to disk.</summary>
        const int WRITE_DELAY_MS = 500;

        readonly Main main;
        readonly System.Threading.Timer writer;

        /// <summary>What the app is running with, to tell what a save changes.</summary>
        Preferences applied;

        public LivePreferencesStore(Main main)
        {
            this.main = main;
            writer = new System.Threading.Timer(_ => Settings.Default.Save());
        }

        public Preferences Load()
        {
            Settings s = Settings.Default;
            applied = new Preferences
            {
                AlwaysOnTop = s.AlwaysOnTop,
                AutoRefresh = s.AutoRefresh,
                ToolTips = s.ToolTips,
                ConnectOnLaunch = s.ConnectOnLaunch,
                ElevationCorrection = s.ElevationCorrection,
                CircleOfActivityNm = s.ActivityCircle,
                FollowDistanceM = s.FollowDistance,
                AutoImportSimbrief = s.SimBriefAutoImport,
                ShowNickname = s.ShowNicknames,
                ShowCallsign = s.ShowCallsign,
                ShowDistance = s.ShowDistance,
                ShowAltitude = s.ShowAltitude,
                ShowSpeed = s.ShowSpeed,
                LabelColor = ToHex(s.ColourLabel),
                ChooseOwnPort = s.LocalPortEnabled,
                LocalPort = s.LocalPort,
                JoinGlobalAtLaunch = s.Global,
                LowBandwidth = s.LowBandwidth,
                GenerateWhazzup = s.Whazzup,
                WhazzupIncludeGlobalUsers = s.WhazzupGlobal,
                WhazzupIncludeAi = s.WhazzupAI,
                Password = (s.Password ?? "").Trim(),
                HubMode = s.Hub,
                HubDomain = s.HubAddress ?? "",
                HubName = s.HubName ?? "",
                HubAbout = s.HubAbout ?? "",
                HubVoice = s.HubVoIP ?? "",
                HubEvent = s.HubEvent ?? "",
                XPlaneAddress = s.XPlanePluginAddress ?? "",
                Tcas = s.TCAS,
            };
            return applied.Clone();
        }

        public void Save(Preferences next)
        {
            Preferences previous = applied ?? Load();
            Settings s = Settings.Default;

            // what the dialog's OK writes, in the same places
            s.AlwaysOnTop = next.AlwaysOnTop;
            s.AutoRefresh = next.AutoRefresh;
            s.ToolTips = next.ToolTips;
            s.ConnectOnLaunch = next.ConnectOnLaunch;
            s.ElevationCorrection = next.ElevationCorrection;
            s.ActivityCircle = next.CircleOfActivityNm;
            s.FollowDistance = next.FollowDistanceM;
            s.SimBriefAutoImport = next.AutoImportSimbrief;
            s.ShowNicknames = next.ShowNickname;
            s.ShowCallsign = next.ShowCallsign;
            s.ShowDistance = next.ShowDistance;
            s.ShowAltitude = next.ShowAltitude;
            s.ShowSpeed = next.ShowSpeed;
            s.ColourLabel = FromHex(next.LabelColor, s.ColourLabel);
            s.LocalPortEnabled = next.ChooseOwnPort;
            s.LocalPort = (ushort)Math.Clamp(next.LocalPort, 1, ushort.MaxValue);
            s.Global = next.JoinGlobalAtLaunch;
            s.LowBandwidth = next.LowBandwidth;
            s.Whazzup = next.GenerateWhazzup;
            s.WhazzupGlobal = next.WhazzupIncludeGlobalUsers;
            s.WhazzupAI = next.WhazzupIncludeAi;
            s.Password = next.Password;
            s.Hub = next.HubMode;
            s.HubAddress = next.HubDomain;
            s.HubName = next.HubName;
            s.HubAbout = next.HubAbout;
            s.HubVoIP = next.HubVoice;
            s.HubEvent = next.HubEvent;
            s.XPlanePluginAddress = next.XPlaneAddress;
            s.TCAS = next.Tcas;

            // the app's own copies, which the network, WhazzUp and the simulator read
            main.settingsConnectOnLaunch = next.ConnectOnLaunch;
            main.settingsActivityCircle = next.CircleOfActivityNm;
            main.settingsWhazzup = next.GenerateWhazzup;
            main.settingsWhazzupPublic = next.WhazzupIncludeGlobalUsers;
            main.settingsPortEnabled = next.ChooseOwnPort;
            main.settingsPort = s.LocalPort;
            main.settingsPassword = next.Password;
            main.settingsHub = next.HubMode;
            main.settingsHubDomain = next.HubDomain;
            main.settingsHubName = next.HubName;
            main.settingsHubAbout = next.HubAbout;
            main.settingsHubVoip = next.HubVoice;
            main.settingsHubEvent = next.HubEvent;
            main.settingsTcas = next.Tcas;

            applied = next.Clone();
            Apply(previous, next);
            writer.Change(WRITE_DELAY_MS, Timeout.Infinite);
        }

        /// <summary>
        /// What the change means for the running app. Only what changed is acted on: a slider that sends a save at every step
        /// must not reopen the port or restart the hub.
        /// </summary>
        void Apply(Preferences previous, Preferences next)
        {
            if (next.LowBandwidth != previous.LowBandwidth)
            {
                lock (main.conch)
                {
                    main.network.LowBandwidth = next.LowBandwidth;
                }
            }

            if (next.ShowNickname != previous.ShowNickname)
            {
                lock (main.conch)
                {
                    // the aircraft already in the simulator carry the old label
                    main.SimCommand(sim => sim.RemoveInjectedObjects());
                }
            }

            if (next.HubMode != previous.HubMode)
            {
                // the hub is created or dropped by the network
                main.network.ScheduleLeave();
                if (next.HubMode)
                {
                    main.network.ScheduleCreate();
                }
            }

            int oldPort = PortInUse(previous);
            int newPort = PortInUse(next);
            if (newPort != oldPort)
            {
                lock (main.conch)
                {
                    if (main.network.Open(newPort))
                    {
                        main.MonitorEvent("Closed UDP port " + oldPort);
                        main.MonitorEvent("Opened UDP port " + newPort);

                        // the hub has to announce itself on the new port
                        if (main.settingsHub)
                        {
                            main.network.ScheduleCreate();
                        }
                    }
                }
            }
        }

        static int PortInUse(Preferences p) => p.ChooseOwnPort ? p.LocalPort : Network.DEFAULT_PORT;

        static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        static Color FromHex(string hex, Color fallback)
        {
            try
            {
                return ColorTranslator.FromHtml(hex);
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
