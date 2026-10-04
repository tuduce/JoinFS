using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// What the About box and title bar say, from the running build
    /// </summary>
    class LiveAppInfo : IAppInfo
    {
        public string Version => Main.Version;

        public string SessionLabel => Main.Name;

        // the link the old About box opened
        public string DocumentationUrl => "https://joinfs.net";

        // where the old update prompt sent people
        public string DownloadUrl => "https://github.com/tuduce/JoinFS/releases";

        // Licence.txt
        public string Copyright => "\u00a9 2025 JoinFS Contributors";

        public bool IsXPlaneBuild =>
#if XPLANE
            true;
#else
            false;
#endif
    }
}
