namespace JoinFS
{
    partial class XPlaneForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(XPlaneForm));
            this.label1 = new System.Windows.Forms.Label();
            this.Text_Folder = new System.Windows.Forms.TextBox();
            this.Button_Browse = new System.Windows.Forms.Button();
            this.Button_Install = new System.Windows.Forms.Button();
            this.Button_Cancel = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // Text_Folder
            // 
            resources.ApplyResources(this.Text_Folder, "Text_Folder");
            this.Text_Folder.Name = "Text_Folder";
            // 
            // Button_Browse
            // 
            resources.ApplyResources(this.Button_Browse, "Button_Browse");
            this.Button_Browse.Name = "Button_Browse";
            this.Button_Browse.UseVisualStyleBackColor = true;
            this.Button_Browse.Click += new System.EventHandler(this.Button_Browse_Click);
            // 
            // Button_Install
            // 
            resources.ApplyResources(this.Button_Install, "Button_Install");
            this.Button_Install.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Button_Install.Name = "Button_Install";
            this.Button_Install.UseVisualStyleBackColor = true;
            // 
            // Button_Cancel
            // 
            resources.ApplyResources(this.Button_Cancel, "Button_Cancel");
            this.Button_Cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.Button_Cancel.Name = "Button_Cancel";
            this.Button_Cancel.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // XPlaneForm
            // 
            this.AcceptButton = this.Button_Install;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.Button_Cancel;
            this.Controls.Add(this.label2);
            this.Controls.Add(this.Button_Cancel);
            this.Controls.Add(this.Button_Install);
            this.Controls.Add(this.Button_Browse);
            this.Controls.Add(this.Text_Folder);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "XPlaneForm";
            this.Load += new System.EventHandler(this.XPlane_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox Text_Folder;
        private System.Windows.Forms.Button Button_Browse;
        private System.Windows.Forms.Button Button_Install;
        private System.Windows.Forms.Button Button_Cancel;
        private System.Windows.Forms.Label label2;
    }
}