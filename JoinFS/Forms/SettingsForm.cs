using System;
using System.Windows.Forms;
using System.Globalization;
using JoinFS.Properties;

namespace JoinFS
{
    public partial class SettingsForm : Form
    {
        readonly Main main;

        void RefreshWindow()
        {
            // check local port
            if (Check_LocalPort.CheckState == CheckState.Checked)
            {
                Text_LocalPort.Enabled = true;
            }
            else
            {
                Text_LocalPort.Enabled = false;
            }

            // check whazzup
            if (Check_Whazzup.CheckState == CheckState.Checked)
            {
                Check_WhazzupGlobal.Enabled = true;
                Check_WhazzupAI.Enabled = true;
            }
            else
            {
                Check_WhazzupGlobal.Enabled = false;
                Check_WhazzupAI.Enabled = false;
            }

            // check if atc mode is enabled
            if (Check_ATC.CheckState == CheckState.Checked)
            {
                Text_Airport.Enabled = true;
                Label_Airport.Enabled = true;
                Check_Euroscope.Enabled = true;
                Label_Level.Enabled = true;
                Combo_Level.Enabled = true;
                Label_Frequency.Enabled = true;
                Text_Frequency.Enabled = true;
            }
            else
            {
                Text_Airport.Enabled = false;
                Label_Airport.Enabled = false;
                Check_Euroscope.Enabled = false;
                Label_Level.Enabled = false;
                Combo_Level.Enabled = false;
                Label_Frequency.Enabled = false;
                Text_Frequency.Enabled = false;
            }

            // get code from input
            string code = Text_Airport.Text.ToUpper();
            // check for airport
            if (Check_ATC.CheckState == CheckState.Checked && main.airportList.ContainsKey(code))
            {
                // valid aiport
                Text_Airport.BackColor = System.Drawing.Color.Lime;
            }
            else
            {
                // invalid code
                Text_Airport.BackColor = System.Drawing.SystemColors.Window;
            }

            // check if hub mode is enabled
            if (Check_Hub.CheckState == CheckState.Checked)
            {
                Label_HubDomain.Enabled = true;
                Text_HubDomain.Enabled = true;
                Label_HubName.Enabled = true;
                Text_HubName.Enabled = true;
                Label_HubAbout.Enabled = true;
                Text_HubAbout.Enabled = true;
                Label_HubVoIP.Enabled = true;
                Text_HubVoIP.Enabled = true;
                Label_HubEvent.Enabled = true;
                Text_HubEvent.Enabled = true;
            }
            else
            {
                Label_HubDomain.Enabled = false;
                Text_HubDomain.Enabled = false;
                Label_HubName.Enabled = false;
                Text_HubName.Enabled = false;
                Label_HubAbout.Enabled = false;
                Text_HubAbout.Enabled = false;
                Label_HubVoIP.Enabled = false;
                Text_HubVoIP.Enabled = false;
                Label_HubEvent.Enabled = false;
                Text_HubEvent.Enabled = false;
            }
        }

        public SettingsForm(Main main)
        {
            InitializeComponent();

#if NO_HUBS || NO_CREATE
            GroupBox_Hub.Visible = false;
            Check_Hub.Visible = false;
            Label_HubDomain.Visible = false;
            Text_HubDomain.Visible = false;
            Label_HubName.Visible = false;
            Text_HubName.Visible = false;
            Label_HubAbout.Visible = false;
            Text_HubAbout.Visible = false;
            Label_HubVoIP.Visible = false;
            Text_HubVoIP.Visible = false;
            Label_HubEvent.Visible = false;
            Text_HubEvent.Visible = false;
            Check_Hub.Checked = false;
#endif

#if NO_GLOBAL
            Check_WhazzupGlobal.Visible = false;
            Check_WhazzupGlobal.Checked = false;
            Check_GlobalJoin.Visible = false;
            Check_GlobalJoin.Checked = false;
#endif

#if !XPLANE
            GroupBox_Xplane.Visible = false;
            Check_ShowAltitude.Visible = false;
            Check_ShowCallsign.Visible = false;
            Check_ShowDistance.Visible = false;
            Check_ShowNickname.Visible = false;
            Check_ShowSpeed.Visible = false;
            Button_LabelColour.Visible = false;
            Label_LabelColour.Visible = false;
#endif

#if SERVER
            Check_LowBandwidth.Visible = false;
#endif

            if (main.settingsNoSim)
            {
                GroupBox_Simulator.Visible = false;
                GroupBox_ATC.Visible = false;
                Track_Follow.Visible = false;
                Track_Circle.Visible = false;
            }

            this.main = main;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // change font
            Text_LocalPort.Font = main.dataFont;
            Text_Password.Font = main.dataFont;
            Text_Nickname.Font = main.dataFont;
            Text_Airport.Font = main.dataFont;
            Combo_Level.Font = main.dataFont;
            Text_Frequency.Font = main.dataFont;
            Text_HubDomain.Font = main.dataFont;
            Text_HubName.Font = main.dataFont;
            Text_HubAbout.Font = main.dataFont;
            Text_HubEvent.Font = main.dataFont;
            Text_HubVoIP.Font = main.dataFont;
            Text_PluginAddress.Font = main.dataFont;
        }

        private void Button_OK_Click(object sender, EventArgs e)
        {
            // get always on top
            Settings.Default.AlwaysOnTop = Check_AlwaysOnTop.CheckState == CheckState.Checked;
            // get tool tips
            Settings.Default.ToolTips = Check_ToolTips.CheckState == CheckState.Checked;
            // update whazzup
            main.settingsWhazzup = Check_Whazzup.CheckState == CheckState.Checked;
            Settings.Default.Whazzup = main.settingsWhazzup;
            // update whazzup global
            main.settingsWhazzupPublic = Check_WhazzupGlobal.CheckState == CheckState.Checked;
            Settings.Default.WhazzupGlobal = main.settingsWhazzupPublic;
            // update whazzup AI
            Settings.Default.WhazzupAI = Check_WhazzupAI.CheckState == CheckState.Checked;
            // update auto refresh
            Settings.Default.AutoRefresh = Check_AutoRefresh.CheckState == CheckState.Checked;
            // update early
            Settings.Default.EarlyUpdate = Check_EarlyUpdate.CheckState == CheckState.Checked;

            // get nickname
            string nickname = Text_Nickname.Text.TrimStart(' ').TrimEnd(' ');
            // check for minimum length
            if (nickname.Length < 2)
            {
                // create random nickname, 2 letters
                nickname = LocalNode.GenerateName(main.storagePath);
            }
            main.settingsNickname = nickname;
            Settings.Default.Nickname = nickname;

            // update show nicknames enabled
            bool newShowNicknames = (Check_ShowNickname.CheckState == CheckState.Checked);
            // check if setting changed
            if (newShowNicknames != Settings.Default.ShowNicknames)
            {
                lock (main.conch)
                {
                    // remove all controlled aircraft
                    main.sim.RemoveInjectedObjects();
                }
            }
            // update nickname
            Settings.Default.ShowNicknames = newShowNicknames;
            // update callsign
            Settings.Default.ShowCallsign = Check_ShowCallsign.CheckState == CheckState.Checked;
            // update callsign
            Settings.Default.ShowDistance = Check_ShowDistance.CheckState == CheckState.Checked;
            // update callsign
            Settings.Default.ShowAltitude = Check_ShowAltitude.CheckState == CheckState.Checked;
            // update callsign
            Settings.Default.ShowSpeed = Check_ShowSpeed.CheckState == CheckState.Checked;

            // update activity circle
            main.settingsActivityCircle = Track_Circle.Value;
            Settings.Default.ActivityCircle = main.settingsActivityCircle;

            // update follow distance
            Settings.Default.FollowDistance = Track_Follow.Value;

            // update connect
            main.settingsConnectOnLaunch = Check_Connect.CheckState == CheckState.Checked;
            Settings.Default.ConnectOnLaunch = main.settingsConnectOnLaunch;
            // update model scan
            main.settingsScan = Check_Scan.CheckState == CheckState.Checked;
            Settings.Default.ModelScanOnConnection = main.settingsScan;
            // update use AI features
            main.settingsUseAIFeatures = Check_UseAIFeatures.CheckState == CheckState.Checked;
            Settings.Default.UseAIFeatures = main.settingsUseAIFeatures;
            // update elevation correction
            Settings.Default.ElevationCorrection = Check_Elevation.CheckState == CheckState.Checked;

            // get old port
            int oldPort = Settings.Default.LocalPortEnabled ? Settings.Default.LocalPort : Network.DEFAULT_PORT;

            // get local port
            int localPort = Network.DEFAULT_PORT;
            if (Int32.TryParse(Text_LocalPort.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out localPort))
            {
                // update local port number
                main.settingsPort = (ushort)localPort;
                Settings.Default.LocalPort = main.settingsPort;
            }

            // update local port enabled
            main.settingsPortEnabled = Check_LocalPort.CheckState == CheckState.Checked;
            Settings.Default.LocalPortEnabled = main.settingsPortEnabled;

            // update auto join enabled
            Settings.Default.Global = Check_GlobalJoin.CheckState == CheckState.Checked;
            // update low bandwidth enabled
            Settings.Default.LowBandwidth = Check_LowBandwidth.CheckState == CheckState.Checked;

            // update broadcast tacpack
            Settings.Default.BroadcastTacpack = Check_Tacpack.CheckState == CheckState.Checked;

            // update hub password
            main.settingsPassword = Text_Password.Text.TrimStart(' ').TrimEnd(' ');
            Settings.Default.Password = main.settingsPassword;

            // ATC mode
            bool atcMode = (Check_ATC.CheckState == CheckState.Checked);
            // check if mode has changed
            if (atcMode != Settings.Default.Atc)
            {
                lock (main.conch)
                {
                    // remove all controlled aircraft
                    main.sim.RemoveInjectedObjects();
                }
                // update settings
                main.settingsAtc = atcMode;
            }
            // update ATC mode
            Settings.Default.Atc = atcMode;
            // write airport
            string airport = Text_Airport.Text.TrimStart(' ').TrimEnd(' ');
            main.settingsAtcAirport = airport[..Math.Min(4, airport.Length)].ToUpper();
            Settings.Default.AtcAirport = main.settingsAtcAirport;
            // write level
            Settings.Default.AtcLevel = Combo_Level.SelectedIndex;
            // write frequency
            Settings.Default.AtcFrequency = Sim.FrequencyStringToInt(Text_Frequency.Text);
            // update Euroscope
            Settings.Default.Euroscope = Check_Euroscope.CheckState == CheckState.Checked;

            // get new and old hub modes
            bool newHubMode = (Check_Hub.CheckState == CheckState.Checked);
            bool oldHubMode = Settings.Default.Hub;

            // check for invalid hub name
            if (newHubMode && Text_HubName.Text.Length < 3)
            {
                // error
                MessageBox.Show("The hub name must be at least three characters long.", Main.Name + ": Invalid Hub Name");
                // don't close
                DialogResult = DialogResult.None;
            }
            else
            {
                // check if hub mode has been switched on
                if (oldHubMode == false && newHubMode)
                {
                    // leave network
                    main.network.ScheduleLeave();
                    // create network
                    main.network.ScheduleCreate();
                }
                else if (oldHubMode && newHubMode == false)
                {
                    // leave network
                    main.network.ScheduleLeave();
                }

                // update hub mode enabled
                main.settingsHub = newHubMode;
                Settings.Default.Hub = newHubMode;
                // update hub Address
                main.settingsHubDomain = Text_HubDomain.Text.TrimStart(' ').TrimEnd(' ');
                Settings.Default.HubAddress = main.settingsHubDomain;
                // update hub Name
                main.settingsHubName = Text_HubName.Text.TrimStart(' ').TrimEnd(' ');
                Settings.Default.HubName = main.settingsHubName;
                // update hub About
                main.settingsHubAbout = Text_HubAbout.Text.TrimStart(' ').TrimEnd(' ');
                Settings.Default.HubAbout = main.settingsHubAbout;
                // update hub VoIP
                main.settingsHubVoip = Text_HubVoIP.Text.TrimStart(' ').TrimEnd(' ');
                Settings.Default.HubVoIP = main.settingsHubVoip;
                // update hub Event
                main.settingsHubEvent = Text_HubEvent.Text.TrimStart(' ').TrimEnd(' ');
                Settings.Default.HubEvent = main.settingsHubEvent;

                // check if hub mode has changed
                if (newHubMode != oldHubMode)
                {
                    // update setting
                    main.settingsHub = newHubMode;
                    // refresh window
                    main.mainForm ?. RefreshNetwork();
                }
            }

            // update xplane plugin address
            Settings.Default.XPlanePluginAddress = Text_PluginAddress.Text.TrimStart(' ').TrimEnd(' ');
            // update xplane TCAS
            main.settingsTcas = Check_TCAS.CheckState == CheckState.Checked;
            Settings.Default.TCAS = main.settingsTcas;

            // update setting
            Settings.Default.ColourActiveBackground = Label_Active.BackColor;
            Settings.Default.ColourActiveText = Label_Active.ForeColor;
            Settings.Default.ColourWaitingBackground = Label_Waiting.BackColor;
            Settings.Default.ColourWaitingText = Label_Waiting.ForeColor;
            Settings.Default.ColourInactiveBackground = Label_Inactive.BackColor;
            Settings.Default.ColourInactiveText = Label_Inactive.ForeColor;

            // update label colour
            Settings.Default.ColourLabel = Label_LabelColour.BackColor;

            // get new port
            int newPort = Settings.Default.LocalPortEnabled ? Settings.Default.LocalPort : Network.DEFAULT_PORT;

            // check if port has changed
            if (newPort != oldPort)
            {
                lock (main.conch)
                {
                    // open the new port
                    if (main.network.localNode.Open(newPort))
                    {
                        // monitor
                        main.MonitorEvent("Closed UDP port " + oldPort);
                        main.MonitorEvent("Opened UDP port " + newPort);

                        // check for Hub mode enabled
                        if (main.settingsHub)
                        {
                            // create network
                            main.network.ScheduleCreate();
                        }
                    }
                }
            }

            Settings.Default.Save();

#if !SERVER
            main.aircraftForm.refresher.Schedule();
            main.objectsForm.refresher.Schedule();
            main.atcForm.refresher.Schedule();
#endif
            main.hubsForm ?. refresher.Schedule();
            main.sessionForm.usersRefresher.Schedule();
            main.monitorForm.refresher.Schedule();
        }

        private void Check_LocalPort_CheckedChanged(object sender, EventArgs e)
        {
            RefreshWindow();
        }

        private void Track_Follow_ValueChanged(object sender, EventArgs e)
        {
            // update text
            Label_Follow.Text = Track_Follow.Value.ToString() + " m";
        }

        private void Check_ATC_CheckedChanged(object sender, EventArgs e)
        {
            // check if enabled
            if (Check_ATC.CheckState == CheckState.Checked)
            {
                // check if warned
                if (Settings.Default.AtcWarning == false)
                {
                    // show warning
                    MessageBox.Show(Resources.Strings.AtcWarning, Main.Name + ": " + Resources.Strings.Warning);
                    // now asked
                    Settings.Default.AtcWarning = true;
                }
            }
            RefreshWindow();
        }

        private void Text_Airport_TextChanged(object sender, EventArgs e)
        {
            RefreshWindow();
        }

        private void Track_Circle_ValueChanged(object sender, EventArgs e)
        {
            // update text
            Label_Circle.Text = Track_Circle.Value + " nm";
        }

        private void Check_Hub_CheckedChanged(object sender, EventArgs e)
        {
            RefreshWindow();
        }

        private void Check_Password_CheckedChanged(object sender, EventArgs e)
        {
            RefreshWindow();
        }

        private void Check_Whazzup_CheckedChanged(object sender, EventArgs e)
        {
            RefreshWindow();
        }

        private void Check_AutoRefresh_CheckedChanged(object sender, EventArgs e)
        {
            if (Check_AutoRefresh.Checked && Settings.Default.AutoRefresh == false)
            {
                MessageBox.Show("With Auto Refresh enabled it may affect performance very slightly during the window refresh.", Main.Name + ": Warning");
            }
       }

        private void SettingsForm_Load(object sender, EventArgs e)
        {
            // get always on top
            Check_AlwaysOnTop.CheckState = Settings.Default.AlwaysOnTop ? CheckState.Checked : CheckState.Unchecked;
            // get tool tips
            Check_ToolTips.CheckState = Settings.Default.ToolTips ? CheckState.Checked : CheckState.Unchecked;
            // get Whazzup
            Check_Whazzup.CheckState = Settings.Default.Whazzup ? CheckState.Checked : CheckState.Unchecked;
            // get Whazzup global
            Check_WhazzupGlobal.CheckState = Settings.Default.WhazzupGlobal ? CheckState.Checked : CheckState.Unchecked;
            // get Whazzup AI
            Check_WhazzupAI.CheckState = Settings.Default.WhazzupAI ? CheckState.Checked : CheckState.Unchecked;
            // get auto refresh
            Check_AutoRefresh.CheckState = Settings.Default.AutoRefresh ? CheckState.Checked : CheckState.Unchecked;
            // get early update
            Check_EarlyUpdate.CheckState = Settings.Default.EarlyUpdate ? CheckState.Checked : CheckState.Unchecked;
            // get nickname
            Text_Nickname.Text = Settings.Default.Nickname;
            // get nickname
            Check_ShowNickname.CheckState = Settings.Default.ShowNicknames ? CheckState.Checked : CheckState.Unchecked;
            // get callsign
            Check_ShowCallsign.CheckState = Settings.Default.ShowCallsign ? CheckState.Checked : CheckState.Unchecked;
            // get callsign
            Check_ShowDistance.CheckState = Settings.Default.ShowDistance ? CheckState.Checked : CheckState.Unchecked;
            // get callsign
            Check_ShowAltitude.CheckState = Settings.Default.ShowAltitude ? CheckState.Checked : CheckState.Unchecked;
            // get callsign
            Check_ShowSpeed.CheckState = Settings.Default.ShowSpeed ? CheckState.Checked : CheckState.Unchecked;
            // get model scan
            Check_Connect.CheckState = Settings.Default.ConnectOnLaunch ? CheckState.Checked : CheckState.Unchecked;
            // get model scan
            Check_Scan.CheckState = Settings.Default.ModelScanOnConnection ? CheckState.Checked : CheckState.Unchecked;
            // get AI state
            Check_UseAIFeatures.CheckState = Settings.Default.UseAIFeatures ? CheckState.Checked : CheckState.Unchecked;
            // get elevation
            Check_Elevation.CheckState = Settings.Default.ElevationCorrection ? CheckState.Checked : CheckState.Unchecked;
            // get local port number
            Text_LocalPort.Text = Settings.Default.LocalPort.ToString();
            // get local port enabled
            Check_LocalPort.CheckState = Settings.Default.LocalPortEnabled ? CheckState.Checked : CheckState.Unchecked;
            // get auto join
            Check_GlobalJoin.CheckState = Settings.Default.Global ? CheckState.Checked : CheckState.Unchecked;
            // get low bandwidth
            Check_LowBandwidth.CheckState = Settings.Default.LowBandwidth ? CheckState.Checked : CheckState.Unchecked;
            // get auto broadcast Tacpack
            Check_Tacpack.CheckState = Settings.Default.BroadcastTacpack ? CheckState.Checked : CheckState.Unchecked;
            // get trimmed password
            Text_Password.Text = Settings.Default.Password.TrimStart(' ').TrimEnd(' ');
            // get activity circle
            int circle = Settings.Default.ActivityCircle;
            Label_Circle.Text = circle + " nm";
            Track_Circle.Value = circle;
            // get follow distance
            int distance = Settings.Default.FollowDistance;
            Label_Follow.Text = distance.ToString() + " m";
            Track_Follow.Value = distance;
            // get ATC mode
            Check_ATC.CheckState = Settings.Default.Atc ? CheckState.Checked : CheckState.Unchecked;
            // get ICAO code
            Text_Airport.Text = Settings.Default.AtcAirport.ToUpper();
            // get ATC level
            Combo_Level.SelectedIndex = Math.Min(4, Math.Max(0, Settings.Default.AtcLevel));
            // get ATC frequency
            Text_Frequency.Text = Sim.FrequencyIntToString(Settings.Default.AtcFrequency);
            // get Euroscope
            Check_Euroscope.CheckState = Settings.Default.Euroscope ? CheckState.Checked : CheckState.Unchecked;
            // get hub mode
            Check_Hub.CheckState = Settings.Default.Hub ? CheckState.Checked : CheckState.Unchecked;
            // get hub address
            Text_HubDomain.Text = Settings.Default.HubAddress;
            // get hub name
            Text_HubName.Text = Settings.Default.HubName;
            // get hub About
            Text_HubAbout.Text = Settings.Default.HubAbout;
            // get hub voip
            Text_HubVoIP.Text = Settings.Default.HubVoIP;
            // get hub event
            Text_HubEvent.Text = Settings.Default.HubEvent;

            // get xplane plugin address
            Text_PluginAddress.Text = Settings.Default.XPlanePluginAddress;
            // get xplane TCAS
            Check_TCAS.CheckState = Settings.Default.TCAS ? CheckState.Checked : CheckState.Unchecked;

            // indicator colours
            Label_Active.BackColor = Settings.Default.ColourActiveBackground;
            Label_Active.ForeColor = Settings.Default.ColourActiveText;
            Label_Waiting.BackColor = Settings.Default.ColourWaitingBackground;
            Label_Waiting.ForeColor = Settings.Default.ColourWaitingText;
            Label_Inactive.BackColor = Settings.Default.ColourInactiveBackground;
            Label_Inactive.ForeColor = Settings.Default.ColourInactiveText;

            // label colour
            Label_LabelColour.BackColor = Settings.Default.ColourLabel;

            // check for tool tips
            if (Settings.Default.ToolTips)
            {
                ToolTip tip = new()
                {
                    ShowAlways = true,
                    IsBalloon = true,
                    AutomaticDelay = 1500
                };
                tip.SetToolTip(Check_AlwaysOnTop, Resources.Strings.Tip_AlwaysOnTop);
                tip.SetToolTip(Check_AutoRefresh, Resources.Strings.Tip_AutoRefresh);
                tip.SetToolTip(Check_EarlyUpdate, Resources.Strings.Tip_EarlyUpdate);
                tip.SetToolTip(Check_ToolTips, Resources.Strings.Tip_ToolTips);
                tip.SetToolTip(Label_Active, Resources.Strings.Tip_Active);
                tip.SetToolTip(Button_ActiveBackground, Resources.Strings.Tip_Active);
                tip.SetToolTip(Button_ActiveText, Resources.Strings.Tip_Active);
                tip.SetToolTip(Label_Waiting, Resources.Strings.Tip_Waiting);
                tip.SetToolTip(Button_WaitingBackground, Resources.Strings.Tip_Waiting);
                tip.SetToolTip(Button_WaitingText, Resources.Strings.Tip_Waiting);
                tip.SetToolTip(Label_Inactive, Resources.Strings.Tip_Inactive);
                tip.SetToolTip(Button_InactiveBackground, Resources.Strings.Tip_Inactive);
                tip.SetToolTip(Button_InactiveText, Resources.Strings.Tip_Inactive);
                tip.SetToolTip(Label_Nickname, Resources.Strings.Tip_Nickname);
                tip.SetToolTip(Text_Nickname, Resources.Strings.Tip_Nickname);
                tip.SetToolTip(Check_ShowNickname, Resources.Strings.Tip_ShowNicknames);
                tip.SetToolTip(Label_Circle, Resources.Strings.Tip_CircleOfActivity);
                tip.SetToolTip(Label_CircleText, Resources.Strings.Tip_CircleOfActivity);
                tip.SetToolTip(Track_Circle, Resources.Strings.Tip_CircleOfActivity);
                tip.SetToolTip(Label_Follow, Resources.Strings.Tip_Follow);
                tip.SetToolTip(Label_FollowText, Resources.Strings.Tip_Follow);
                tip.SetToolTip(Track_Follow, Resources.Strings.Tip_Follow);
                tip.SetToolTip(Check_Elevation, Resources.Strings.Tip_Elevation);
                tip.SetToolTip(Check_Scan, Resources.Strings.Tip_ModelScan);
                tip.SetToolTip(Check_ATC, Resources.Strings.Tip_ATC);
                tip.SetToolTip(Text_Airport, Resources.Strings.Tip_Airport);
                tip.SetToolTip(Label_Airport, Resources.Strings.Tip_Airport);
                tip.SetToolTip(Check_Euroscope, Resources.Strings.Tip_Euroscope);
                tip.SetToolTip(Combo_Level, Resources.Strings.Tip_Level);
                tip.SetToolTip(Label_Level, Resources.Strings.Tip_Level);
                tip.SetToolTip(Text_Frequency, Resources.Strings.Tip_Frequency);
                tip.SetToolTip(Label_Frequency, Resources.Strings.Tip_Frequency);
                tip.SetToolTip(Check_LocalPort, Resources.Strings.Tip_LocalPort);
                tip.SetToolTip(Text_LocalPort, Resources.Strings.Tip_LocalPort);
                tip.SetToolTip(Check_GlobalJoin, Resources.Strings.Tip_GlobalJoin);
                tip.SetToolTip(Check_LowBandwidth, Resources.Strings.Tip_LowBandwidth);
                tip.SetToolTip(Check_Tacpack, Resources.Strings.Tip_VRS);
                tip.SetToolTip(Label_Password, Resources.Strings.Tip_Password);
                tip.SetToolTip(Check_Whazzup, Resources.Strings.Tip_Whazzup);
                tip.SetToolTip(Check_WhazzupGlobal, Resources.Strings.Tip_WhazzupGlobal);
                tip.SetToolTip(Check_WhazzupAI, Resources.Strings.Tip_WhazzupAI);
                tip.SetToolTip(Check_Hub, Resources.Strings.Tip_HubMode);
                tip.SetToolTip(Label_HubDomain, Resources.Strings.Tip_HubDomain);
                tip.SetToolTip(Text_HubDomain, Resources.Strings.Tip_HubDomain);
                tip.SetToolTip(Label_HubName, Resources.Strings.Tip_HubName);
                tip.SetToolTip(Text_HubName, Resources.Strings.Tip_HubName);
                tip.SetToolTip(Label_HubAbout, Resources.Strings.Tip_HubAbout);
                tip.SetToolTip(Text_HubAbout, Resources.Strings.Tip_HubAbout);
                tip.SetToolTip(Label_HubVoIP, Resources.Strings.Tip_HubVoice);
                tip.SetToolTip(Text_HubVoIP, Resources.Strings.Tip_HubVoice);
                tip.SetToolTip(Label_HubEvent, Resources.Strings.Tip_HubEvent);
                tip.SetToolTip(Text_HubEvent, Resources.Strings.Tip_HubEvent);
                tip.SetToolTip(Button_InstallPlugin, Resources.Strings.Tip_Plugin);
                tip.SetToolTip(Button_InstallCPP, Resources.Strings.Tip_CPP);
                tip.SetToolTip(Label_PluginAddress, Resources.Strings.Tip_PluginAddress);
                tip.SetToolTip(Text_PluginAddress, Resources.Strings.Tip_PluginAddress);
                tip.SetToolTip(Text_PluginAddress, Resources.Strings.Tip_PluginAddress);
                tip.SetToolTip(Check_TCAS, Resources.Strings.Tip_TCAS);
                tip.SetToolTip(Check_ShowNickname, Resources.Strings.Tip_ShowNickname);
                tip.SetToolTip(Check_ShowCallsign, Resources.Strings.Tip_ShowCallsign);
                tip.SetToolTip(Check_ShowDistance, Resources.Strings.Tip_ShowDistance);
                tip.SetToolTip(Check_ShowAltitude, Resources.Strings.Tip_ShowAltitude);
                tip.SetToolTip(Check_ShowSpeed, Resources.Strings.Tip_ShowSpeed);
            }

            RefreshWindow();
        }

        private void Button_InstallPlugin_Click(object sender, EventArgs e)
        {
            // install plugin
            main.scheduleAskPlugin = true;
            RefreshWindow();
        }

        private void Button_ActiveBackground_Click(object sender, EventArgs e)
        {
            // open colour picker
            ColorDialog dialog = new()
            {
                AllowFullOpen = true,
                AnyColor = true,
                SolidColorOnly = true,
                FullOpen = true,
                Color = Label_Active.BackColor
            };
            DialogResult result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                // update colour
                Label_Active.BackColor = dialog.Color;
                // refresh window
                RefreshWindow();
            }
        }

        private void Button_ActiveText_Click(object sender, EventArgs e)
        {
            // open colour picker
            ColorDialog dialog = new()
            {
                AllowFullOpen = true,
                AnyColor = true,
                SolidColorOnly = true,
                FullOpen = true,
                Color = Label_Active.ForeColor
            };
            DialogResult result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                // update colour
                Label_Active.ForeColor = dialog.Color;
                // refresh window
                RefreshWindow();
            }
        }

        private void Button_WaitingBackground_Click(object sender, EventArgs e)
        {
            // open colour picker
            ColorDialog dialog = new()
            {
                AllowFullOpen = true,
                AnyColor = true,
                SolidColorOnly = true,
                FullOpen = true,
                Color = Label_Waiting.BackColor
            };
            DialogResult result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                // update colour
                Label_Waiting.BackColor = dialog.Color;
                // refresh window
                RefreshWindow();
            }
        }

        private void Button_WaitingText_Click(object sender, EventArgs e)
        {
            // open colour picker
            ColorDialog dialog = new()
            {
                AllowFullOpen = true,
                AnyColor = true,
                SolidColorOnly = true,
                FullOpen = true,
                Color = Label_Waiting.ForeColor
            };
            DialogResult result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                // update colour
                Label_Waiting.ForeColor = dialog.Color;
                // refresh window
                RefreshWindow();
            }
        }

        private void Button_InactiveColour_Click(object sender, EventArgs e)
        {
            // open colour picker
            ColorDialog dialog = new()
            {
                AllowFullOpen = true,
                AnyColor = true,
                SolidColorOnly = true,
                FullOpen = true,
                Color = Label_Inactive.BackColor
            };
            DialogResult result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                // update colour
                Label_Inactive.BackColor = dialog.Color;
                // refresh window
                RefreshWindow();
            }
        }

        private void Button_InactiveText_Click(object sender, EventArgs e)
        {
            // open colour picker
            ColorDialog dialog = new()
            {
                AllowFullOpen = true,
                AnyColor = true,
                SolidColorOnly = true,
                FullOpen = true,
                Color = Label_Inactive.ForeColor
            };
            DialogResult result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                // update colour
                Label_Inactive.ForeColor = dialog.Color;
                // refresh window
                RefreshWindow();
            }
        }

        private void Button_InstallCPP_Click(object sender, EventArgs e)
        {
            // string sc = Program.Code("https://aka.ms/vs/17/release/VC_redist.x64.exe", true, 1234);
            // why?
            Main.Launch("https://aka.ms/vs/17/release/VC_redist.x64.exe");
        }

        private void Button_LabelColour_Click(object sender, EventArgs e)
        {
            // open colour picker
            ColorDialog dialog = new()
            {
                AllowFullOpen = true,
                AnyColor = true,
                SolidColorOnly = true,
                FullOpen = true,
                Color = Label_LabelColour.BackColor
            };
            DialogResult result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                // update colour
                Label_LabelColour.BackColor = dialog.Color;
            }
        }

        private void Button_Reset_Click(object sender, EventArgs e)
        {
            // Confirm
            if (MessageBox.Show(Resources.Strings.ResetSettings, Main.Name, MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                // keep last position GUID
                Guid lastPosition = Settings.Default.LastPosition;
                // reset settings
                Settings.Default.Reset();
                // restore last position and keep migrated and first run settings
                Settings.Default.LastPosition = lastPosition;
                Settings.Default.MigratedSettings = true;
                Settings.Default.AskImport = "3.1.6";
                Settings.Default.FirstRun = false;
                Settings.Default.Save();
                SettingsForm_Load(sender, e);
            }
        }

        private void Check_EarlyUpdate_CheckedChanged(object sender, EventArgs e)
        {
            RefreshWindow();
        }
    }
}
