namespace RecordingXRay;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
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
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        openButton = new Button();
        fileTextBox = new TextBox();
        summaryPanel = new TableLayoutPanel();
        versionCaptionLabel = new Label();
        versionValueLabel = new Label();
        aircraftCaptionLabel = new Label();
        aircraftValueLabel = new Label();
        objectCaptionLabel = new Label();
        objectValueLabel = new Label();
        frameCaptionLabel = new Label();
        frameValueLabel = new Label();
        durationCaptionLabel = new Label();
        durationValueLabel = new Label();
        splitContainer = new SplitContainer();
        framesTreeView = new TreeView();
        detailsTextBox = new TextBox();
        summaryPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
        splitContainer.Panel1.SuspendLayout();
        splitContainer.Panel2.SuspendLayout();
        splitContainer.SuspendLayout();
        SuspendLayout();
        // 
        // openButton
        // 
        openButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        openButton.Location = new Point(825, 12);
        openButton.Name = "openButton";
        openButton.Size = new Size(100, 27);
        openButton.TabIndex = 0;
        openButton.Text = "Open...";
        openButton.UseVisualStyleBackColor = true;
        openButton.Click += OpenButton_Click;
        // 
        // fileTextBox
        // 
        fileTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        fileTextBox.Location = new Point(12, 14);
        fileTextBox.Name = "fileTextBox";
        fileTextBox.ReadOnly = true;
        fileTextBox.Size = new Size(807, 23);
        fileTextBox.TabIndex = 1;
        // 
        // summaryPanel
        // 
        summaryPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        summaryPanel.ColumnCount = 10;
        summaryPanel.ColumnStyles.Add(new ColumnStyle());
        summaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        summaryPanel.ColumnStyles.Add(new ColumnStyle());
        summaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        summaryPanel.ColumnStyles.Add(new ColumnStyle());
        summaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        summaryPanel.ColumnStyles.Add(new ColumnStyle());
        summaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        summaryPanel.ColumnStyles.Add(new ColumnStyle());
        summaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        summaryPanel.Controls.Add(versionCaptionLabel, 0, 0);
        summaryPanel.Controls.Add(versionValueLabel, 1, 0);
        summaryPanel.Controls.Add(aircraftCaptionLabel, 2, 0);
        summaryPanel.Controls.Add(aircraftValueLabel, 3, 0);
        summaryPanel.Controls.Add(objectCaptionLabel, 4, 0);
        summaryPanel.Controls.Add(objectValueLabel, 5, 0);
        summaryPanel.Controls.Add(frameCaptionLabel, 6, 0);
        summaryPanel.Controls.Add(frameValueLabel, 7, 0);
        summaryPanel.Controls.Add(durationCaptionLabel, 8, 0);
        summaryPanel.Controls.Add(durationValueLabel, 9, 0);
        summaryPanel.Location = new Point(12, 47);
        summaryPanel.Name = "summaryPanel";
        summaryPanel.RowCount = 1;
        summaryPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        summaryPanel.Size = new Size(913, 28);
        summaryPanel.TabIndex = 2;
        // 
        // versionCaptionLabel
        // 
        versionCaptionLabel.Anchor = AnchorStyles.Left;
        versionCaptionLabel.AutoSize = true;
        versionCaptionLabel.Location = new Point(3, 6);
        versionCaptionLabel.Name = "versionCaptionLabel";
        versionCaptionLabel.Size = new Size(50, 15);
        versionCaptionLabel.TabIndex = 0;
        versionCaptionLabel.Text = "Version:";
        // 
        // versionValueLabel
        // 
        versionValueLabel.Anchor = AnchorStyles.Left;
        versionValueLabel.AutoSize = true;
        versionValueLabel.Location = new Point(59, 6);
        versionValueLabel.Name = "versionValueLabel";
        versionValueLabel.Size = new Size(10, 15);
        versionValueLabel.TabIndex = 1;
        versionValueLabel.Text = "-";
        // 
        // aircraftCaptionLabel
        // 
        aircraftCaptionLabel.Anchor = AnchorStyles.Left;
        aircraftCaptionLabel.AutoSize = true;
        aircraftCaptionLabel.Location = new Point(244, 6);
        aircraftCaptionLabel.Name = "aircraftCaptionLabel";
        aircraftCaptionLabel.Size = new Size(52, 15);
        aircraftCaptionLabel.TabIndex = 2;
        aircraftCaptionLabel.Text = "Aircraft:";
        // 
        // aircraftValueLabel
        // 
        aircraftValueLabel.Anchor = AnchorStyles.Left;
        aircraftValueLabel.AutoSize = true;
        aircraftValueLabel.Location = new Point(302, 6);
        aircraftValueLabel.Name = "aircraftValueLabel";
        aircraftValueLabel.Size = new Size(10, 15);
        aircraftValueLabel.TabIndex = 3;
        aircraftValueLabel.Text = "-";
        // 
        // objectCaptionLabel
        // 
        objectCaptionLabel.Anchor = AnchorStyles.Left;
        objectCaptionLabel.AutoSize = true;
        objectCaptionLabel.Location = new Point(487, 6);
        objectCaptionLabel.Name = "objectCaptionLabel";
        objectCaptionLabel.Size = new Size(48, 15);
        objectCaptionLabel.TabIndex = 4;
        objectCaptionLabel.Text = "Objects:";
        // 
        // objectValueLabel
        // 
        objectValueLabel.Anchor = AnchorStyles.Left;
        objectValueLabel.AutoSize = true;
        objectValueLabel.Location = new Point(541, 6);
        objectValueLabel.Name = "objectValueLabel";
        objectValueLabel.Size = new Size(10, 15);
        objectValueLabel.TabIndex = 5;
        objectValueLabel.Text = "-";
        // 
        // frameCaptionLabel
        // 
        frameCaptionLabel.Anchor = AnchorStyles.Left;
        frameCaptionLabel.AutoSize = true;
        frameCaptionLabel.Location = new Point(726, 6);
        frameCaptionLabel.Name = "frameCaptionLabel";
        frameCaptionLabel.Size = new Size(47, 15);
        frameCaptionLabel.TabIndex = 6;
        frameCaptionLabel.Text = "Frames:";
        // 
        // frameValueLabel
        // 
        frameValueLabel.Anchor = AnchorStyles.Left;
        frameValueLabel.AutoSize = true;
        frameValueLabel.Location = new Point(779, 6);
        frameValueLabel.Name = "frameValueLabel";
        frameValueLabel.Size = new Size(10, 15);
        frameValueLabel.TabIndex = 7;
        frameValueLabel.Text = "-";
        // 
        // durationCaptionLabel
        // 
        durationCaptionLabel.Anchor = AnchorStyles.Left;
        durationCaptionLabel.AutoSize = true;
        durationCaptionLabel.Location = new Point(964, 6);
        durationCaptionLabel.Name = "durationCaptionLabel";
        durationCaptionLabel.Size = new Size(56, 15);
        durationCaptionLabel.TabIndex = 8;
        durationCaptionLabel.Text = "Duration:";
        // 
        // durationValueLabel
        // 
        durationValueLabel.Anchor = AnchorStyles.Left;
        durationValueLabel.AutoSize = true;
        durationValueLabel.Location = new Point(1026, 6);
        durationValueLabel.Name = "durationValueLabel";
        durationValueLabel.Size = new Size(10, 15);
        durationValueLabel.TabIndex = 9;
        durationValueLabel.Text = "-";
        // 
        // splitContainer
        // 
        splitContainer.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        splitContainer.Location = new Point(12, 81);
        splitContainer.Name = "splitContainer";
        // 
        // splitContainer.Panel1
        // 
        splitContainer.Panel1.Controls.Add(framesTreeView);
        // 
        // splitContainer.Panel2
        // 
        splitContainer.Panel2.Controls.Add(detailsTextBox);
        splitContainer.Size = new Size(913, 557);
        splitContainer.SplitterDistance = 362;
        splitContainer.TabIndex = 3;
        // 
        // framesTreeView
        // 
        framesTreeView.Dock = DockStyle.Fill;
        framesTreeView.HideSelection = false;
        framesTreeView.Location = new Point(0, 0);
        framesTreeView.Name = "framesTreeView";
        framesTreeView.Size = new Size(362, 557);
        framesTreeView.TabIndex = 0;
        framesTreeView.AfterSelect += FramesTreeView_AfterSelect;
        // 
        // detailsTextBox
        // 
        detailsTextBox.Dock = DockStyle.Fill;
        detailsTextBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
        detailsTextBox.Location = new Point(0, 0);
        detailsTextBox.Multiline = true;
        detailsTextBox.Name = "detailsTextBox";
        detailsTextBox.ReadOnly = true;
        detailsTextBox.ScrollBars = ScrollBars.Both;
        detailsTextBox.Size = new Size(547, 557);
        detailsTextBox.TabIndex = 0;
        detailsTextBox.WordWrap = false;
        // 
        // Form1
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(937, 650);
        Controls.Add(splitContainer);
        Controls.Add(summaryPanel);
        Controls.Add(fileTextBox);
        Controls.Add(openButton);
        MinimumSize = new Size(820, 520);
        Name = "Form1";
        Text = "RecordingXRay";
        summaryPanel.ResumeLayout(false);
        summaryPanel.PerformLayout();
        splitContainer.Panel1.ResumeLayout(false);
        splitContainer.Panel2.ResumeLayout(false);
        splitContainer.Panel2.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
        splitContainer.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Button openButton;
    private TextBox fileTextBox;
    private TableLayoutPanel summaryPanel;
    private Label versionCaptionLabel;
    private Label versionValueLabel;
    private Label aircraftCaptionLabel;
    private Label aircraftValueLabel;
    private Label objectCaptionLabel;
    private Label objectValueLabel;
    private Label frameCaptionLabel;
    private Label frameValueLabel;
    private Label durationCaptionLabel;
    private Label durationValueLabel;
    private SplitContainer splitContainer;
    private TreeView framesTreeView;
    private TextBox detailsTextBox;
}
