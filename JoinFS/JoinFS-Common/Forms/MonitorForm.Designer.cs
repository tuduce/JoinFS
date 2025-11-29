namespace JoinFS
{
    partial class MonitorForm
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MonitorForm));
            this.Text_Events = new System.Windows.Forms.TextBox();
            this.Context_Monitor = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Context_Monitor_Node = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Monitor_Packet = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripSeparator();
            this.Context_Monitor_Network = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Monitor_Variables = new System.Windows.Forms.ToolStripMenuItem();
            this.Check_Save = new System.Windows.Forms.CheckBox();
            this.Button_ViewLogs = new System.Windows.Forms.Button();
            this.Label_FPS = new System.Windows.Forms.Label();
            this.Button_Refresh = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.Context_Monitor.SuspendLayout();
            this.SuspendLayout();
            // 
            // Text_Events
            // 
            resources.ApplyResources(this.Text_Events, "Text_Events");
            this.Text_Events.BackColor = System.Drawing.Color.Black;
            this.Text_Events.ContextMenuStrip = this.Context_Monitor;
            this.Text_Events.ForeColor = System.Drawing.Color.White;
            this.Text_Events.Name = "Text_Events";
            this.Text_Events.ReadOnly = true;
            // 
            // Context_Monitor
            // 
            resources.ApplyResources(this.Context_Monitor, "Context_Monitor");
            this.Context_Monitor.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Context_Monitor_Node,
            this.Context_Monitor_Packet,
            this.toolStripMenuItem1,
            this.Context_Monitor_Network,
            this.Context_Monitor_Variables});
            this.Context_Monitor.Name = "Context_Monitor";
            this.Context_Monitor.Opening += new System.ComponentModel.CancelEventHandler(this.Context_Monitor_Opening);
            // 
            // Context_Monitor_Node
            // 
            resources.ApplyResources(this.Context_Monitor_Node, "Context_Monitor_Node");
            this.Context_Monitor_Node.Name = "Context_Monitor_Node";
            this.Context_Monitor_Node.Click += new System.EventHandler(this.Context_Monitor_Node_Click);
            // 
            // Context_Monitor_Packet
            // 
            resources.ApplyResources(this.Context_Monitor_Packet, "Context_Monitor_Packet");
            this.Context_Monitor_Packet.Name = "Context_Monitor_Packet";
            this.Context_Monitor_Packet.Click += new System.EventHandler(this.Context_Monitor_Packet_Click);
            // 
            // toolStripMenuItem1
            // 
            resources.ApplyResources(this.toolStripMenuItem1, "toolStripMenuItem1");
            this.toolStripMenuItem1.Name = "toolStripMenuItem1";
            // 
            // Context_Monitor_Network
            // 
            resources.ApplyResources(this.Context_Monitor_Network, "Context_Monitor_Network");
            this.Context_Monitor_Network.Name = "Context_Monitor_Network";
            this.Context_Monitor_Network.Click += new System.EventHandler(this.Context_Monitor_Network_Click);
            // 
            // Context_Monitor_Variables
            // 
            resources.ApplyResources(this.Context_Monitor_Variables, "Context_Monitor_Variables");
            this.Context_Monitor_Variables.Name = "Context_Monitor_Variables";
            this.Context_Monitor_Variables.Click += new System.EventHandler(this.Context_Monitor_Variables_Click);
            // 
            // Check_Save
            // 
            resources.ApplyResources(this.Check_Save, "Check_Save");
            this.Check_Save.Name = "Check_Save";
            this.Check_Save.UseVisualStyleBackColor = true;
            this.Check_Save.CheckedChanged += new System.EventHandler(this.Check_Save_CheckedChanged);
            // 
            // Button_ViewLogs
            // 
            resources.ApplyResources(this.Button_ViewLogs, "Button_ViewLogs");
            this.Button_ViewLogs.Name = "Button_ViewLogs";
            this.Button_ViewLogs.UseVisualStyleBackColor = true;
            this.Button_ViewLogs.Click += new System.EventHandler(this.Button_ViewLogs_Click);
            // 
            // Label_FPS
            // 
            resources.ApplyResources(this.Label_FPS, "Label_FPS");
            this.Label_FPS.Name = "Label_FPS";
            // 
            // Button_Refresh
            // 
            resources.ApplyResources(this.Button_Refresh, "Button_Refresh");
            this.Button_Refresh.Name = "Button_Refresh";
            this.Button_Refresh.UseVisualStyleBackColor = true;
            this.Button_Refresh.Click += new System.EventHandler(this.Button_Refresh_Click);
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // MonitorForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Button_Refresh);
            this.Controls.Add(this.Label_FPS);
            this.Controls.Add(this.Button_ViewLogs);
            this.Controls.Add(this.Check_Save);
            this.Controls.Add(this.Text_Events);
            this.MaximizeBox = false;
            this.Name = "MonitorForm";
            this.Activated += new System.EventHandler(this.MonitorForm_Activated);
            this.Deactivate += new System.EventHandler(this.MonitorForm_Deactivate);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Monitor_FormClosing);
            this.Load += new System.EventHandler(this.MonitorForm_Load);
            this.ResizeEnd += new System.EventHandler(this.MonitorForm_ResizeEnd);
            this.VisibleChanged += new System.EventHandler(this.MonitorForm_VisibleChanged);
            this.Resize += new System.EventHandler(this.MonitorForm_Resize);
            this.Context_Monitor.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox Text_Events;
        private System.Windows.Forms.CheckBox Check_Save;
        private System.Windows.Forms.Button Button_ViewLogs;
        private System.Windows.Forms.Label Label_FPS;
        private System.Windows.Forms.Button Button_Refresh;
        private System.Windows.Forms.ContextMenuStrip Context_Monitor;
        private System.Windows.Forms.ToolStripMenuItem Context_Monitor_Node;
        private System.Windows.Forms.ToolStripMenuItem Context_Monitor_Packet;
        private System.Windows.Forms.ToolStripSeparator toolStripMenuItem1;
        private System.Windows.Forms.ToolStripMenuItem Context_Monitor_Network;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ToolStripMenuItem Context_Monitor_Variables;
    }
}