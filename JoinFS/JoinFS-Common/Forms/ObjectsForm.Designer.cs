namespace JoinFS
{
    partial class ObjectsForm
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
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle6 = new System.Windows.Forms.DataGridViewCellStyle();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ObjectsForm));
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle7 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle8 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle9 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle10 = new System.Windows.Forms.DataGridViewCellStyle();
            this.DataGrid_ObjectList = new System.Windows.Forms.DataGridView();
            this.ColNickname = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColModel = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColCount = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColBearing = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColDistance = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColBroadcast = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.ColIgnoreOwner = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.ColIgnoreModel = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.Button_Substitute = new System.Windows.Forms.Button();
            this.Check_ListIgnoredObjects = new System.Windows.Forms.CheckBox();
            this.Check_Group = new System.Windows.Forms.CheckBox();
            this.Button_Refresh = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_ObjectList)).BeginInit();
            this.SuspendLayout();
            // 
            // DataGrid_ObjectList
            // 
            this.DataGrid_ObjectList.AllowUserToAddRows = false;
            this.DataGrid_ObjectList.AllowUserToDeleteRows = false;
            this.DataGrid_ObjectList.AllowUserToResizeColumns = false;
            this.DataGrid_ObjectList.AllowUserToResizeRows = false;
            this.DataGrid_ObjectList.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.DataGrid_ObjectList.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DataGrid_ObjectList.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ColNickname,
            this.ColModel,
            this.ColCount,
            this.ColBearing,
            this.ColDistance,
            this.ColBroadcast,
            this.ColIgnoreOwner,
            this.ColIgnoreModel});
            resources.ApplyResources(this.DataGrid_ObjectList, "DataGrid_ObjectList");
            this.DataGrid_ObjectList.MultiSelect = false;
            this.DataGrid_ObjectList.Name = "DataGrid_ObjectList";
            this.DataGrid_ObjectList.RowHeadersVisible = false;
            this.DataGrid_ObjectList.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.DataGrid_ObjectList.ShowCellToolTips = false;
            this.DataGrid_ObjectList.ShowEditingIcon = false;
            this.DataGrid_ObjectList.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DataGrid_ObjectList_CellClick);
            this.DataGrid_ObjectList.SelectionChanged += new System.EventHandler(this.DataGrid_ObjectList_SelectionChanged);
            // 
            // ColNickname
            // 
            dataGridViewCellStyle6.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            this.ColNickname.DefaultCellStyle = dataGridViewCellStyle6;
            resources.ApplyResources(this.ColNickname, "ColNickname");
            this.ColNickname.MaxInputLength = 50;
            this.ColNickname.Name = "ColNickname";
            this.ColNickname.ReadOnly = true;
            this.ColNickname.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColNickname.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColModel
            // 
            dataGridViewCellStyle7.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            this.ColModel.DefaultCellStyle = dataGridViewCellStyle7;
            resources.ApplyResources(this.ColModel, "ColModel");
            this.ColModel.MaxInputLength = 50;
            this.ColModel.Name = "ColModel";
            this.ColModel.ReadOnly = true;
            this.ColModel.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColModel.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColCount
            // 
            dataGridViewCellStyle8.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColCount.DefaultCellStyle = dataGridViewCellStyle8;
            resources.ApplyResources(this.ColCount, "ColCount");
            this.ColCount.MaxInputLength = 20;
            this.ColCount.Name = "ColCount";
            this.ColCount.ReadOnly = true;
            this.ColCount.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColCount.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColBearing
            // 
            dataGridViewCellStyle9.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColBearing.DefaultCellStyle = dataGridViewCellStyle9;
            resources.ApplyResources(this.ColBearing, "ColBearing");
            this.ColBearing.Name = "ColBearing";
            this.ColBearing.ReadOnly = true;
            this.ColBearing.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColBearing.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColDistance
            // 
            dataGridViewCellStyle10.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColDistance.DefaultCellStyle = dataGridViewCellStyle10;
            resources.ApplyResources(this.ColDistance, "ColDistance");
            this.ColDistance.Name = "ColDistance";
            this.ColDistance.ReadOnly = true;
            this.ColDistance.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColDistance.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColBroadcast
            // 
            resources.ApplyResources(this.ColBroadcast, "ColBroadcast");
            this.ColBroadcast.Name = "ColBroadcast";
            this.ColBroadcast.ReadOnly = true;
            // 
            // ColIgnoreOwner
            // 
            resources.ApplyResources(this.ColIgnoreOwner, "ColIgnoreOwner");
            this.ColIgnoreOwner.Name = "ColIgnoreOwner";
            this.ColIgnoreOwner.ReadOnly = true;
            this.ColIgnoreOwner.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            // 
            // ColIgnoreModel
            // 
            resources.ApplyResources(this.ColIgnoreModel, "ColIgnoreModel");
            this.ColIgnoreModel.Name = "ColIgnoreModel";
            this.ColIgnoreModel.ReadOnly = true;
            // 
            // Button_Substitute
            // 
            resources.ApplyResources(this.Button_Substitute, "Button_Substitute");
            this.Button_Substitute.Name = "Button_Substitute";
            this.Button_Substitute.UseVisualStyleBackColor = true;
            this.Button_Substitute.Click += new System.EventHandler(this.Button_Substitute_Click);
            // 
            // Check_ListIgnoredObjects
            // 
            resources.ApplyResources(this.Check_ListIgnoredObjects, "Check_ListIgnoredObjects");
            this.Check_ListIgnoredObjects.Name = "Check_ListIgnoredObjects";
            this.Check_ListIgnoredObjects.UseVisualStyleBackColor = true;
            this.Check_ListIgnoredObjects.CheckedChanged += new System.EventHandler(this.Check_ListIgnored_CheckedChanged);
            // 
            // Check_Group
            // 
            resources.ApplyResources(this.Check_Group, "Check_Group");
            this.Check_Group.Name = "Check_Group";
            this.Check_Group.UseVisualStyleBackColor = true;
            this.Check_Group.CheckedChanged += new System.EventHandler(this.Check_Group_CheckedChanged);
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
            // ObjectsForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Button_Refresh);
            this.Controls.Add(this.Check_Group);
            this.Controls.Add(this.Check_ListIgnoredObjects);
            this.Controls.Add(this.Button_Substitute);
            this.Controls.Add(this.DataGrid_ObjectList);
            this.Name = "ObjectsForm";
            this.Activated += new System.EventHandler(this.ObjectsForm_Activated);
            this.Deactivate += new System.EventHandler(this.ObjectsForm_Deactivate);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ObjectsForm_FormClosing);
            this.Load += new System.EventHandler(this.ObjectsForm_Load);
            this.ResizeEnd += new System.EventHandler(this.ObjectsForm_ResizeEnd);
            this.VisibleChanged += new System.EventHandler(this.ObjectsForm_VisibleChanged);
            this.Resize += new System.EventHandler(this.ObjectForm_Resize);
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_ObjectList)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.DataGridView DataGrid_ObjectList;
        private System.Windows.Forms.Button Button_Substitute;
        private System.Windows.Forms.CheckBox Check_ListIgnoredObjects;
        private System.Windows.Forms.CheckBox Check_Group;
        private System.Windows.Forms.Button Button_Refresh;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColNickname;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColModel;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColCount;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColBearing;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColDistance;
        private System.Windows.Forms.DataGridViewCheckBoxColumn ColBroadcast;
        private System.Windows.Forms.DataGridViewCheckBoxColumn ColIgnoreOwner;
        private System.Windows.Forms.DataGridViewCheckBoxColumn ColIgnoreModel;
    }
}