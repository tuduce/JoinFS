using System;
using System.Windows.Forms;
using JoinFS.Properties;

namespace JoinFS
{
    /// <summary>
    /// First-run setup: nickname, SimBrief username, and - only if the caller says
    /// detection couldn't resolve it - the simulator folder. Whether that folder prompt
    /// is even needed differs per simulator:
    /// - FSX/P3D/X-Plane: model matching is a pure folder scan, so this dialog's manual
    ///   folder pick (or an earlier successful auto-detect) is enough on its own -
    ///   Program.cs runs the scan immediately once a folder is known.
    /// - MSFS2020: needs a folder scan too, but the add-ons list it also reads is only
    ///   populated once the sim actually connects, so the real scan happens then.
    /// - MSFS2024: fetches its community model set by asking the running sim directly
    ///   (SimConnect), not by scanning the folder - a folder is still saved here (for the
    ///   base/default aircraft), but the model list can't be completed until connected.
    /// </summary>
    public partial class InitialSetupForm : Form
    {
        // return nickname
        public string nickname = "";

        // return SimBrief username (may be blank)
        public string simBriefUsername = "";

        // return simulator folder (only meaningful if the folder section was shown)
        public string simulatorFolder = "";

        readonly bool showFolderPrompt;

        /// <param name="main">Main instance.</param>
        /// <param name="showFolderPrompt">
        /// Whether to show the simulator-folder section. Must be passed in by the caller
        /// (rather than read from <c>main.scheduleSimFolderPrompt</c> here) because that
        /// flag is already reset to false by the time this constructor runs - see
        /// MainForm.cs's scheduled-setup handler, which captures it into a local before
        /// clearing it.
        /// </param>
        public InitialSetupForm(Main main, bool showFolderPrompt)
        {
            InitializeComponent();

            this.showFolderPrompt = showFolderPrompt;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // set font
            Text_Nickname.Font = main.dataFont;
            Text_SimBriefUsername.Font = main.dataFont;
            Text_Folder.Font = main.dataFont;

            // pre-fill the existing nickname, so re-showing this dialog for an unrelated
            // reason (e.g. only the folder needs asking) can't blank out an already-valid one
            Text_Nickname.Text = main.settingsNickname;

            // pre-fill the existing SimBrief username, if any
            Text_SimBriefUsername.Text = Settings.Default.SimBriefUsername;

            // only ask for the simulator folder when auto-detection couldn't resolve it
            if (showFolderPrompt)
            {
                Label_FolderPrompt.Text = GetFolderPromptText();
            }
            else
            {
                HideFolderSection();
            }
        }

        static string GetFolderPromptText()
        {
#if FS2020 || FS2024
            return "JoinFS couldn't automatically find your Flight Simulator Packages folder. Please specify the 'Flight Simulator Packages' folder (the parent of Official/Community):";
#elif XPLANE
            return "JoinFS couldn't automatically find your X-Plane installation. Please specify the main X-Plane folder:";
#else
            return "JoinFS couldn't automatically find your simulator installation. Please specify the main simulator folder:";
#endif
        }

        /// <summary>
        /// Hide the folder-picker section and shrink the dialog to fit.
        /// </summary>
        void HideFolderSection()
        {
            int reclaimedHeight = Button_OK.Top - Label_FolderPrompt.Top;

            Label_FolderPrompt.Visible = false;
            Text_Folder.Visible = false;
            Button_Browse.Visible = false;

            Button_OK.Top -= reclaimedHeight;
            ClientSize = new System.Drawing.Size(ClientSize.Width, ClientSize.Height - reclaimedHeight);
        }

        private void Button_Browse_Click(object sender, EventArgs e)
        {
            var dialog = new FolderBrowserDialog
            {
                Description = "Select the main simulator folder",
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                Text_Folder.Text = dialog.SelectedPath;
            }
        }

        private void Button_OK_Click(object sender, EventArgs e)
        {
#if XPLANE
            // refuse a folder that is not an X-Plane install and keep the dialog open
            if (showFolderPrompt && XPlaneForm.ConfirmValidFolder(Text_Folder.Text.Trim()) == false)
            {
                DialogResult = DialogResult.None;
                return;
            }
#endif
            // return nickname
            nickname = Text_Nickname.Text;
            // return SimBrief username
            simBriefUsername = Text_SimBriefUsername.Text.Trim();
            // return simulator folder, if it was asked for
            simulatorFolder = showFolderPrompt ? Text_Folder.Text.Trim() : "";
        }
    }
}
