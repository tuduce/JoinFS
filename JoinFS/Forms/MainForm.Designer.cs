namespace JoinFS
{
    partial class MainForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.Button_Create = new System.Windows.Forms.Button();
            this.StatusStrip_Main = new System.Windows.Forms.StatusStrip();
            this.Tool_Version = new System.Windows.Forms.ToolStripStatusLabel();
            this.Tool_Map = new System.Windows.Forms.ToolStripStatusLabel();
            this.Tool_Users = new System.Windows.Forms.ToolStripStatusLabel();
            this.Tool_Update = new System.Windows.Forms.ToolStripStatusLabel();
            this.Text_MyIP = new System.Windows.Forms.TextBox();
            this.Combo_Join = new System.Windows.Forms.ComboBox();
            this.Button_Simulator = new System.Windows.Forms.Button();
            this.Button_Network = new System.Windows.Forms.Button();
            this.Button_Join = new System.Windows.Forms.Button();
            this.Button_Global = new System.Windows.Forms.Button();
            this.Main_Menu = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_File_ScanModels = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_File_ModelMatching = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_File_Variables = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_File_Recorder = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripSeparator();
            this.Menu_File_AddressBook = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_File_Shortcuts = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_File_Options = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem3 = new System.Windows.Forms.ToolStripSeparator();
            this.Menu_File_SimComX = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem4 = new System.Windows.Forms.ToolStripSeparator();
            this.Menu_File_Settings = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem5 = new System.Windows.Forms.ToolStripSeparator();
            this.Menu_File_Exit = new System.Windows.Forms.ToolStripMenuItem();
            this.viewToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_View_Hubs = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_View_Atc = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_View_Session = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_View_Aircraft = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_View_Objects = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_View_Monitor = new System.Windows.Forms.ToolStripMenuItem();
            this.helpToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_Help_Support = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_Help_Manual = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_Help_Radar = new System.Windows.Forms.ToolStripMenuItem();
            this.Menu_Help_License = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem6 = new System.Windows.Forms.ToolStripSeparator();
            this.Menu_Help_About = new System.Windows.Forms.ToolStripMenuItem();
            this.StatusStrip_Main.SuspendLayout();
            this.Main_Menu.SuspendLayout();
            this.SuspendLayout();
            // 
            // Button_Create
            // 
            resources.ApplyResources(this.Button_Create, "Button_Create");
            this.Button_Create.Name = "Button_Create";
            this.Button_Create.UseVisualStyleBackColor = true;
            this.Button_Create.Click += new System.EventHandler(this.Button_Create_Click);
            // 
            // StatusStrip_Main
            // 
            resources.ApplyResources(this.StatusStrip_Main, "StatusStrip_Main");
            this.StatusStrip_Main.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Tool_Version,
            this.Tool_Map,
            this.Tool_Users,
            this.Tool_Update});
            this.StatusStrip_Main.Name = "StatusStrip_Main";
            this.StatusStrip_Main.SizingGrip = false;
            // 
            // Tool_Version
            // 
            resources.ApplyResources(this.Tool_Version, "Tool_Version");
            this.Tool_Version.ActiveLinkColor = System.Drawing.Color.Blue;
            this.Tool_Version.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.Tool_Version.ForeColor = System.Drawing.Color.Gray;
            this.Tool_Version.LinkBehavior = System.Windows.Forms.LinkBehavior.HoverUnderline;
            this.Tool_Version.LinkColor = System.Drawing.Color.DodgerBlue;
            this.Tool_Version.Name = "Tool_Version";
            // 
            // Tool_Map
            // 
            resources.ApplyResources(this.Tool_Map, "Tool_Map");
            this.Tool_Map.ActiveLinkColor = System.Drawing.Color.Blue;
            this.Tool_Map.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left;
            this.Tool_Map.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.Tool_Map.ForeColor = System.Drawing.Color.DodgerBlue;
            this.Tool_Map.IsLink = true;
            this.Tool_Map.LinkBehavior = System.Windows.Forms.LinkBehavior.HoverUnderline;
            this.Tool_Map.LinkColor = System.Drawing.Color.DodgerBlue;
            this.Tool_Map.Name = "Tool_Map";
            this.Tool_Map.Click += new System.EventHandler(this.Tool_Map_Click);
            // 
            // Tool_Users
            // 
            resources.ApplyResources(this.Tool_Users, "Tool_Users");
            this.Tool_Users.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left;
            this.Tool_Users.ForeColor = System.Drawing.Color.Gray;
            this.Tool_Users.Name = "Tool_Users";
            // 
            // Tool_Update
            // 
            resources.ApplyResources(this.Tool_Update, "Tool_Update");
            this.Tool_Update.ActiveLinkColor = System.Drawing.Color.Blue;
            this.Tool_Update.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left;
            this.Tool_Update.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.Tool_Update.ForeColor = System.Drawing.Color.DodgerBlue;
            this.Tool_Update.IsLink = true;
            this.Tool_Update.LinkBehavior = System.Windows.Forms.LinkBehavior.HoverUnderline;
            this.Tool_Update.LinkColor = System.Drawing.Color.DodgerBlue;
            this.Tool_Update.Name = "Tool_Update";
            this.Tool_Update.Click += new System.EventHandler(this.Tool_Update_Click);
            // 
            // Text_MyIP
            // 
            resources.ApplyResources(this.Text_MyIP, "Text_MyIP");
            this.Text_MyIP.Name = "Text_MyIP";
            this.Text_MyIP.ReadOnly = true;
            this.Text_MyIP.Click += new System.EventHandler(this.Text_MyIP_Click);
            // 
            // Combo_Join
            // 
            resources.ApplyResources(this.Combo_Join, "Combo_Join");
            this.Combo_Join.DropDownWidth = 200;
            this.Combo_Join.FormattingEnabled = true;
            this.Combo_Join.Name = "Combo_Join";
            // 
            // Button_Simulator
            // 
            resources.ApplyResources(this.Button_Simulator, "Button_Simulator");
            this.Button_Simulator.BackColor = System.Drawing.SystemColors.Control;
            this.Button_Simulator.ForeColor = System.Drawing.SystemColors.ControlText;
            this.Button_Simulator.Name = "Button_Simulator";
            this.Button_Simulator.UseVisualStyleBackColor = false;
            this.Button_Simulator.Click += new System.EventHandler(this.Button_Simulator_Click);
            // 
            // Button_Network
            // 
            resources.ApplyResources(this.Button_Network, "Button_Network");
            this.Button_Network.BackColor = System.Drawing.SystemColors.Control;
            this.Button_Network.ForeColor = System.Drawing.SystemColors.ControlText;
            this.Button_Network.Name = "Button_Network";
            this.Button_Network.UseVisualStyleBackColor = false;
            this.Button_Network.Click += new System.EventHandler(this.Button_Network_Click);
            // 
            // Button_Join
            // 
            resources.ApplyResources(this.Button_Join, "Button_Join");
            this.Button_Join.Name = "Button_Join";
            this.Button_Join.UseVisualStyleBackColor = true;
            this.Button_Join.Click += new System.EventHandler(this.Button_Join_Click);
            // 
            // Button_Global
            // 
            resources.ApplyResources(this.Button_Global, "Button_Global");
            this.Button_Global.Name = "Button_Global";
            this.Button_Global.UseVisualStyleBackColor = true;
            this.Button_Global.Click += new System.EventHandler(this.Button_Global_Click);
            // 
            // Main_Menu
            // 
            resources.ApplyResources(this.Main_Menu, "Main_Menu");
            this.Main_Menu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem,
            this.viewToolStripMenuItem,
            this.helpToolStripMenuItem});
            this.Main_Menu.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.HorizontalStackWithOverflow;
            this.Main_Menu.Name = "Main_Menu";
            this.Main_Menu.RenderMode = System.Windows.Forms.ToolStripRenderMode.System;
            // 
            // fileToolStripMenuItem
            // 
            resources.ApplyResources(this.fileToolStripMenuItem, "fileToolStripMenuItem");
            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Menu_File_ScanModels,
            this.Menu_File_ModelMatching,
            this.Menu_File_Variables,
            this.Menu_File_Recorder,
            this.toolStripMenuItem1,
            this.Menu_File_AddressBook,
            this.Menu_File_Shortcuts,
            this.Menu_File_Options,
            this.toolStripMenuItem3,
            this.Menu_File_SimComX,
            this.toolStripMenuItem4,
            this.Menu_File_Settings,
            this.toolStripMenuItem5,
            this.Menu_File_Exit});
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            // 
            // Menu_File_ScanModels
            // 
            resources.ApplyResources(this.Menu_File_ScanModels, "Menu_File_ScanModels");
            this.Menu_File_ScanModels.Name = "Menu_File_ScanModels";
            this.Menu_File_ScanModels.Click += new System.EventHandler(this.Menu_File_ScanModels_Click);
            // 
            // Menu_File_ModelMatching
            // 
            resources.ApplyResources(this.Menu_File_ModelMatching, "Menu_File_ModelMatching");
            this.Menu_File_ModelMatching.Name = "Menu_File_ModelMatching";
            this.Menu_File_ModelMatching.Click += new System.EventHandler(this.Menu_File_ModelMatching_Click);
            // 
            // Menu_File_Variables
            // 
            resources.ApplyResources(this.Menu_File_Variables, "Menu_File_Variables");
            this.Menu_File_Variables.Name = "Menu_File_Variables";
            this.Menu_File_Variables.Click += new System.EventHandler(this.Menu_File_Variables_Click);
            // 
            // Menu_File_Recorder
            // 
            resources.ApplyResources(this.Menu_File_Recorder, "Menu_File_Recorder");
            this.Menu_File_Recorder.Name = "Menu_File_Recorder";
            this.Menu_File_Recorder.Click += new System.EventHandler(this.Menu_File_Recorder_Click);
            // 
            // toolStripMenuItem1
            // 
            resources.ApplyResources(this.toolStripMenuItem1, "toolStripMenuItem1");
            this.toolStripMenuItem1.Name = "toolStripMenuItem1";
            // 
            // Menu_File_AddressBook
            // 
            resources.ApplyResources(this.Menu_File_AddressBook, "Menu_File_AddressBook");
            this.Menu_File_AddressBook.Name = "Menu_File_AddressBook";
            this.Menu_File_AddressBook.Click += new System.EventHandler(this.Menu_File_AddressBook_Click);
            // 
            // Menu_File_Shortcuts
            // 
            resources.ApplyResources(this.Menu_File_Shortcuts, "Menu_File_Shortcuts");
            this.Menu_File_Shortcuts.Name = "Menu_File_Shortcuts";
            this.Menu_File_Shortcuts.Click += new System.EventHandler(this.Menu_File_Shortcuts_Click);
            // 
            // Menu_File_Options
            // 
            resources.ApplyResources(this.Menu_File_Options, "Menu_File_Options");
            this.Menu_File_Options.Name = "Menu_File_Options";
            this.Menu_File_Options.Click += new System.EventHandler(this.Menu_File_Options_Click);
            // 
            // toolStripMenuItem3
            // 
            resources.ApplyResources(this.toolStripMenuItem3, "toolStripMenuItem3");
            this.toolStripMenuItem3.Name = "toolStripMenuItem3";
            // 
            // Menu_File_SimComX
            // 
            resources.ApplyResources(this.Menu_File_SimComX, "Menu_File_SimComX");
            this.Menu_File_SimComX.Name = "Menu_File_SimComX";
            this.Menu_File_SimComX.Click += new System.EventHandler(this.Menu_File_SimComX_Click);
            // 
            // toolStripMenuItem4
            // 
            resources.ApplyResources(this.toolStripMenuItem4, "toolStripMenuItem4");
            this.toolStripMenuItem4.Name = "toolStripMenuItem4";
            // 
            // Menu_File_Settings
            // 
            resources.ApplyResources(this.Menu_File_Settings, "Menu_File_Settings");
            this.Menu_File_Settings.Name = "Menu_File_Settings";
            this.Menu_File_Settings.Click += new System.EventHandler(this.Menu_File_Settings_Click);
            // 
            // toolStripMenuItem5
            // 
            resources.ApplyResources(this.toolStripMenuItem5, "toolStripMenuItem5");
            this.toolStripMenuItem5.Name = "toolStripMenuItem5";
            // 
            // Menu_File_Exit
            // 
            resources.ApplyResources(this.Menu_File_Exit, "Menu_File_Exit");
            this.Menu_File_Exit.Name = "Menu_File_Exit";
            this.Menu_File_Exit.Click += new System.EventHandler(this.Menu_Exit_Click);
            // 
            // viewToolStripMenuItem
            // 
            resources.ApplyResources(this.viewToolStripMenuItem, "viewToolStripMenuItem");
            this.viewToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Menu_View_Hubs,
            this.Menu_View_Atc,
            this.Menu_View_Session,
            this.Menu_View_Aircraft,
            this.Menu_View_Objects,
            this.Menu_View_Monitor});
            this.viewToolStripMenuItem.Name = "viewToolStripMenuItem";
            // 
            // Menu_View_Hubs
            // 
            resources.ApplyResources(this.Menu_View_Hubs, "Menu_View_Hubs");
            this.Menu_View_Hubs.Name = "Menu_View_Hubs";
            this.Menu_View_Hubs.Click += new System.EventHandler(this.Menu_View_Hubs_Click);
            // 
            // Menu_View_Atc
            // 
            resources.ApplyResources(this.Menu_View_Atc, "Menu_View_Atc");
            this.Menu_View_Atc.Name = "Menu_View_Atc";
            this.Menu_View_Atc.Click += new System.EventHandler(this.Menu_View_Atc_Click);
            // 
            // Menu_View_Session
            // 
            resources.ApplyResources(this.Menu_View_Session, "Menu_View_Session");
            this.Menu_View_Session.Name = "Menu_View_Session";
            this.Menu_View_Session.Click += new System.EventHandler(this.Menu_View_Users_Click);
            // 
            // Menu_View_Aircraft
            // 
            resources.ApplyResources(this.Menu_View_Aircraft, "Menu_View_Aircraft");
            this.Menu_View_Aircraft.Name = "Menu_View_Aircraft";
            this.Menu_View_Aircraft.Click += new System.EventHandler(this.Menu_View_Aircraft_Click);
            // 
            // Menu_View_Objects
            // 
            resources.ApplyResources(this.Menu_View_Objects, "Menu_View_Objects");
            this.Menu_View_Objects.Name = "Menu_View_Objects";
            this.Menu_View_Objects.Click += new System.EventHandler(this.Menu_View_Objects_Click);
            // 
            // Menu_View_Monitor
            // 
            resources.ApplyResources(this.Menu_View_Monitor, "Menu_View_Monitor");
            this.Menu_View_Monitor.Name = "Menu_View_Monitor";
            this.Menu_View_Monitor.Click += new System.EventHandler(this.Menu_View_Monitor_Click);
            // 
            // helpToolStripMenuItem
            // 
            resources.ApplyResources(this.helpToolStripMenuItem, "helpToolStripMenuItem");
            this.helpToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Menu_Help_Support,
            this.Menu_Help_Manual,
            this.Menu_Help_Radar,
            this.Menu_Help_License,
            this.toolStripMenuItem6,
            this.Menu_Help_About});
            this.helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            // 
            // Menu_Help_Support
            // 
            resources.ApplyResources(this.Menu_Help_Support, "Menu_Help_Support");
            this.Menu_Help_Support.Name = "Menu_Help_Support";
            this.Menu_Help_Support.Click += new System.EventHandler(this.Menu_Help_Support_Click);
            // 
            // Menu_Help_Manual
            // 
            resources.ApplyResources(this.Menu_Help_Manual, "Menu_Help_Manual");
            this.Menu_Help_Manual.Name = "Menu_Help_Manual";
            this.Menu_Help_Manual.Click += new System.EventHandler(this.Menu_Help_Manual_Click);
            // 
            // Menu_Help_Radar
            // 
            resources.ApplyResources(this.Menu_Help_Radar, "Menu_Help_Radar");
            this.Menu_Help_Radar.Name = "Menu_Help_Radar";
            this.Menu_Help_Radar.Click += new System.EventHandler(this.Menu_Help_Radar_Click);
            // 
            // Menu_Help_License
            // 
            resources.ApplyResources(this.Menu_Help_License, "Menu_Help_License");
            this.Menu_Help_License.Name = "Menu_Help_License";
            this.Menu_Help_License.Click += new System.EventHandler(this.Menu_Help_Licence_Click);
            // 
            // toolStripMenuItem6
            // 
            resources.ApplyResources(this.toolStripMenuItem6, "toolStripMenuItem6");
            this.toolStripMenuItem6.Name = "toolStripMenuItem6";
            // 
            // Menu_Help_About
            // 
            resources.ApplyResources(this.Menu_Help_About, "Menu_Help_About");
            this.Menu_Help_About.Name = "Menu_Help_About";
            this.Menu_Help_About.Click += new System.EventHandler(this.Menu_Help_About_Click);
            // 
            // MainForm
            // 
            this.AcceptButton = this.Button_Join;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoValidate = System.Windows.Forms.AutoValidate.EnablePreventFocusChange;
            this.Controls.Add(this.Button_Global);
            this.Controls.Add(this.Button_Join);
            this.Controls.Add(this.Button_Network);
            this.Controls.Add(this.Button_Simulator);
            this.Controls.Add(this.Combo_Join);
            this.Controls.Add(this.Text_MyIP);
            this.Controls.Add(this.StatusStrip_Main);
            this.Controls.Add(this.Main_Menu);
            this.Controls.Add(this.Button_Create);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.KeyPreview = true;
            this.MainMenuStrip = this.Main_Menu;
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.Activated += new System.EventHandler(this.MainForm_Activated);
            this.Deactivate += new System.EventHandler(this.MainForm_Deactivate);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.ResizeEnd += new System.EventHandler(this.MainForm_ResizeEnd);
            this.VisibleChanged += new System.EventHandler(this.MainForm_VisibleChanged);
            this.StatusStrip_Main.ResumeLayout(false);
            this.StatusStrip_Main.PerformLayout();
            this.Main_Menu.ResumeLayout(false);
            this.Main_Menu.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Button Button_Create;
        private System.Windows.Forms.StatusStrip StatusStrip_Main;
        private System.Windows.Forms.TextBox Text_MyIP;
        private System.Windows.Forms.ToolStripStatusLabel Tool_Version;
        private System.Windows.Forms.ToolStripStatusLabel Tool_Update;
        private System.Windows.Forms.ComboBox Combo_Join;
        private System.Windows.Forms.Button Button_Simulator;
        private System.Windows.Forms.Button Button_Network;
        private System.Windows.Forms.Button Button_Join;
        private System.Windows.Forms.ToolStripStatusLabel Tool_Map;
        private System.Windows.Forms.ToolStripStatusLabel Tool_Users;
        private System.Windows.Forms.Button Button_Global;
        private System.Windows.Forms.MenuStrip Main_Menu;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem Menu_File_ScanModels;
        private System.Windows.Forms.ToolStripMenuItem Menu_File_ModelMatching;
        private System.Windows.Forms.ToolStripMenuItem Menu_File_Variables;
        private System.Windows.Forms.ToolStripMenuItem Menu_File_Recorder;
        private System.Windows.Forms.ToolStripSeparator toolStripMenuItem1;
        private System.Windows.Forms.ToolStripMenuItem Menu_File_AddressBook;
        private System.Windows.Forms.ToolStripMenuItem Menu_File_Shortcuts;
        private System.Windows.Forms.ToolStripMenuItem Menu_File_Options;
        private System.Windows.Forms.ToolStripSeparator toolStripMenuItem3;
        private System.Windows.Forms.ToolStripMenuItem Menu_File_SimComX;
        private System.Windows.Forms.ToolStripSeparator toolStripMenuItem4;
        private System.Windows.Forms.ToolStripMenuItem Menu_File_Settings;
        private System.Windows.Forms.ToolStripSeparator toolStripMenuItem5;
        private System.Windows.Forms.ToolStripMenuItem Menu_File_Exit;
        private System.Windows.Forms.ToolStripMenuItem viewToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem Menu_View_Hubs;
        private System.Windows.Forms.ToolStripMenuItem Menu_View_Atc;
        private System.Windows.Forms.ToolStripMenuItem Menu_View_Session;
        private System.Windows.Forms.ToolStripMenuItem Menu_View_Aircraft;
        private System.Windows.Forms.ToolStripMenuItem Menu_View_Objects;
        private System.Windows.Forms.ToolStripMenuItem Menu_View_Monitor;
        private System.Windows.Forms.ToolStripMenuItem helpToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem Menu_Help_Support;
        private System.Windows.Forms.ToolStripMenuItem Menu_Help_Manual;
        private System.Windows.Forms.ToolStripMenuItem Menu_Help_Radar;
        private System.Windows.Forms.ToolStripMenuItem Menu_Help_License;
        private System.Windows.Forms.ToolStripSeparator toolStripMenuItem6;
        private System.Windows.Forms.ToolStripMenuItem Menu_Help_About;
    }
}

