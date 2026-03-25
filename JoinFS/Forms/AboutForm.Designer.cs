namespace JoinFS
{
    partial class AboutForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AboutForm));
            Label_About = new System.Windows.Forms.Label();
            Button_OK = new System.Windows.Forms.Button();
            pictureBox1 = new System.Windows.Forms.PictureBox();
            aboutLink = new System.Windows.Forms.LinkLabel();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();
            // 
            // Label_About
            // 
            resources.ApplyResources(Label_About, "Label_About");
            Label_About.Name = "Label_About";
            // 
            // Button_OK
            // 
            Button_OK.DialogResult = System.Windows.Forms.DialogResult.OK;
            resources.ApplyResources(Button_OK, "Button_OK");
            Button_OK.Name = "Button_OK";
            Button_OK.UseVisualStyleBackColor = true;
            // 
            // pictureBox1
            // 
            pictureBox1.Image = Properties.Resources.joinfs64;
            resources.ApplyResources(pictureBox1, "pictureBox1");
            pictureBox1.Name = "pictureBox1";
            pictureBox1.TabStop = false;
            // 
            // aboutLink
            // 
            resources.ApplyResources(aboutLink, "aboutLink");
            aboutLink.Name = "aboutLink";
            aboutLink.TabStop = true;
            aboutLink.LinkClicked += aboutLink_LinkClicked;
            // 
            // AboutForm
            // 
            AcceptButton = Button_OK;
            resources.ApplyResources(this, "$this");
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = Button_OK;
            Controls.Add(aboutLink);
            Controls.Add(pictureBox1);
            Controls.Add(Button_OK);
            Controls.Add(Label_About);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "AboutForm";
            ShowInTaskbar = false;
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label Label_About;
        private System.Windows.Forms.Button Button_OK;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.LinkLabel aboutLink;
    }
}