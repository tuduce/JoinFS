namespace JoinFS
{
    partial class RecorderForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(RecorderForm));
            this.Track_Position = new System.Windows.Forms.TrackBar();
            this.Button_Play = new System.Windows.Forms.Button();
            this.Button_Record = new System.Windows.Forms.Button();
            this.Button_Stop = new System.Windows.Forms.Button();
            this.Button_Overdub = new System.Windows.Forms.Button();
            this.Label_Time = new System.Windows.Forms.Label();
            this.Label_EndTime = new System.Windows.Forms.Label();
            this.Recorder_Menu = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.RecorderMenu_File_OpenRecording = new System.Windows.Forms.ToolStripMenuItem();
            this.RecorderMenu_File_SaveRecording = new System.Windows.Forms.ToolStripMenuItem();
            this.RecorderMenu_File_AddRecording = new System.Windows.Forms.ToolStripMenuItem();
            this.editToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.RecorderMenu_Edit_TrimFromStart = new System.Windows.Forms.ToolStripMenuItem();
            this.RecorderMenu_Edit_TrimToEnd = new System.Windows.Forms.ToolStripMenuItem();
            this.Check_Loop = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.Track_Position)).BeginInit();
            this.Recorder_Menu.SuspendLayout();
            this.SuspendLayout();
            // 
            // Track_Position
            // 
            resources.ApplyResources(this.Track_Position, "Track_Position");
            this.Track_Position.Maximum = 0;
            this.Track_Position.Name = "Track_Position";
            this.Track_Position.TickFrequency = 60;
            this.Track_Position.TickStyle = System.Windows.Forms.TickStyle.Both;
            this.Track_Position.Scroll += new System.EventHandler(this.Track_Position_Scroll);
            // 
            // Button_Play
            // 
            resources.ApplyResources(this.Button_Play, "Button_Play");
            this.Button_Play.Image = global::JoinFS.Properties.Resources.play;
            this.Button_Play.Name = "Button_Play";
            this.Button_Play.UseVisualStyleBackColor = true;
            this.Button_Play.Click += new System.EventHandler(this.Button_Play_Click);
            // 
            // Button_Record
            // 
            resources.ApplyResources(this.Button_Record, "Button_Record");
            this.Button_Record.Image = global::JoinFS.Properties.Resources.record;
            this.Button_Record.Name = "Button_Record";
            this.Button_Record.UseVisualStyleBackColor = true;
            this.Button_Record.Click += new System.EventHandler(this.Button_Record_Click);
            // 
            // Button_Stop
            // 
            resources.ApplyResources(this.Button_Stop, "Button_Stop");
            this.Button_Stop.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.Button_Stop.Image = global::JoinFS.Properties.Resources.stop;
            this.Button_Stop.Name = "Button_Stop";
            this.Button_Stop.UseVisualStyleBackColor = true;
            this.Button_Stop.Click += new System.EventHandler(this.Button_Stop_Click);
            // 
            // Button_Overdub
            // 
            resources.ApplyResources(this.Button_Overdub, "Button_Overdub");
            this.Button_Overdub.Image = global::JoinFS.Properties.Resources.record;
            this.Button_Overdub.Name = "Button_Overdub";
            this.Button_Overdub.UseVisualStyleBackColor = true;
            this.Button_Overdub.Click += new System.EventHandler(this.Button_Overdub_Click);
            // 
            // Label_Time
            // 
            resources.ApplyResources(this.Label_Time, "Label_Time");
            this.Label_Time.Name = "Label_Time";
            // 
            // Label_EndTime
            // 
            resources.ApplyResources(this.Label_EndTime, "Label_EndTime");
            this.Label_EndTime.Name = "Label_EndTime";
            // 
            // Recorder_Menu
            // 
            resources.ApplyResources(this.Recorder_Menu, "Recorder_Menu");
            this.Recorder_Menu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem,
            this.editToolStripMenuItem});
            this.Recorder_Menu.Name = "Recorder_Menu";
            // 
            // fileToolStripMenuItem
            // 
            resources.ApplyResources(this.fileToolStripMenuItem, "fileToolStripMenuItem");
            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.RecorderMenu_File_OpenRecording,
            this.RecorderMenu_File_SaveRecording,
            this.RecorderMenu_File_AddRecording});
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            // 
            // RecorderMenu_File_OpenRecording
            // 
            resources.ApplyResources(this.RecorderMenu_File_OpenRecording, "RecorderMenu_File_OpenRecording");
            this.RecorderMenu_File_OpenRecording.Name = "RecorderMenu_File_OpenRecording";
            this.RecorderMenu_File_OpenRecording.Click += new System.EventHandler(this.RecorderMenu_File_OpenRecording_Click);
            // 
            // RecorderMenu_File_SaveRecording
            // 
            resources.ApplyResources(this.RecorderMenu_File_SaveRecording, "RecorderMenu_File_SaveRecording");
            this.RecorderMenu_File_SaveRecording.Name = "RecorderMenu_File_SaveRecording";
            this.RecorderMenu_File_SaveRecording.Click += new System.EventHandler(this.RecorderMenu_File_SaveRecording_Click);
            // 
            // RecorderMenu_File_AddRecording
            // 
            resources.ApplyResources(this.RecorderMenu_File_AddRecording, "RecorderMenu_File_AddRecording");
            this.RecorderMenu_File_AddRecording.Name = "RecorderMenu_File_AddRecording";
            this.RecorderMenu_File_AddRecording.Click += new System.EventHandler(this.RecorderMenu_File_AddRecording_Click);
            // 
            // editToolStripMenuItem
            // 
            resources.ApplyResources(this.editToolStripMenuItem, "editToolStripMenuItem");
            this.editToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.RecorderMenu_Edit_TrimFromStart,
            this.RecorderMenu_Edit_TrimToEnd});
            this.editToolStripMenuItem.Name = "editToolStripMenuItem";
            // 
            // RecorderMenu_Edit_TrimFromStart
            // 
            resources.ApplyResources(this.RecorderMenu_Edit_TrimFromStart, "RecorderMenu_Edit_TrimFromStart");
            this.RecorderMenu_Edit_TrimFromStart.Name = "RecorderMenu_Edit_TrimFromStart";
            this.RecorderMenu_Edit_TrimFromStart.Click += new System.EventHandler(this.RecorderMenu_Edit_TrimFromStart_Click);
            // 
            // RecorderMenu_Edit_TrimToEnd
            // 
            resources.ApplyResources(this.RecorderMenu_Edit_TrimToEnd, "RecorderMenu_Edit_TrimToEnd");
            this.RecorderMenu_Edit_TrimToEnd.Name = "RecorderMenu_Edit_TrimToEnd";
            this.RecorderMenu_Edit_TrimToEnd.Click += new System.EventHandler(this.RecorderMenu_Edit_TrimToEnd_Click);
            // 
            // Check_Loop
            // 
            resources.ApplyResources(this.Check_Loop, "Check_Loop");
            this.Check_Loop.Name = "Check_Loop";
            this.Check_Loop.UseVisualStyleBackColor = true;
            this.Check_Loop.Click += new System.EventHandler(this.Check_Loop_Click);
            // 
            // RecorderForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.Button_Stop;
            this.Controls.Add(this.Check_Loop);
            this.Controls.Add(this.Label_EndTime);
            this.Controls.Add(this.Label_Time);
            this.Controls.Add(this.Button_Overdub);
            this.Controls.Add(this.Button_Stop);
            this.Controls.Add(this.Button_Record);
            this.Controls.Add(this.Button_Play);
            this.Controls.Add(this.Track_Position);
            this.Controls.Add(this.Recorder_Menu);
            this.MainMenuStrip = this.Recorder_Menu;
            this.Name = "RecorderForm";
            this.Activated += new System.EventHandler(this.RecorderForm_Activated);
            this.Deactivate += new System.EventHandler(this.RecorderForm_Deactivate);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.RecorderForm_FormClosing);
            this.Load += new System.EventHandler(this.RecorderForm_Load);
            this.ResizeEnd += new System.EventHandler(this.RecorderForm_ResizeEnd);
            this.VisibleChanged += new System.EventHandler(this.RecorderForm_VisibleChanged);
            this.Resize += new System.EventHandler(this.RecorderForm_Resize);
            ((System.ComponentModel.ISupportInitialize)(this.Track_Position)).EndInit();
            this.Recorder_Menu.ResumeLayout(false);
            this.Recorder_Menu.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TrackBar Track_Position;
        private System.Windows.Forms.Button Button_Play;
        private System.Windows.Forms.Button Button_Record;
        private System.Windows.Forms.Button Button_Stop;
        private System.Windows.Forms.Button Button_Overdub;
        private System.Windows.Forms.Label Label_Time;
        private System.Windows.Forms.Label Label_EndTime;
        private System.Windows.Forms.MenuStrip Recorder_Menu;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem editToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem RecorderMenu_File_OpenRecording;
        private System.Windows.Forms.ToolStripMenuItem RecorderMenu_File_SaveRecording;
        private System.Windows.Forms.ToolStripMenuItem RecorderMenu_Edit_TrimFromStart;
        private System.Windows.Forms.ToolStripMenuItem RecorderMenu_Edit_TrimToEnd;
        private System.Windows.Forms.ToolStripMenuItem RecorderMenu_File_AddRecording;
        private System.Windows.Forms.CheckBox Check_Loop;
    }
}