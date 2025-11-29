namespace JoinFS
{
    partial class ShortcutsForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ShortcutsForm));
            this.DataGrid_Shortcuts = new System.Windows.Forms.DataGridView();
            this.ColKey = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColDescription = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Context_Shortcut = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Context_Shortcut_Enable = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Shortcut_Disable = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Shortcut_Change = new System.Windows.Forms.ToolStripMenuItem();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_Shortcuts)).BeginInit();
            this.Context_Shortcut.SuspendLayout();
            this.SuspendLayout();
            // 
            // DataGrid_Shortcuts
            // 
            resources.ApplyResources(this.DataGrid_Shortcuts, "DataGrid_Shortcuts");
            this.DataGrid_Shortcuts.AllowUserToAddRows = false;
            this.DataGrid_Shortcuts.AllowUserToDeleteRows = false;
            this.DataGrid_Shortcuts.AllowUserToResizeColumns = false;
            this.DataGrid_Shortcuts.AllowUserToResizeRows = false;
            this.DataGrid_Shortcuts.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.DataGrid_Shortcuts.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DataGrid_Shortcuts.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ColKey,
            this.ColDescription});
            this.DataGrid_Shortcuts.ContextMenuStrip = this.Context_Shortcut;
            this.DataGrid_Shortcuts.MultiSelect = false;
            this.DataGrid_Shortcuts.Name = "DataGrid_Shortcuts";
            this.DataGrid_Shortcuts.ReadOnly = true;
            this.DataGrid_Shortcuts.RowHeadersVisible = false;
            this.DataGrid_Shortcuts.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            // 
            // ColKey
            // 
            resources.ApplyResources(this.ColKey, "ColKey");
            this.ColKey.Name = "ColKey";
            this.ColKey.ReadOnly = true;
            // 
            // ColDescription
            // 
            resources.ApplyResources(this.ColDescription, "ColDescription");
            this.ColDescription.Name = "ColDescription";
            this.ColDescription.ReadOnly = true;
            // 
            // Context_Shortcut
            // 
            resources.ApplyResources(this.Context_Shortcut, "Context_Shortcut");
            this.Context_Shortcut.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Context_Shortcut_Enable,
            this.Context_Shortcut_Disable,
            this.Context_Shortcut_Change});
            this.Context_Shortcut.Name = "Context_Shortcut";
            this.Context_Shortcut.Opening += new System.ComponentModel.CancelEventHandler(this.Context_Shortcut_Opening);
            // 
            // Context_Shortcut_Enable
            // 
            resources.ApplyResources(this.Context_Shortcut_Enable, "Context_Shortcut_Enable");
            this.Context_Shortcut_Enable.Name = "Context_Shortcut_Enable";
            this.Context_Shortcut_Enable.Click += new System.EventHandler(this.Context_Shortcut_Enable_Click);
            // 
            // Context_Shortcut_Disable
            // 
            resources.ApplyResources(this.Context_Shortcut_Disable, "Context_Shortcut_Disable");
            this.Context_Shortcut_Disable.Name = "Context_Shortcut_Disable";
            this.Context_Shortcut_Disable.Click += new System.EventHandler(this.Context_Shortcut_Disable_Click);
            // 
            // Context_Shortcut_Change
            // 
            resources.ApplyResources(this.Context_Shortcut_Change, "Context_Shortcut_Change");
            this.Context_Shortcut_Change.Name = "Context_Shortcut_Change";
            this.Context_Shortcut_Change.Click += new System.EventHandler(this.Context_Shortcut_Change_Click);
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
            // ShortcutsForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.DataGrid_Shortcuts);
            this.Name = "ShortcutsForm";
            this.Activated += new System.EventHandler(this.ShortcutsForm_Activated);
            this.Deactivate += new System.EventHandler(this.ShortcutsForm_Deactivate);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ShortcutsForm_FormClosing);
            this.Load += new System.EventHandler(this.ShortcutsForm_Load);
            this.ResizeEnd += new System.EventHandler(this.ShortcutsForm_ResizeEnd);
            this.VisibleChanged += new System.EventHandler(this.ShortcutsForm_VisibleChanged);
            this.Resize += new System.EventHandler(this.ShortcutsForm_Resize);
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_Shortcuts)).EndInit();
            this.Context_Shortcut.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.DataGridView DataGrid_Shortcuts;
        private System.Windows.Forms.ContextMenuStrip Context_Shortcut;
        private System.Windows.Forms.ToolStripMenuItem Context_Shortcut_Enable;
        private System.Windows.Forms.ToolStripMenuItem Context_Shortcut_Disable;
        private System.Windows.Forms.ToolStripMenuItem Context_Shortcut_Change;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColKey;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColDescription;
    }
}
