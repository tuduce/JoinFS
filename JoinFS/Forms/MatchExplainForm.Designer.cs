using System.Drawing;
using System.Windows.Forms;

namespace JoinFS
{
    partial class MatchExplainForm
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
            this.Grid_Attributes = new DataGridView();
            this.Col_Attribute = new DataGridViewTextBoxColumn();
            this.Col_Requested = new DataGridViewTextBoxColumn();
            this.Col_Matched = new DataGridViewTextBoxColumn();
            this.Label_Outcome = new Label();
            this.Label_IcaoGuessed = new Label();
            this.Label_TraceHeader = new Label();
            this.Text_Trace = new TextBox();
            this.Label_Footer = new Label();
            this.Button_OpenModelsList = new Button();
            this.Button_Copy = new Button();
            this.Button_ExportBundle = new Button();
            this.Button_Close = new Button();
            ((System.ComponentModel.ISupportInitialize)(this.Grid_Attributes)).BeginInit();
            this.SuspendLayout();
            //
            // Grid_Attributes
            //
            this.Grid_Attributes.Location = new Point(12, 12);
            this.Grid_Attributes.Size = new Size(664, 200);
            this.Grid_Attributes.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.Grid_Attributes.AllowUserToAddRows = false;
            this.Grid_Attributes.AllowUserToDeleteRows = false;
            this.Grid_Attributes.AllowUserToResizeRows = false;
            this.Grid_Attributes.ReadOnly = true;
            this.Grid_Attributes.RowHeadersVisible = false;
            this.Grid_Attributes.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            this.Grid_Attributes.MultiSelect = false;
            this.Grid_Attributes.ShowCellToolTips = false;
            this.Grid_Attributes.ShowEditingIcon = false;
            this.Grid_Attributes.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            this.Grid_Attributes.Columns.AddRange(new DataGridViewColumn[] {
            this.Col_Attribute,
            this.Col_Requested,
            this.Col_Matched});
            this.Grid_Attributes.Name = "Grid_Attributes";
            //
            // Col_Attribute
            //
            this.Col_Attribute.HeaderText = "Attribute";
            this.Col_Attribute.Name = "Col_Attribute";
            this.Col_Attribute.ReadOnly = true;
            this.Col_Attribute.FillWeight = 30;
            //
            // Col_Requested
            //
            this.Col_Requested.HeaderText = "Requested";
            this.Col_Requested.Name = "Col_Requested";
            this.Col_Requested.ReadOnly = true;
            this.Col_Requested.FillWeight = 35;
            //
            // Col_Matched
            //
            this.Col_Matched.HeaderText = "Matched Model";
            this.Col_Matched.Name = "Col_Matched";
            this.Col_Matched.ReadOnly = true;
            this.Col_Matched.FillWeight = 35;
            //
            // Label_Outcome
            //
            this.Label_Outcome.Location = new Point(12, 220);
            this.Label_Outcome.Size = new Size(664, 20);
            this.Label_Outcome.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.Label_Outcome.Name = "Label_Outcome";
            this.Label_Outcome.Font = new Font(this.Font, FontStyle.Bold);
            //
            // Label_IcaoGuessed
            //
            this.Label_IcaoGuessed.Location = new Point(12, 244);
            this.Label_IcaoGuessed.Size = new Size(664, 32);
            this.Label_IcaoGuessed.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.Label_IcaoGuessed.Name = "Label_IcaoGuessed";
            this.Label_IcaoGuessed.ForeColor = Color.DarkOrange;
            this.Label_IcaoGuessed.Visible = false;
            //
            // Label_TraceHeader
            //
            this.Label_TraceHeader.Location = new Point(12, 282);
            this.Label_TraceHeader.Size = new Size(300, 16);
            this.Label_TraceHeader.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            this.Label_TraceHeader.Name = "Label_TraceHeader";
            this.Label_TraceHeader.Text = "Matching steps (in the order they were tried):";
            //
            // Text_Trace
            //
            this.Text_Trace.Location = new Point(12, 300);
            this.Text_Trace.Size = new Size(664, 200);
            this.Text_Trace.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.Text_Trace.Multiline = true;
            this.Text_Trace.ReadOnly = true;
            this.Text_Trace.ScrollBars = ScrollBars.Vertical;
            this.Text_Trace.WordWrap = true;
            this.Text_Trace.Name = "Text_Trace";
            //
            // Label_Footer
            //
            this.Label_Footer.Location = new Point(12, 506);
            this.Label_Footer.Size = new Size(664, 32);
            this.Label_Footer.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.Label_Footer.Name = "Label_Footer";
            //
            // Button_OpenModelsList
            //
            this.Button_OpenModelsList.Location = new Point(12, 544);
            this.Button_OpenModelsList.Size = new Size(170, 28);
            this.Button_OpenModelsList.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            this.Button_OpenModelsList.Name = "Button_OpenModelsList";
            this.Button_OpenModelsList.Text = "Open known-models list";
            this.Button_OpenModelsList.UseVisualStyleBackColor = true;
            this.Button_OpenModelsList.Click += new System.EventHandler(this.Button_OpenModelsList_Click);
            //
            // Button_Copy
            //
            this.Button_Copy.Location = new Point(330, 544);
            this.Button_Copy.Size = new Size(120, 28);
            this.Button_Copy.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.Button_Copy.Name = "Button_Copy";
            this.Button_Copy.Text = "Copy to clipboard";
            this.Button_Copy.UseVisualStyleBackColor = true;
            this.Button_Copy.Click += new System.EventHandler(this.Button_Copy_Click);
            //
            // Button_ExportBundle
            //
            this.Button_ExportBundle.Location = new Point(456, 544);
            this.Button_ExportBundle.Size = new Size(140, 28);
            this.Button_ExportBundle.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.Button_ExportBundle.Name = "Button_ExportBundle";
            this.Button_ExportBundle.Text = "Export Debug Bundle...";
            this.Button_ExportBundle.UseVisualStyleBackColor = true;
            this.Button_ExportBundle.Click += new System.EventHandler(this.Button_ExportBundle_Click);
            //
            // Button_Close
            //
            this.Button_Close.Location = new Point(596, 544);
            this.Button_Close.Size = new Size(80, 28);
            this.Button_Close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.Button_Close.Name = "Button_Close";
            this.Button_Close.Text = "Close";
            this.Button_Close.UseVisualStyleBackColor = true;
            this.Button_Close.Click += new System.EventHandler(this.Button_Close_Click);
            //
            // MatchExplainForm
            //
            this.AutoScaleDimensions = new SizeF(6F, 13F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(688, 584);
            this.Controls.Add(this.Grid_Attributes);
            this.Controls.Add(this.Label_Outcome);
            this.Controls.Add(this.Label_IcaoGuessed);
            this.Controls.Add(this.Label_TraceHeader);
            this.Controls.Add(this.Text_Trace);
            this.Controls.Add(this.Label_Footer);
            this.Controls.Add(this.Button_OpenModelsList);
            this.Controls.Add(this.Button_Copy);
            this.Controls.Add(this.Button_ExportBundle);
            this.Controls.Add(this.Button_Close);
            this.MinimumSize = new Size(560, 420);
            this.Name = "MatchExplainForm";
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "JoinFS: Explain Match";
            ((System.ComponentModel.ISupportInitialize)(this.Grid_Attributes)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private DataGridView Grid_Attributes;
        private DataGridViewTextBoxColumn Col_Attribute;
        private DataGridViewTextBoxColumn Col_Requested;
        private DataGridViewTextBoxColumn Col_Matched;
        private Label Label_Outcome;
        private Label Label_IcaoGuessed;
        private Label Label_TraceHeader;
        private TextBox Text_Trace;
        private Label Label_Footer;
        private Button Button_OpenModelsList;
        private Button Button_Copy;
        private Button Button_ExportBundle;
        private Button Button_Close;
    }
}
