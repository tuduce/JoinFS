using System.Text;

namespace RecordingXRay;

public partial class Form1 : Form
{
    private RecordingFile? loadedRecording;
    private readonly VariableLookup variableLookup = VariableLookup.Create();

    private RecordingEditSession? _editSession;
    private TabControl? _tabControl;
    private TimelineControl? _timeline;
    private VariableEditorPanel? _editorPanel;
    private Button? _saveButton;

    public Form1()
    {
        InitializeComponent();
        ApplyModernLook();
        SetupEditorComponents();
        UpdateSummary(null);
        detailsTextBox.Text = $"Open a JoinFS recording file to inspect its content.{Environment.NewLine}Variable lookup entries loaded: {variableLookup.Count}.";
    }

    private void ApplyModernLook()
    {
        Color background = Color.FromArgb(15, 23, 42);
        Color surface = Color.FromArgb(30, 41, 59);
        Color panel = Color.FromArgb(51, 65, 85);
        Color text = Color.FromArgb(226, 232, 240);
        Color muted = Color.FromArgb(148, 163, 184);
        Color accent = Color.FromArgb(59, 130, 246);

        BackColor = background;
        ForeColor = text;
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

        openButton.FlatStyle = FlatStyle.Flat;
        openButton.FlatAppearance.BorderSize = 0;
        openButton.BackColor = accent;
        openButton.ForeColor = Color.White;
        openButton.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point);

        fileTextBox.BorderStyle = BorderStyle.FixedSingle;
        fileTextBox.BackColor = panel;
        fileTextBox.ForeColor = text;

        summaryPanel.BackColor = surface;
        summaryPanel.CellBorderStyle = TableLayoutPanelCellBorderStyle.None;
        summaryPanel.Padding = new Padding(6, 2, 6, 2);
        summaryPanel.ColumnStyles.Clear();
        for (int i = 0; i < 10; i++)
        {
            summaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10f));
        }

        Label[] captions = [versionCaptionLabel, aircraftCaptionLabel, objectCaptionLabel, frameCaptionLabel, durationCaptionLabel];
        foreach (Label caption in captions)
        {
            caption.ForeColor = muted;
            caption.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        }

        Label[] values = [versionValueLabel, aircraftValueLabel, objectValueLabel, frameValueLabel, durationValueLabel];
        foreach (Label value in values)
        {
            value.ForeColor = text;
            value.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point);
        }

        splitContainer.BackColor = background;
        splitContainer.BorderStyle = BorderStyle.FixedSingle;
        splitContainer.Panel1.BackColor = surface;
        splitContainer.Panel2.BackColor = surface;

        framesTreeView.BorderStyle = BorderStyle.None;
        framesTreeView.BackColor = surface;
        framesTreeView.ForeColor = text;
        framesTreeView.LineColor = panel;
        framesTreeView.ShowLines = false;

        detailsTextBox.BorderStyle = BorderStyle.None;
        detailsTextBox.BackColor = surface;
        detailsTextBox.ForeColor = text;
        detailsTextBox.Font = new Font("Cascadia Mono", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    }

    private void OpenButton_Click(object? sender, EventArgs e)
    {
        using OpenFileDialog openFileDialog = new()
        {
            Filter = "JoinFS recordings|*.jfs|All files|*.*",
            DefaultExt = "jfs",
            Title = "Open JoinFS Recording"
        };

        if (openFileDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            loadedRecording = RecordingReader.Read(openFileDialog.FileName);
            fileTextBox.Text = openFileDialog.FileName;
            PopulateTree(loadedRecording);
            UpdateSummary(loadedRecording);
            detailsTextBox.Text = $"Select an aircraft, object, or frame from the tree to view details.{Environment.NewLine}Variable lookup entries loaded: {variableLookup.Count}.";
            _editSession = null;
            _timeline?.SetFrames([], 0);
            _editorPanel?.SetSession(null);
            UpdateSaveButtonState();
        }
        catch (Exception ex)
        {
            loadedRecording = null;
            framesTreeView.Nodes.Clear();
            UpdateSummary(null);
            detailsTextBox.Text = string.Empty;
            MessageBox.Show(this, ex.Message, "Unable to read recording", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PopulateTree(RecordingFile recording)
    {
        framesTreeView.BeginUpdate();
        framesTreeView.Nodes.Clear();

        TreeNode aircraftRoot = new($"Aircraft ({recording.Aircraft.Count})");
        foreach (RecordedAircraft aircraft in recording.Aircraft)
        {
            aircraftRoot.Nodes.Add(CreateObjectNode(aircraft));
        }

        TreeNode objectsRoot = new($"Objects ({recording.Objects.Count})");
        foreach (RecordedObject obj in recording.Objects)
        {
            objectsRoot.Nodes.Add(CreateObjectNode(obj));
        }

        framesTreeView.Nodes.Add(aircraftRoot);
        framesTreeView.Nodes.Add(objectsRoot);
        aircraftRoot.Expand();
        objectsRoot.Expand();
        framesTreeView.EndUpdate();
    }

    private static TreeNode CreateObjectNode(RecordedObject obj)
    {
        string title = obj is RecordedAircraft aircraft
            ? $"{aircraft.Callsign} ({aircraft.Model}) - {aircraft.Frames.Count} frames"
            : $"{obj.Model} - {obj.Frames.Count} frames";

        TreeNode objectNode = new(title) { Tag = obj };

        for (int i = 0; i < obj.Frames.Count; i++)
        {
            RecordedFrame frame = obj.Frames[i];
            TreeNode frameNode = new($"[{i}] {frame.Time:0.000}s {frame.Type}") { Tag = frame };
            objectNode.Nodes.Add(frameNode);
        }

        return objectNode;
    }

    private void UpdateSummary(RecordingFile? recording)
    {
        if (recording is null)
        {
            versionValueLabel.Text = "-";
            aircraftValueLabel.Text = "-";
            objectValueLabel.Text = "-";
            frameValueLabel.Text = "-";
            durationValueLabel.Text = "-";
            return;
        }

        int totalFrames = recording.Aircraft.Sum(a => a.Frames.Count) + recording.Objects.Sum(o => o.Frames.Count);
        double duration = recording.Aircraft.SelectMany(a => a.Frames).Concat(recording.Objects.SelectMany(o => o.Frames)).Select(f => f.Time).DefaultIfEmpty(0).Max();

        versionValueLabel.Text = recording.Version.ToString();
        aircraftValueLabel.Text = recording.Aircraft.Count.ToString();
        objectValueLabel.Text = recording.Objects.Count.ToString();
        frameValueLabel.Text = totalFrames.ToString();
        durationValueLabel.Text = $"{duration:0.000}s";
    }

    private void FramesTreeView_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        switch (e.Node?.Tag)
        {
            case RecordedAircraft aircraft:
                detailsTextBox.Text = FormatAircraft(aircraft);
                OpenEditorForObject(aircraft);
                _tabControl?.SelectTab(1);
                break;

            case RecordedObject obj:
                detailsTextBox.Text = FormatObject(obj);
                OpenEditorForObject(obj);
                _tabControl?.SelectTab(1);
                break;

            case RecordedFrame frame:
                detailsTextBox.Text = FormatFrame(frame, variableLookup);
                _tabControl?.SelectTab(0);
                break;

            default:
                detailsTextBox.Text = string.Empty;
                break;
        }
    }

    private static string FormatAircraft(RecordedAircraft aircraft)
    {
        StringBuilder sb = new();
        sb.AppendLine("Type: Aircraft");
        sb.AppendLine($"Callsign: {aircraft.Callsign}");
        sb.AppendLine($"Nickname: {aircraft.Nickname}");
        sb.AppendLine($"Plane: {aircraft.Plane}");
        sb.AppendLine($"Model: {aircraft.Model}");
        sb.AppendLine($"TypeRole: {aircraft.TypeRole} ({TypeRoleToText(aircraft.TypeRole)})");
        sb.AppendLine($"Frames: {aircraft.Frames.Count}");
        if (!string.IsNullOrEmpty(aircraft.Livery))
        {
            sb.AppendLine($"Livery: {aircraft.Livery}");
        }

        return sb.ToString();
    }

    private static string FormatObject(RecordedObject obj)
    {
        StringBuilder sb = new();
        sb.AppendLine("Type: Object");
        sb.AppendLine($"Model: {obj.Model}");
        sb.AppendLine($"TypeRole: {obj.TypeRole}");
        sb.AppendLine($"Frames: {obj.Frames.Count}");
        if (!string.IsNullOrEmpty(obj.Livery))
        {
            sb.AppendLine($"Livery: {obj.Livery}");
        }

        return sb.ToString();
    }

    private static string FormatFrame(RecordedFrame frame, VariableLookup lookup)
    {
        StringBuilder sb = new();
        sb.AppendLine($"Type: {frame.Type}");
        sb.AppendLine($"Time: {frame.Time:0.000}");

        switch (frame)
        {
            case ObjectPositionFrame op:
                sb.AppendLine($"Latitude: {op.Latitude}");
                sb.AppendLine($"Longitude: {op.Longitude}");
                sb.AppendLine($"Altitude: {op.Altitude}");
                sb.AppendLine($"Pitch: {op.Pitch}");
                sb.AppendLine($"Bank: {op.Bank}");
                sb.AppendLine($"Heading: {op.Heading}");
                sb.AppendLine($"VelocityX: {op.VelocityX}");
                sb.AppendLine($"VelocityY: {op.VelocityY}");
                sb.AppendLine($"VelocityZ: {op.VelocityZ}");
                sb.AppendLine($"AngularVelocityX: {op.AngularVelocityX}");
                sb.AppendLine($"AngularVelocityY: {op.AngularVelocityY}");
                sb.AppendLine($"AngularVelocityZ: {op.AngularVelocityZ}");
                sb.AppendLine($"AccelerationX: {op.AccelerationX}");
                sb.AppendLine($"AccelerationY: {op.AccelerationY}");
                sb.AppendLine($"AccelerationZ: {op.AccelerationZ}");
                sb.AppendLine($"Height: {op.Height}");
                sb.AppendLine($"Ground: {op.Ground}");
                sb.AppendLine($"ElevationCorrection: {op.ElevationCorrection}");
                break;
            case AircraftPositionFrame ap:
                sb.AppendLine($"Latitude: {ap.Latitude}");
                sb.AppendLine($"Longitude: {ap.Longitude}");
                sb.AppendLine($"Altitude: {ap.Altitude}");
                sb.AppendLine($"Pitch: {ap.Pitch}");
                sb.AppendLine($"Bank: {ap.Bank}");
                sb.AppendLine($"Heading: {ap.Heading}");
                sb.AppendLine($"VelocityX: {ap.VelocityX}");
                sb.AppendLine($"VelocityY: {ap.VelocityY}");
                sb.AppendLine($"VelocityZ: {ap.VelocityZ}");
                sb.AppendLine($"AngularVelocityX: {ap.AngularVelocityX}");
                sb.AppendLine($"AngularVelocityY: {ap.AngularVelocityY}");
                sb.AppendLine($"AngularVelocityZ: {ap.AngularVelocityZ}");
                sb.AppendLine($"AccelerationX: {ap.AccelerationX}");
                sb.AppendLine($"AccelerationY: {ap.AccelerationY}");
                sb.AppendLine($"AccelerationZ: {ap.AccelerationZ}");
                sb.AppendLine($"RudderRaw: {ap.RudderRaw}");
                sb.AppendLine($"ElevatorRaw: {ap.ElevatorRaw}");
                sb.AppendLine($"AileronRaw: {ap.AileronRaw}");
                sb.AppendLine($"BrakeLeftRaw: {ap.BrakeLeftRaw}");
                sb.AppendLine($"BrakeRightRaw: {ap.BrakeRightRaw}");
                sb.AppendLine($"Elevation: {ap.Elevation}");
                sb.AppendLine($"Ground: {ap.Ground}");
                sb.AppendLine($"ElevationCorrection: {ap.ElevationCorrection}");
                break;
            case SimEventFrame simEvent:
                sb.AppendLine($"EventId: {simEvent.EventId}");
                sb.AppendLine($"Data: {simEvent.Data}");
                break;
            case IntegerVariablesFrame intVars:
                sb.AppendLine("Variables:");
                AppendVariables(sb, intVars.Variables.Select(v => $"  {v.Key} ({lookup.Resolve(v.Key)}) = {v.Value}"));
                break;
            case FloatVariablesFrame floatVars:
                sb.AppendLine("Variables:");
                AppendVariables(sb, floatVars.Variables.Select(v => $"  {v.Key} ({lookup.Resolve(v.Key)}) = {v.Value}"));
                break;
            case String8VariablesFrame stringVars:
                sb.AppendLine("Variables:");
                AppendVariables(sb, stringVars.Variables.Select(v => $"  {v.Key} ({lookup.Resolve(v.Key)}) = {v.Value}"));
                break;
        }

        return sb.ToString();
    }

    private static void AppendVariables(StringBuilder sb, IEnumerable<string> values)
    {
        bool any = false;
        foreach (string value in values)
        {
            sb.AppendLine(value);
            any = true;
        }

        if (!any)
        {
            sb.AppendLine("  (none)");
        }
    }

    private static string TypeRoleToText(int typeRole)
    {
        return typeRole switch
        {
            1 => "SingleProp",
            2 => "TwinProp",
            3 => "Airliner",
            4 => "Rotorcraft",
            5 => "Glider",
            6 => "Fighter",
            7 => "Bomber",
            8 => "FourProp",
            _ => "Unknown"
        };
    }

    // ── Editor setup ─────────────────────────────────────────────────────────────

    private void SetupEditorComponents()
    {
        Color background = Color.FromArgb(15, 23, 42);
        Color surface    = Color.FromArgb(30, 41, 59);
        Color text       = Color.FromArgb(226, 232, 240);
        Color muted      = Color.FromArgb(148, 163, 184);
        Color accent     = Color.FromArgb(59, 130, 246);

        // Tab control replaces the raw details text box in Panel2.
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            BackColor = surface,
            ForeColor = text,
        };

        TabPage detailsPage = new("Details") { BackColor = surface, Padding = new Padding(0) };
        detailsTextBox.Dock = DockStyle.Fill;
        detailsPage.Controls.Add(detailsTextBox);

        TabPage editorPage = new("Editor") { BackColor = surface, Padding = new Padding(0) };

        _timeline = new TimelineControl { Dock = DockStyle.Top };
        _editorPanel = new VariableEditorPanel { Dock = DockStyle.Fill };

        _timeline.CurrentFrameChanged += OnTimelineCurrentFrameChanged;
        _timeline.RangeChanged        += OnTimelineRangeChanged;
        _editorPanel.SaveRequested    += OnSaveRequested;

        editorPage.Controls.Add(_editorPanel);  // Fill — added before Top so Top is processed first.
        editorPage.Controls.Add(_timeline);     // Top

        _tabControl.TabPages.Add(detailsPage);
        _tabControl.TabPages.Add(editorPage);

        splitContainer.Panel2.Controls.Remove(detailsTextBox);
        splitContainer.Panel2.Controls.Add(_tabControl);

        // Save button — placed to the right of the Open button.
        _saveButton = new Button
        {
            Text = "Save...",
            Enabled = false,
            Size = new Size(80, 27),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _saveButton.FlatAppearance.BorderSize = 0;
        _saveButton.Location = new Point(openButton.Left - _saveButton.Width - 8, openButton.Top);
        _saveButton.Click += OnSaveRequested;
        Controls.Add(_saveButton);
        _saveButton.BringToFront();
    }

    private void OpenEditorForObject(RecordedObject obj)
    {
        _editSession = new RecordingEditSession(obj);
        _editSession.Changed += (_, _) => UpdateSaveButtonState();

        var timelineFrames = obj.Frames.Select((f, i) => (
            Index: i,
            Time: f.Time,
            IsVariable: f.Type is FrameType.IntegerVariables
                                  or FrameType.FloatVariables
                                  or FrameType.String8Variables
        ));

        double duration = obj.Frames.Count > 0 ? obj.Frames[^1].Time : 0;
        _timeline?.SetFrames(timelineFrames, duration);
        _editorPanel?.SetSession(_editSession);
        UpdateSaveButtonState();
    }

    private void OnTimelineCurrentFrameChanged(object? sender, int frameIndex)
    {
        _editorPanel?.SetCurrentFrame(frameIndex);
    }

    private void OnTimelineRangeChanged(object? sender, EventArgs e)
    {
        if (_timeline == null || _editorPanel == null) return;

        if (_timeline.HasRange)
            _editorPanel.SetRange(_timeline.RangeStartIndex, _timeline.RangeEndIndex);
        else
            _editorPanel.ClearRange();
    }

    private void OnSaveRequested(object? sender, EventArgs e)
    {
        if (loadedRecording == null) return;

        using SaveFileDialog sfd = new()
        {
            Filter = "JoinFS recordings|*.jfs|All files|*.*",
            DefaultExt = "jfs",
            Title = "Save Modified Recording",
            FileName = Path.GetFileNameWithoutExtension(fileTextBox.Text) + "_edited.jfs",
            InitialDirectory = Path.GetDirectoryName(fileTextBox.Text),
        };

        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        // Commit all pending overlays into the in-memory data structure.
        _editSession?.CommitEditsToObject();

        try
        {
            RecordingWriter.Write(loadedRecording, sfd.FileName);
        }
        catch (NotImplementedException)
        {
            MessageBox.Show(
                this,
                "Edits have been committed to the in-memory recording.\n\n" +
                "Implement RecordingWriter.Write() to complete binary serialisation.",
                "Save — Writer Not Yet Implemented",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateSaveButtonState()
    {
        if (_saveButton != null)
            _saveButton.Enabled = loadedRecording != null;
    }
}
