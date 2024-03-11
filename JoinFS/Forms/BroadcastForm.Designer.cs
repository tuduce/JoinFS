namespace JoinFS
{
    partial class BroadcastForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(BroadcastForm));
            this.Check_Object = new System.Windows.Forms.CheckBox();
            this.Check_Model = new System.Windows.Forms.CheckBox();
            this.Check_Tacpack = new System.Windows.Forms.CheckBox();
            this.Button_OK = new System.Windows.Forms.Button();
            this.Check_Everything = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // Check_Object
            // 
            resources.ApplyResources(this.Check_Object, "Check_Object");
            this.Check_Object.Name = "Check_Object";
            this.Check_Object.UseVisualStyleBackColor = true;
            // 
            // Check_Model
            // 
            resources.ApplyResources(this.Check_Model, "Check_Model");
            this.Check_Model.Name = "Check_Model";
            this.Check_Model.UseVisualStyleBackColor = true;
            // 
            // Check_Tacpack
            // 
            resources.ApplyResources(this.Check_Tacpack, "Check_Tacpack");
            this.Check_Tacpack.Name = "Check_Tacpack";
            this.Check_Tacpack.UseVisualStyleBackColor = true;
            // 
            // Button_OK
            // 
            resources.ApplyResources(this.Button_OK, "Button_OK");
            this.Button_OK.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Button_OK.Name = "Button_OK";
            this.Button_OK.UseVisualStyleBackColor = true;
            this.Button_OK.Click += new System.EventHandler(this.Button_OK_Click);
            // 
            // Check_Everything
            // 
            resources.ApplyResources(this.Check_Everything, "Check_Everything");
            this.Check_Everything.Name = "Check_Everything";
            this.Check_Everything.UseVisualStyleBackColor = true;
            // 
            // BroadcastForm
            // 
            this.AcceptButton = this.Button_OK;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Check_Everything);
            this.Controls.Add(this.Button_OK);
            this.Controls.Add(this.Check_Tacpack);
            this.Controls.Add(this.Check_Model);
            this.Controls.Add(this.Check_Object);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "BroadcastForm";
            this.Load += new System.EventHandler(this.BroadcastForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox Check_Object;
        private System.Windows.Forms.CheckBox Check_Model;
        private System.Windows.Forms.CheckBox Check_Tacpack;
        private System.Windows.Forms.Button Button_OK;
        private System.Windows.Forms.CheckBox Check_Everything;
    }
}