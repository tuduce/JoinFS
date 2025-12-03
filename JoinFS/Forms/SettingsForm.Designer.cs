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
            Button_OK = new System.Windows.Forms.Button();
            Check_LocalPort = new System.Windows.Forms.CheckBox();
            Text_LocalPort = new System.Windows.Forms.TextBox();
            Button_Cancel = new System.Windows.Forms.Button();
            Track_Follow = new System.Windows.Forms.TrackBar();
            Label_Follow = new System.Windows.Forms.Label();
            Check_LowBandwidth = new System.Windows.Forms.CheckBox();
            Label_Nickname = new System.Windows.Forms.Label();
            Text_Nickname = new System.Windows.Forms.TextBox();
            Check_ShowNickname = new System.Windows.Forms.CheckBox();
            Label_Circle = new System.Windows.Forms.Label();
            Track_Circle = new System.Windows.Forms.TrackBar();
            GroupBox_Simulator = new System.Windows.Forms.GroupBox();
            Check_UseAIFeatures = new System.Windows.Forms.CheckBox();
            Label_FollowText = new System.Windows.Forms.Label();
            Check_Connect = new System.Windows.Forms.CheckBox();
            Button_LabelColour = new System.Windows.Forms.Button();
            Label_LabelColour = new System.Windows.Forms.Label();
            Check_ShowDistance = new System.Windows.Forms.CheckBox();
            Check_ShowSpeed = new System.Windows.Forms.CheckBox();
            Check_ShowAltitude = new System.Windows.Forms.CheckBox();
            Check_ShowCallsign = new System.Windows.Forms.CheckBox();
            Check_Scan = new System.Windows.Forms.CheckBox();
            Check_Elevation = new System.Windows.Forms.CheckBox();
            Label_CircleText = new System.Windows.Forms.Label();
            groupBox2 = new System.Windows.Forms.GroupBox();
            Label_Password = new System.Windows.Forms.Label();
            Check_WhazzupAI = new System.Windows.Forms.CheckBox();
            Check_GlobalJoin = new System.Windows.Forms.CheckBox();
            Check_WhazzupGlobal = new System.Windows.Forms.CheckBox();
            Check_Tacpack = new System.Windows.Forms.CheckBox();
            Check_Whazzup = new System.Windows.Forms.CheckBox();
            Text_Password = new System.Windows.Forms.TextBox();
            GroupBox_ATC = new System.Windows.Forms.GroupBox();
            Label_Frequency = new System.Windows.Forms.Label();
            Text_Frequency = new System.Windows.Forms.TextBox();
            Label_Level = new System.Windows.Forms.Label();
            Combo_Level = new System.Windows.Forms.ComboBox();
            Check_Euroscope = new System.Windows.Forms.CheckBox();
            Text_Airport = new System.Windows.Forms.TextBox();
            Label_Airport = new System.Windows.Forms.Label();
            Check_ATC = new System.Windows.Forms.CheckBox();
            GroupBox_Hub = new System.Windows.Forms.GroupBox();
            Text_HubDomain = new System.Windows.Forms.TextBox();
            Label_HubDomain = new System.Windows.Forms.Label();
            Text_HubEvent = new System.Windows.Forms.TextBox();
            Label_HubEvent = new System.Windows.Forms.Label();
            Text_HubVoIP = new System.Windows.Forms.TextBox();
            Text_HubAbout = new System.Windows.Forms.TextBox();
            Label_HubAbout = new System.Windows.Forms.Label();
            Label_HubVoIP = new System.Windows.Forms.Label();
            Label_HubName = new System.Windows.Forms.Label();
            Text_HubName = new System.Windows.Forms.TextBox();
            Check_Hub = new System.Windows.Forms.CheckBox();
            Check_AlwaysOnTop = new System.Windows.Forms.CheckBox();
            groupBox5 = new System.Windows.Forms.GroupBox();
            Check_EarlyUpdate = new System.Windows.Forms.CheckBox();
            Check_ToolTips = new System.Windows.Forms.CheckBox();
            Label_Inactive = new System.Windows.Forms.Label();
            Label_Waiting = new System.Windows.Forms.Label();
            Button_InactiveText = new System.Windows.Forms.Button();
            Button_WaitingText = new System.Windows.Forms.Button();
            Button_ActiveText = new System.Windows.Forms.Button();
            Button_InactiveBackground = new System.Windows.Forms.Button();
            Button_WaitingBackground = new System.Windows.Forms.Button();
            Button_ActiveBackground = new System.Windows.Forms.Button();
            Label_Active = new System.Windows.Forms.Label();
            Check_AutoRefresh = new System.Windows.Forms.CheckBox();
            GroupBox_Xplane = new System.Windows.Forms.GroupBox();
            Check_TCAS = new System.Windows.Forms.CheckBox();
            Button_InstallCPP = new System.Windows.Forms.Button();
            Text_PluginAddress = new System.Windows.Forms.TextBox();
            Label_PluginAddress = new System.Windows.Forms.Label();
            Button_InstallPlugin = new System.Windows.Forms.Button();
            Button_Reset = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)Track_Follow).BeginInit();
            ((System.ComponentModel.ISupportInitialize)Track_Circle).BeginInit();
            GroupBox_Simulator.SuspendLayout();
            groupBox2.SuspendLayout();
            GroupBox_ATC.SuspendLayout();
            GroupBox_Hub.SuspendLayout();
            groupBox5.SuspendLayout();
            GroupBox_Xplane.SuspendLayout();
            SuspendLayout();
            // 
            // Button_OK
            // 
            Button_OK.DialogResult = System.Windows.Forms.DialogResult.OK;
            resources.ApplyResources(Button_OK, "Button_OK");
            Button_OK.Name = "Button_OK";
            Button_OK.UseVisualStyleBackColor = true;
            Button_OK.Click += Button_OK_Click;
            // 
            // Check_LocalPort
            // 
            resources.ApplyResources(Check_LocalPort, "Check_LocalPort");
            Check_LocalPort.Name = "Check_LocalPort";
            Check_LocalPort.UseVisualStyleBackColor = true;
            Check_LocalPort.CheckedChanged += Check_LocalPort_CheckedChanged;
            // 
            // Text_LocalPort
            // 
            resources.ApplyResources(Text_LocalPort, "Text_LocalPort");
            Text_LocalPort.Name = "Text_LocalPort";
            // 
            // Button_Cancel
            // 
            Button_Cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            resources.ApplyResources(Button_Cancel, "Button_Cancel");
            Button_Cancel.Name = "Button_Cancel";
            Button_Cancel.UseVisualStyleBackColor = true;
            // 
            // Track_Follow
            // 
            resources.ApplyResources(Track_Follow, "Track_Follow");
            Track_Follow.Maximum = 1000;
            Track_Follow.Minimum = 20;
            Track_Follow.Name = "Track_Follow";
            Track_Follow.TickFrequency = 10;
            Track_Follow.TickStyle = System.Windows.Forms.TickStyle.TopLeft;
            Track_Follow.Value = 20;
            Track_Follow.ValueChanged += Track_Follow_ValueChanged;
            // 
            // Label_Follow
            // 
            resources.ApplyResources(Label_Follow, "Label_Follow");
            Label_Follow.Name = "Label_Follow";
            // 
            // Check_LowBandwidth
            // 
            resources.ApplyResources(Check_LowBandwidth, "Check_LowBandwidth");
            Check_LowBandwidth.Name = "Check_LowBandwidth";
            Check_LowBandwidth.UseVisualStyleBackColor = true;
            // 
            // Label_Nickname
            // 
            resources.ApplyResources(Label_Nickname, "Label_Nickname");
            Label_Nickname.Name = "Label_Nickname";
            // 
            // Text_Nickname
            // 
            resources.ApplyResources(Text_Nickname, "Text_Nickname");
            Text_Nickname.Name = "Text_Nickname";
            // 
            // Check_ShowNickname
            // 
            resources.ApplyResources(Check_ShowNickname, "Check_ShowNickname");
            Check_ShowNickname.Name = "Check_ShowNickname";
            Check_ShowNickname.UseVisualStyleBackColor = true;
            // 
            // Label_Circle
            // 
            resources.ApplyResources(Label_Circle, "Label_Circle");
            Label_Circle.Name = "Label_Circle";
            // 
            // Track_Circle
            // 
            resources.ApplyResources(Track_Circle, "Track_Circle");
            Track_Circle.Maximum = 600;
            Track_Circle.Minimum = 2;
            Track_Circle.Name = "Track_Circle";
            Track_Circle.TickStyle = System.Windows.Forms.TickStyle.TopLeft;
            Track_Circle.Value = 40;
            Track_Circle.ValueChanged += Track_Circle_ValueChanged;
            // 
            // GroupBox_Simulator
            // 
            GroupBox_Simulator.Controls.Add(Check_UseAIFeatures);
            GroupBox_Simulator.Controls.Add(Label_FollowText);
            GroupBox_Simulator.Controls.Add(Check_Connect);
            GroupBox_Simulator.Controls.Add(Button_LabelColour);
            GroupBox_Simulator.Controls.Add(Label_LabelColour);
            GroupBox_Simulator.Controls.Add(Check_ShowDistance);
            GroupBox_Simulator.Controls.Add(Check_ShowSpeed);
            GroupBox_Simulator.Controls.Add(Check_ShowAltitude);
            GroupBox_Simulator.Controls.Add(Check_ShowCallsign);
            GroupBox_Simulator.Controls.Add(Check_Scan);
            GroupBox_Simulator.Controls.Add(Check_Elevation);
            GroupBox_Simulator.Controls.Add(Label_CircleText);
            GroupBox_Simulator.Controls.Add(Label_Follow);
            GroupBox_Simulator.Controls.Add(Label_Circle);
            GroupBox_Simulator.Controls.Add(Text_Nickname);
            GroupBox_Simulator.Controls.Add(Label_Nickname);
            GroupBox_Simulator.Controls.Add(Check_ShowNickname);
            GroupBox_Simulator.Controls.Add(Track_Circle);
            GroupBox_Simulator.Controls.Add(Track_Follow);
            resources.ApplyResources(GroupBox_Simulator, "GroupBox_Simulator");
            GroupBox_Simulator.Name = "GroupBox_Simulator";
            GroupBox_Simulator.TabStop = false;
            // 
            // Check_UseAIFeatures
            // 
            resources.ApplyResources(Check_UseAIFeatures, "Check_UseAIFeatures");
            Check_UseAIFeatures.Name = "Check_UseAIFeatures";
            Check_UseAIFeatures.UseVisualStyleBackColor = true;
            // 
            // Label_FollowText
            // 
            resources.ApplyResources(Label_FollowText, "Label_FollowText");
            Label_FollowText.Name = "Label_FollowText";
            // 
            // Check_Connect
            // 
            resources.ApplyResources(Check_Connect, "Check_Connect");
            Check_Connect.Name = "Check_Connect";
            Check_Connect.UseVisualStyleBackColor = true;
            // 
            // Button_LabelColour
            // 
            resources.ApplyResources(Button_LabelColour, "Button_LabelColour");
            Button_LabelColour.Name = "Button_LabelColour";
            Button_LabelColour.UseVisualStyleBackColor = true;
            Button_LabelColour.Click += Button_LabelColour_Click;
            // 
            // Label_LabelColour
            // 
            Label_LabelColour.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            resources.ApplyResources(Label_LabelColour, "Label_LabelColour");
            Label_LabelColour.Name = "Label_LabelColour";
            // 
            // Check_ShowDistance
            // 
            resources.ApplyResources(Check_ShowDistance, "Check_ShowDistance");
            Check_ShowDistance.Name = "Check_ShowDistance";
            Check_ShowDistance.UseVisualStyleBackColor = true;
            // 
            // Check_ShowSpeed
            // 
            resources.ApplyResources(Check_ShowSpeed, "Check_ShowSpeed");
            Check_ShowSpeed.Name = "Check_ShowSpeed";
            Check_ShowSpeed.UseVisualStyleBackColor = true;
            // 
            // Check_ShowAltitude
            // 
            resources.ApplyResources(Check_ShowAltitude, "Check_ShowAltitude");
            Check_ShowAltitude.Name = "Check_ShowAltitude";
            Check_ShowAltitude.UseVisualStyleBackColor = true;
            // 
            // Check_ShowCallsign
            // 
            resources.ApplyResources(Check_ShowCallsign, "Check_ShowCallsign");
            Check_ShowCallsign.Name = "Check_ShowCallsign";
            Check_ShowCallsign.UseVisualStyleBackColor = true;
            // 
            // Check_Scan
            // 
            resources.ApplyResources(Check_Scan, "Check_Scan");
            Check_Scan.Name = "Check_Scan";
            Check_Scan.UseVisualStyleBackColor = true;
            // 
            // Check_Elevation
            // 
            resources.ApplyResources(Check_Elevation, "Check_Elevation");
            Check_Elevation.Name = "Check_Elevation";
            Check_Elevation.UseVisualStyleBackColor = true;
            // 
            // Label_CircleText
            // 
            resources.ApplyResources(Label_CircleText, "Label_CircleText");
            Label_CircleText.Name = "Label_CircleText";
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(Label_Password);
            groupBox2.Controls.Add(Check_WhazzupAI);
            groupBox2.Controls.Add(Check_GlobalJoin);
            groupBox2.Controls.Add(Check_WhazzupGlobal);
            groupBox2.Controls.Add(Check_Tacpack);
            groupBox2.Controls.Add(Check_Whazzup);
            groupBox2.Controls.Add(Check_LocalPort);
            groupBox2.Controls.Add(Text_Password);
            groupBox2.Controls.Add(Check_LowBandwidth);
            groupBox2.Controls.Add(Text_LocalPort);
            resources.ApplyResources(groupBox2, "groupBox2");
            groupBox2.Name = "groupBox2";
            groupBox2.TabStop = false;
            // 
            // Label_Password
            // 
            resources.ApplyResources(Label_Password, "Label_Password");
            Label_Password.Name = "Label_Password";
            // 
            // Check_WhazzupAI
            // 
            resources.ApplyResources(Check_WhazzupAI, "Check_WhazzupAI");
            Check_WhazzupAI.Name = "Check_WhazzupAI";
            Check_WhazzupAI.UseVisualStyleBackColor = true;
            // 
            // Check_GlobalJoin
            // 
            resources.ApplyResources(Check_GlobalJoin, "Check_GlobalJoin");
            Check_GlobalJoin.Name = "Check_GlobalJoin";
            Check_GlobalJoin.UseVisualStyleBackColor = true;
            // 
            // Check_WhazzupGlobal
            // 
            resources.ApplyResources(Check_WhazzupGlobal, "Check_WhazzupGlobal");
            Check_WhazzupGlobal.Name = "Check_WhazzupGlobal";
            Check_WhazzupGlobal.UseVisualStyleBackColor = true;
            // 
            // Check_Tacpack
            // 
            resources.ApplyResources(Check_Tacpack, "Check_Tacpack");
            Check_Tacpack.Name = "Check_Tacpack";
            Check_Tacpack.UseVisualStyleBackColor = true;
            // 
            // Check_Whazzup
            // 
            resources.ApplyResources(Check_Whazzup, "Check_Whazzup");
            Check_Whazzup.Name = "Check_Whazzup";
            Check_Whazzup.UseVisualStyleBackColor = true;
            Check_Whazzup.CheckedChanged += Check_Whazzup_CheckedChanged;
            // 
            // Text_Password
            // 
            resources.ApplyResources(Text_Password, "Text_Password");
            Text_Password.Name = "Text_Password";
            // 
            // GroupBox_ATC
            // 
            GroupBox_ATC.Controls.Add(Label_Frequency);
            GroupBox_ATC.Controls.Add(Text_Frequency);
            GroupBox_ATC.Controls.Add(Label_Level);
            GroupBox_ATC.Controls.Add(Combo_Level);
            GroupBox_ATC.Controls.Add(Check_Euroscope);
            GroupBox_ATC.Controls.Add(Text_Airport);
            GroupBox_ATC.Controls.Add(Label_Airport);
            GroupBox_ATC.Controls.Add(Check_ATC);
            resources.ApplyResources(GroupBox_ATC, "GroupBox_ATC");
            GroupBox_ATC.Name = "GroupBox_ATC";
            GroupBox_ATC.TabStop = false;
            // 
            // Label_Frequency
            // 
            resources.ApplyResources(Label_Frequency, "Label_Frequency");
            Label_Frequency.Name = "Label_Frequency";
            // 
            // Text_Frequency
            // 
            resources.ApplyResources(Text_Frequency, "Text_Frequency");
            Text_Frequency.Name = "Text_Frequency";
            // 
            // Label_Level
            // 
            resources.ApplyResources(Label_Level, "Label_Level");
            Label_Level.Name = "Label_Level";
            // 
            // Combo_Level
            // 
            Combo_Level.FormattingEnabled = true;
            Combo_Level.Items.AddRange(new object[] { resources.GetString("Combo_Level.Items"), resources.GetString("Combo_Level.Items1"), resources.GetString("Combo_Level.Items2"), resources.GetString("Combo_Level.Items3"), resources.GetString("Combo_Level.Items4") });
            resources.ApplyResources(Combo_Level, "Combo_Level");
            Combo_Level.Name = "Combo_Level";
            // 
            // Check_Euroscope
            // 
            resources.ApplyResources(Check_Euroscope, "Check_Euroscope");
            Check_Euroscope.Name = "Check_Euroscope";
            Check_Euroscope.UseVisualStyleBackColor = true;
            // 
            // Text_Airport
            // 
            Text_Airport.BackColor = System.Drawing.Color.White;
            resources.ApplyResources(Text_Airport, "Text_Airport");
            Text_Airport.Name = "Text_Airport";
            Text_Airport.TextChanged += Text_Airport_TextChanged;
            // 
            // Label_Airport
            // 
            resources.ApplyResources(Label_Airport, "Label_Airport");
            Label_Airport.Name = "Label_Airport";
            // 
            // Check_ATC
            // 
            resources.ApplyResources(Check_ATC, "Check_ATC");
            Check_ATC.Name = "Check_ATC";
            Check_ATC.UseVisualStyleBackColor = true;
            Check_ATC.CheckedChanged += Check_ATC_CheckedChanged;
            // 
            // GroupBox_Hub
            // 
            GroupBox_Hub.Controls.Add(Text_HubDomain);
            GroupBox_Hub.Controls.Add(Label_HubDomain);
            GroupBox_Hub.Controls.Add(Text_HubEvent);
            GroupBox_Hub.Controls.Add(Label_HubEvent);
            GroupBox_Hub.Controls.Add(Text_HubVoIP);
            GroupBox_Hub.Controls.Add(Text_HubAbout);
            GroupBox_Hub.Controls.Add(Label_HubAbout);
            GroupBox_Hub.Controls.Add(Label_HubVoIP);
            GroupBox_Hub.Controls.Add(Label_HubName);
            GroupBox_Hub.Controls.Add(Text_HubName);
            GroupBox_Hub.Controls.Add(Check_Hub);
            resources.ApplyResources(GroupBox_Hub, "GroupBox_Hub");
            GroupBox_Hub.Name = "GroupBox_Hub";
            GroupBox_Hub.TabStop = false;
            // 
            // Text_HubDomain
            // 
            resources.ApplyResources(Text_HubDomain, "Text_HubDomain");
            Text_HubDomain.Name = "Text_HubDomain";
            // 
            // Label_HubDomain
            // 
            resources.ApplyResources(Label_HubDomain, "Label_HubDomain");
            Label_HubDomain.Name = "Label_HubDomain";
            // 
            // Text_HubEvent
            // 
            resources.ApplyResources(Text_HubEvent, "Text_HubEvent");
            Text_HubEvent.Name = "Text_HubEvent";
            // 
            // Label_HubEvent
            // 
            resources.ApplyResources(Label_HubEvent, "Label_HubEvent");
            Label_HubEvent.Name = "Label_HubEvent";
            // 
            // Text_HubVoIP
            // 
            resources.ApplyResources(Text_HubVoIP, "Text_HubVoIP");
            Text_HubVoIP.Name = "Text_HubVoIP";
            // 
            // Text_HubAbout
            // 
            resources.ApplyResources(Text_HubAbout, "Text_HubAbout");
            Text_HubAbout.Name = "Text_HubAbout";
            // 
            // Label_HubAbout
            // 
            resources.ApplyResources(Label_HubAbout, "Label_HubAbout");
            Label_HubAbout.Name = "Label_HubAbout";
            // 
            // Label_HubVoIP
            // 
            resources.ApplyResources(Label_HubVoIP, "Label_HubVoIP");
            Label_HubVoIP.Name = "Label_HubVoIP";
            // 
            // Label_HubName
            // 
            resources.ApplyResources(Label_HubName, "Label_HubName");
            Label_HubName.Name = "Label_HubName";
            // 
            // Text_HubName
            // 
            resources.ApplyResources(Text_HubName, "Text_HubName");
            Text_HubName.Name = "Text_HubName";
            // 
            // Check_Hub
            // 
            resources.ApplyResources(Check_Hub, "Check_Hub");
            Check_Hub.Name = "Check_Hub";
            Check_Hub.UseVisualStyleBackColor = true;
            Check_Hub.CheckedChanged += Check_Hub_CheckedChanged;
            // 
            // Check_AlwaysOnTop
            // 
            resources.ApplyResources(Check_AlwaysOnTop, "Check_AlwaysOnTop");
            Check_AlwaysOnTop.Name = "Check_AlwaysOnTop";
            Check_AlwaysOnTop.UseVisualStyleBackColor = true;
            // 
            // groupBox5
            // 
            groupBox5.Controls.Add(Check_EarlyUpdate);
            groupBox5.Controls.Add(Check_ToolTips);
            groupBox5.Controls.Add(Label_Inactive);
            groupBox5.Controls.Add(Label_Waiting);
            groupBox5.Controls.Add(Button_InactiveText);
            groupBox5.Controls.Add(Button_WaitingText);
            groupBox5.Controls.Add(Button_ActiveText);
            groupBox5.Controls.Add(Button_InactiveBackground);
            groupBox5.Controls.Add(Button_WaitingBackground);
            groupBox5.Controls.Add(Button_ActiveBackground);
            groupBox5.Controls.Add(Label_Active);
            groupBox5.Controls.Add(Check_AutoRefresh);
            groupBox5.Controls.Add(Check_AlwaysOnTop);
            resources.ApplyResources(groupBox5, "groupBox5");
            groupBox5.Name = "groupBox5";
            groupBox5.TabStop = false;
            // 
            // Check_EarlyUpdate
            // 
            resources.ApplyResources(Check_EarlyUpdate, "Check_EarlyUpdate");
            Check_EarlyUpdate.Name = "Check_EarlyUpdate";
            Check_EarlyUpdate.UseVisualStyleBackColor = true;
            Check_EarlyUpdate.CheckedChanged += Check_EarlyUpdate_CheckedChanged;
            // 
            // Check_ToolTips
            // 
            resources.ApplyResources(Check_ToolTips, "Check_ToolTips");
            Check_ToolTips.Name = "Check_ToolTips";
            Check_ToolTips.UseVisualStyleBackColor = true;
            // 
            // Label_Inactive
            // 
            Label_Inactive.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            resources.ApplyResources(Label_Inactive, "Label_Inactive");
            Label_Inactive.Name = "Label_Inactive";
            // 
            // Label_Waiting
            // 
            Label_Waiting.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            resources.ApplyResources(Label_Waiting, "Label_Waiting");
            Label_Waiting.Name = "Label_Waiting";
            // 
            // Button_InactiveText
            // 
            resources.ApplyResources(Button_InactiveText, "Button_InactiveText");
            Button_InactiveText.Name = "Button_InactiveText";
            Button_InactiveText.UseVisualStyleBackColor = true;
            Button_InactiveText.Click += Button_InactiveText_Click;
            // 
            // Button_WaitingText
            // 
            resources.ApplyResources(Button_WaitingText, "Button_WaitingText");
            Button_WaitingText.Name = "Button_WaitingText";
            Button_WaitingText.UseVisualStyleBackColor = true;
            Button_WaitingText.Click += Button_WaitingText_Click;
            // 
            // Button_ActiveText
            // 
            resources.ApplyResources(Button_ActiveText, "Button_ActiveText");
            Button_ActiveText.Name = "Button_ActiveText";
            Button_ActiveText.UseVisualStyleBackColor = true;
            Button_ActiveText.Click += Button_ActiveText_Click;
            // 
            // Button_InactiveBackground
            // 
            resources.ApplyResources(Button_InactiveBackground, "Button_InactiveBackground");
            Button_InactiveBackground.Name = "Button_InactiveBackground";
            Button_InactiveBackground.UseVisualStyleBackColor = true;
            Button_InactiveBackground.Click += Button_InactiveColour_Click;
            // 
            // Button_WaitingBackground
            // 
            resources.ApplyResources(Button_WaitingBackground, "Button_WaitingBackground");
            Button_WaitingBackground.Name = "Button_WaitingBackground";
            Button_WaitingBackground.UseVisualStyleBackColor = true;
            Button_WaitingBackground.Click += Button_WaitingBackground_Click;
            // 
            // Button_ActiveBackground
            // 
            resources.ApplyResources(Button_ActiveBackground, "Button_ActiveBackground");
            Button_ActiveBackground.Name = "Button_ActiveBackground";
            Button_ActiveBackground.UseVisualStyleBackColor = true;
            Button_ActiveBackground.Click += Button_ActiveBackground_Click;
            // 
            // Label_Active
            // 
            Label_Active.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            resources.ApplyResources(Label_Active, "Label_Active");
            Label_Active.Name = "Label_Active";
            // 
            // Check_AutoRefresh
            // 
            resources.ApplyResources(Check_AutoRefresh, "Check_AutoRefresh");
            Check_AutoRefresh.Name = "Check_AutoRefresh";
            Check_AutoRefresh.UseVisualStyleBackColor = true;
            Check_AutoRefresh.CheckedChanged += Check_AutoRefresh_CheckedChanged;
            // 
            // GroupBox_Xplane
            // 
            GroupBox_Xplane.Controls.Add(Check_TCAS);
            GroupBox_Xplane.Controls.Add(Button_InstallCPP);
            GroupBox_Xplane.Controls.Add(Text_PluginAddress);
            GroupBox_Xplane.Controls.Add(Label_PluginAddress);
            GroupBox_Xplane.Controls.Add(Button_InstallPlugin);
            resources.ApplyResources(GroupBox_Xplane, "GroupBox_Xplane");
            GroupBox_Xplane.Name = "GroupBox_Xplane";
            GroupBox_Xplane.TabStop = false;
            // 
            // Check_TCAS
            // 
            resources.ApplyResources(Check_TCAS, "Check_TCAS");
            Check_TCAS.Name = "Check_TCAS";
            Check_TCAS.UseVisualStyleBackColor = true;
            // 
            // Button_InstallCPP
            // 
            resources.ApplyResources(Button_InstallCPP, "Button_InstallCPP");
            Button_InstallCPP.Name = "Button_InstallCPP";
            Button_InstallCPP.UseVisualStyleBackColor = true;
            Button_InstallCPP.Click += Button_InstallCPP_Click;
            // 
            // Text_PluginAddress
            // 
            resources.ApplyResources(Text_PluginAddress, "Text_PluginAddress");
            Text_PluginAddress.Name = "Text_PluginAddress";
            // 
            // Label_PluginAddress
            // 
            resources.ApplyResources(Label_PluginAddress, "Label_PluginAddress");
            Label_PluginAddress.Name = "Label_PluginAddress";
            // 
            // Button_InstallPlugin
            // 
            resources.ApplyResources(Button_InstallPlugin, "Button_InstallPlugin");
            Button_InstallPlugin.Name = "Button_InstallPlugin";
            Button_InstallPlugin.UseVisualStyleBackColor = true;
            Button_InstallPlugin.Click += Button_InstallPlugin_Click;
            // 
            // Button_Reset
            // 
            resources.ApplyResources(Button_Reset, "Button_Reset");
            Button_Reset.Name = "Button_Reset";
            Button_Reset.UseVisualStyleBackColor = true;
            Button_Reset.Click += Button_Reset_Click;
            // 
            // SettingsForm
            // 
            AcceptButton = Button_OK;
            resources.ApplyResources(this, "$this");
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = Button_Cancel;
            Controls.Add(Button_Reset);
            Controls.Add(GroupBox_Xplane);
            Controls.Add(groupBox5);
            Controls.Add(GroupBox_ATC);
            Controls.Add(Button_Cancel);
            Controls.Add(Button_OK);
            Controls.Add(groupBox2);
            Controls.Add(GroupBox_Hub);
            Controls.Add(GroupBox_Simulator);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Name = "SettingsForm";
            Load += SettingsForm_Load;
            ((System.ComponentModel.ISupportInitialize)Track_Follow).EndInit();
            ((System.ComponentModel.ISupportInitialize)Track_Circle).EndInit();
            GroupBox_Simulator.ResumeLayout(false);
            GroupBox_Simulator.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            GroupBox_ATC.ResumeLayout(false);
            GroupBox_ATC.PerformLayout();
            GroupBox_Hub.ResumeLayout(false);
            GroupBox_Hub.PerformLayout();
            groupBox5.ResumeLayout(false);
            groupBox5.PerformLayout();
            GroupBox_Xplane.ResumeLayout(false);
            GroupBox_Xplane.PerformLayout();
            ResumeLayout(false);

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
        private System.Windows.Forms.CheckBox Check_UseAIFeatures;
    }
}