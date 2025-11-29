namespace JoinFS
{
    partial class AircraftForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AircraftForm));
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle5 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle6 = new System.Windows.Forms.DataGridViewCellStyle();
            this.DataGrid_AircraftList = new System.Windows.Forms.DataGridView();
            this.ColCallsign = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColAircraftOwner = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColDistance = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColHeading = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColAltitude = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColGS = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColModel = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColWind = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColWeather = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.ColBroadcast = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.ColRecord = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.ColIgnore = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.Context_Aircraft = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Context_Aircraft_Substitute = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Aircraft_Callsign = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Aircraft_FlightPlan = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Aircraft_EditFlightPlan = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Aircraft_Variables = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Aircraft_ShowOnRadar = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Aircraft_AdjustHeight = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Aircraft_Follow = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Aircraft_EnterCockpit = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Aircraft_TrackHeading = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Aircraft_TrackBearing = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Aircraft_CopyWeather = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Aircraft_RemoveFromRecording = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.Context_Aircraft_Hub = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Aircraft_Simulator = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Aircraft_Ignored = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Aircraft_StopTracking = new System.Windows.Forms.ToolStripMenuItem();
            this.Button_Refresh = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.Label_FlightPlan1 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.Label_Details = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.Label_FlightPlan2 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_AircraftList)).BeginInit();
            this.Context_Aircraft.SuspendLayout();
            this.SuspendLayout();
            // 
            // DataGrid_AircraftList
            // 
            resources.ApplyResources(this.DataGrid_AircraftList, "DataGrid_AircraftList");
            this.DataGrid_AircraftList.AllowUserToAddRows = false;
            this.DataGrid_AircraftList.AllowUserToDeleteRows = false;
            this.DataGrid_AircraftList.AllowUserToResizeColumns = false;
            this.DataGrid_AircraftList.AllowUserToResizeRows = false;
            this.DataGrid_AircraftList.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.DataGrid_AircraftList.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DataGrid_AircraftList.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ColCallsign,
            this.ColAircraftOwner,
            this.ColDistance,
            this.ColHeading,
            this.ColAltitude,
            this.ColGS,
            this.ColModel,
            this.ColWind,
            this.ColWeather,
            this.ColBroadcast,
            this.ColRecord,
            this.ColIgnore});
            this.DataGrid_AircraftList.ContextMenuStrip = this.Context_Aircraft;
            this.DataGrid_AircraftList.MultiSelect = false;
            this.DataGrid_AircraftList.Name = "DataGrid_AircraftList";
            this.DataGrid_AircraftList.ReadOnly = true;
            this.DataGrid_AircraftList.RowHeadersVisible = false;
            this.DataGrid_AircraftList.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.DataGrid_AircraftList.ShowCellToolTips = false;
            this.DataGrid_AircraftList.ShowEditingIcon = false;
            this.DataGrid_AircraftList.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DataGrid_AircraftList_CellClick);
            this.DataGrid_AircraftList.SelectionChanged += new System.EventHandler(this.DataGrid_Aircraft_SelectionChanged);
            // 
            // ColCallsign
            // 
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            this.ColCallsign.DefaultCellStyle = dataGridViewCellStyle1;
            resources.ApplyResources(this.ColCallsign, "ColCallsign");
            this.ColCallsign.Name = "ColCallsign";
            this.ColCallsign.ReadOnly = true;
            this.ColCallsign.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColCallsign.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColAircraftOwner
            // 
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            this.ColAircraftOwner.DefaultCellStyle = dataGridViewCellStyle2;
            resources.ApplyResources(this.ColAircraftOwner, "ColAircraftOwner");
            this.ColAircraftOwner.Name = "ColAircraftOwner";
            this.ColAircraftOwner.ReadOnly = true;
            this.ColAircraftOwner.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColAircraftOwner.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColDistance
            // 
            dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            this.ColDistance.DefaultCellStyle = dataGridViewCellStyle3;
            resources.ApplyResources(this.ColDistance, "ColDistance");
            this.ColDistance.Name = "ColDistance";
            this.ColDistance.ReadOnly = true;
            this.ColDistance.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColDistance.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColHeading
            // 
            dataGridViewCellStyle4.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColHeading.DefaultCellStyle = dataGridViewCellStyle4;
            resources.ApplyResources(this.ColHeading, "ColHeading");
            this.ColHeading.Name = "ColHeading";
            this.ColHeading.ReadOnly = true;
            this.ColHeading.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColHeading.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColAltitude
            // 
            dataGridViewCellStyle5.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            this.ColAltitude.DefaultCellStyle = dataGridViewCellStyle5;
            resources.ApplyResources(this.ColAltitude, "ColAltitude");
            this.ColAltitude.Name = "ColAltitude";
            this.ColAltitude.ReadOnly = true;
            this.ColAltitude.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColAltitude.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColGS
            // 
            dataGridViewCellStyle6.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            this.ColGS.DefaultCellStyle = dataGridViewCellStyle6;
            resources.ApplyResources(this.ColGS, "ColGS");
            this.ColGS.Name = "ColGS";
            this.ColGS.ReadOnly = true;
            this.ColGS.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColGS.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColModel
            // 
            resources.ApplyResources(this.ColModel, "ColModel");
            this.ColModel.Name = "ColModel";
            this.ColModel.ReadOnly = true;
            this.ColModel.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColModel.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColWind
            // 
            resources.ApplyResources(this.ColWind, "ColWind");
            this.ColWind.Name = "ColWind";
            this.ColWind.ReadOnly = true;
            this.ColWind.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColWind.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColWeather
            // 
            resources.ApplyResources(this.ColWeather, "ColWeather");
            this.ColWeather.Name = "ColWeather";
            this.ColWeather.ReadOnly = true;
            this.ColWeather.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            // 
            // ColBroadcast
            // 
            resources.ApplyResources(this.ColBroadcast, "ColBroadcast");
            this.ColBroadcast.Name = "ColBroadcast";
            this.ColBroadcast.ReadOnly = true;
            this.ColBroadcast.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            // 
            // ColRecord
            // 
            resources.ApplyResources(this.ColRecord, "ColRecord");
            this.ColRecord.Name = "ColRecord";
            this.ColRecord.ReadOnly = true;
            this.ColRecord.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            // 
            // ColIgnore
            // 
            resources.ApplyResources(this.ColIgnore, "ColIgnore");
            this.ColIgnore.Name = "ColIgnore";
            this.ColIgnore.ReadOnly = true;
            this.ColIgnore.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            // 
            // Context_Aircraft
            // 
            resources.ApplyResources(this.Context_Aircraft, "Context_Aircraft");
            this.Context_Aircraft.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Context_Aircraft_Substitute,
            this.Context_Aircraft_Callsign,
            this.Context_Aircraft_FlightPlan,
            this.Context_Aircraft_EditFlightPlan,
            this.Context_Aircraft_Variables,
            this.Context_Aircraft_ShowOnRadar,
            this.Context_Aircraft_AdjustHeight,
            this.Context_Aircraft_Follow,
            this.Context_Aircraft_EnterCockpit,
            this.Context_Aircraft_TrackHeading,
            this.Context_Aircraft_TrackBearing,
            this.Context_Aircraft_CopyWeather,
            this.Context_Aircraft_RemoveFromRecording,
            this.toolStripSeparator1,
            this.Context_Aircraft_Hub,
            this.Context_Aircraft_Simulator,
            this.Context_Aircraft_Ignored,
            this.Context_Aircraft_StopTracking});
            this.Context_Aircraft.Name = "Context_Aircraft";
            this.Context_Aircraft.Opening += new System.ComponentModel.CancelEventHandler(this.Context_Aircraft_Opening);
            // 
            // Context_Aircraft_Substitute
            // 
            resources.ApplyResources(this.Context_Aircraft_Substitute, "Context_Aircraft_Substitute");
            this.Context_Aircraft_Substitute.Name = "Context_Aircraft_Substitute";
            this.Context_Aircraft_Substitute.Click += new System.EventHandler(this.Context_Aircraft_Substitute_Click);
            // 
            // Context_Aircraft_Callsign
            // 
            resources.ApplyResources(this.Context_Aircraft_Callsign, "Context_Aircraft_Callsign");
            this.Context_Aircraft_Callsign.Name = "Context_Aircraft_Callsign";
            this.Context_Aircraft_Callsign.Click += new System.EventHandler(this.Context_Aircraft_Callsign_Click);
            // 
            // Context_Aircraft_FlightPlan
            // 
            resources.ApplyResources(this.Context_Aircraft_FlightPlan, "Context_Aircraft_FlightPlan");
            this.Context_Aircraft_FlightPlan.Name = "Context_Aircraft_FlightPlan";
            this.Context_Aircraft_FlightPlan.Click += new System.EventHandler(this.Context_Aircraft_FlightPlan_Click);
            // 
            // Context_Aircraft_EditFlightPlan
            // 
            resources.ApplyResources(this.Context_Aircraft_EditFlightPlan, "Context_Aircraft_EditFlightPlan");
            this.Context_Aircraft_EditFlightPlan.Name = "Context_Aircraft_EditFlightPlan";
            this.Context_Aircraft_EditFlightPlan.Click += new System.EventHandler(this.Context_Aircraft_EditFlightPlan_Click);
            // 
            // Context_Aircraft_Variables
            // 
            resources.ApplyResources(this.Context_Aircraft_Variables, "Context_Aircraft_Variables");
            this.Context_Aircraft_Variables.Name = "Context_Aircraft_Variables";
            this.Context_Aircraft_Variables.Click += new System.EventHandler(this.Context_Aircraft_Variables_Click);
            // 
            // Context_Aircraft_ShowOnRadar
            // 
            resources.ApplyResources(this.Context_Aircraft_ShowOnRadar, "Context_Aircraft_ShowOnRadar");
            this.Context_Aircraft_ShowOnRadar.Name = "Context_Aircraft_ShowOnRadar";
            this.Context_Aircraft_ShowOnRadar.Click += new System.EventHandler(this.Context_Aircraft_ShowOnRadar_Click);
            // 
            // Context_Aircraft_AdjustHeight
            // 
            resources.ApplyResources(this.Context_Aircraft_AdjustHeight, "Context_Aircraft_AdjustHeight");
            this.Context_Aircraft_AdjustHeight.Name = "Context_Aircraft_AdjustHeight";
            this.Context_Aircraft_AdjustHeight.Click += new System.EventHandler(this.Context_Aircraft_AdjustHeight_Click);
            // 
            // Context_Aircraft_Follow
            // 
            resources.ApplyResources(this.Context_Aircraft_Follow, "Context_Aircraft_Follow");
            this.Context_Aircraft_Follow.Name = "Context_Aircraft_Follow";
            this.Context_Aircraft_Follow.Click += new System.EventHandler(this.Context_Aircraft_Follow_Click);
            // 
            // Context_Aircraft_EnterCockpit
            // 
            resources.ApplyResources(this.Context_Aircraft_EnterCockpit, "Context_Aircraft_EnterCockpit");
            this.Context_Aircraft_EnterCockpit.Name = "Context_Aircraft_EnterCockpit";
            this.Context_Aircraft_EnterCockpit.Click += new System.EventHandler(this.Context_Aircraft_EnterCockpit_Click);
            // 
            // Context_Aircraft_TrackHeading
            // 
            resources.ApplyResources(this.Context_Aircraft_TrackHeading, "Context_Aircraft_TrackHeading");
            this.Context_Aircraft_TrackHeading.Name = "Context_Aircraft_TrackHeading";
            this.Context_Aircraft_TrackHeading.Click += new System.EventHandler(this.Context_Aircraft_TrackHeading_Click);
            // 
            // Context_Aircraft_TrackBearing
            // 
            resources.ApplyResources(this.Context_Aircraft_TrackBearing, "Context_Aircraft_TrackBearing");
            this.Context_Aircraft_TrackBearing.Name = "Context_Aircraft_TrackBearing";
            this.Context_Aircraft_TrackBearing.Click += new System.EventHandler(this.Context_Aircraft_TrackBearing_Click);
            // 
            // Context_Aircraft_CopyWeather
            // 
            resources.ApplyResources(this.Context_Aircraft_CopyWeather, "Context_Aircraft_CopyWeather");
            this.Context_Aircraft_CopyWeather.Name = "Context_Aircraft_CopyWeather";
            this.Context_Aircraft_CopyWeather.Click += new System.EventHandler(this.Context_Aircraft_CopyWeather_Click);
            // 
            // Context_Aircraft_RemoveFromRecording
            // 
            resources.ApplyResources(this.Context_Aircraft_RemoveFromRecording, "Context_Aircraft_RemoveFromRecording");
            this.Context_Aircraft_RemoveFromRecording.Name = "Context_Aircraft_RemoveFromRecording";
            this.Context_Aircraft_RemoveFromRecording.Click += new System.EventHandler(this.Context_Aircraft_RemoveFromRecording_Click);
            // 
            // toolStripSeparator1
            // 
            resources.ApplyResources(this.toolStripSeparator1, "toolStripSeparator1");
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            // 
            // Context_Aircraft_Hub
            // 
            resources.ApplyResources(this.Context_Aircraft_Hub, "Context_Aircraft_Hub");
            this.Context_Aircraft_Hub.Name = "Context_Aircraft_Hub";
            this.Context_Aircraft_Hub.Click += new System.EventHandler(this.Context_Aircraft_Public_Click);
            // 
            // Context_Aircraft_Simulator
            // 
            resources.ApplyResources(this.Context_Aircraft_Simulator, "Context_Aircraft_Simulator");
            this.Context_Aircraft_Simulator.Name = "Context_Aircraft_Simulator";
            this.Context_Aircraft_Simulator.Click += new System.EventHandler(this.Context_Aircraft_Simulator_Click);
            // 
            // Context_Aircraft_Ignored
            // 
            resources.ApplyResources(this.Context_Aircraft_Ignored, "Context_Aircraft_Ignored");
            this.Context_Aircraft_Ignored.Name = "Context_Aircraft_Ignored";
            this.Context_Aircraft_Ignored.Click += new System.EventHandler(this.Context_Aircraft_Ignored_Click);
            // 
            // Context_Aircraft_StopTracking
            // 
            resources.ApplyResources(this.Context_Aircraft_StopTracking, "Context_Aircraft_StopTracking");
            this.Context_Aircraft_StopTracking.Name = "Context_Aircraft_StopTracking";
            this.Context_Aircraft_StopTracking.Click += new System.EventHandler(this.Context_Aircraft_StopTracking_Click);
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
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // Label_FlightPlan1
            // 
            resources.ApplyResources(this.Label_FlightPlan1, "Label_FlightPlan1");
            this.Label_FlightPlan1.BackColor = System.Drawing.SystemColors.Window;
            this.Label_FlightPlan1.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.Label_FlightPlan1.Name = "Label_FlightPlan1";
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // Label_Details
            // 
            resources.ApplyResources(this.Label_Details, "Label_Details");
            this.Label_Details.BackColor = System.Drawing.SystemColors.Window;
            this.Label_Details.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.Label_Details.Name = "Label_Details";
            // 
            // label6
            // 
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            // 
            // Label_FlightPlan2
            // 
            resources.ApplyResources(this.Label_FlightPlan2, "Label_FlightPlan2");
            this.Label_FlightPlan2.BackColor = System.Drawing.SystemColors.Window;
            this.Label_FlightPlan2.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.Label_FlightPlan2.Name = "Label_FlightPlan2";
            // 
            // AircraftForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Label_FlightPlan2);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.Label_Details);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.Label_FlightPlan1);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Button_Refresh);
            this.Controls.Add(this.DataGrid_AircraftList);
            this.KeyPreview = true;
            this.Name = "AircraftForm";
            this.Activated += new System.EventHandler(this.AircraftForm_Activated);
            this.Deactivate += new System.EventHandler(this.AircraftForm_Deactivate);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.AircraftForm_FormClosing);
            this.Load += new System.EventHandler(this.AircraftForm_Load);
            this.ResizeEnd += new System.EventHandler(this.AircraftForm_ResizeEnd);
            this.VisibleChanged += new System.EventHandler(this.AircraftForm_VisibleChanged);
            this.Resize += new System.EventHandler(this.AircraftForm_Resize);
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_AircraftList)).EndInit();
            this.Context_Aircraft.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.DataGridView DataGrid_AircraftList;
        private System.Windows.Forms.Button Button_Refresh;
        private System.Windows.Forms.ContextMenuStrip Context_Aircraft;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_RemoveFromRecording;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_Substitute;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_Follow;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_EnterCockpit;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_CopyWeather;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_Hub;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_Ignored;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_TrackHeading;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_TrackBearing;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_StopTracking;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_Callsign;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_FlightPlan;
        private System.Windows.Forms.Label Label_FlightPlan1;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label Label_Details;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_AdjustHeight;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_Simulator;
        private System.Windows.Forms.Label Label_FlightPlan2;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_EditFlightPlan;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_ShowOnRadar;
        private System.Windows.Forms.ToolStripMenuItem Context_Aircraft_Variables;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColCallsign;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColAircraftOwner;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColDistance;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColHeading;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColAltitude;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColGS;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColModel;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColWind;
        private System.Windows.Forms.DataGridViewCheckBoxColumn ColWeather;
        private System.Windows.Forms.DataGridViewCheckBoxColumn ColBroadcast;
        private System.Windows.Forms.DataGridViewCheckBoxColumn ColRecord;
        private System.Windows.Forms.DataGridViewCheckBoxColumn ColIgnore;
    }
}