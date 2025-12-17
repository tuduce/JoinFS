using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Net;
using System.Drawing;
using System.IO;
using System.Globalization;
using JoinFS.Properties;
using System.Net.Http;
using System.Threading.Tasks;


namespace JoinFS
{
    public partial class MainForm : Form
    {
        // User-defined win32 event
        public const int WM_USER_SIMCONNECT = 0x0402;

        const int SHORTCUTS_INTERVAL = 100;
        const int REFRESH_WINDOWS_INTERVAL = 1000;
        const int REFRESH_MAIN_INTERVAL = 1000;
        const int REFRESH_COMMS_INTERVAL = 300;

        /// <summary>
        /// Timers
        /// </summary>
        System.Windows.Forms.Timer shortcutsTimer = new();
        System.Windows.Forms.Timer refreshWindowsTimer = new();
        System.Windows.Forms.Timer refreshMainTimer = new();
        System.Windows.Forms.Timer refreshCommsTimer = new();

        /// <summary>
        /// Has the current recording been saved
        /// </summary>
        public bool unsaved = false;

        /// <summary>
        /// New version available
        /// </summary>
        bool newVersion = false;

        /// <summary>
        /// Main instance
        /// </summary>
        Main main;

        /// <summary>
        /// Constructor
        /// </summary>
        public MainForm(Main main)
        {
            try
            {
                InitializeComponent();

                // set main
                this.main = main;

#if NO_CREATE
                Text_MyIP.Visible = false;
                Button_Create.Visible = false;
                Combo_Join.Location = new Point(Combo_Join.Location.X, Combo_Join.Location.Y - 24);
                Button_Join.Location = new Point(Button_Join.Location.X, Button_Join.Location.Y - 24);
                Button_Network.Location = new Point(Button_Network.Location.X, Button_Network.Location.Y - 24);
                Button_Simulator.Location = new Point(Button_Simulator.Location.X, Button_Simulator.Location.Y - 24);
                Button_Global.Location = new Point(Button_Global.Location.X, Button_Global.Location.Y - 24);
                Size = new Size(Size.Width, Size.Height - 44);
#endif

#if NO_JOIN
                Combo_Join.Visible = false;
                Button_Join.Visible = false;
                Button_Network.Location = new Point(Button_Network.Location.X, Button_Network.Location.Y - 26);
                Button_Simulator.Location = new Point(Button_Simulator.Location.X, Button_Simulator.Location.Y - 26);
                Button_Global.Location = new Point(Button_Global.Location.X, Button_Global.Location.Y - 26);
                Button_Network.Size = new Size(Button_Network.Width, Button_Network.Height + 4);
                Button_Simulator.Size = new Size(Button_Simulator.Width, Button_Simulator.Height + 4);
                Button_Global.Size = new Size(Button_Global.Width, Button_Global.Height + 4);
                Size = new Size(Size.Width, Size.Height - 20);
#endif

                if (main.settingsNoSim)
                {
                    Button_Simulator.Visible = false;
                    Button_Network.Size = new Size(Button_Network.Size.Width + Button_Network.Location.X - Button_Simulator.Location.X, Button_Network.Size.Height);
                    Button_Network.Location = new Point(Button_Simulator.Location.X, Button_Network.Location.Y);

                    Menu_File_ScanModels.Visible = false;
                    Menu_File_ModelMatching.Visible = false;
                }

#if SERVER
                Button_Global.Visible = false;
                Button_Network.Size = new Size(Button_Global.Location.X - Button_Network.Location.X + Button_Global.Size.Width, Button_Network.Size.Height);

                Menu_File_Variables.Visible = false;
                Menu_File_SimComX.Visible = false;
                Menu_View_Atc.Visible = false;
                Menu_View_Aircraft.Visible = false;
                Menu_View_Objects.Visible = false;
                Menu_Help_Radar.Visible = false;
#endif

                // change font
                Text_MyIP.Font = main.dataFont;
                Combo_Join.Font = main.dataFont;

                // change title
                Text = Main.Name;
                // change icon
                Icon = main.icon;
                // remove JoinFS from title
                Text = Text.Replace("JoinFS: ", "");
                // check About menu option
                Menu_Help_About.Text = Menu_Help_About.Text.Replace("JoinFS", Main.Name + "...");
                // set join address
                Combo_Join.Text = Settings.Default.JoinAddress;

#if NO_HUBS
                Combo_Join.Text = Network.EncodeIP(Settings.Default.JoinAddress);
                Menu_View_Hubs.Visible = false;
                Menu_View_Atc.Visible = false;
                Settings.Default.AtcFormOpen = false;
                Settings.Default.HubsFormOpen = false;
#else
                Text_MyIP.Text = Network.UuidToString(main.uuid);
#endif

                Tool_Version.Text = Main.Version;

#if NO_MAP
                Tool_Map.Enabled = false;
                Tool_Map.Text = "";
#endif


#if NO_GLOBAL
                Button_Global.Visible = false;
#endif


#if NO_UPDATE
                Tool_Update.Enabled = false;
                Tool_Update.Text = "";
#else
                Tool_Update.Text = Resources.Strings.Download;
#endif


#if !NO_UPDATE
                // Replaces WebClient usage
                _ = FetchLatestVersionAsync();
#endif

                // load shortcuts
                LoadShortcuts();

                RefreshNetwork();
                RefreshSim();

                // check if recording
                if (main.recorder.recording)
                {
                    // set unsaved flag
                    unsaved = true;
                }

                // check for minimize
                if (main.settingsMinimized)
                {
                    // minimize
                    WindowState = FormWindowState.Minimized;
                }

                // check nickname length
                if (main.settingsNickname.Length < 2)
                {
                    // show nickname form
                    main.scheduleNickname = true;
                }

                // initialize shortcuts timer
                shortcutsTimer.Tick += new EventHandler(DoShortcuts);
                shortcutsTimer.Interval = SHORTCUTS_INTERVAL;
                shortcutsTimer.Start();
                // initialize window refresh timer
                refreshWindowsTimer.Tick += new EventHandler(RefreshWindows);
                refreshWindowsTimer.Interval = REFRESH_WINDOWS_INTERVAL;
                refreshWindowsTimer.Start();
                // initialize main refresh timer
                refreshMainTimer.Tick += new EventHandler(RefreshMain);
                refreshMainTimer.Interval = REFRESH_MAIN_INTERVAL;
                refreshMainTimer.Start();
#if !NO_COMMS
                // initialize comms refresh timer
                refreshCommsTimer.Tick += new EventHandler(RefreshComms);
                refreshCommsTimer.Interval = REFRESH_COMMS_INTERVAL;
                refreshCommsTimer.Start();
#endif
            }
            catch (Exception ex)
            {
                main.ShowMessage(ex.Message);
            }
        }

        /// <summary>
        /// For checking version
        /// </summary>
        // WebClient versionWebClient = new WebClient(); // obsolete
        HttpClient versionHttpClient = new();
        string latestVersion = null;

        /// <summary>
        /// Fetch latest version async (replaces WebClient)
        /// </summary>
        private async Task FetchLatestVersionAsync()
        {
            try
            {
                string url;
                if (Settings.Default.EarlyUpdate)
                {
                    url = "https://raw.githubusercontent.com/tuduce/JoinFS/refs/heads/main/JoinFS/util/version.txt";
                }
                else
                {
                    url = "https://raw.githubusercontent.com/tuduce/JoinFS/refs/heads/main/JoinFS/util/version.txt";
                }
                var result = await versionHttpClient.GetStringAsync(url);
                if (!string.IsNullOrEmpty(result) && result[0] != '<')
                {
                    latestVersion = result;
                }
                else
                {
                    latestVersion = "";
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent(ex.Message);
                latestVersion = "";
            }
        }

#region Shortcuts

        // Get Key state
        [DllImport("User32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        // key codes
        static readonly int VK_CONTROL = 0x11;
        static readonly int VK_SHIFT = 0x10;
        static readonly int VK_ALT = 0x12;
        static readonly int VK_A = 0x41;
        static readonly int VK_Z = 0x5A;

        /// <summary>
        /// Shortcut
        /// </summary>
        public class Shortcut
        {
            public string combination;
            public bool control;
            public bool shift;
            public bool alt;
            public int letter;
            public bool state;
        }

        /// <summary>
        /// Shortcuts
        /// </summary>
        public Shortcut networkShortcut = new();
        public Shortcut simulatorShortcut = new();
        public Shortcut allowSharedShortcut = new();
        public Shortcut handOverShortcut = new();
        public Shortcut enterShortcut = new();
        public Shortcut followShortcut = new();

        /// <summary>
        /// Check if a particular key is pressed
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        static bool KeyPressed(int key)
        {
            return ((GetAsyncKeyState(key) >> 15) & 0x0001) == 0x0001;
        }

        /// <summary>
        /// Check if a key combination is pressed
        /// </summary>
        /// <param name="control"></param>
        /// <param name="shift"></param>
        /// <param name="alt"></param>
        /// <param name="shortcut"></param>
        /// <returns></returns>
        static bool CombinationPressed(bool control, bool shift, bool alt, Shortcut shortcut)
        {
            // update key state
            bool before = shortcut.state;
            shortcut.state = KeyPressed(shortcut.letter);
            // check if combination pressed
            return shortcut.control == control && shortcut.shift == shift && shortcut.alt == alt && before == false && shortcut.state;
        }

        /// <summary>
        /// Load a shortcut
        /// </summary>
        /// <param name="name"></param>
        /// <param name="defaultKey"></param>
        /// <param name="shortcut"></param>
        static void LoadShortcut(string combination, Shortcut shortcut)
        {
            // initialize shortcut
            shortcut.control = false;
            shortcut.shift = false;
            shortcut.alt = false;
            shortcut.letter = VK_A;
            shortcut.state = false;
            // get combination setting
            shortcut.combination = combination;
            // parse keys
            string[] keys = shortcut.combination.Split('+');

            // for each combination key
            for (int index = 0; index < keys.Length - 1; index++)
            {
                // set combination key
                if (keys[index].Equals("CTRL"))
                {
                    shortcut.control = true;
                }
                else if (keys[index].Equals("SHIFT"))
                {
                    shortcut.shift = true;
                }
                else if (keys[index].Equals("ALT"))
                {
                    shortcut.alt = true;
                }
            }

            // check for valid key
            if (keys.Length > 0)
            {
                // convert to char array
                char[] chars = keys[keys.Length - 1].ToCharArray();
                // check for valid letter
                if (chars.Length > 0 && chars[0] >= VK_A && chars[0] <= VK_Z)
                {
                    shortcut.letter = chars[0];
                }
            }
        }

        /// <summary>
        /// Load all shortcuts
        /// </summary>
        public void LoadShortcuts()
        {
            LoadShortcut(Settings.Default.ShortcutNetworkKey, networkShortcut);
            LoadShortcut(Settings.Default.ShortcutSimulatorKey, simulatorShortcut);
            LoadShortcut(Settings.Default.ShortcutAllowSharedKey, allowSharedShortcut);
            LoadShortcut(Settings.Default.ShortcutHandOverKey, handOverShortcut);
            LoadShortcut(Settings.Default.ShortcutEnterKey, enterShortcut);
            LoadShortcut(Settings.Default.ShortcutFollowKey, followShortcut);
        }

        /// <summary>
        /// Scanning for shorcuts
        /// </summary>
        public bool shortcutScanning = false;

        /// <summary>
        /// Process shortcuts
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void DoShortcuts(object sender, System.EventArgs e)
        {
            // get combination state
            bool control = KeyPressed(VK_CONTROL);
            bool shift = KeyPressed(VK_SHIFT);
            bool alt = KeyPressed(VK_ALT);

            // check if scanning for new shortcuts
            if (shortcutScanning == false)
            {
                // check if network key pressed
                if (Settings.Default.ShortcutNetwork && CombinationPressed(control, shift, alt, networkShortcut))
                {
                    // toggle network state
                    ToggleNetwork();
                }

                // check if simulator key pressed
                if (Settings.Default.ShortcutSimulator && CombinationPressed(control, shift, alt, simulatorShortcut))
                {
                    // toggle simulator state
                    main.ToggleSimulator();
                    RefreshSim();
                }

                // check if allow shared key pressed
                if (Settings.Default.ShortcutAllowShared && CombinationPressed(control, shift, alt, allowSharedShortcut))
                {
                    // check for session form
                    if (main.sessionForm != null)
                    {
                        // get selected user
                        LocalNode.Nuid nuid = main.sessionForm.GetSelectedNuid();
                        // check for valid user
                        if (nuid.Valid())
                        {
                            // get current share state
                            bool share = main.log.ShareCockpit(nuid);
                            // check state
                            if (share)
                            {
                                // disable
                                main.log.RemoveShareCockpit(nuid);
                            }
                            else
                            {
                                // enable
                                main.log.AddShareCockpit(nuid);
                            }
                            // refresh
                            main.sessionForm ?. usersRefresher.Schedule();
                        }
                    }
                }

                // check if hand over key pressed
                if (Settings.Default.ShortcutHandOver && CombinationPressed(control, shift, alt, handOverShortcut))
                {
                    // check for session form
                    if (main.sessionForm != null)
                    {
                        // get selected user
                        LocalNode.Nuid nuid = main.sessionForm.GetSelectedNuid();
                        // check for valid user
                        if (nuid.Valid())
                        {
                            // get current hand over state
                            bool handOver = main.network.shareFlightControls == nuid;
                            // check state
                            if (handOver)
                            {
                                // disable
                                main.network.shareFlightControls = new LocalNode.Nuid();
                            }
                            else
                            {
                                // enable
                                main.network.shareFlightControls = nuid;
                            }
                            // refresh
                            main.sessionForm ?. usersRefresher.Schedule();
                        }
                    }
                }

#if !SERVER
                // check if enter key pressed
                if (Settings.Default.ShortcutEnter && CombinationPressed(control, shift, alt, enterShortcut))
                {
                    // check for aircraft form
                    // enter/leave cockpit
                    main.aircraftForm?.Context_Aircraft_EnterCockpit_Click(null, EventArgs.Empty);
                }

                // check if follow key pressed
                if (Settings.Default.ShortcutFollow && CombinationPressed(control, shift, alt, followShortcut))
                {
                    // check for aircraft form
                    // follow aircraft
                    main.aircraftForm?.Context_Aircraft_Follow_Click(null, EventArgs.Empty);
                }
#endif
            }
        }

#endregion

        /// <summary>
        /// Refresh is active
        /// </summary>
        volatile bool refreshActive = false;
        bool refreshForce = false;

        private void RefreshWindows(object sender, System.EventArgs e)
        {
            // check if refresh is already active
            if (refreshActive)
            {
                return;
            }

            // refresh is now active
            refreshActive = true;

            // check for first iterations
            if (main.ElapsedTime < 6.0)
            {
                // force a refresh
                refreshForce = true;
            }

            // refresh hub refresh button
            main.hubsForm?.DoRefreshButton(refreshForce);

            // refresh address book window
            main.addressBookForm?.DoRefreshButton(refreshForce);

#if !SERVER
            // refresh aircraft window
            main.aircraftForm?.DoRefreshButton(refreshForce);

            // refresh objects window
            main.objectsForm?.DoRefreshButton(refreshForce);

            // refresh ATC window
            main.atcForm?.DoRefreshButton(refreshForce);
#endif

            // refresh users window
            main.sessionForm?.DoRefreshButton(refreshForce);

            // refresh monitor window
            main.monitorForm?.DoRefreshButton(refreshForce);

            // reset force flag
            refreshForce = false;
            // refresh is no longer active
            refreshActive = false;
        }

        /// <summary>
        /// Refresh the join box
        /// </summary>
        public bool refreshJoinCombo = false;


        private void RefreshMain(object sender, System.EventArgs e)
        {
            // check if refresh is already active
            if (refreshActive)
            {
                return;
            }

            // refresh is now active
            refreshActive = true;

            // check for shutdown
            if (main.shutdown != null)
            {
                // check for message
                if (main.shutdown.Length > 0)
                {
                    // show message
                    MessageBox.Show(main.shutdown, Main.Name);
                }

                // quit
                Application.Exit();
            }

            // check if latest version available
            if (latestVersion != null)
            {
                if (latestVersion.Length > 0)
                {
                    Version latest = new(latestVersion);
                    Version current = new(Main.Version);
                    if (current < latest)
                    {
                        // check if this version has been asked about
                        if (Settings.Default.AskVersion.Equals(latestVersion) == false)
                        {
                            DialogResult result = MessageBox.Show(Resources.Strings.NewVersion, Main.Name + ": New Version", MessageBoxButtons.YesNo);
                            if (result == DialogResult.Yes)
                            {
                                // check for early update
                                if (Settings.Default.EarlyUpdate)
                                {
                                    // open install page
                                    //string sc = Program.Code("https://joinfs.net/development.html", true, 1234);
                                    //string sc = Program.Code("https://github.com/tuduce/JoinFS/releases", true, 1234);
                                    // send the users to the releases page of the github repo 
                                    Main.Launch("https://github.com/tuduce/JoinFS/releases");
                                }
                                else
                                {
                                    // open install page
                                    //string sc = Program.Code("https://joinfs.net/install.html", true, 1234);
                                    //string sc = Program.Code("https://github.com/tuduce/JoinFS/releases", true, 1234);
                                    // send the users to the releases page of the github repo
                                    Main.Launch("https://github.com/tuduce/JoinFS/releases");
                                }
                                // shutdown
                                main.shutdown = "";
                            }

                            // write version
                            Settings.Default.AskVersion = latestVersion;
                        }
                        // new version available
                        newVersion = true;
                        // update toolbar
                        Tool_Update.Text = Resources.Strings.NewVersionStatus;
                        Tool_Update.LinkColor = Color.DodgerBlue;

                    }
                }

                // reset latest version
                latestVersion = null;
                // No longer dispose or null versionWebClient (removed)
            }

            // refresh forms
#if !SERVER
            main.aircraftForm.CheckRefresher();
            main.objectsForm.CheckRefresher();
            main.atcForm.CheckRefresher();
            main.matchingForm.CheckRefresher();
#endif
            main.hubsForm ?. CheckRefresher();
            main.addressBookForm.CheckRefresher();
            main.sessionForm.CheckUsersRefresher();
            main.monitorForm.CheckRefresher();

            // check for scheduled message
            if (main.scheduleShowMessage != null)
            {
                // show message
                MessageBox.Show(main.scheduleShowMessage, Main.Name);
                // reset 
                main.scheduleShowMessage = null;
            }

            // check for scheduled ask simconnect
            if (main.scheduleAskSimConnect)
            {
                // reset flag
                main.scheduleAskSimConnect = false;

                // check if not yet asked about SimConnect
                if (Settings.Default.AskSimConnect == false)
                {
                    // has now been asked
                    Settings.Default.AskSimConnect = true;

                    // ask
                    if (MessageBox.Show(Resources.Strings.AskSimConnect, Main.Name, MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        // download SimConnect
                        //string sc = Program.Code("https://joinfs.net/SimConnect.msi", true, 1234);
                        // TODO: see if we can replace it with something useful
                        //Main.LaunchEncoded(@")]ORHyb$9EKiU^m?s-@NY]e)""HH`[T*{%");
                    }
                }
            }

#if XPLANE
            // check for scheduled install plugin
            if (main.scheduleAskPlugin)
            {
                // reset flag
                main.scheduleAskPlugin = false;
                // install plugin
                main.sim ?. xplane.InstallPluginUI();
            }
#endif

#if !SERVER
            // check for scheduled nickname
            if (main.scheduleNickname)
            {
                // reset flag
                main.scheduleNickname = false;
                // request nickname
                NicknameForm nicknameForm = new(main);
                // obtain nickname
                if (nicknameForm.ShowDialog() == DialogResult.OK)
                {
                    // get nickname
                    main.settingsNickname = nicknameForm.nickname.TrimStart(' ').TrimEnd(' ');
                    // check for minimum length
                    if (main.settingsNickname.Length < 2)
                    {
                        // create hash nickname
                        main.settingsNickname = LocalNode.GenerateName(main.storagePath);
                    }
                    // get nickname
                    Settings.Default.Nickname = main.settingsNickname;
                }
            }

            // check for scheduled flight plan
            if (main.sim != null && main.scheduleFlightPlan)
            {
                // reset flag
                main.scheduleFlightPlan = false;
                // file flight plan
                if (new FlightPlanForm(main, main.sim.userFlightPlan).ShowDialog() == DialogResult.OK)
                {
                    // check for user aircraft
                    if (main.sim.userAircraft != null)
                    {
                        // update version
                        main.sim.userAircraft.flightPlanVersion++;
                        if (main.sim.userAircraft.flightPlanVersion == 0) main.sim.userAircraft.flightPlanVersion = 1;
                    }
                }
            }

            // check for scheduled scan
            if (main.scheduleScanForModels)
            {
                // reset flag
                main.scheduleScanForModels = false;

                // show message
                DialogResult result = MessageBox.Show(Resources.Strings.ModelListEmpty, Main.Name + ": Models", MessageBoxButtons.YesNo);
                if (result == DialogResult.Yes)
                {
                    // scan with UI
                    main.substitution ?. ScanUI();
                }
            }
#endif

            // refresh recorder window
            if (main.recorderForm != null && main.recorderForm.Visible)
            {
                main.recorderForm.RefreshWindow();
            }

            // check if main form is visible
            if (Visible)
            {
#if NO_HUBS
                // get myip
                string myip = Network.EncodeIP(Settings.Default.MyIp);

                // get port
                if (main.settingsPortEnabled)
                {
                    // add port
                    myip += " " + main.settingsPort.ToString(CultureInfo.InvariantCulture);
                }

                // check if myip has changed
                if (Text_MyIP.Text.Equals(myip) == false)
                {
                    // change myip
                    Text_MyIP.Text = myip;
                }
#endif

                // check if join box needs refresh
                if (refreshJoinCombo)
                {
                    // reset flag
                    refreshJoinCombo = false;
                    string joinText = "";

                    lock (main.conch)
                    {
                        // check if connected
                        if (main.network.localNode.CurrentState != LocalNode.State.Unconnected)
                        {
                            // check if connected to global session
                            if (main.network.localNode.GlobalSession)
                            {
                                // global session
                                joinText = Resources.Strings.Global;
                            }
                            else
                            {
                                AddressBook.AddressBookEntry entry = main.addressBook.entries.Find(f => f.endPoint.Equals(main.network.joinEndPoint));
                                if (entry != null)
                                {
                                    joinText = entry.name;
                                }
                                else
                                {
                                    // find entry
                                    Network.Hub hub = main.network.hubList.Find(h => h.endPoint.Equals(main.network.joinEndPoint));
                                    if (hub != null)
                                    {
                                        joinText = hub.name;
                                    }
                                }
                            }
                        }
                    }

                    // check for named join address
                    if (joinText.Length > 0)
                    {
                        // update
                        Combo_Join.Text = joinText;
                    }

                    // save address
                    Settings.Default.JoinAddress = Combo_Join.Text;
                }

                // get number of users
                string users = "";

                lock (main.conch)
                {
                    // user count
                    int publicCount = 0;
                    int maxGlobal = 0;

                    // check if this is a hub
                    if (main.settingsHub)
                    {
                        // check for global session
                        if (main.network.localNode.GlobalSession)
                        {
                            // update maximum
                            maxGlobal = main.network.localNode.NodeCount + 1;
                        }
                        else
                        {
                            // add to total
                            publicCount += main.network.localNode.NodeCount + 1;
                        }
                    }

                    // for each hub
                    foreach (var hub in main.network.hubList)
                    {
                        // check for global hub
                        if (hub.globalSession)
                        {
                            // check for new maximum
                            if (hub.users > maxGlobal)
                            {
                                // update maximum
                                maxGlobal = hub.users;
                            }
                        }
                        else
                        {
                            // add count
                            publicCount += hub.users;
                        }
                    }

                    // add global users
                    publicCount += maxGlobal;

                    int sessionCount = main.network.nodeList.Count + (main.network.localNode.Connected ? 1 : 0);

                    // get user count as string
#if NO_HUBS
                    users = sessionCount.ToString(CultureInfo.InvariantCulture);
#else
                    users = publicCount.ToString(CultureInfo.InvariantCulture) + "," + sessionCount.ToString(CultureInfo.InvariantCulture);
#endif
                }

                // check if users has changed
                if (Tool_Users.Text.Equals(users) == false)
                {
                    // set users
                    Tool_Users.Text = users;
                }

                RefreshSim();
                RefreshNetwork();
            }

            // refresh is no longer active
            refreshActive = false;
        }

        /// <summary>
        /// Refresh comms window
        /// </summary>
        public bool refreshComms = false;

        private void RefreshComms(object sender, System.EventArgs e)
        {
            // check if refresh is already active
            if (refreshActive)
            {
                return;
            }

            // refresh is now active
            refreshActive = true;

            // refresh comms window
            if (main.sessionForm != null && main.sessionForm.Visible && refreshComms)
            {
                // refresh comms
                main.sessionForm.RefreshComms();
                // reset
                refreshComms = false;
            }

            // refresh is no longer active
            refreshActive = false;
        }

        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MINIMIZE = 0xF020;

        protected override void DefWndProc(ref Message m)
        {

            base.DefWndProc(ref m);
        }

        /// <summary>
        /// Check for a URL
        /// </summary>
        public static bool CheckUrl(string text)
        {
            // split string
            string[] parts = text.Split(' ', '\t');
            // for each part
            foreach (var part in parts)
            {
                // check for URL
                if (Uri.IsWellFormedUriString(part, UriKind.Absolute))
                {
                    // URL
                    return true;
                }
            }
            // not a URL
            return false;
        }

        /// <summary>
        /// Check for and open a URL
        /// </summary>
        public static void OpenUrl(string text)
        {
            // split string
            string[] parts = text.Split(' ', '\t');
            // for each part
            foreach (var part in parts)
            {
                // check for URL
                if (Uri.IsWellFormedUriString(part, UriKind.Absolute))
                {
                    // open link
                    Main.Launch(part);
                    // finished
                    break;
                }
            }
        }

        /// <summary>
        /// Save current recording
        /// </summary>
        public void SaveRecording()
        {
            // get filename
            Stream stream;
            SaveFileDialog dialog = new()
            {
                InitialDirectory = Settings.Default.RecordingFolder,
                Filter = "JoinFS files (*.jfs)|*.jfs",
                FilterIndex = 1,
                RestoreDirectory = true
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                if ((stream = dialog.OpenFile()) != null)
                {
                    lock (main.conch)
                    {
                        main.recorder.Write(new BinaryWriter(stream));
                        stream.Close();
                    }
                    // save folder
                    Settings.Default.RecordingFolder = Path.GetDirectoryName(dialog.FileName);
                    // now saved
                    unsaved = false;
                }
            }
        }

        /// <summary>
        /// Check if there is an unsaved recording
        /// </summary>
        public void CheckRecording()
        {
            // check if recording is unsaved
            if (unsaved)
            {
                // ask to save recording
                DialogResult result = MessageBox.Show(Resources.Strings.SaveCurrentRecording, Main.Name + ": " + Resources.Strings.UnsavedRecording, MessageBoxButtons.YesNo);
                if (result == DialogResult.Yes)
                {
                    lock (main.conch)
                    {
                        SaveRecording();
                    }
                }
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // get saved position
            Point location = Settings.Default.MainFormLocation;

            // check for first time
            if (location.X == -1 && location.Y == -1)
            {
                // save current position
                Settings.Default.MainFormLocation = Location;
            }
            else
            {
                // window area
                Rectangle rectangle = new(location, Size);
                // is window hidden
                bool hidden = true;
                // for each screen
                foreach (Screen screen in Screen.AllScreens)
                {
                    // if screen does contain window
                    if (screen.WorkingArea.Contains(rectangle))
                    {
                        // not hidden
                        hidden = false;
                    }
                }

                // check if window is hidden
                if (hidden)
                {
                    // reload at default position
                    StartPosition = FormStartPosition.WindowsDefaultBounds;
                }
                else
                {
                    // restore position
                    StartPosition = FormStartPosition.Manual;
                    Location = location;
                }
            }

            // check for tool tips
            if (Settings.Default.ToolTips)
            {
                ToolTip tip = new()
                {
                    ShowAlways = true,
                    IsBalloon = true,
                    AutomaticDelay = 2000
                };
                tip.SetToolTip(Button_Create, Resources.Strings.Tip_ButtonCreate);
                tip.SetToolTip(Text_MyIP, Resources.Strings.Tip_Me);
                tip.SetToolTip(Combo_Join, Resources.Strings.Tip_Join);
                tip.SetToolTip(Button_Join, Resources.Strings.Tip_JoinButton);
                tip.SetToolTip(Button_Global, Resources.Strings.Tip_GlobalButton);
                tip.SetToolTip(Button_Network, Resources.Strings.Tip_NetworkButton);
                tip.SetToolTip(Button_Simulator, Resources.Strings.Tip_SimulatorButton);
                tip.SetToolTip(StatusStrip_Main, Resources.Strings.Tip_Status);
            }

            // refresh address book
            RefreshComboList();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // leave network
            CheckRecording();

            base.OnFormClosed(e);
        }

        /// <summary>
        /// Update window information
        /// </summary>
        public void RefreshNetwork()
        {
            // check if main form is visible
            if (Visible)
            {
#if !SERVER
                // check if password failed
                if (main.network.localNode.CurrentState == LocalNode.State.Connecting)
                {
                    // check for password fail
                    if (main.network.localNode.ActiveJoinResult == LocalNode.JoinResult.PasswordRequired)
                    {
                        // leave existing network
                        main.network.ScheduleLeave();

                        // get used password
                        uint passwordHash = 0;

                        // check if not yet used password
                        if (main.attempedUsedPassword == false)
                        {
                            lock (main.conch)
                            {
                                // get used password
                                passwordHash = main.log.GetUsedPassword(main.network.joinEndPoint);
                                // check for used password
                                if (passwordHash != 0)
                                {
                                    // join session
                                    main.network.ScheduleJoin(main.network.joinEndPoint, passwordHash);
                                    // attempted used password
                                    main.attempedUsedPassword = true;
                                }
                            }
                        }

                        // check if no used password
                        if (passwordHash == 0)
                        {
                            // request password
                            PasswordForm passwordForm = new(main);
                            // obtain password
                            if (passwordForm.ShowDialog() == DialogResult.OK)
                            {
                                // get hashed password
                                passwordHash = LocalNode.HashPassword(passwordForm.password.TrimStart(' ').TrimEnd(' '));

                                lock (main.conch)
                                {
                                    // join session
                                    main.network.ScheduleJoin(main.network.joinEndPoint, passwordHash);
                                    // check for valid password
                                    if (passwordHash != 0)
                                    {
                                        // store used password
                                        main.log.AddPassword(main.network.joinEndPoint, passwordHash);
                                    }
                                }
                            }
                        }
                    }
                    // check for login required
                    else if (main.network.localNode.ActiveJoinResult == LocalNode.JoinResult.LoginRequired)
                    {
                        // leave existing network
                        main.network.ScheduleLeave();

                        // read credentials
                        //string sc = Program.Code("credentials", true, 1234);
                        string filename = Path.Combine(main.documentsPath, Program.Code("e1PQ\"%,M1E;", false, 1234));
                        BinaryReader reader = null;
                        string email = "";
                        string password = "";

                        try
                        {
                            // check for file
                            if (File.Exists(filename))
                            {
                                // open file
                                reader = new BinaryReader(File.Open(filename, FileMode.Open));
                                // read length
                                int length = reader.ReadByte();
                                // convert to bytes
                                byte[] emailBytes = reader.ReadBytes(length);
                                // unscramble email
                                for (int index = 0; index < emailBytes.Length; index++)
                                {
                                    emailBytes[index] ^= 0x55;
                                }
                                // convert
                                email = System.Text.Encoding.ASCII.GetString(emailBytes);
                                // read length
                                length = reader.ReadByte();
                                // convert to bytes
                                byte[] passwordBytes = reader.ReadBytes(length);
                                // unscramble email
                                for (int index = 0; index < passwordBytes.Length; index++)
                                {
                                    passwordBytes[index] ^= 0x55;
                                }
                                // convert
                                password = System.Text.Encoding.ASCII.GetString(passwordBytes);
                            }
                        }
                        catch { }
                        finally
                        {
                            // close writer
                            reader?.Close();
                        }

                        // request login
                        LoginForm loginForm = new(main, email, password, false);
                        // obtain password
                        if (loginForm.ShowDialog() == DialogResult.OK)
                        {
                            // save credentials
                            BinaryWriter writer = null;

                            try
                            {
                                writer = new BinaryWriter(File.Create(filename));
                                // convert to bytes
                                byte[] emailBytes = System.Text.Encoding.ASCII.GetBytes(loginForm.email);
                                // scramble email
                                for (int index = 0; index < emailBytes.Length; index++)
                                {
                                    emailBytes[index] ^= 0x55;
                                }
                                // write email
                                writer.Write((byte)emailBytes.Length);
                                writer.Write(emailBytes);
                                // convert to bytes
                                byte[] passwordBytes = System.Text.Encoding.ASCII.GetBytes(loginForm.password);
                                // scramble email
                                for (int index = 0; index < passwordBytes.Length; index++)
                                {
                                    passwordBytes[index] ^= 0x55;
                                }
                                // write password
                                writer.Write((byte)passwordBytes.Length);
                                writer.Write(passwordBytes);
                            }
                            catch { }
                            finally
                            {
                                // close writer
                                writer?.Close();
                            }

                            lock (main.conch)
                            {
                                // join session
                                main.network.ScheduleLogin(main.network.joinEndPoint, loginForm.email, LocalNode.HashString(loginForm.password), false);
                            }
                        }
                    }
                    // check for verify required
                    else if (main.network.localNode.ActiveLoginResult == LocalNode.LoginResult.VerifyPassword)
                    {
                        // leave existing network
                        main.network.ScheduleLeave();

                        // request login
                        LoginForm loginForm = new(main, main.network.ScheduleLoginEmail, "", true);
                        // obtain password
                        if (loginForm.ShowDialog() == DialogResult.OK)
                        {
                            // get password hash
                            uint hash = LocalNode.HashString(loginForm.password);
                            if (hash == 0) hash = 1;
                            // verify password
                            if (hash == main.network.ScheduleLoginHash)
                            {
                                lock (main.conch)
                                {
                                    // join session
                                    main.network.ScheduleLogin(main.network.joinEndPoint, loginForm.email, hash, true);
                                }
                            }
                            else
                            {
                                // message
                                main.ShowMessage(Resources.Strings.InvalidEmail);
                            }
                        }
                    }
                    // check for invalid email
                    else if (main.network.localNode.ActiveLoginResult == LocalNode.LoginResult.InvalidAddress)
                    {
                        // leave existing network
                        main.network.ScheduleLeave();
                        // message
                        main.ShowMessage(Resources.Strings.InvalidEmail);
                    }
                    // check for invalid password
                    else if (main.network.localNode.ActiveLoginResult == LocalNode.LoginResult.InvalidPassword)
                    {
                        // leave existing network
                        main.network.ScheduleLeave();
                        // message
                        main.ShowMessage(Resources.Strings.InvalidEmail);
                    }
                }
#endif

                bool createEnabled = true;
                bool joinEnabled = true;
                bool comboEnabled = true;
                bool globalEnabled = true;
                string createText = Resources.Strings.Create;

                lock (main.conch)
                {
                    // check for hub mode
                    if (main.settingsHub)
                    {
                        // update button
                        createEnabled = false;
                        // update button text
                        createText = "Hub";
                    }
                    else if (main.network.localNode.CurrentState != LocalNode.State.Unconnected)
                    {
                        // update create button
                        createEnabled = false;
                        // update join button
                        joinEnabled = false;
                        // update join box
                        comboEnabled = false;
                        // update global button
                        globalEnabled = false;
                    }
                }

                // update create button
                if (Button_Create.Enabled != createEnabled)
                {
                    Button_Create.Enabled = createEnabled;
                }
                // update join button
                if (Button_Join.Enabled != joinEnabled)
                {
                    Button_Join.Enabled = joinEnabled;
                }
                // update combo
                if (Combo_Join.Enabled != comboEnabled)
                {
                    Combo_Join.Enabled = comboEnabled;
                }
                // update global
                if (Button_Global.Enabled != globalEnabled)
                {
                    Button_Global.Enabled = globalEnabled;
                }
                // update create text
                if (Button_Create.Text.Equals(createText) == false)
                {
                    Button_Create.Text = createText;
                }

                Color backColor = Settings.Default.ColourWaitingBackground;
                Color foreColor = Settings.Default.ColourWaitingText;
                string buttonText = Resources.Strings.Network;

                lock (main.conch)
                {
                    // check connection state
                    switch (main.network.localNode.CurrentState)
                    {
                        case LocalNode.State.Connected:
                            // update label
                            backColor = Settings.Default.ColourActiveBackground;
                            foreColor = Settings.Default.ColourActiveText;
                            // check for password
                            if (main.network.localNode.Password)
                            {
                                buttonText = Resources.Strings.Password;
                            }
                            break;

                        case LocalNode.State.Unconnected:
                            // check not auto joining
                            if (main.network.scheduleJoinUser == false)
                            {
                                // update label
                                backColor = Settings.Default.ColourInactiveBackground;
                                foreColor = Settings.Default.ColourInactiveText;
                            }
                            break;
                    }
                }

                // update back color
                if (Button_Network.BackColor != backColor)
                {
                    // update
                    Button_Network.BackColor = backColor;
                    // force refresh
                    refreshForce = true;
                }
                // update fore color
                if (Button_Network.ForeColor != foreColor)
                {
                    // update
                    Button_Network.ForeColor = foreColor;
                    // force refresh
                    refreshForce = true;
                }
                // update text
                if (Button_Network.Text != buttonText)
                {
                    // update
                    Button_Network.Text = buttonText;
                }
            }
        }


        /// <summary>
        /// Update window information
        /// </summary>
        public void RefreshSim()
        {
            if (main.sim != null && Visible)
            {
                Color backColor = Settings.Default.ColourInactiveBackground;
                Color foreColor = Settings.Default.ColourInactiveText;

                lock (main.conch)
                {
                    // check if FS connected
                    if (main.sim.Connected)
                    {
                        // update label
                        backColor = Settings.Default.ColourActiveBackground;
                        foreColor = Settings.Default.ColourActiveText;
                    }
                    else if (main.sim.Connecting)
                    {
                        // update label
                        backColor = Settings.Default.ColourWaitingBackground;
                        foreColor = Settings.Default.ColourWaitingText;
                    }
                }

                // check if back color changed
                if (Button_Simulator.BackColor != backColor)
                {
                    // update label
                    Button_Simulator.BackColor = backColor;
                }
                // check if fore color changed
                if (Button_Simulator.ForeColor != foreColor)
                {
                    // update label
                    Button_Simulator.ForeColor = foreColor;
                }
            }
        }

        public void RefreshComboList()
        {
            // names list
            List<string> names = [];
            lock (main.conch)
            {
                // for each entry
                foreach (var entry in main.addressBook.entries)
                {
                    // add address to list
                    names.Add(entry.name);
                }
            }
            // sort names
            names.Sort();
            // clear current join list
            Combo_Join.Items.Clear();
            // add names to join list
            Combo_Join.Items.AddRange(names.ToArray());
        }

        private void Button_Create_Click(object sender, EventArgs e)
        {
            // leave network
            main.network.ScheduleLeave();
            // create network
            main.network.ScheduleCreate();
            // update window
            RefreshNetwork();
        }

        private void Button_Join_Click(object sender, EventArgs e)
        {
            // join
#if NO_HUBS
            main.Join(Network.DecodeIP(Combo_Join.Text.TrimStart(' ').TrimEnd(' ')));
#else
            main.Join(Combo_Join.Text.TrimStart(' ').TrimEnd(' '));
#endif
        }

        private void Button_Simulator_Click(object sender, EventArgs e)
        {
            main.ToggleSimulator();
            RefreshSim();
        }

        public void ToggleNetwork()
        {
            // get connected state
            bool connected = main.network.localNode.CurrentState != LocalNode.State.Unconnected;

            // check if user join scheduled
            if (main.network.scheduleJoinUser)
            {
                // stop join
                main.network.scheduleJoinUser = false;
            }
            else if (connected)
            {
                // leave network
                main.network.ScheduleLeave();
            }
            else if (Combo_Join.Text == null || Combo_Join.Text.Length == 0)
            {
                // leave network
                main.network.ScheduleLeave();
                // create network
                main.network.ScheduleCreate();
            }
            else
            {
                // join
#if NO_HUBS
                main.Join(Network.DecodeIP(Combo_Join.Text.TrimStart(' ').TrimEnd(' ')));
#else
                main.Join(Combo_Join.Text.TrimStart(' ').TrimEnd(' '));
#endif
            }

            // update window
            RefreshNetwork();
        }

        private void Button_Network_Click(object sender, EventArgs e)
        {
            ToggleNetwork();
        }

        private void Text_MyIP_Click(object sender, EventArgs e)
        {
            try
            {
                Text_MyIP.SelectAll();
                // check for valid text
                if (Text_MyIP.Text != null && Text_MyIP.Text.Length > 0)
                {
                    // copy to windows clipboard
                    Clipboard.SetText(Text_MyIP.Text);
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent("Failed to copy my address to the clipboard." + ex.Message);
            }
        }

#region Menu

        private void Menu_File_ScanModels_Click(object sender, EventArgs e)
        {
            // scan
            main.substitution ?. ScanUI();
        }

        private void Menu_File_Settings_Click(object sender, EventArgs e)
        {
            // open dialog to choose jfs file
            (new SettingsForm(main)).ShowDialog();
            lock (main.conch)
            {
                // low bandwidth
                main.network.localNode.lowBandwidth = Settings.Default.LowBandwidth;
            }
        }

        private void Menu_Exit_Click(object sender, EventArgs e)
        {
            main.shutdown = "";
        }

        private void Menu_View_Hubs_Click(object sender, EventArgs e)
        {
            if (main.hubsForm != null)
            {
                main.hubsForm.refresher.Schedule();
                main.hubsForm.WindowState = FormWindowState.Minimized;
                main.hubsForm.Show();
                main.hubsForm.WindowState = FormWindowState.Normal;
            }
        }

        private void Menu_View_Aircraft_Click(object sender, EventArgs e)
        {
#if !SERVER
            if (main.aircraftForm != null)
            {
                main.aircraftForm.refresher.Schedule();

                main.aircraftForm.WindowState = FormWindowState.Minimized;
                main.aircraftForm.Show();
                main.aircraftForm.WindowState = FormWindowState.Normal;
            }
#endif
        }

        private void Menu_View_Atc_Click(object sender, EventArgs e)
        {
#if !SERVER
            if (main.atcForm != null)
            {
                main.atcForm.refresher.Schedule();
                main.atcForm.WindowState = FormWindowState.Minimized;
                main.atcForm.Show();
                main.atcForm.WindowState = FormWindowState.Normal;
            }
#endif
        }

        private void Menu_View_Objects_Click(object sender, EventArgs e)
        {
#if !SERVER
            if (main.objectsForm != null)
            {
                main.objectsForm.refresher.Schedule();
                main.objectsForm.WindowState = FormWindowState.Minimized;
                main.objectsForm.Show();
                main.objectsForm.WindowState = FormWindowState.Normal;
            }
#endif
        }

        private void Menu_View_Users_Click(object sender, EventArgs e)
        {
            if (main.sessionForm != null)
            {
                main.sessionForm.RefreshWindow();

                main.sessionForm.WindowState = FormWindowState.Minimized;
                main.sessionForm.Show();
                main.sessionForm.WindowState = FormWindowState.Normal;
            }
        }

        private void Menu_View_Monitor_Click(object sender, EventArgs e)
        {
            if (main.monitorForm != null)
            {
                main.monitorForm.WindowState = FormWindowState.Minimized;
                main.monitorForm.Show();
                main.monitorForm.WindowState = FormWindowState.Normal;
            }
        }

        private void Menu_Help_Manual_Click(object sender, EventArgs e)
        {
            //string sc = Program.Code("https://joinfs.net/manual.html", true, 1234);
            // TODO: see if we can replace it with something useful
            Main.Launch("https://github.com/tuduce/JoinFS/wiki");
        }

        private void Menu_Help_Licence_Click(object sender, EventArgs e)
        {
            //string sc = Program.Code(@".\Licence.rtf", true, 1234);
            Main.Launch("https://raw.githubusercontent.com/tuduce/JoinFS/refs/heads/main/Licence.txt");
        }

        private void Menu_Help_About_Click(object sender, EventArgs e)
        {
            // show about form
            (new AboutForm(main)).ShowDialog();
        }

#endregion

#region Toolbar

        private void Tool_Update_Click(object sender, EventArgs e)
        {
            // check for new version
            if (newVersion)
            {
                // confirm
                DialogResult result = MessageBox.Show(Resources.Strings.DownloadNew, Main.Name + ": " + Resources.Strings.NewVersionStatus, MessageBoxButtons.YesNo);
                if (result == DialogResult.Yes)
                {
                    // check for early update
                    if (Settings.Default.EarlyUpdate)
                    {
                        //string sc = Program.Code("https://joinfs.net/development.html", true, 1234);
                        //string sc = Program.Code("https://github.com/tuduce/JoinFS/releases", true, 1234);
                        // send the users to the releases page of the github repo
                        Main.Launch("https://github.com/tuduce/JoinFS/releases");
                    }
                    else
                    {
                        //string sc = Program.Code("https://joinfs.net/install.html", true, 1234);
                        //string sc = Program.Code("https://github.com/tuduce/JoinFS/releases", true, 1234);
                        // send the users to the releases page of the github repo
                        Main.Launch("https://github.com/tuduce/JoinFS/releases");
                    }
                    // shutdown
                    main.shutdown = "";
                }
            }
            else
            {
                //string sc = Program.Code("https://joinfs.net/install.html", true, 1234);
                //string sc = Program.Code("https://github.com/tuduce/JoinFS/releases", true, 1234);
                // send the users to the releases page of the github repo
                Main.Launch("https://github.com/tuduce/JoinFS/releases");
            }
        }

        private void Tool_Map_Click(object sender, EventArgs e)
        {
            //string sc = Program.Code(@"https://joinfsmap.dotdash.space", true, 1234);
            Main.Launch(@"https://joinfsmap.dotdash.space");
        }

        #endregion

        private void MainForm_Activated(object sender, EventArgs e)
        {
            // check always on top
            if (Settings.Default.AlwaysOnTop)
            {
                TopMost = true;
            }
            else
            {
                TopMost = false;
            }
        }

        private void MainForm_Deactivate(object sender, EventArgs e)
        {
            // check always on top
            if (Settings.Default.AlwaysOnTop)
            {
                TopMost = true;
                Activate();
            }
            else
            {
                TopMost = false;
            }
        }

        private void Button_Global_Click(object sender, EventArgs e)
        {
#if !NO_GLOBAL
            // leave existing network
            main.network.ScheduleLeave();
            // schedule join
            main.network.ScheduleJoinGlobal();
            // update window
            RefreshNetwork();
            refreshJoinCombo = true;
#endif
        }

        private void Menu_Help_Radar_Click(object sender, EventArgs e)
        {
            //string sc = Program.Code("https://joinfs.net/forum/viewtopic.php?f=4&t=58", true, 1234);
            // TODO: see if we can replace it with something useful
            // Main.LaunchEncoded(@"!oR:5E5Tn(25)'b?Myq6Usewn7b^)fJHb2bi06]&l|f,.~W");
        }

        private void MainForm_VisibleChanged(object sender, EventArgs e)
        {
            if (Visible)
            {
                RefreshMain(sender, e);
            }
        }

        private void Menu_Help_Support_Click(object sender, EventArgs e)
        {
            //string sc = Program.Code("https://joinfs.net/forum", true, 1234);
            // TODO: see if we can replace it with something useful
            // Main.LaunchEncoded(@"iN{.mqs]O5*|#P(SZ!8R!#_#");
        }

        private void Menu_File_ModelMatching_Click(object sender, EventArgs e)
        {
            // check if no simulator connected
#if !XPLANE
            if (main.sim != null && main.sim.Connected == false)
            {
                MessageBox.Show(Resources.Strings.EditMatchingWarning, Main.Name + ": " + Resources.Strings.EditModelMatching);
            }
#endif

#if !SERVER
            if (main.matchingForm != null)
            {
                main.matchingForm.WindowState = FormWindowState.Minimized;
                main.matchingForm.Show();
                main.matchingForm.WindowState = FormWindowState.Normal;
            }
#endif
        }

        private void Menu_File_Shortcuts_Click(object sender, EventArgs e)
        {
            main.shortcutsForm.WindowState = FormWindowState.Minimized;
            main.shortcutsForm.Show();
            main.shortcutsForm.WindowState = FormWindowState.Normal;
        }

        private void Menu_File_Options_Click(object sender, EventArgs e)
        {
            main.optionsForm.WindowState = FormWindowState.Minimized;
            main.optionsForm.Show();
            main.optionsForm.WindowState = FormWindowState.Normal;
        }

        private void Menu_File_Recorder_Click(object sender, EventArgs e)
        {
            if (main.recorderForm != null)
            {
                main.recorderForm.RefreshWindow();

                main.recorderForm.WindowState = FormWindowState.Minimized;
                main.recorderForm.Show();
                main.recorderForm.WindowState = FormWindowState.Normal;
            }
        }

        private void Menu_File_AddressBook_Click(object sender, EventArgs e)
        {
            if (main.addressBookForm != null)
            {
                main.addressBookForm.refresher.Schedule();
                main.addressBookForm.WindowState = FormWindowState.Minimized;
                main.addressBookForm.Show();
                main.addressBookForm.WindowState = FormWindowState.Normal;
            }
        }

        private void Menu_Help_Shop_Click(object sender, EventArgs e)
        {
        }

        private void MainForm_ResizeEnd(object sender, EventArgs e)
        {
            // save form position
            Settings.Default.MainFormLocation = Location;
        }

        private void Menu_File_SimComX_Click(object sender, EventArgs e)
        {
            try
            {
                // string sc = Program.Code(@"c:\program files (x86)\simcomx\simcom x.exe", true, 1234);
                if (File.Exists(@"c:\program files (x86)\simcomx\simcom x.exe"))
                {
                    Main.LaunchEncoded(@"c:\program files (x86)\simcomx\simcom x.exe");
                }
                else
                {
                    //string sc = Program.Code(@"https://dotdash.space/scx/", true, 1234);
                    Main.Launch("https://dotdash.space/scx/");
                }
            }
            catch { }
        }

        private void Menu_File_Variables_Click(object sender, EventArgs e)
        {
#if !SERVER
            // check if no simulator connected
            if (main.sim != null && main.sim.Connected == false)
            {
                MessageBox.Show(Resources.Strings.AssignVariablesWarning, Main.Name);
            }
            else
            {
                // show dialog for assigning variables
                new VariablesForm(main, main.sim ?. userAircraft ?. ownerModel).ShowDialog();
            }
#endif
        }
    }
}
