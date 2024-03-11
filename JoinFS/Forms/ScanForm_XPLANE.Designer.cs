namespace JoinFS
{
    partial class ScanForm_XPLANE
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ScanForm_XPLANE));
            this.Label_Specify = new System.Windows.Forms.Label();
            this.Text_Folder = new System.Windows.Forms.TextBox();
            this.Button_Browse = new System.Windows.Forms.Button();
            this.Button_Scan = new System.Windows.Forms.Button();
            this.Button_Cancel = new System.Windows.Forms.Button();
            this.DataGrid_Folders = new System.Windows.Forms.DataGridView();
            this.ColScanOption = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.ColFolder = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Check_Scan = new System.Windows.Forms.CheckBox();
            this.label1 = new System.Windows.Forms.Label();
            this.Text_CSL = new System.Windows.Forms.TextBox();
            this.Check_Generate = new System.Windows.Forms.CheckBox();
            this.Check_Skip = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_Folders)).BeginInit();
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
            // Check_Scan
            // 
            resources.ApplyResources(this.Check_Scan, "Check_Scan");
            this.Check_Scan.Name = "Check_Scan";
            this.Check_Scan.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // Text_CSL
            // 
            resources.ApplyResources(this.Text_CSL, "Text_CSL");
            this.Text_CSL.Name = "Text_CSL";
            this.Text_CSL.ReadOnly = true;
            // 
            // Check_Generate
            // 
            resources.ApplyResources(this.Check_Generate, "Check_Generate");
            this.Check_Generate.Name = "Check_Generate";
            this.Check_Generate.UseVisualStyleBackColor = true;
            this.Check_Generate.CheckedChanged += new System.EventHandler(this.Check_Generate_CheckedChanged);
            // 
            // Check_Skip
            // 
            resources.ApplyResources(this.Check_Skip, "Check_Skip");
            this.Check_Skip.Name = "Check_Skip";
            this.Check_Skip.UseVisualStyleBackColor = true;
            // 
            // ScanForm_XPLANE
            // 
            this.AcceptButton = this.Button_Scan;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.Button_Cancel;
            this.Controls.Add(this.Check_Skip);
            this.Controls.Add(this.Check_Generate);
            this.Controls.Add(this.Text_CSL);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Check_Scan);
            this.Controls.Add(this.DataGrid_Folders);
            this.Controls.Add(this.Button_Cancel);
            this.Controls.Add(this.Button_Scan);
            this.Controls.Add(this.Button_Browse);
            this.Controls.Add(this.Text_Folder);
            this.Controls.Add(this.Label_Specify);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ScanForm_XPLANE";
            this.ShowInTaskbar = false;
            this.Load += new System.EventHandler(this.ScanForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_Folders)).EndInit();
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
        private System.Windows.Forms.CheckBox Check_Scan;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox Text_CSL;
        private System.Windows.Forms.DataGridViewCheckBoxColumn ColScanOption;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColFolder;
        private System.Windows.Forms.CheckBox Check_Generate;
        private System.Windows.Forms.CheckBox Check_Skip;
    }
}