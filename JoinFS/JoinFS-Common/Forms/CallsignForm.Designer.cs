namespace JoinFS
{
    partial class CallsignForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CallsignForm));
            this.label1 = new System.Windows.Forms.Label();
            this.Text_Model = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.Text_Callsign = new System.Windows.Forms.TextBox();
            this.Button_Cancel = new System.Windows.Forms.Button();
            this.Button_OK = new System.Windows.Forms.Button();
            this.label3 = new System.Windows.Forms.Label();
            this.Text_Original = new System.Windows.Forms.TextBox();
            this.Button_Original = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // Text_Model
            // 
            resources.ApplyResources(this.Text_Model, "Text_Model");
            this.Text_Model.Name = "Text_Model";
            this.Text_Model.ReadOnly = true;
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // Text_Callsign
            // 
            resources.ApplyResources(this.Text_Callsign, "Text_Callsign");
            this.Text_Callsign.Name = "Text_Callsign";
            // 
            // Button_Cancel
            // 
            resources.ApplyResources(this.Button_Cancel, "Button_Cancel");
            this.Button_Cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.Button_Cancel.Name = "Button_Cancel";
            this.Button_Cancel.UseVisualStyleBackColor = true;
            // 
            // Button_OK
            // 
            resources.ApplyResources(this.Button_OK, "Button_OK");
            this.Button_OK.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Button_OK.Name = "Button_OK";
            this.Button_OK.UseVisualStyleBackColor = true;
            this.Button_OK.Click += new System.EventHandler(this.Button_OK_Click);
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // Text_Original
            // 
            resources.ApplyResources(this.Text_Original, "Text_Original");
            this.Text_Original.Name = "Text_Original";
            this.Text_Original.ReadOnly = true;
            // 
            // Button_Original
            // 
            resources.ApplyResources(this.Button_Original, "Button_Original");
            this.Button_Original.DialogResult = System.Windows.Forms.DialogResult.No;
            this.Button_Original.Name = "Button_Original";
            this.Button_Original.UseVisualStyleBackColor = true;
            // 
            // CallsignForm
            // 
            this.AcceptButton = this.Button_OK;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.Button_Cancel;
            this.Controls.Add(this.Button_Original);
            this.Controls.Add(this.Text_Original);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.Button_OK);
            this.Controls.Add(this.Button_Cancel);
            this.Controls.Add(this.Text_Callsign);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.Text_Model);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "CallsignForm";
            this.ShowInTaskbar = false;
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox Text_Model;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox Text_Callsign;
        private System.Windows.Forms.Button Button_Cancel;
        private System.Windows.Forms.Button Button_OK;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox Text_Original;
        private System.Windows.Forms.Button Button_Original;
    }
}
