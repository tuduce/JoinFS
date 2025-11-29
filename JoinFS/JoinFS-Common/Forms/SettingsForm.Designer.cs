namespace JoinFS
{
    partial class SettingsForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SettingsForm));
            this.Button_OK = new System.Windows.Forms.Button();
            this.Check_LocalPort = new System.Windows.Forms.CheckBox();
            this.Text_LocalPort = new System.Windows.Forms.TextBox();
            this.Button_Cancel = new System.Windows.Forms.Button();
            this.Track_Follow = new System.Windows.Forms.TrackBar();
            this.Label_Follow = new System.Windows.Forms.Label();
            this.Check_LowBandwidth = new System.Windows.Forms.CheckBox();
            this.Label_Nickname = new System.Windows.Forms.Label();
            this.Text_Nickname = new System.Windows.Forms.TextBox();
            this.Check_ShowNickname = new System.Windows.Forms.CheckBox();
            this.Label_Circle = new System.Windows.Forms.Label();
            this.Track_Circle = new System.Windows.Forms.TrackBar();
            this.GroupBox_Simulator = new System.Windows.Forms.GroupBox();
            this.Label_FollowText = new System.Windows.Forms.Label();
            this.Check_Connect = new System.Windows.Forms.CheckBox();
            this.Button_LabelColour = new System.Windows.Forms.Button();
            this.Label_LabelColour = new System.Windows.Forms.Label();
            this.Check_ShowDistance = new System.Windows.Forms.CheckBox();
            this.Check_ShowSpeed = new System.Windows.Forms.CheckBox();
            this.Check_ShowAltitude = new System.Windows.Forms.CheckBox();
            this.Check_ShowCallsign = new System.Windows.Forms.CheckBox();
            this.Check_Scan = new System.Windows.Forms.CheckBox();
            this.Check_Elevation = new System.Windows.Forms.CheckBox();
            this.Label_CircleText = new System.Windows.Forms.Label();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.Label_Password = new System.Windows.Forms.Label();
            this.Check_WhazzupAI = new System.Windows.Forms.CheckBox();
            this.Check_GlobalJoin = new System.Windows.Forms.CheckBox();
            this.Check_WhazzupGlobal = new System.Windows.Forms.CheckBox();
            this.Check_Tacpack = new System.Windows.Forms.CheckBox();
            this.Check_Whazzup = new System.Windows.Forms.CheckBox();
            this.Text_Password = new System.Windows.Forms.TextBox();
            this.GroupBox_ATC = new System.Windows.Forms.GroupBox();
            this.Label_Frequency = new System.Windows.Forms.Label();
            this.Text_Frequency = new System.Windows.Forms.TextBox();
            this.Label_Level = new System.Windows.Forms.Label();
            this.Combo_Level = new System.Windows.Forms.ComboBox();
            this.Check_Euroscope = new System.Windows.Forms.CheckBox();
            this.Text_Airport = new System.Windows.Forms.TextBox();
            this.Label_Airport = new System.Windows.Forms.Label();
            this.Check_ATC = new System.Windows.Forms.CheckBox();
            this.GroupBox_Hub = new System.Windows.Forms.GroupBox();
            this.Text_HubDomain = new System.Windows.Forms.TextBox();
            this.Label_HubDomain = new System.Windows.Forms.Label();
            this.Text_HubEvent = new System.Windows.Forms.TextBox();
            this.Label_HubEvent = new System.Windows.Forms.Label();
            this.Text_HubVoIP = new System.Windows.Forms.TextBox();
            this.Text_HubAbout = new System.Windows.Forms.TextBox();
            this.Label_HubAbout = new System.Windows.Forms.Label();
            this.Label_HubVoIP = new System.Windows.Forms.Label();
            this.Label_HubName = new System.Windows.Forms.Label();
            this.Text_HubName = new System.Windows.Forms.TextBox();
            this.Check_Hub = new System.Windows.Forms.CheckBox();
            this.Check_AlwaysOnTop = new System.Windows.Forms.CheckBox();
            this.groupBox5 = new System.Windows.Forms.GroupBox();
            this.Check_EarlyUpdate = new System.Windows.Forms.CheckBox();
            this.Check_ToolTips = new System.Windows.Forms.CheckBox();
            this.Label_Inactive = new System.Windows.Forms.Label();
            this.Label_Waiting = new System.Windows.Forms.Label();
            this.Button_InactiveText = new System.Windows.Forms.Button();
            this.Button_WaitingText = new System.Windows.Forms.Button();
            this.Button_ActiveText = new System.Windows.Forms.Button();
            this.Button_InactiveBackground = new System.Windows.Forms.Button();
            this.Button_WaitingBackground = new System.Windows.Forms.Button();
            this.Button_ActiveBackground = new System.Windows.Forms.Button();
            this.Label_Active = new System.Windows.Forms.Label();
            this.Check_AutoRefresh = new System.Windows.Forms.CheckBox();
            this.GroupBox_Xplane = new System.Windows.Forms.GroupBox();
            this.Check_TCAS = new System.Windows.Forms.CheckBox();
            this.Button_InstallCPP = new System.Windows.Forms.Button();
            this.Text_PluginAddress = new System.Windows.Forms.TextBox();
            this.Label_PluginAddress = new System.Windows.Forms.Label();
            this.Button_InstallPlugin = new System.Windows.Forms.Button();
            this.Button_Reset = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.Track_Follow)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.Track_Circle)).BeginInit();
            this.GroupBox_Simulator.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.GroupBox_ATC.SuspendLayout();
            this.GroupBox_Hub.SuspendLayout();
            this.groupBox5.SuspendLayout();
            this.GroupBox_Xplane.SuspendLayout();
            this.SuspendLayout();
            // 
            // Button_OK
            // 
            this.Button_OK.DialogResult = System.Windows.Forms.DialogResult.OK;
            resources.ApplyResources(this.Button_OK, "Button_OK");
            this.Button_OK.Name = "Button_OK";
            this.Button_OK.UseVisualStyleBackColor = true;
            this.Button_OK.Click += new System.EventHandler(this.Button_OK_Click);
            // 
            // Check_LocalPort
            // 
            resources.ApplyResources(this.Check_LocalPort, "Check_LocalPort");
            this.Check_LocalPort.Name = "Check_LocalPort";
            this.Check_LocalPort.UseVisualStyleBackColor = true;
            this.Check_LocalPort.CheckedChanged += new System.EventHandler(this.Check_LocalPort_CheckedChanged);
            // 
            // Text_LocalPort
            // 
            resources.ApplyResources(this.Text_LocalPort, "Text_LocalPort");
            this.Text_LocalPort.Name = "Text_LocalPort";
            // 
            // Button_Cancel
            // 
            this.Button_Cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            resources.ApplyResources(this.Button_Cancel, "Button_Cancel");
            this.Button_Cancel.Name = "Button_Cancel";
            this.Button_Cancel.UseVisualStyleBackColor = true;
            // 
            // Track_Follow
            // 
            resources.ApplyResources(this.Track_Follow, "Track_Follow");
            this.Track_Follow.Maximum = 1000;
            this.Track_Follow.Minimum = 20;
            this.Track_Follow.Name = "Track_Follow";
            this.Track_Follow.TickFrequency = 10;
            this.Track_Follow.TickStyle = System.Windows.Forms.TickStyle.TopLeft;
            this.Track_Follow.Value = 20;
            this.Track_Follow.ValueChanged += new System.EventHandler(this.Track_Follow_ValueChanged);
            // 
            // Label_Follow
            // 
            resources.ApplyResources(this.Label_Follow, "Label_Follow");
            this.Label_Follow.Name = "Label_Follow";
            // 
            // Check_LowBandwidth
            // 
            resources.ApplyResources(this.Check_LowBandwidth, "Check_LowBandwidth");
            this.Check_LowBandwidth.Name = "Check_LowBandwidth";
            this.Check_LowBandwidth.UseVisualStyleBackColor = true;
            // 
            // Label_Nickname
            // 
            resources.ApplyResources(this.Label_Nickname, "Label_Nickname");
            this.Label_Nickname.Name = "Label_Nickname";
            // 
            // Text_Nickname
            // 
            resources.ApplyResources(this.Text_Nickname, "Text_Nickname");
            this.Text_Nickname.Name = "Text_Nickname";
            // 
            // Check_ShowNickname
            // 
            resources.ApplyResources(this.Check_ShowNickname, "Check_ShowNickname");
            this.Check_ShowNickname.Name = "Check_ShowNickname";
            this.Check_ShowNickname.UseVisualStyleBackColor = true;
            // 
            // Label_Circle
            // 
            resources.ApplyResources(this.Label_Circle, "Label_Circle");
            this.Label_Circle.Name = "Label_Circle";
            // 
            // Track_Circle
            // 
            resources.ApplyResources(this.Track_Circle, "Track_Circle");
            this.Track_Circle.Maximum = 600;
            this.Track_Circle.Minimum = 2;
            this.Track_Circle.Name = "Track_Circle";
            this.Track_Circle.TickStyle = System.Windows.Forms.TickStyle.TopLeft;
            this.Track_Circle.Value = 40;
            this.Track_Circle.ValueChanged += new System.EventHandler(this.Track_Circle_ValueChanged);
            // 
            // GroupBox_Simulator
            // 
            this.GroupBox_Simulator.Controls.Add(this.Label_FollowText);
            this.GroupBox_Simulator.Controls.Add(this.Check_Connect);
            this.GroupBox_Simulator.Controls.Add(this.Button_LabelColour);
            this.GroupBox_Simulator.Controls.Add(this.Label_LabelColour);
            this.GroupBox_Simulator.Controls.Add(this.Check_ShowDistance);
            this.GroupBox_Simulator.Controls.Add(this.Check_ShowSpeed);
            this.GroupBox_Simulator.Controls.Add(this.Check_ShowAltitude);
            this.GroupBox_Simulator.Controls.Add(this.Check_ShowCallsign);
            this.GroupBox_Simulator.Controls.Add(this.Check_Scan);
            this.GroupBox_Simulator.Controls.Add(this.Check_Elevation);
            this.GroupBox_Simulator.Controls.Add(this.Label_CircleText);
            this.GroupBox_Simulator.Controls.Add(this.Label_Follow);
            this.GroupBox_Simulator.Controls.Add(this.Label_Circle);
            this.GroupBox_Simulator.Controls.Add(this.Text_Nickname);
            this.GroupBox_Simulator.Controls.Add(this.Label_Nickname);
            this.GroupBox_Simulator.Controls.Add(this.Check_ShowNickname);
            this.GroupBox_Simulator.Controls.Add(this.Track_Circle);
            this.GroupBox_Simulator.Controls.Add(this.Track_Follow);
            resources.ApplyResources(this.GroupBox_Simulator, "GroupBox_Simulator");
            this.GroupBox_Simulator.Name = "GroupBox_Simulator";
            this.GroupBox_Simulator.TabStop = false;
            // 
            // Label_FollowText
            // 
            resources.ApplyResources(this.Label_FollowText, "Label_FollowText");
            this.Label_FollowText.Name = "Label_FollowText";
            // 
            // Check_Connect
            // 
            resources.ApplyResources(this.Check_Connect, "Check_Connect");
            this.Check_Connect.Name = "Check_Connect";
            this.Check_Connect.UseVisualStyleBackColor = true;
            // 
            // Button_LabelColour
            // 
            resources.ApplyResources(this.Button_LabelColour, "Button_LabelColour");
            this.Button_LabelColour.Name = "Button_LabelColour";
            this.Button_LabelColour.UseVisualStyleBackColor = true;
            this.Button_LabelColour.Click += new System.EventHandler(this.Button_LabelColour_Click);
            // 
            // Label_LabelColour
            // 
            this.Label_LabelColour.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            resources.ApplyResources(this.Label_LabelColour, "Label_LabelColour");
            this.Label_LabelColour.Name = "Label_LabelColour";
            // 
            // Check_ShowDistance
            // 
            resources.ApplyResources(this.Check_ShowDistance, "Check_ShowDistance");
            this.Check_ShowDistance.Name = "Check_ShowDistance";
            this.Check_ShowDistance.UseVisualStyleBackColor = true;
            // 
            // Check_ShowSpeed
            // 
            resources.ApplyResources(this.Check_ShowSpeed, "Check_ShowSpeed");
            this.Check_ShowSpeed.Name = "Check_ShowSpeed";
            this.Check_ShowSpeed.UseVisualStyleBackColor = true;
            // 
            // Check_ShowAltitude
            // 
            resources.ApplyResources(this.Check_ShowAltitude, "Check_ShowAltitude");
            this.Check_ShowAltitude.Name = "Check_ShowAltitude";
            this.Check_ShowAltitude.UseVisualStyleBackColor = true;
            // 
            // Check_ShowCallsign
            // 
            resources.ApplyResources(this.Check_ShowCallsign, "Check_ShowCallsign");
            this.Check_ShowCallsign.Name = "Check_ShowCallsign";
            this.Check_ShowCallsign.UseVisualStyleBackColor = true;
            // 
            // Check_Scan
            // 
            resources.ApplyResources(this.Check_Scan, "Check_Scan");
            this.Check_Scan.Name = "Check_Scan";
            this.Check_Scan.UseVisualStyleBackColor = true;
            // 
            // Check_Elevation
            // 
            resources.ApplyResources(this.Check_Elevation, "Check_Elevation");
            this.Check_Elevation.Name = "Check_Elevation";
            this.Check_Elevation.UseVisualStyleBackColor = true;
            // 
            // Label_CircleText
            // 
            resources.ApplyResources(this.Label_CircleText, "Label_CircleText");
            this.Label_CircleText.Name = "Label_CircleText";
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.Label_Password);
            this.groupBox2.Controls.Add(this.Check_WhazzupAI);
            this.groupBox2.Controls.Add(this.Check_GlobalJoin);
            this.groupBox2.Controls.Add(this.Check_WhazzupGlobal);
            this.groupBox2.Controls.Add(this.Check_Tacpack);
            this.groupBox2.Controls.Add(this.Check_Whazzup);
            this.groupBox2.Controls.Add(this.Check_LocalPort);
            this.groupBox2.Controls.Add(this.Text_Password);
            this.groupBox2.Controls.Add(this.Check_LowBandwidth);
            this.groupBox2.Controls.Add(this.Text_LocalPort);
            resources.ApplyResources(this.groupBox2, "groupBox2");
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.TabStop = false;
            // 
            // Label_Password
            // 
            resources.ApplyResources(this.Label_Password, "Label_Password");
            this.Label_Password.Name = "Label_Password";
            // 
            // Check_WhazzupAI
            // 
            resources.ApplyResources(this.Check_WhazzupAI, "Check_WhazzupAI");
            this.Check_WhazzupAI.Name = "Check_WhazzupAI";
            this.Check_WhazzupAI.UseVisualStyleBackColor = true;
            // 
            // Check_GlobalJoin
            // 
            resources.ApplyResources(this.Check_GlobalJoin, "Check_GlobalJoin");
            this.Check_GlobalJoin.Name = "Check_GlobalJoin";
            this.Check_GlobalJoin.UseVisualStyleBackColor = true;
            // 
            // Check_WhazzupGlobal
            // 
            resources.ApplyResources(this.Check_WhazzupGlobal, "Check_WhazzupGlobal");
            this.Check_WhazzupGlobal.Name = "Check_WhazzupGlobal";
            this.Check_WhazzupGlobal.UseVisualStyleBackColor = true;
            // 
            // Check_Tacpack
            // 
            resources.ApplyResources(this.Check_Tacpack, "Check_Tacpack");
            this.Check_Tacpack.Name = "Check_Tacpack";
            this.Check_Tacpack.UseVisualStyleBackColor = true;
            // 
            // Check_Whazzup
            // 
            resources.ApplyResources(this.Check_Whazzup, "Check_Whazzup");
            this.Check_Whazzup.Name = "Check_Whazzup";
            this.Check_Whazzup.UseVisualStyleBackColor = true;
            this.Check_Whazzup.CheckedChanged += new System.EventHandler(this.Check_Whazzup_CheckedChanged);
            // 
            // Text_Password
            // 
            resources.ApplyResources(this.Text_Password, "Text_Password");
            this.Text_Password.Name = "Text_Password";
            // 
            // GroupBox_ATC
            // 
            this.GroupBox_ATC.Controls.Add(this.Label_Frequency);
            this.GroupBox_ATC.Controls.Add(this.Text_Frequency);
            this.GroupBox_ATC.Controls.Add(this.Label_Level);
            this.GroupBox_ATC.Controls.Add(this.Combo_Level);
            this.GroupBox_ATC.Controls.Add(this.Check_Euroscope);
            this.GroupBox_ATC.Controls.Add(this.Text_Airport);
            this.GroupBox_ATC.Controls.Add(this.Label_Airport);
            this.GroupBox_ATC.Controls.Add(this.Check_ATC);
            resources.ApplyResources(this.GroupBox_ATC, "GroupBox_ATC");
            this.GroupBox_ATC.Name = "GroupBox_ATC";
            this.GroupBox_ATC.TabStop = false;
            // 
            // Label_Frequency
            // 
            resources.ApplyResources(this.Label_Frequency, "Label_Frequency");
            this.Label_Frequency.Name = "Label_Frequency";
            // 
            // Text_Frequency
            // 
            resources.ApplyResources(this.Text_Frequency, "Text_Frequency");
            this.Text_Frequency.Name = "Text_Frequency";
            // 
            // Label_Level
            // 
            resources.ApplyResources(this.Label_Level, "Label_Level");
            this.Label_Level.Name = "Label_Level";
            // 
            // Combo_Level
            // 
            this.Combo_Level.FormattingEnabled = true;
            this.Combo_Level.Items.AddRange(new object[] {
            resources.GetString("Combo_Level.Items"),
            resources.GetString("Combo_Level.Items1"),
            resources.GetString("Combo_Level.Items2"),
            resources.GetString("Combo_Level.Items3"),
            resources.GetString("Combo_Level.Items4")});
            resources.ApplyResources(this.Combo_Level, "Combo_Level");
            this.Combo_Level.Name = "Combo_Level";
            // 
            // Check_Euroscope
            // 
            resources.ApplyResources(this.Check_Euroscope, "Check_Euroscope");
            this.Check_Euroscope.Name = "Check_Euroscope";
            this.Check_Euroscope.UseVisualStyleBackColor = true;
            // 
            // Text_Airport
            // 
            this.Text_Airport.BackColor = System.Drawing.Color.White;
            resources.ApplyResources(this.Text_Airport, "Text_Airport");
            this.Text_Airport.Name = "Text_Airport";
            this.Text_Airport.TextChanged += new System.EventHandler(this.Text_Airport_TextChanged);
            // 
            // Label_Airport
            // 
            resources.ApplyResources(this.Label_Airport, "Label_Airport");
            this.Label_Airport.Name = "Label_Airport";
            // 
            // Check_ATC
            // 
            resources.ApplyResources(this.Check_ATC, "Check_ATC");
            this.Check_ATC.Name = "Check_ATC";
            this.Check_ATC.UseVisualStyleBackColor = true;
            this.Check_ATC.CheckedChanged += new System.EventHandler(this.Check_ATC_CheckedChanged);
            // 
            // GroupBox_Hub
            // 
            this.GroupBox_Hub.Controls.Add(this.Text_HubDomain);
            this.GroupBox_Hub.Controls.Add(this.Label_HubDomain);
            this.GroupBox_Hub.Controls.Add(this.Text_HubEvent);
            this.GroupBox_Hub.Controls.Add(this.Label_HubEvent);
            this.GroupBox_Hub.Controls.Add(this.Text_HubVoIP);
            this.GroupBox_Hub.Controls.Add(this.Text_HubAbout);
            this.GroupBox_Hub.Controls.Add(this.Label_HubAbout);
            this.GroupBox_Hub.Controls.Add(this.Label_HubVoIP);
            this.GroupBox_Hub.Controls.Add(this.Label_HubName);
            this.GroupBox_Hub.Controls.Add(this.Text_HubName);
            this.GroupBox_Hub.Controls.Add(this.Check_Hub);
            resources.ApplyResources(this.GroupBox_Hub, "GroupBox_Hub");
            this.GroupBox_Hub.Name = "GroupBox_Hub";
            this.GroupBox_Hub.TabStop = false;
            // 
            // Text_HubDomain
            // 
            resources.ApplyResources(this.Text_HubDomain, "Text_HubDomain");
            this.Text_HubDomain.Name = "Text_HubDomain";
            // 
            // Label_HubDomain
            // 
            resources.ApplyResources(this.Label_HubDomain, "Label_HubDomain");
            this.Label_HubDomain.Name = "Label_HubDomain";
            // 
            // Text_HubEvent
            // 
            resources.ApplyResources(this.Text_HubEvent, "Text_HubEvent");
            this.Text_HubEvent.Name = "Text_HubEvent";
            // 
            // Label_HubEvent
            // 
            resources.ApplyResources(this.Label_HubEvent, "Label_HubEvent");
            this.Label_HubEvent.Name = "Label_HubEvent";
            // 
            // Text_HubVoIP
            // 
            resources.ApplyResources(this.Text_HubVoIP, "Text_HubVoIP");
            this.Text_HubVoIP.Name = "Text_HubVoIP";
            // 
            // Text_HubAbout
            // 
            resources.ApplyResources(this.Text_HubAbout, "Text_HubAbout");
            this.Text_HubAbout.Name = "Text_HubAbout";
            // 
            // Label_HubAbout
            // 
            resources.ApplyResources(this.Label_HubAbout, "Label_HubAbout");
            this.Label_HubAbout.Name = "Label_HubAbout";
            // 
            // Label_HubVoIP
            // 
            resources.ApplyResources(this.Label_HubVoIP, "Label_HubVoIP");
            this.Label_HubVoIP.Name = "Label_HubVoIP";
            // 
            // Label_HubName
            // 
            resources.ApplyResources(this.Label_HubName, "Label_HubName");
            this.Label_HubName.Name = "Label_HubName";
            // 
            // Text_HubName
            // 
            resources.ApplyResources(this.Text_HubName, "Text_HubName");
            this.Text_HubName.Name = "Text_HubName";
            // 
            // Check_Hub
            // 
            resources.ApplyResources(this.Check_Hub, "Check_Hub");
            this.Check_Hub.Name = "Check_Hub";
            this.Check_Hub.UseVisualStyleBackColor = true;
            this.Check_Hub.CheckedChanged += new System.EventHandler(this.Check_Hub_CheckedChanged);
            // 
            // Check_AlwaysOnTop
            // 
            resources.ApplyResources(this.Check_AlwaysOnTop, "Check_AlwaysOnTop");
            this.Check_AlwaysOnTop.Name = "Check_AlwaysOnTop";
            this.Check_AlwaysOnTop.UseVisualStyleBackColor = true;
            // 
            // groupBox5
            // 
            this.groupBox5.Controls.Add(this.Check_EarlyUpdate);
            this.groupBox5.Controls.Add(this.Check_ToolTips);
            this.groupBox5.Controls.Add(this.Label_Inactive);
            this.groupBox5.Controls.Add(this.Label_Waiting);
            this.groupBox5.Controls.Add(this.Button_InactiveText);
            this.groupBox5.Controls.Add(this.Button_WaitingText);
            this.groupBox5.Controls.Add(this.Button_ActiveText);
            this.groupBox5.Controls.Add(this.Button_InactiveBackground);
            this.groupBox5.Controls.Add(this.Button_WaitingBackground);
            this.groupBox5.Controls.Add(this.Button_ActiveBackground);
            this.groupBox5.Controls.Add(this.Label_Active);
            this.groupBox5.Controls.Add(this.Check_AutoRefresh);
            this.groupBox5.Controls.Add(this.Check_AlwaysOnTop);
            resources.ApplyResources(this.groupBox5, "groupBox5");
            this.groupBox5.Name = "groupBox5";
            this.groupBox5.TabStop = false;
            // 
            // Check_EarlyUpdate
            // 
            resources.ApplyResources(this.Check_EarlyUpdate, "Check_EarlyUpdate");
            this.Check_EarlyUpdate.Name = "Check_EarlyUpdate";
            this.Check_EarlyUpdate.UseVisualStyleBackColor = true;
            this.Check_EarlyUpdate.CheckedChanged += new System.EventHandler(this.Check_EarlyUpdate_CheckedChanged);
            // 
            // Check_ToolTips
            // 
            resources.ApplyResources(this.Check_ToolTips, "Check_ToolTips");
            this.Check_ToolTips.Name = "Check_ToolTips";
            this.Check_ToolTips.UseVisualStyleBackColor = true;
            // 
            // Label_Inactive
            // 
            this.Label_Inactive.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            resources.ApplyResources(this.Label_Inactive, "Label_Inactive");
            this.Label_Inactive.Name = "Label_Inactive";
            // 
            // Label_Waiting
            // 
            this.Label_Waiting.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            resources.ApplyResources(this.Label_Waiting, "Label_Waiting");
            this.Label_Waiting.Name = "Label_Waiting";
            // 
            // Button_InactiveText
            // 
            resources.ApplyResources(this.Button_InactiveText, "Button_InactiveText");
            this.Button_InactiveText.Name = "Button_InactiveText";
            this.Button_InactiveText.UseVisualStyleBackColor = true;
            this.Button_InactiveText.Click += new System.EventHandler(this.Button_InactiveText_Click);
            // 
            // Button_WaitingText
            // 
            resources.ApplyResources(this.Button_WaitingText, "Button_WaitingText");
            this.Button_WaitingText.Name = "Button_WaitingText";
            this.Button_WaitingText.UseVisualStyleBackColor = true;
            this.Button_WaitingText.Click += new System.EventHandler(this.Button_WaitingText_Click);
            // 
            // Button_ActiveText
            // 
            resources.ApplyResources(this.Button_ActiveText, "Button_ActiveText");
            this.Button_ActiveText.Name = "Button_ActiveText";
            this.Button_ActiveText.UseVisualStyleBackColor = true;
            this.Button_ActiveText.Click += new System.EventHandler(this.Button_ActiveText_Click);
            // 
            // Button_InactiveBackground
            // 
            resources.ApplyResources(this.Button_InactiveBackground, "Button_InactiveBackground");
            this.Button_InactiveBackground.Name = "Button_InactiveBackground";
            this.Button_InactiveBackground.UseVisualStyleBackColor = true;
            this.Button_InactiveBackground.Click += new System.EventHandler(this.Button_InactiveColour_Click);
            // 
            // Button_WaitingBackground
            // 
            resources.ApplyResources(this.Button_WaitingBackground, "Button_WaitingBackground");
            this.Button_WaitingBackground.Name = "Button_WaitingBackground";
            this.Button_WaitingBackground.UseVisualStyleBackColor = true;
            this.Button_WaitingBackground.Click += new System.EventHandler(this.Button_WaitingBackground_Click);
            // 
            // Button_ActiveBackground
            // 
            resources.ApplyResources(this.Button_ActiveBackground, "Button_ActiveBackground");
            this.Button_ActiveBackground.Name = "Button_ActiveBackground";
            this.Button_ActiveBackground.UseVisualStyleBackColor = true;
            this.Button_ActiveBackground.Click += new System.EventHandler(this.Button_ActiveBackground_Click);
            // 
            // Label_Active
            // 
            this.Label_Active.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            resources.ApplyResources(this.Label_Active, "Label_Active");
            this.Label_Active.Name = "Label_Active";
            // 
            // Check_AutoRefresh
            // 
            resources.ApplyResources(this.Check_AutoRefresh, "Check_AutoRefresh");
            this.Check_AutoRefresh.Name = "Check_AutoRefresh";
            this.Check_AutoRefresh.UseVisualStyleBackColor = true;
            this.Check_AutoRefresh.CheckedChanged += new System.EventHandler(this.Check_AutoRefresh_CheckedChanged);
            // 
            // GroupBox_Xplane
            // 
            this.GroupBox_Xplane.Controls.Add(this.Check_TCAS);
            this.GroupBox_Xplane.Controls.Add(this.Button_InstallCPP);
            this.GroupBox_Xplane.Controls.Add(this.Text_PluginAddress);
            this.GroupBox_Xplane.Controls.Add(this.Label_PluginAddress);
            this.GroupBox_Xplane.Controls.Add(this.Button_InstallPlugin);
            resources.ApplyResources(this.GroupBox_Xplane, "GroupBox_Xplane");
            this.GroupBox_Xplane.Name = "GroupBox_Xplane";
            this.GroupBox_Xplane.TabStop = false;
            // 
            // Check_TCAS
            // 
            resources.ApplyResources(this.Check_TCAS, "Check_TCAS");
            this.Check_TCAS.Name = "Check_TCAS";
            this.Check_TCAS.UseVisualStyleBackColor = true;
            // 
            // Button_InstallCPP
            // 
            resources.ApplyResources(this.Button_InstallCPP, "Button_InstallCPP");
            this.Button_InstallCPP.Name = "Button_InstallCPP";
            this.Button_InstallCPP.UseVisualStyleBackColor = true;
            this.Button_InstallCPP.Click += new System.EventHandler(this.Button_InstallCPP_Click);
            // 
            // Text_PluginAddress
            // 
            resources.ApplyResources(this.Text_PluginAddress, "Text_PluginAddress");
            this.Text_PluginAddress.Name = "Text_PluginAddress";
            // 
            // Label_PluginAddress
            // 
            resources.ApplyResources(this.Label_PluginAddress, "Label_PluginAddress");
            this.Label_PluginAddress.Name = "Label_PluginAddress";
            // 
            // Button_InstallPlugin
            // 
            resources.ApplyResources(this.Button_InstallPlugin, "Button_InstallPlugin");
            this.Button_InstallPlugin.Name = "Button_InstallPlugin";
            this.Button_InstallPlugin.UseVisualStyleBackColor = true;
            this.Button_InstallPlugin.Click += new System.EventHandler(this.Button_InstallPlugin_Click);
            // 
            // Button_Reset
            // 
            resources.ApplyResources(this.Button_Reset, "Button_Reset");
            this.Button_Reset.Name = "Button_Reset";
            this.Button_Reset.UseVisualStyleBackColor = true;
            this.Button_Reset.Click += new System.EventHandler(this.Button_Reset_Click);
            // 
            // SettingsForm
            // 
            this.AcceptButton = this.Button_OK;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.Button_Cancel;
            this.Controls.Add(this.Button_Reset);
            this.Controls.Add(this.GroupBox_Xplane);
            this.Controls.Add(this.groupBox5);
            this.Controls.Add(this.GroupBox_ATC);
            this.Controls.Add(this.Button_Cancel);
            this.Controls.Add(this.Button_OK);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.GroupBox_Hub);
            this.Controls.Add(this.GroupBox_Simulator);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.Name = "SettingsForm";
            this.Load += new System.EventHandler(this.SettingsForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.Track_Follow)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.Track_Circle)).EndInit();
            this.GroupBox_Simulator.ResumeLayout(false);
            this.GroupBox_Simulator.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.GroupBox_ATC.ResumeLayout(false);
            this.GroupBox_ATC.PerformLayout();
            this.GroupBox_Hub.ResumeLayout(false);
            this.GroupBox_Hub.PerformLayout();
            this.groupBox5.ResumeLayout(false);
            this.groupBox5.PerformLayout();
            this.GroupBox_Xplane.ResumeLayout(false);
            this.GroupBox_Xplane.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button Button_OK;
        private System.Windows.Forms.CheckBox Check_LocalPort;
        private System.Windows.Forms.TextBox Text_LocalPort;
        private System.Windows.Forms.Button Button_Cancel;
        private System.Windows.Forms.TrackBar Track_Follow;
        private System.Windows.Forms.Label Label_Follow;
        private System.Windows.Forms.CheckBox Check_LowBandwidth;
        private System.Windows.Forms.Label Label_Nickname;
        private System.Windows.Forms.TextBox Text_Nickname;
        private System.Windows.Forms.CheckBox Check_ShowNickname;
        private System.Windows.Forms.Label Label_Circle;
        private System.Windows.Forms.TrackBar Track_Circle;
        private System.Windows.Forms.GroupBox GroupBox_Simulator;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.GroupBox GroupBox_ATC;
        private System.Windows.Forms.TextBox Text_Airport;
        private System.Windows.Forms.Label Label_Airport;
        private System.Windows.Forms.CheckBox Check_ATC;
        private System.Windows.Forms.GroupBox GroupBox_Hub;
        private System.Windows.Forms.TextBox Text_Password;
        private System.Windows.Forms.TextBox Text_HubVoIP;
        private System.Windows.Forms.TextBox Text_HubAbout;
        private System.Windows.Forms.Label Label_HubAbout;
        private System.Windows.Forms.Label Label_HubVoIP;
        private System.Windows.Forms.Label Label_HubName;
        private System.Windows.Forms.TextBox Text_HubName;
        private System.Windows.Forms.CheckBox Check_Hub;
        private System.Windows.Forms.TextBox Text_HubEvent;
        private System.Windows.Forms.Label Label_HubEvent;
        private System.Windows.Forms.TextBox Text_HubDomain;
        private System.Windows.Forms.Label Label_HubDomain;
        private System.Windows.Forms.CheckBox Check_AlwaysOnTop;
        private System.Windows.Forms.GroupBox groupBox5;
        private System.Windows.Forms.Label Label_FollowText;
        private System.Windows.Forms.Label Label_CircleText;
        private System.Windows.Forms.CheckBox Check_Euroscope;
        private System.Windows.Forms.Label Label_Level;
        private System.Windows.Forms.ComboBox Combo_Level;
        private System.Windows.Forms.CheckBox Check_Whazzup;
        private System.Windows.Forms.Label Label_Frequency;
        private System.Windows.Forms.TextBox Text_Frequency;
        private System.Windows.Forms.CheckBox Check_Tacpack;
        private System.Windows.Forms.CheckBox Check_WhazzupGlobal;
        private System.Windows.Forms.CheckBox Check_GlobalJoin;
        private System.Windows.Forms.CheckBox Check_Elevation;
        private System.Windows.Forms.CheckBox Check_AutoRefresh;
        private System.Windows.Forms.GroupBox GroupBox_Xplane;
        private System.Windows.Forms.TextBox Text_PluginAddress;
        private System.Windows.Forms.Label Label_PluginAddress;
        private System.Windows.Forms.Button Button_InstallPlugin;
        private System.Windows.Forms.Label Label_Inactive;
        private System.Windows.Forms.Label Label_Waiting;
        private System.Windows.Forms.Button Button_InactiveText;
        private System.Windows.Forms.Button Button_WaitingText;
        private System.Windows.Forms.Button Button_ActiveText;
        private System.Windows.Forms.Button Button_InactiveBackground;
        private System.Windows.Forms.Button Button_WaitingBackground;
        private System.Windows.Forms.Button Button_ActiveBackground;
        private System.Windows.Forms.Label Label_Active;
        private System.Windows.Forms.CheckBox Check_Scan;
        private System.Windows.Forms.Button Button_InstallCPP;
        private System.Windows.Forms.CheckBox Check_WhazzupAI;
        private System.Windows.Forms.Label Label_Password;
        private System.Windows.Forms.CheckBox Check_ToolTips;
        private System.Windows.Forms.CheckBox Check_ShowSpeed;
        private System.Windows.Forms.CheckBox Check_ShowAltitude;
        private System.Windows.Forms.CheckBox Check_ShowCallsign;
        private System.Windows.Forms.CheckBox Check_ShowDistance;
        private System.Windows.Forms.Button Button_LabelColour;
        private System.Windows.Forms.Label Label_LabelColour;
        private System.Windows.Forms.CheckBox Check_Connect;
        private System.Windows.Forms.Button Button_Reset;
        private System.Windows.Forms.CheckBox Check_EarlyUpdate;
        private System.Windows.Forms.CheckBox Check_TCAS;
    }
}