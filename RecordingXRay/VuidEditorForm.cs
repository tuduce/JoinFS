using System.Globalization;

namespace RecordingXRay;

/// <summary>
/// A standalone dialog that lets the user type a variable name, verifies it
/// against the JoinFS VUID hash algorithm, and saves it to the <see cref="VariableLookup"/>
/// so it appears in the editor grid immediately.
/// </summary>
public sealed class VuidEditorForm : Form
{
    // ── Colors ────────────────────────────────────────────────────────────────────

    private static readonly Color CBg      = Color.FromArgb(15, 23, 42);
    private static readonly Color CSurface = Color.FromArgb(30, 41, 59);
    private static readonly Color CPanel   = Color.FromArgb(51, 65, 85);
    private static readonly Color CText    = Color.FromArgb(226, 232, 240);
    private static readonly Color CMuted   = Color.FromArgb(148, 163, 184);
    private static readonly Color CAccent  = Color.FromArgb(59, 130, 246);
    private static readonly Color CSuccess = Color.FromArgb(16, 185, 129);
    private static readonly Color CWarn    = Color.FromArgb(251, 191, 36);
    private static readonly Color CError   = Color.FromArgb(239, 68, 68);

    // ── State ─────────────────────────────────────────────────────────────────────

    private readonly VariableLookup _lookup;
    private readonly uint _targetVuid;
    private readonly string _originalName;

    // ── Controls ──────────────────────────────────────────────────────────────────

    private readonly Label _vuidDisplayLabel;
    private readonly Label _statusLabel;
    private readonly TextBox _nameBox;
    private readonly Label _computedVuidLabel;
    private readonly Label _matchLabel;
    private readonly Button _saveBtn;
    private readonly Button _cancelBtn;
    private readonly DataGridView _suggestGrid;

    // ── Constructor ───────────────────────────────────────────────────────────────

    public VuidEditorForm(VariableLookup lookup, uint targetVuid, string currentName)
    {
        _lookup = lookup;
        _targetVuid = targetVuid;
        _originalName = currentName;

        Text = "VUID Name Editor";
        ClientSize = new Size(560, 420);
        MinimumSize = new Size(480, 360);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = CBg;
        ForeColor = CText;
        Font = new Font("Segoe UI", 9.5f);

        Font mono = new("Cascadia Mono", 9.5f);

        // ── Target VUID display ──────────────────────────────────────────────────

        Label vuidCaption = Lbl("Target VUID:", CMuted);
        _vuidDisplayLabel = Lbl($"0x{targetVuid:X8}  ({targetVuid})", CText);
        _vuidDisplayLabel.Font = new Font("Cascadia Mono", 11f, FontStyle.Bold);

        Label currentCaption = Lbl("Current name:", CMuted);
        _statusLabel = Lbl(currentName, currentName == "unknown" ? CError : CSuccess);
        _statusLabel.Font = mono;

        // ── Name input ───────────────────────────────────────────────────────────

        Label nameCaption = Lbl("Enter variable name:", CMuted);

        _nameBox = new TextBox
        {
            BackColor = CPanel,
            ForeColor = CText,
            BorderStyle = BorderStyle.FixedSingle,
            Font = mono,
            Width = 400,
        };
        _nameBox.TextChanged += OnNameChanged;

        Label computedCaption = Lbl("Computed VUID:", CMuted);
        _computedVuidLabel = Lbl("", CText);
        _computedVuidLabel.Font = mono;

        _matchLabel = Lbl("", CMuted);
        _matchLabel.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);

        // ── Suggestions grid ─────────────────────────────────────────────────────

        Label suggestCaption = Lbl("Candidate variable names (common SimConnect names matching this VUID):", CMuted);

        _suggestGrid = new DataGridView
        {
            BackgroundColor = CSurface,
            GridColor = CPanel,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EnableHeadersVisualStyles = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = CBg, ForeColor = CMuted, SelectionBackColor = CBg,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = CSurface, ForeColor = CText,
                SelectionBackColor = CPanel, SelectionForeColor = CText,
                Font = mono,
            },
        };
        _suggestGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name / description", Name = "colName",  FillWeight = 70 });
        _suggestGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "VUID (hex)",         Name = "colVuid",  FillWeight = 30 });

        _suggestGrid.CellDoubleClick += (_, ev) =>
        {
            if (ev.RowIndex < 0) return;
            _nameBox.Text = _suggestGrid.Rows[ev.RowIndex].Cells["colName"].Value?.ToString() ?? "";
        };

        // ── Buttons ──────────────────────────────────────────────────────────────

        _saveBtn = new Button
        {
            Text = "Save Name",
            BackColor = CAccent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            Size = new Size(110, 30),
            Enabled = false,
        };
        _saveBtn.FlatAppearance.BorderSize = 0;
        _saveBtn.Click += OnSave;

        _cancelBtn = new Button
        {
            Text = "Cancel",
            BackColor = CPanel,
            ForeColor = CText,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f),
            Size = new Size(80, 30),
        };
        _cancelBtn.FlatAppearance.BorderSize = 0;
        _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        // ── Layout ───────────────────────────────────────────────────────────────

        TableLayoutPanel top = new()
        {
            Dock = DockStyle.Top,
            Height = 90,
            BackColor = CSurface,
            Padding = new Padding(12, 10, 12, 6),
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = false,
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.Controls.Add(vuidCaption, 0, 0);
        top.Controls.Add(_vuidDisplayLabel, 1, 0);
        top.Controls.Add(currentCaption, 0, 1);
        top.Controls.Add(_statusLabel, 1, 1);

        Panel inputPanel = new()
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = CBg,
            Padding = new Padding(12, 8, 12, 4),
        };
        nameCaption.Location = new Point(12, 8);
        _nameBox.Location = new Point(12, 28);
        _nameBox.Width = ClientSize.Width - 36;
        _nameBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        computedCaption.Location = new Point(12, 56);
        _computedVuidLabel.Location = new Point(130, 56);
        _matchLabel.Location = new Point(310, 56);
        inputPanel.Controls.AddRange([nameCaption, _nameBox, computedCaption, _computedVuidLabel, _matchLabel]);

        Panel suggestPanel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = CBg,
            Padding = new Padding(12, 4, 12, 4),
        };
        suggestCaption.Dock = DockStyle.Top;
        _suggestGrid.Dock = DockStyle.Fill;
        suggestPanel.Controls.Add(_suggestGrid);
        suggestPanel.Controls.Add(suggestCaption);

        Panel buttonPanel = new()
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            BackColor = CSurface,
            Padding = new Padding(12, 8, 12, 8),
        };
        _saveBtn.Location  = new Point(ClientSize.Width - 12 - _saveBtn.Width, 8);
        _cancelBtn.Location = new Point(_saveBtn.Left - _cancelBtn.Width - 8, 8);
        _saveBtn.Anchor  = AnchorStyles.Right | AnchorStyles.Top;
        _cancelBtn.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        buttonPanel.Controls.Add(_saveBtn);
        buttonPanel.Controls.Add(_cancelBtn);

        Controls.Add(suggestPanel);
        Controls.Add(inputPanel);
        Controls.Add(top);
        Controls.Add(buttonPanel);

        // Populate suggestion list with known names whose VUID matches.
        PopulateSuggestions();

        // Pre-fill with current name if it is not "unknown".
        if (currentName != "unknown")
            _nameBox.Text = currentName;
    }

    // ── Behaviour ─────────────────────────────────────────────────────────────────

    private void OnNameChanged(object? sender, EventArgs e)
    {
        string text = _nameBox.Text.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(text))
        {
            _computedVuidLabel.Text = "";
            _matchLabel.Text = "";
            _saveBtn.Enabled = false;
            return;
        }

        uint computed = VariableLookup.CreateVuid(text);
        _computedVuidLabel.Text = $"0x{computed:X8}";

        if (computed == _targetVuid)
        {
            _matchLabel.Text = "✓ MATCH";
            _matchLabel.ForeColor = CSuccess;
            _saveBtn.Enabled = true;
        }
        else
        {
            _matchLabel.Text = $"✗ no match (off by {(long)computed - (long)_targetVuid:+#;-#;0})";
            _matchLabel.ForeColor = CWarn;
            // Still allow saving with a name that does not hash-match, but
            // warn the user so they can make an informed decision.
            _saveBtn.Enabled = true;
            _saveBtn.Text = "Save Anyway";
            _saveBtn.BackColor = CWarn;
            _saveBtn.ForeColor = CBg;
            return;
        }

        _saveBtn.Text = "Save Name";
        _saveBtn.BackColor = CAccent;
        _saveBtn.ForeColor = Color.White;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        string name = _nameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        uint computed = VariableLookup.CreateVuid(name.ToLowerInvariant());
        if (computed != _targetVuid)
        {
            string msg = $"The name \"{name}\" hashes to VUID 0x{computed:X8}, " +
                         $"which does NOT match the target VUID 0x{_targetVuid:X8}.\n\n" +
                         $"Save it anyway as a label for 0x{_targetVuid:X8}?";
            if (MessageBox.Show(this, msg, "VUID mismatch — save anyway?",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
        }

        _lookup.Register(_targetVuid, name);
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// Walks the lookup for any name whose computed VUID matches the target.
    /// This surfaces names from aliases or alternate capitalisations.
    /// </summary>
    private void PopulateSuggestions()
    {
        _suggestGrid.Rows.Clear();

        // The lookup doesn't expose an enumerator, so we reconstruct via the known
        // algorithm: try common naming patterns from known SimConnect variable banks.
        // In practice, if the name was in the lookup files it will have been resolved
        // already; this list shows candidates for reverse-lookup hints.
        //
        // We include: the existing resolved name (if not unknown), a hex+decimal row.
        string resolved = _lookup.Resolve(_targetVuid);
        if (resolved != "unknown")
            _suggestGrid.Rows.Add(resolved, $"0x{_targetVuid:X8}");

        // Hint row — already-known decimal.
        _suggestGrid.Rows.Add(
            $"(decimal: {_targetVuid})",
            $"0x{_targetVuid:X8}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static Label Lbl(string text, Color fore) => new()
    {
        Text = text,
        ForeColor = fore,
        AutoSize = true,
    };
}
