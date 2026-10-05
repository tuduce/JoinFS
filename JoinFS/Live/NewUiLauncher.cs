using System;
using JoinFS.UI;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;

namespace JoinFS.Live
{
    /// <summary>
    /// Starts the Avalonia UI (JoinFS.UI) on top of the running app. Each service below that is a Live* class talks to the real
    /// JoinFS; the rest are still JoinFS.UI's fakes, replaced here one at a time as they are wired.
    /// </summary>
    static class NewUiLauncher
    {
        /// <summary>
        /// Run the UI on the calling thread. Returns when its window is closed.
        /// </summary>
        public static void Run(Main main)
        {
            // the plan and SimBrief share what an import brings that the tab has no field for
            LiveFlightPlanSource flightPlan = new(main);
            UiHost.Run(platform => FakeServices.Create(TimeSpan.FromMilliseconds(900), platform: platform) with
            {
                FlightPlan = flightPlan,
                SimBrief = flightPlan,
                Simulator = new LiveSimulatorLink(main),
                Network = new LiveNetworkLink(main),
                Session = new LiveSessionSource(main),
                Traffic = new LiveTrafficSource(main),
                Hubs = new LiveHubDirectory(main),
                App = new LiveAppInfo(),
                Settings = new LiveSettingsStore(main),
                AddressBook = new LiveAddressBookStore(main),
                Updates = new LiveUpdateChecker(main),
                Preferences = new LivePreferencesStore(main),
                Models = new LiveModelCatalog(main),
                Variables = new LiveVariablesCatalog(main),
                Monitor = new LiveMonitorSource(main),
                Chat = new LiveChatSource(main),
                Recorder = new LiveRecorderSource(main),
                ModelScan = new LiveModelScanSource(main),
                XPlaneScan = new LiveXPlaneScanSource(main),
                MapTiles = new OsmTileSource("JoinFS/" + Main.Version + " (+https://github.com/tuduce/JoinFS)", System.IO.Path.Combine(main.storagePath, "map-tiles")),
            }, [], main.MonitorEvent);
        }
    }
}
