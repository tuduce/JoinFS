using System;
using System.Threading.Tasks;
using System.Net.Http;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// Is there a newer release? Reads the same version file the forms did, once, in the background.
    /// </summary>
    class LiveUpdateChecker : IUpdateChecker
    {
        const string VERSION_URL = "https://raw.githubusercontent.com/tuduce/JoinFS/refs/heads/main/JoinFS/util/version.txt";

        readonly Main main;
        volatile string latest = "";

        public LiveUpdateChecker(Main main)
        {
            this.main = main;
            _ = FetchAsync();
        }

        async Task FetchAsync()
        {
            try
            {
                using HttpClient client = new();
                string text = await client.GetStringAsync(VERSION_URL);
                // a web page (an error, a redirect) is not a version
                if (!string.IsNullOrEmpty(text) && text[0] != '<')
                {
                    latest = text.Trim();
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent(ex.Message);
            }
        }

        public UpdateInfo CheckForUpdate() =>
            IsNewer(Main.Version, latest) ? new UpdateInfo(latest, "https://github.com/tuduce/JoinFS/releases") : null;

        /// <summary>
        /// True when <paramref name="latest"/> is a version above <paramref name="current"/>. Anything unreadable is not newer.
        /// </summary>
        public static bool IsNewer(string current, string latest) =>
            Version.TryParse(current?.Trim(), out Version c) && Version.TryParse(latest?.Trim(), out Version l) && c < l;
    }
}
