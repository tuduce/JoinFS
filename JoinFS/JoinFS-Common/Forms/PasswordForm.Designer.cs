namespace JoinFS
{
    partial class PasswordForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PasswordForm));
            this.Button_Cancel = new System.Windows.Forms.Button();
            this.Button_Join = new System.Windows.Forms.Button();
            this.Text_Password = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // Button_Cancel
            // 
            resources.ApplyResources(this.Button_Cancel, "Button_Cancel");
            this.Button_Cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.Button_Cancel.Name = "Button_Cancel";
            this.Button_Cancel.UseVisualStyleBackColor = true;
            // 
            // Button_Join
            // 
            resources.ApplyResources(this.Button_Join, "Button_Join");
            this.Button_Join.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Button_Join.Name = "Button_Join";
            this.Button_Join.UseVisualStyleBackColor = true;
            this.Button_Join.Click += new System.EventHandler(this.Button_Join_Click);
            // 
            // Text_Password
            // 
            resources.ApplyResources(this.Text_Password, "Text_Password");
            this.Text_Password.Name = "Text_Password";
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // PasswordForm
            // 
            this.AcceptButton = this.Button_Join;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.Button_Cancel;
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Text_Password);
            this.Controls.Add(this.Button_Join);
            this.Controls.Add(this.Button_Cancel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "PasswordForm";
            this.ShowInTaskbar = false;
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button Button_Cancel;
        private System.Windows.Forms.Button Button_Join;
        private System.Windows.Forms.TextBox Text_Password;
        private System.Windows.Forms.Label label1;
    }
}
