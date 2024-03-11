namespace JoinFS
{
    partial class HeightForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(HeightForm));
            this.Button_Cancel = new System.Windows.Forms.Button();
            this.Button_OK = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.Text_Model = new System.Windows.Forms.TextBox();
            this.Button_Off = new System.Windows.Forms.Button();
            this.Button_Plus5 = new System.Windows.Forms.Button();
            this.Button_Plus50 = new System.Windows.Forms.Button();
            this.Button_Minus5 = new System.Windows.Forms.Button();
            this.Button_Minus50 = new System.Windows.Forms.Button();
            this.Text_Adjustment = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // Button_Cancel
            // 
            this.Button_Cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            resources.ApplyResources(this.Button_Cancel, "Button_Cancel");
            this.Button_Cancel.Name = "Button_Cancel";
            this.Button_Cancel.UseVisualStyleBackColor = true;
            // 
            // Button_OK
            // 
            this.Button_OK.DialogResult = System.Windows.Forms.DialogResult.OK;
            resources.ApplyResources(this.Button_OK, "Button_OK");
            this.Button_OK.Name = "Button_OK";
            this.Button_OK.UseVisualStyleBackColor = true;
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
            // Button_Off
            // 
            resources.ApplyResources(this.Button_Off, "Button_Off");
            this.Button_Off.Name = "Button_Off";
            this.Button_Off.UseVisualStyleBackColor = true;
            this.Button_Off.Click += new System.EventHandler(this.Button_Off_Click);
            // 
            // Button_Plus5
            // 
            resources.ApplyResources(this.Button_Plus5, "Button_Plus5");
            this.Button_Plus5.Name = "Button_Plus5";
            this.Button_Plus5.UseVisualStyleBackColor = true;
            this.Button_Plus5.Click += new System.EventHandler(this.Button_Plus5_Click);
            // 
            // Button_Plus50
            // 
            resources.ApplyResources(this.Button_Plus50, "Button_Plus50");
            this.Button_Plus50.Name = "Button_Plus50";
            this.Button_Plus50.UseVisualStyleBackColor = true;
            this.Button_Plus50.Click += new System.EventHandler(this.Button_Plus50_Click);
            // 
            // Button_Minus5
            // 
            resources.ApplyResources(this.Button_Minus5, "Button_Minus5");
            this.Button_Minus5.Name = "Button_Minus5";
            this.Button_Minus5.UseVisualStyleBackColor = true;
            this.Button_Minus5.Click += new System.EventHandler(this.Button_Minus5_Click);
            // 
            // Button_Minus50
            // 
            resources.ApplyResources(this.Button_Minus50, "Button_Minus50");
            this.Button_Minus50.Name = "Button_Minus50";
            this.Button_Minus50.UseVisualStyleBackColor = true;
            this.Button_Minus50.Click += new System.EventHandler(this.Button_Minus50_Click);
            // 
            // Text_Adjustment
            // 
            resources.ApplyResources(this.Text_Adjustment, "Text_Adjustment");
            this.Text_Adjustment.Name = "Text_Adjustment";
            this.Text_Adjustment.ReadOnly = true;
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // HeightForm
            // 
            this.AcceptButton = this.Button_OK;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.Button_Cancel;
            this.Controls.Add(this.label2);
            this.Controls.Add(this.Text_Adjustment);
            this.Controls.Add(this.Button_Minus50);
            this.Controls.Add(this.Button_Minus5);
            this.Controls.Add(this.Button_Plus50);
            this.Controls.Add(this.Button_Plus5);
            this.Controls.Add(this.Button_Off);
            this.Controls.Add(this.Text_Model);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Button_OK);
            this.Controls.Add(this.Button_Cancel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "HeightForm";
            this.ShowInTaskbar = false;
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button Button_Cancel;
        private System.Windows.Forms.Button Button_OK;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox Text_Model;
        private System.Windows.Forms.Button Button_Off;
        private System.Windows.Forms.Button Button_Plus5;
        private System.Windows.Forms.Button Button_Plus50;
        private System.Windows.Forms.Button Button_Minus5;
        private System.Windows.Forms.Button Button_Minus50;
        private System.Windows.Forms.TextBox Text_Adjustment;
        private System.Windows.Forms.Label label2;
    }
}
