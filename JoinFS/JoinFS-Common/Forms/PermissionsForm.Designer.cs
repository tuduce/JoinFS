namespace JoinFS
{
    partial class PermissionsForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PermissionsForm));
            this.Button_OK = new System.Windows.Forms.Button();
            this.Check_Cockpit = new System.Windows.Forms.CheckBox();
            this.Check_Flight = new System.Windows.Forms.CheckBox();
            this.Check_Multiple = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // Button_OK
            // 
            resources.ApplyResources(this.Button_OK, "Button_OK");
            this.Button_OK.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Button_OK.Name = "Button_OK";
            this.Button_OK.UseVisualStyleBackColor = true;
            this.Button_OK.Click += new System.EventHandler(this.Button_OK_Click);
            // 
            // Check_Cockpit
            // 
            resources.ApplyResources(this.Check_Cockpit, "Check_Cockpit");
            this.Check_Cockpit.Name = "Check_Cockpit";
            this.Check_Cockpit.UseVisualStyleBackColor = true;
            this.Check_Cockpit.CheckedChanged += new System.EventHandler(this.Check_Cockpit_CheckedChanged);
            // 
            // Check_Flight
            // 
            resources.ApplyResources(this.Check_Flight, "Check_Flight");
            this.Check_Flight.Name = "Check_Flight";
            this.Check_Flight.UseVisualStyleBackColor = true;
            // 
            // Check_Multiple
            // 
            resources.ApplyResources(this.Check_Multiple, "Check_Multiple");
            this.Check_Multiple.Name = "Check_Multiple";
            this.Check_Multiple.UseVisualStyleBackColor = true;
            // 
            // PermissionsForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Check_Multiple);
            this.Controls.Add(this.Check_Flight);
            this.Controls.Add(this.Check_Cockpit);
            this.Controls.Add(this.Button_OK);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "PermissionsForm";
            this.Load += new System.EventHandler(this.PermissionsForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button Button_OK;
        private System.Windows.Forms.CheckBox Check_Cockpit;
        private System.Windows.Forms.CheckBox Check_Flight;
        private System.Windows.Forms.CheckBox Check_Multiple;
    }
}