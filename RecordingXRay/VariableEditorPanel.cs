using System.Globalization;

namespace RecordingXRay;

/// <summary>
/// Displays the variable contents of the current timeline position for a
/// <see cref="RecordingEditSession"/> and exposes inline single-frame editing
/// and range-apply operations.
/// </summary>
public sealed class VariableEditorPanel : UserControl
{
    // ── Colors ───────────────────────────────────────────────────────────────────

    private static readonly Color CBg      = Color.FromArgb(30, 41, 59);
    private static readonly Color CSurface = Color.FromArgb(51, 65, 85);
    private static readonly Color CHeader  = Color.FromArgb(15, 23, 42);
    private static readonly Color CText    = Color.FromArgb(226, 232, 240);
    private static readonly Color CMuted   = Color.FromArgb(148, 163, 184);
    private static readonly Color CAccent  = Color.FromArgb(59, 130, 246);
    private static readonly Color CDirty   = Color.FromArgb(251, 191, 36);
    private static readonly Color CSuccess = Color.FromArgb(16, 185, 129);
    private static readonly Color CDirtyBg = Color.FromArgb(38, 251, 191, 36);
    private static readonly Color CError   = Color.FromArgb(239, 68, 68);

    private const int ColValueIndex = 2;

    // ── Data ─────────────────────────────────────────────────────────────────────

    private RecordingEditSession? _session;
    private readonly VariableLookup _lookup = VariableLookup.Create();
    private int _currentFrameIndex = -1;
    private int _rangeStart = -1;
    private int _rangeEnd = -1;
    private bool _suppressCellEvents;

    // Debounce grid refresh so rapid scrubbing stays responsive.
    private readonly System.Windows.Forms.Timer _debounce;

    // ── Controls ─────────────────────────────────────────────────────────────────

    private readonly Label _frameInfoLabel;
    private readonly DataGridView _grid;
    private readonly Panel _rangePanel;
    private readonly Label _rangeInfoLabel;
    private readonly RadioButton _constantRadio;
    private readonly RadioButton _lerpRadio;
    private readonly Label _startLabel;
    private readonly TextBox _startBox;
    private readonly Label _endLabel;
    private readonly TextBox _endBox;
    private readonly Button _applyBtn;
    private readonly Button _clearRangeBtn;

    // ── Row metadata ─────────────────────────────────────────────────────────────

    private record RowTag(uint VarId, int FrameIndex, VariableKind Kind);

    // ── Public API ───────────────────────────────────────────────────────────────

    public event EventHandler? SaveRequested;

    public VariableEditorPanel()
    {
        BackColor = CBg;
        ForeColor = CText;
        Dock = DockStyle.Fill;

        _debounce = new System.Windows.Forms.Timer { Interval = 80 };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RefreshGrid(); };

        Font ui = new("Segoe UI", 9.5f);
        Font mono = new("Cascadia Mono", 9f);

        // ── Frame info label ─────────────────────────────────────────────────────
        _frameInfoLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            BackColor = CHeader,
            ForeColor = CMuted,
            Font = ui,
            Text = "Select an aircraft or object in the tree to begin editing.",
            Padding = new Padding(6, 5, 0, 0),
            AutoEllipsis = true,
        };

        // ── DataGridView ─────────────────────────────────────────────────────────
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = CBg,
            GridColor = CSurface,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = CHeader,
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                SelectionBackColor = CHeader,
                SelectionForeColor = CMuted,
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = CBg,
                ForeColor = CText,
                SelectionBackColor = CSurface,
                SelectionForeColor = CText,
                Font = mono,
            },
        };

        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Variable", Name = "colName", ReadOnly = true, FillWeight = 38 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type", Name = "colType", ReadOnly = true, FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", Name = "colValue", ReadOnly = false, FillWeight = 38 });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "✎",
            Name = "colDirty",
            ReadOnly = true,
            FillWeight = 8,
            FalseValue = false,
            TrueValue = true,
        });

        _grid.CellEndEdit += OnCellEndEdit;
        _grid.RowPrePaint += (_, e) => e.PaintParts &= ~DataGridViewPaintParts.Focus;
        _grid.SelectionChanged += (_, _) => PopulateRangeStartValue();

        // ── Range panel ──────────────────────────────────────────────────────────
        _rangePanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 72,
            BackColor = CHeader,
            Padding = new Padding(6),
        };

        _rangeInfoLabel = new Label
        {
            Text = "Range: not set",
            ForeColor = CMuted,
            Font = ui,
            AutoSize = true,
            Location = new Point(6, 8),
        };

        _clearRangeBtn = StyledButton("Clear Range", CSurface);
        _clearRangeBtn.Location = new Point(6, 38);
        _clearRangeBtn.Width = 100;
        _clearRangeBtn.Click += (_, _) => ClearRange();

        _constantRadio = new RadioButton
        {
            Text = "Constant",
            Checked = true,
            ForeColor = CText,
            Font = ui,
            AutoSize = true,
            BackColor = Color.Transparent,
            Location = new Point(116, 40),
        };
        _constantRadio.CheckedChanged += (_, _) => UpdateRangeModeUI();

        _lerpRadio = new RadioButton
        {
            Text = "Linear Interp",
            ForeColor = CText,
            Font = ui,
            AutoSize = true,
            BackColor = Color.Transparent,
            Location = new Point(210, 40),
        };

        _startLabel = new Label
        {
            Text = "Value:",
            ForeColor = CMuted,
            Font = ui,
            AutoSize = true,
            Location = new Point(318, 42),
        };

        _startBox = StyledTextBox(mono);
        _startBox.Location = new Point(362, 38);
        _startBox.Width = 110;

        _endLabel = new Label
        {
            Text = "→",
            ForeColor = CMuted,
            Font = ui,
            AutoSize = true,
            Location = new Point(480, 42),
            Visible = false,
        };

        _endBox = StyledTextBox(mono);
        _endBox.Location = new Point(498, 38);
        _endBox.Width = 110;
        _endBox.Visible = false;

        _applyBtn = StyledButton("Apply to Range", CAccent);
        _applyBtn.Location = new Point(616, 38);
        _applyBtn.Width = 130;
        _applyBtn.Click += (_, _) => ApplyRange();

        _rangePanel.Controls.AddRange([
            _rangeInfoLabel, _clearRangeBtn,
            _constantRadio, _lerpRadio,
            _startLabel, _startBox, _endLabel, _endBox,
            _applyBtn,
        ]);

        // Add in reverse layout order: Bottom first, then Top, then Fill.
        Controls.Add(_rangePanel);
        Controls.Add(_frameInfoLabel);
        Controls.Add(_grid);
    }

    public void SetSession(RecordingEditSession? session)
    {
        _session = session;
        _currentFrameIndex = -1;
        _rangeStart = -1;
        _rangeEnd = -1;
        UpdateRangeLabel();

        if (session == null)
        {
            _grid.Rows.Clear();
            _frameInfoLabel.Text = "Select an aircraft or object in the tree to begin editing.";
            _frameInfoLabel.ForeColor = CMuted;
            return;
        }

        _frameInfoLabel.Text = "Scrub the timeline to select a frame.";
        _frameInfoLabel.ForeColor = CMuted;
    }

    public void SetCurrentFrame(int frameIndex)
    {
        _currentFrameIndex = frameIndex;
        _debounce.Stop();
        _debounce.Start();
    }

    public void SetRange(int start, int end)
    {
        _rangeStart = start;
        _rangeEnd = end;
        UpdateRangeLabel();
    }

    public void ClearRange()
    {
        _rangeStart = -1;
        _rangeEnd = -1;
        UpdateRangeLabel();
    }

    // ── Grid population ──────────────────────────────────────────────────────────

    private void RefreshGrid()
    {
        if (_session == null || _currentFrameIndex < 0)
        {
            _grid.Rows.Clear();
            return;
        }

        RecordedObject src = _session.Source;
        double t = _currentFrameIndex < src.Frames.Count ? src.Frames[_currentFrameIndex].Time : 0;
        bool frameDirty = _session.IsFrameDirty(_currentFrameIndex);

        _frameInfoLabel.ForeColor = frameDirty ? CDirty : CMuted;
        _frameInfoLabel.Text = frameDirty
            ? $"Frame {_currentFrameIndex}  T={t:0.000}s  [EDITED]"
            : $"Frame {_currentFrameIndex}  T={t:0.000}s";

        _suppressCellEvents = true;
        _grid.SuspendLayout();
        _grid.Rows.Clear();

        int intFi   = _session.FindMostRecentFrameOfType(_currentFrameIndex, FrameType.IntegerVariables);
        int floatFi = _session.FindMostRecentFrameOfType(_currentFrameIndex, FrameType.FloatVariables);
        int strFi   = _session.FindMostRecentFrameOfType(_currentFrameIndex, FrameType.String8Variables);

        if (intFi >= 0 && src.Frames[intFi] is IntegerVariablesFrame intFrame)
        {
            foreach (uint varId in intFrame.Variables.Keys)
            {
                string val = _session.GetEffectiveInt(intFi, varId)?.ToString(CultureInfo.InvariantCulture) ?? "";
                bool dirty = _session.IsIntVarDirty(intFi, varId);
                AddRow(varId, "Int", val, dirty, new RowTag(varId, intFi, VariableKind.Integer));
            }
        }

        if (floatFi >= 0 && src.Frames[floatFi] is FloatVariablesFrame floatFrame)
        {
            foreach (uint varId in floatFrame.Variables.Keys)
            {
                string val = _session.GetEffectiveFloat(floatFi, varId)?.ToString("G7", CultureInfo.InvariantCulture) ?? "";
                bool dirty = _session.IsFloatVarDirty(floatFi, varId);
                AddRow(varId, "Float", val, dirty, new RowTag(varId, floatFi, VariableKind.Float));
            }
        }

        if (strFi >= 0 && src.Frames[strFi] is String8VariablesFrame strFrame)
        {
            foreach (uint varId in strFrame.Variables.Keys)
            {
                string val = _session.GetEffectiveString(strFi, varId) ?? "";
                bool dirty = _session.IsStringVarDirty(strFi, varId);
                AddRow(varId, "String", val, dirty, new RowTag(varId, strFi, VariableKind.String8));
            }
        }

        if (_grid.Rows.Count == 0)
        {
            _frameInfoLabel.Text += "  (no variable data at this position)";
        }

        _grid.ResumeLayout();
        _suppressCellEvents = false;
    }

    private void AddRow(uint varId, string typeName, string value, bool dirty, RowTag tag)
    {
        int ri = _grid.Rows.Add(_lookup.Resolve(varId), typeName, value, dirty);
        DataGridViewRow row = _grid.Rows[ri];
        row.Tag = tag;

        if (dirty)
            row.DefaultCellStyle.BackColor = CDirtyBg;
    }

    // ── Cell editing ─────────────────────────────────────────────────────────────

    private void OnCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_suppressCellEvents || e.ColumnIndex != ColValueIndex || _session == null) return;
        if (_grid.Rows[e.RowIndex].Tag is not RowTag tag) return;

        string newText = _grid.Rows[e.RowIndex].Cells[ColValueIndex].Value?.ToString() ?? "";

        try
        {
            switch (tag.Kind)
            {
                case VariableKind.Integer:
                    if (!int.TryParse(newText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                    { ShowError($"'{newText}' is not a valid integer."); RestoreCell(e.RowIndex, tag); return; }
                    _session.SetInt(tag.FrameIndex, tag.VarId, iv);
                    break;

                case VariableKind.Float:
                    if (!float.TryParse(newText, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv))
                    { ShowError($"'{newText}' is not a valid float."); RestoreCell(e.RowIndex, tag); return; }
                    _session.SetFloat(tag.FrameIndex, tag.VarId, fv);
                    break;

                case VariableKind.String8:
                    _session.SetString(tag.FrameIndex, tag.VarId, newText);
                    break;
            }

            _grid.Rows[e.RowIndex].Cells["colDirty"].Value = true;
            _grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = CDirtyBg;
            ClearError();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            RestoreCell(e.RowIndex, tag);
        }
    }

    private void RestoreCell(int rowIndex, RowTag tag)
    {
        string original = tag.Kind switch
        {
            VariableKind.Integer => _session?.GetEffectiveInt(tag.FrameIndex, tag.VarId)
                                             ?.ToString(CultureInfo.InvariantCulture) ?? "",
            VariableKind.Float   => _session?.GetEffectiveFloat(tag.FrameIndex, tag.VarId)
                                             ?.ToString("G7", CultureInfo.InvariantCulture) ?? "",
            VariableKind.String8 => _session?.GetEffectiveString(tag.FrameIndex, tag.VarId) ?? "",
            _                    => ""
        };
        _suppressCellEvents = true;
        _grid.Rows[rowIndex].Cells[ColValueIndex].Value = original;
        _suppressCellEvents = false;
    }

    // ── Range operations ─────────────────────────────────────────────────────────

    private void ApplyRange()
    {
        if (_session == null) { ShowError("No session loaded."); return; }

        bool hasRange = _rangeStart >= 0 && _rangeEnd >= 0 && _rangeEnd >= _rangeStart;
        if (!hasRange) { ShowError("Set a range on the timeline before applying (right-click the timeline)."); return; }

        if (_grid.SelectedRows.Count == 0) { ShowError("Select a variable row first."); return; }

        if (_grid.SelectedRows[0].Tag is not RowTag tag) return;

        RangeEditMode mode = _lerpRadio.Checked
            ? RangeEditMode.LinearInterpolation
            : RangeEditMode.ConstantOverwrite;

        string sv = _startBox.Text.Trim();
        string ev = _endBox.Text.Trim();

        try
        {
            switch (tag.Kind)
            {
                case VariableKind.Integer:
                {
                    if (!int.TryParse(sv, NumberStyles.Integer, CultureInfo.InvariantCulture, out int si))
                    { ShowError("Start value must be a valid integer."); return; }
                    int ei = si;
                    if (mode == RangeEditMode.LinearInterpolation)
                    {
                        if (!int.TryParse(ev, NumberStyles.Integer, CultureInfo.InvariantCulture, out ei))
                        { ShowError("End value must be a valid integer."); return; }
                    }
                    _session.ApplyRangeInt(_rangeStart, _rangeEnd, tag.VarId, mode, si, ei);
                    break;
                }

                case VariableKind.Float:
                {
                    if (!float.TryParse(sv, NumberStyles.Float, CultureInfo.InvariantCulture, out float sf))
                    { ShowError("Start value must be a valid float."); return; }
                    float ef = sf;
                    if (mode == RangeEditMode.LinearInterpolation)
                    {
                        if (!float.TryParse(ev, NumberStyles.Float, CultureInfo.InvariantCulture, out ef))
                        { ShowError("End value must be a valid float."); return; }
                    }
                    _session.ApplyRangeFloat(_rangeStart, _rangeEnd, tag.VarId, mode, sf, ef);
                    break;
                }

                case VariableKind.String8:
                    if (mode == RangeEditMode.LinearInterpolation)
                    { ShowError("Linear interpolation is not available for string variables."); return; }
                    _session.ApplyRangeString(_rangeStart, _rangeEnd, tag.VarId, sv);
                    break;
            }

            ClearError();
            _debounce.Stop();
            RefreshGrid();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    // Pre-fill the start value box with the currently selected variable's value.
    private void PopulateRangeStartValue()
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not RowTag)
            return;

        _startBox.Text = _grid.SelectedRows[0].Cells["colValue"].Value?.ToString() ?? "";
    }

    // ── UI helpers ───────────────────────────────────────────────────────────────

    private void UpdateRangeLabel()
    {
        bool has = _rangeStart >= 0 && _rangeEnd >= 0 && _rangeEnd >= _rangeStart;
        if (!has)
        {
            _rangeInfoLabel.Text = "Range: not set  (right-click timeline to set)";
            _rangeInfoLabel.ForeColor = CMuted;
            _applyBtn.Enabled = false;
        }
        else
        {
            double ts = _session != null && _rangeStart < _session.Source.Frames.Count
                ? _session.Source.Frames[_rangeStart].Time : 0;
            double te = _session != null && _rangeEnd < _session.Source.Frames.Count
                ? _session.Source.Frames[_rangeEnd].Time : 0;
            _rangeInfoLabel.Text = $"Range: frames {_rangeStart} → {_rangeEnd}  ({ts:0.00}s → {te:0.00}s)";
            _rangeInfoLabel.ForeColor = CSuccess;
            _applyBtn.Enabled = true;
        }
    }

    private void UpdateRangeModeUI()
    {
        bool lerp = _lerpRadio.Checked;
        _startLabel.Text = lerp ? "Start:" : "Value:";
        _endLabel.Visible = lerp;
        _endBox.Visible = lerp;
    }

    private void ShowError(string message)
    {
        _frameInfoLabel.ForeColor = CError;
        _frameInfoLabel.Text = $"⚠ {message}";
    }

    private void ClearError()
    {
        // Re-trigger the normal label from the current state.
        if (_session != null && _currentFrameIndex >= 0)
        {
            bool dirty = _session.IsFrameDirty(_currentFrameIndex);
            double t = _currentFrameIndex < _session.Source.Frames.Count
                ? _session.Source.Frames[_currentFrameIndex].Time : 0;
            _frameInfoLabel.ForeColor = dirty ? CDirty : CMuted;
            _frameInfoLabel.Text = dirty
                ? $"Frame {_currentFrameIndex}  T={t:0.000}s  [EDITED]"
                : $"Frame {_currentFrameIndex}  T={t:0.000}s";
        }
    }

    private static Button StyledButton(string text, Color back) => new()
    {
        Text = text,
        BackColor = back,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Height = 26,
        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
    };

    private static TextBox StyledTextBox(Font font) => new()
    {
        BackColor = Color.FromArgb(51, 65, 85),
        ForeColor = Color.FromArgb(226, 232, 240),
        BorderStyle = BorderStyle.FixedSingle,
        Font = font,
    };
}
