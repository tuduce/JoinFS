using System.Drawing;

namespace JoinFS.Properties
{
    public class Settings
    {
        // marked as private to prevent outside classes from creating new.
        private Settings() { }

        private static Settings settings = new Settings();

        public static Settings Default { get { return settings; } }

        public void Save() { }

        public bool Euroscope { get; set; } = false;
        public bool AutoBroadcast { get; set; } = false;
        public int AtcLevel { get; set; } = 2;
        public int AtcFrequency { get; set; } = 22800;
        public string AtcAirport { get; set; } = "";
        public string MyIp { get; set; } = "";
        public bool LowBandwidth { get; set; } = false;
        public string Password { get; set; } = "";
        public bool ShareCockpitEveryone { get; set; } = false;
        public string AskImport { get; set; } = "0.0.0";
        public bool MigratedSettings { get; set; } = false;
        public string JoinAddress { get; set; } = "";
        public string Nickname { get; set; } = "";
        public string HubAddress { get; set; } = "";
        public string HubName { get; set; } = "";
        public string HubAbout { get; set; } = "";
        public string HubVoIP { get; set; } = "";
        public string HubEvent { get; set; } = "";
        public string AskVersion { get; set; } = "";
        public string RecordingFolder { get; set; } = "";
        public Guid LastPosition { get; set; } = Guid.Empty;
        public string XPlanePluginAddress { get; set; } = "";
        public bool AskSimConnect { get; set; } = false;
        public int SortBookmarksColumn { get; set; } = 0;
        public bool AutoRefresh { get; set; } = true;
        public bool BroadcastTacpack { get; set; } = false;
        public bool AutoLog { get; set; } = false;
        public bool MultipleObjectsEveryone { get; set; } = false;
        public bool LocalPortEnabled { get; set; } = false;
        public ushort LocalPort { get; set; } = 6112;
        public bool ConnectOnLaunch { get; set; } = true;
        public int ActivityCircle { get; set; } = 40;
        public bool Atc { get; set; } = false;
        public bool Hub { get; set; } = false;
        public bool Whazzup { get; set; } = false;
        public bool WhazzupGlobal { get; set; } = false;
        public bool Global { get; set; } = false;
        public bool ModelScanOnConnection { get; set; } = false;
        public bool ShowNicknames { get; set; } = true;
        public bool AtcWarning { get; set; } = false;
        public bool WhazzupAI { get; set; } = false;
        public bool ShowCallsign { get; set; } = true;
        public bool ShowDistance { get; set; } = true;
        public bool ShowAltitude { get; set; } = false;
        public bool ShowSpeed { get; set; } = false;
        public bool ElevationCorrection { get; set; } = true;
        public int FollowDistance { get; set; } = 80;
        public bool XPlane { get; set; } = false;
        public bool Loop { get; set; } = false;
        public Color ColourLabel { get; set; } = Color.Chartreuse;
        public bool SkipCsl { get; set; } = false;
        public bool GenerateCsl { get; set; } = false;
        public bool IncludeIgnoredAircraft { get; set; } = true;
        public bool IncludeSimulatorAircraft { get; set; } = true;
        public string XPlaneFolder { get; set; } = "";
        public bool TCAS { get; set; } = false;
    }
}