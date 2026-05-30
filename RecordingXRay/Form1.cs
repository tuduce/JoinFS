using System.Text;

namespace RecordingXRay;

public partial class Form1 : Form
{
    private RecordingFile? loadedRecording;
    private readonly VariableLookup variableLookup = VariableLookup.Create();

    public Form1()
    {
        InitializeComponent();
        UpdateSummary(null);
        detailsTextBox.Text = $"Open a JoinFS recording file to inspect its content.{Environment.NewLine}Variable lookup entries loaded: {variableLookup.Count}.";
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
        detailsTextBox.Text = e.Node?.Tag switch
        {
            RecordedAircraft aircraft => FormatAircraft(aircraft),
            RecordedObject obj => FormatObject(obj),
            RecordedFrame frame => FormatFrame(frame, variableLookup),
            _ => string.Empty
        };
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
}
