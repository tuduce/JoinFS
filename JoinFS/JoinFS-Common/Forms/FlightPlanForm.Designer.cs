namespace JoinFS
{
    partial class FlightPlanForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FlightPlanForm));
            this.label1 = new System.Windows.Forms.Label();
            this.Text_Callsign = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.Text_Type = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.Text_From = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.Text_To = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.Text_Route = new System.Windows.Forms.TextBox();
            this.Combo_Rules = new System.Windows.Forms.ComboBox();
            this.label6 = new System.Windows.Forms.Label();
            this.Text_Remarks = new System.Windows.Forms.TextBox();
            this.label7 = new System.Windows.Forms.Label();
            this.Button_Cancel = new System.Windows.Forms.Button();
            this.Button_OK = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // Text_Callsign
            // 
            resources.ApplyResources(this.Text_Callsign, "Text_Callsign");
            this.Text_Callsign.Name = "Text_Callsign";
            this.Text_Callsign.ReadOnly = true;
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // Text_Type
            // 
            resources.ApplyResources(this.Text_Type, "Text_Type");
            this.Text_Type.Name = "Text_Type";
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // Text_From
            // 
            resources.ApplyResources(this.Text_From, "Text_From");
            this.Text_From.Name = "Text_From";
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // Text_To
            // 
            resources.ApplyResources(this.Text_To, "Text_To");
            this.Text_To.Name = "Text_To";
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            // 
            // Text_Route
            // 
            resources.ApplyResources(this.Text_Route, "Text_Route");
            this.Text_Route.Name = "Text_Route";
            // 
            // Combo_Rules
            // 
            resources.ApplyResources(this.Combo_Rules, "Combo_Rules");
            this.Combo_Rules.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.Combo_Rules.FormattingEnabled = true;
            this.Combo_Rules.Name = "Combo_Rules";
            // 
            // label6
            // 
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            // 
            // Text_Remarks
            // 
            resources.ApplyResources(this.Text_Remarks, "Text_Remarks");
            this.Text_Remarks.Name = "Text_Remarks";
            // 
            // label7
            // 
            resources.ApplyResources(this.label7, "label7");
            this.label7.Name = "label7";
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
            // FlightPlanForm
            // 
            this.AcceptButton = this.Button_OK;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.Button_Cancel;
            this.Controls.Add(this.Button_OK);
            this.Controls.Add(this.Button_Cancel);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.Text_Remarks);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.Combo_Rules);
            this.Controls.Add(this.Text_Route);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.Text_To);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.Text_From);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.Text_Type);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.Text_Callsign);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FlightPlanForm";
            this.ShowInTaskbar = false;
            this.Load += new System.EventHandler(this.FlightPlanForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox Text_Callsign;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox Text_Type;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox Text_From;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox Text_To;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.TextBox Text_Route;
        private System.Windows.Forms.ComboBox Combo_Rules;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.TextBox Text_Remarks;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Button Button_Cancel;
        private System.Windows.Forms.Button Button_OK;
    }
}
