namespace JoinFS
{
    partial class ScanForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ScanForm));
            this.Label_Specify = new System.Windows.Forms.Label();
            this.Text_Folder = new System.Windows.Forms.TextBox();
            this.Button_Browse = new System.Windows.Forms.Button();
            this.Button_Scan = new System.Windows.Forms.Button();
            this.Button_Cancel = new System.Windows.Forms.Button();
            this.DataGrid_Folders = new System.Windows.Forms.DataGridView();
            this.ColScanOption = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.ColFolder = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.label2 = new System.Windows.Forms.Label();
            this.Text_Additional = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.Label_Simulator = new System.Windows.Forms.Label();
            this.Check_Scan = new System.Windows.Forms.CheckBox();
            this.DataGrid_AddOns = new System.Windows.Forms.DataGridView();
            this.ColInclude = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.ColAddOn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_Folders)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_AddOns)).BeginInit();
            this.SuspendLayout();
            // 
            // Label_Specify
            // 
            resources.ApplyResources(this.Label_Specify, "Label_Specify");
            this.Label_Specify.Name = "Label_Specify";
            // 
            // Text_Folder
            // 
            resources.ApplyResources(this.Text_Folder, "Text_Folder");
            this.Text_Folder.Name = "Text_Folder";
            this.Text_Folder.TextChanged += new System.EventHandler(this.Text_Folder_TextChanged);
            // 
            // Button_Browse
            // 
            resources.ApplyResources(this.Button_Browse, "Button_Browse");
            this.Button_Browse.Name = "Button_Browse";
            this.Button_Browse.UseVisualStyleBackColor = true;
            this.Button_Browse.Click += new System.EventHandler(this.Button_Browse_Click);
            // 
            // Button_Scan
            // 
            resources.ApplyResources(this.Button_Scan, "Button_Scan");
            this.Button_Scan.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Button_Scan.Name = "Button_Scan";
            this.Button_Scan.UseVisualStyleBackColor = true;
            this.Button_Scan.Click += new System.EventHandler(this.Button_Scan_Click);
            // 
            // Button_Cancel
            // 
            resources.ApplyResources(this.Button_Cancel, "Button_Cancel");
            this.Button_Cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.Button_Cancel.Name = "Button_Cancel";
            this.Button_Cancel.UseVisualStyleBackColor = true;
            // 
            // DataGrid_Folders
            // 
            resources.ApplyResources(this.DataGrid_Folders, "DataGrid_Folders");
            this.DataGrid_Folders.AllowUserToAddRows = false;
            this.DataGrid_Folders.AllowUserToDeleteRows = false;
            this.DataGrid_Folders.AllowUserToResizeColumns = false;
            this.DataGrid_Folders.AllowUserToResizeRows = false;
            this.DataGrid_Folders.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.DataGrid_Folders.AutoSizeRowsMode = System.Windows.Forms.DataGridViewAutoSizeRowsMode.AllCells;
            this.DataGrid_Folders.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DataGrid_Folders.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ColScanOption,
            this.ColFolder});
            this.DataGrid_Folders.MultiSelect = false;
            this.DataGrid_Folders.Name = "DataGrid_Folders";
            this.DataGrid_Folders.ReadOnly = true;
            this.DataGrid_Folders.RowHeadersVisible = false;
            this.DataGrid_Folders.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
            this.DataGrid_Folders.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DataGrid_Folders_CellClick);
            // 
            // ColScanOption
            // 
            resources.ApplyResources(this.ColScanOption, "ColScanOption");
            this.ColScanOption.Name = "ColScanOption";
            this.ColScanOption.ReadOnly = true;
            this.ColScanOption.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            // 
            // ColFolder
            // 
            this.ColFolder.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            resources.ApplyResources(this.ColFolder, "ColFolder");
            this.ColFolder.MaxInputLength = 256;
            this.ColFolder.Name = "ColFolder";
            this.ColFolder.ReadOnly = true;
            this.ColFolder.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColFolder.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // Text_Additional
            // 
            this.Text_Additional.AcceptsReturn = true;
            resources.ApplyResources(this.Text_Additional, "Text_Additional");
            this.Text_Additional.Name = "Text_Additional";
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // Label_Simulator
            // 
            resources.ApplyResources(this.Label_Simulator, "Label_Simulator");
            this.Label_Simulator.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.Label_Simulator.Name = "Label_Simulator";
            // 
            // Check_Scan
            // 
            resources.ApplyResources(this.Check_Scan, "Check_Scan");
            this.Check_Scan.Name = "Check_Scan";
            this.Check_Scan.UseVisualStyleBackColor = true;
            // 
            // DataGrid_AddOns
            // 
            resources.ApplyResources(this.DataGrid_AddOns, "DataGrid_AddOns");
            this.DataGrid_AddOns.AllowUserToAddRows = false;
            this.DataGrid_AddOns.AllowUserToDeleteRows = false;
            this.DataGrid_AddOns.AllowUserToResizeColumns = false;
            this.DataGrid_AddOns.AllowUserToResizeRows = false;
            this.DataGrid_AddOns.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.DataGrid_AddOns.AutoSizeRowsMode = System.Windows.Forms.DataGridViewAutoSizeRowsMode.AllCells;
            this.DataGrid_AddOns.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DataGrid_AddOns.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ColInclude,
            this.ColAddOn});
            this.DataGrid_AddOns.MultiSelect = false;
            this.DataGrid_AddOns.Name = "DataGrid_AddOns";
            this.DataGrid_AddOns.ReadOnly = true;
            this.DataGrid_AddOns.RowHeadersVisible = false;
            this.DataGrid_AddOns.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
            this.DataGrid_AddOns.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DataGrid_AddOns_CellContentClick);
            // 
            // ColInclude
            // 
            resources.ApplyResources(this.ColInclude, "ColInclude");
            this.ColInclude.Name = "ColInclude";
            this.ColInclude.ReadOnly = true;
            // 
            // ColAddOn
            // 
            resources.ApplyResources(this.ColAddOn, "ColAddOn");
            this.ColAddOn.Name = "ColAddOn";
            this.ColAddOn.ReadOnly = true;
            // 
            // ScanForm
            // 
            this.AcceptButton = this.Button_Scan;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.Button_Cancel;
            this.Controls.Add(this.DataGrid_AddOns);
            this.Controls.Add(this.Check_Scan);
            this.Controls.Add(this.Label_Simulator);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.Text_Additional);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.DataGrid_Folders);
            this.Controls.Add(this.Button_Cancel);
            this.Controls.Add(this.Button_Scan);
            this.Controls.Add(this.Button_Browse);
            this.Controls.Add(this.Text_Folder);
            this.Controls.Add(this.Label_Specify);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ScanForm";
            this.ShowInTaskbar = false;
            this.Load += new System.EventHandler(this.ScanForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_Folders)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_AddOns)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label Label_Specify;
        private System.Windows.Forms.TextBox Text_Folder;
        private System.Windows.Forms.Button Button_Browse;
        private System.Windows.Forms.Button Button_Scan;
        private System.Windows.Forms.Button Button_Cancel;
        private System.Windows.Forms.DataGridView DataGrid_Folders;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox Text_Additional;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label Label_Simulator;
        private System.Windows.Forms.CheckBox Check_Scan;
        private System.Windows.Forms.DataGridView DataGrid_AddOns;
        private System.Windows.Forms.DataGridViewCheckBoxColumn ColScanOption;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColFolder;
        private System.Windows.Forms.DataGridViewCheckBoxColumn ColInclude;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColAddOn;
    }
}