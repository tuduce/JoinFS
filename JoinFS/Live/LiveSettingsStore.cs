using JoinFS.Properties;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// The user's settings, kept in the same Settings.Default the forms use, so the old and new UI see each other's changes.
    /// What Settings does not hold yet (model overrides, height adjustments) stays in memory for now.
    /// </summary>
    class LiveSettingsStore : ISettingsStore
    {
        /// <summary>
        /// The shortest nickname the app accepts. Anything shorter and it asks for first-run setup.
        /// </summary>
        public const int MIN_NICKNAME_LENGTH = 2;

        readonly Main main;
        readonly UserSettings user = new();

        public LiveSettingsStore(Main main)
        {
            this.main = main;
        }

        public static bool IsOnboarded(string nickname) => (nickname ?? "").Length >= MIN_NICKNAME_LENGTH;

        public UserSettings Load()
        {
            Settings s = Settings.Default;
            user.Nickname = s.Nickname ?? "";
            user.Onboarded = IsOnboarded(user.Nickname);
            user.SimbriefUsername = string.IsNullOrWhiteSpace(s.SimBriefUsername) ? null : s.SimBriefUsername;
            user.BroadcastTacpack = s.BroadcastTacpack;
            user.BroadcastEverything = s.AutoBroadcast;
            user.ModelScanOnConnect = s.ModelScanOnConnection;
            user.GenerateCsl = s.GenerateCsl;
            user.SkipCsl = s.SkipCsl;
            return user;
        }

        public void Save(UserSettings settings)
        {
            Settings s = Settings.Default;

            // the app's own copies, which the network and the scanner read
            main.settingsNickname = settings.Nickname ?? "";
            main.settingsScan = settings.ModelScanOnConnect;
#if XPLANE
            main.settingsGenerateCsl = settings.GenerateCsl;
            main.settingsSkipCsl = settings.SkipCsl;
#endif

            s.Nickname = settings.Nickname ?? "";
            s.SimBriefUsername = settings.SimbriefUsername ?? "";
            s.BroadcastTacpack = settings.BroadcastTacpack;
            s.AutoBroadcast = settings.BroadcastEverything;
            s.ModelScanOnConnection = settings.ModelScanOnConnect;
            s.GenerateCsl = settings.GenerateCsl;
            s.SkipCsl = settings.SkipCsl;
            s.Save();
        }
    }
}
