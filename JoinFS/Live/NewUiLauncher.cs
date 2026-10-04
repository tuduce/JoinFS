using System;
using JoinFS.UI;
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
            UiHost.Run(platform => FakeServices.Create(TimeSpan.FromMilliseconds(900), platform: platform) with
            {
                Simulator = new LiveSimulatorLink(main),
                Network = new LiveNetworkLink(main),
                Session = new LiveSessionSource(main),
                App = new LiveAppInfo(),
                Settings = new LiveSettingsStore(main),
                AddressBook = new LiveAddressBookStore(main),
                Updates = new LiveUpdateChecker(main),
            }, [], main.MonitorEvent);
        }
    }
}
