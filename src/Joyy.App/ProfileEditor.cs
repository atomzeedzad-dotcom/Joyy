using Joyy.Core;

namespace Joyy.App;

/// <summary>
/// Profiles tab: the global profile plus per-game profiles. Edits are kept in memory until
/// Save, which validates and writes config.json through the save callback.
/// </summary>
internal sealed class ProfileEditor : UserControl
{
    static readonly (string Label, X360Buttons Value)[] Targets =
    {
        ("None", X360Buttons.None),
        ("A", X360Buttons.A), ("B", X360Buttons.B), ("X", X360Buttons.X), ("Y", X360Buttons.Y),
        ("LB", X360Buttons.LeftShoulder), ("RB", X360Buttons.RightShoulder),
        ("LS (left stick click)", X360Buttons.LeftThumb), ("RS (right stick click)", X360Buttons.RightThumb),
        ("Start", X360Buttons.Start), ("Back", X360Buttons.Back), ("Guide", X360Buttons.Guide),
        ("D-pad Up", X360Buttons.DpadUp), ("D-pad Down", X360Buttons.DpadDown),
        ("D-pad Left", X360Buttons.DpadLeft), ("D-pad Right", X360Buttons.DpadRight),
    };

    static readonly Dictionary<PhysicalButton, string> PhysicalNames = new()
    {
        [PhysicalButton.Cross] = "Cross", [PhysicalButton.Circle] = "Circle",
        [PhysicalButton.Square] = "Square", [PhysicalButton.Triangle] = "Triangle",
        [PhysicalButton.L1] = "L1", [PhysicalButton.R1] = "R1",
        [PhysicalButton.L3] = "L3 (left stick click)", [PhysicalButton.R3] = "R3 (right stick click)",
        [PhysicalButton.Options] = "Options", [PhysicalButton.Create] = "Create",
        [PhysicalButton.PS] = "PS button", [PhysicalButton.Touchpad] = "Touchpad click",
        [PhysicalButton.Mute] = "Mute (microphone)",
        [PhysicalButton.DpadUp] = "D-pad Up", [PhysicalButton.DpadDown] = "D-pad Down",
        [PhysicalButton.DpadLeft] = "D-pad Left", [PhysicalButton.DpadRight] = "D-pad Right",
    };

    readonly Func<Profile, IReadOnlyList<Profile>, string?> _save;
    readonly List<Profile> _profiles = new(); // [0] is the global profile
    AppConfig _saved = new();
    int _current;
    bool _loading;
    bool _dirty;

    readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None };
    readonly Button _add = new() { Text = "Add game…", AutoSize = true };
    readonly Button _remove = new() { Text = "Remove", AutoSize = true };
    readonly Label _title = new() { AutoSize = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Theme.Text };
    readonly Label _subtitle = new() { AutoSize = true, ForeColor = Theme.Muted };
    readonly NumericUpDown _deadzone = Number(0, 90, 1, 0);
    readonly NumericUpDown _saturation = Number(10, 100, 1, 0);
    readonly NumericUpDown _threshold = Number(0, 90, 1, 0);
    readonly NumericUpDown _curve = Number(0.1m, 10, 0.1m, 2);
    readonly NumericUpDown _rumble = Number(0, 200, 10, 0);
    readonly CheckBox _invLX = Check("Invert left X"), _invLY = Check("Invert left Y");
    readonly CheckBox _invRX = Check("Invert right X"), _invRY = Check("Invert right Y");
    readonly CheckBox _nintendo = Check("Nintendo layout (swap A/B and X/Y)");
    readonly DataGridView _grid = new();
    readonly Button _saveButton = new() { Text = "Save", AutoSize = true };
    readonly Button _revert = new() { Text = "Revert", AutoSize = true };
    readonly Button _reset = new() { Text = "Reset this profile to defaults", AutoSize = true };
    readonly Label _message = new() { AutoSize = true, ForeColor = Theme.Bad, Padding = new Padding(8, 6, 0, 0) };

    public ProfileEditor(Func<Profile, IReadOnlyList<Profile>, string?> save)
    {
        _save = save;
        BackColor = Theme.Background;
        Padding = new Padding(12);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildList(), 0, 0);
        root.Controls.Add(BuildEditor(), 1, 0);
        Controls.Add(root);

        _list.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _list.SelectedIndex >= 0)
                ShowProfile(_list.SelectedIndex);
        };
        _add.Click += (_, _) => AddGame();
        _remove.Click += (_, _) => RemoveGame();
        _saveButton.Click += (_, _) => Save();
        _revert.Click += (_, _) => LoadConfig(_saved);
        _reset.Click += (_, _) => ResetCurrent();

        foreach (var n in new[] { _deadzone, _saturation, _threshold, _curve, _rumble })
            n.ValueChanged += (_, _) => OnEdited();
        foreach (var c in new[] { _invLX, _invLY, _invRX, _invRY })
            c.CheckedChanged += (_, _) => OnEdited();
        _nintendo.CheckedChanged += (_, _) => { OnEdited(); ReloadGrid(); };

        UpdateButtons();
    }

    public bool IsDirty => _dirty;

    public void LoadConfig(AppConfig config)
    {
        _saved = config;
        _profiles.Clear();
        _profiles.Add(config.Global);
        _profiles.AddRange(config.Games);
        _dirty = false;
        _message.Text = "";
        RefreshList(Math.Min(_current, _profiles.Count - 1));
        UpdateButtons();
    }

    /// <summary>The file changed on disk. Take it unless the user has unsaved edits.</summary>
    public void ExternalReload(AppConfig config)
    {
        if (_dirty)
        {
            _saved = config;
            _message.ForeColor = Theme.Warn;
            _message.Text = "config.json changed on disk. Save to keep your edits, or Revert to load the file.";
            return;
        }
        LoadConfig(config);
    }

    Control BuildList()
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(8), Margin = new Padding(0, 0, 12, 0) };
        var header = new Label { Text = "Profiles", Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Theme.Text };
        var hint = new Label
        {
            Text = "A game profile is used automatically while that game's window is in front.",
            Dock = DockStyle.Bottom, Height = 48, ForeColor = Theme.Muted,
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(_add);
        buttons.Controls.Add(_remove);
        card.Controls.Add(_list);
        card.Controls.Add(header);
        card.Controls.Add(buttons);
        card.Controls.Add(hint);
        return card;
    }

    Control BuildEditor()
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(14, 10, 14, 10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titles = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 0, 0, 6) };
        titles.Controls.Add(_title);
        titles.Controls.Add(_subtitle);
        layout.Controls.Add(titles, 0, 0);

        var inverts = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 2, 0, 0) };
        inverts.Controls.Add(_invLX, 0, 0);
        inverts.Controls.Add(_invRX, 1, 0);
        inverts.Controls.Add(_invLY, 0, 1);
        inverts.Controls.Add(_invRY, 1, 1);

        var groups = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };
        groups.Controls.Add(Group("Sticks",
            Row("Deadzone (%)", _deadzone),
            Row("Full tilt at (%)", _saturation),
            inverts));
        groups.Controls.Add(Group("Triggers (L2 / R2) and rumble",
            Row("Ignore first (%)", _threshold),
            Row("Curve (1 = linear)", _curve),
            Row("Rumble strength (%)", _rumble)));
        layout.Controls.Add(groups, 0, 1);

        var buttonsHeader = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
        buttonsHeader.Controls.Add(new Label { Text = "Buttons", AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Theme.Text, Padding = new Padding(0, 4, 16, 0) });
        buttonsHeader.Controls.Add(_nintendo);
        layout.Controls.Add(buttonsHeader, 0, 2);

        SetupGrid();
        layout.Controls.Add(_grid, 0, 3);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        actions.Controls.Add(_saveButton);
        actions.Controls.Add(_revert);
        actions.Controls.Add(_reset);
        actions.Controls.Add(_message);
        layout.Controls.Add(actions, 0, 4);

        card.Controls.Add(layout);
        return card;
    }

    void SetupGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Theme.Card;
        _grid.BorderStyle = BorderStyle.None;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "DualSense button", ReadOnly = true, FillWeight = 45 });
        _grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Game sees (Xbox 360)", FillWeight = 55, FlatStyle = FlatStyle.Flat });
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 1)
                OnEdited();
        };
        _grid.DataError += (_, e) => e.ThrowException = false;
    }

    void RefreshList(int select)
    {
        _loading = true;
        _list.Items.Clear();
        _list.Items.Add("All games (global)");
        foreach (var p in _profiles.Skip(1))
            _list.Items.Add($"{p.Name}   ({ProfileSelector.NormalizeProcessName(p.ProcessName)}.exe)");
        _loading = false;
        ShowProfile(Math.Max(0, select));
    }

    void ShowProfile(int index)
    {
        _current = index;
        _loading = true;
        _list.SelectedIndex = index;
        var p = _profiles[index];
        _title.Text = index == 0 ? "All games" : p.Name;
        _subtitle.Text = index == 0
            ? "Used for every game that has no profile of its own."
            : $"Used while {ProfileSelector.NormalizeProcessName(p.ProcessName)}.exe is the active window.";

        _deadzone.Value = Clamp(_deadzone, (decimal)p.StickDeadzone * 100);
        _saturation.Value = Clamp(_saturation, (decimal)p.StickOuterSaturation * 100);
        _threshold.Value = Clamp(_threshold, (decimal)p.TriggerThreshold * 100);
        _curve.Value = Clamp(_curve, (decimal)p.TriggerCurve);
        _rumble.Value = Clamp(_rumble, (decimal)p.RumbleScale * 100);
        _invLX.Checked = p.InvertLeftX;
        _invLY.Checked = p.InvertLeftY;
        _invRX.Checked = p.InvertRightX;
        _invRY.Checked = p.InvertRightY;
        _nintendo.Checked = p.NintendoLayout;
        LoadGrid(p);
        _loading = false;
        UpdateButtons();
    }

    void LoadGrid(Profile p)
    {
        var defaults = ButtonMap.Create(p.NintendoLayout);
        _grid.Rows.Clear();
        foreach (var b in Enum.GetValues<PhysicalButton>())
        {
            int row = _grid.Rows.Add();
            _grid.Rows[row].Tag = b;
            _grid.Rows[row].Cells[0].Value = PhysicalNames[b];
            var cell = (DataGridViewComboBoxCell)_grid.Rows[row].Cells[1];
            string defaultLabel = $"Default: {TargetLabel(defaults[b])}";
            cell.Items.Add(defaultLabel);
            foreach (var t in Targets)
                cell.Items.Add(t.Label);
            cell.Value = p.ButtonOverrides.TryGetValue(b, out var target) ? TargetLabel(target) : defaultLabel;
        }
    }

    void ReloadGrid()
    {
        if (_loading)
            return;
        _loading = true;
        LoadGrid(_profiles[_current]);
        _loading = false;
    }

    void OnEdited()
    {
        if (_loading)
            return;

        var p = ReadEditors(_profiles[_current]);
        try
        {
            p.Validate();
        }
        catch (ArgumentOutOfRangeException)
        {
            _message.ForeColor = Theme.Bad;
            _message.Text = "\"Full tilt at\" must be larger than the deadzone.";
            return;
        }

        _profiles[_current] = p;
        _dirty = true;
        _message.Text = "";
        UpdateButtons();
    }

    Profile ReadEditors(Profile basis)
    {
        var overrides = new Dictionary<PhysicalButton, X360Buttons>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is PhysicalButton b && row.Cells[1].Value is string label && !label.StartsWith("Default"))
                overrides[b] = Targets.First(t => t.Label == label).Value;
        }

        return basis with
        {
            StickDeadzone = (double)_deadzone.Value / 100,
            StickOuterSaturation = (double)_saturation.Value / 100,
            TriggerThreshold = (double)_threshold.Value / 100,
            TriggerCurve = (double)_curve.Value,
            RumbleScale = (double)_rumble.Value / 100,
            InvertLeftX = _invLX.Checked,
            InvertLeftY = _invLY.Checked,
            InvertRightX = _invRX.Checked,
            InvertRightY = _invRY.Checked,
            NintendoLayout = _nintendo.Checked,
            ButtonOverrides = overrides,
        };
    }

    void Save()
    {
        var config = _saved with { Global = _profiles[0], Games = _profiles.Skip(1).ToList() };
        string? error = _save(config.Global, config.Games);
        if (error is not null)
        {
            _message.ForeColor = Theme.Bad;
            _message.Text = error;
            return;
        }
        _saved = config;
        _dirty = false;
        _message.ForeColor = Theme.Good;
        _message.Text = "Saved. Changes are active now.";
        UpdateButtons();
    }

    void AddGame()
    {
        using var dialog = new AddGameDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string process = dialog.ProcessName;
        if (_profiles.Skip(1).Any(p => string.Equals(ProfileSelector.NormalizeProcessName(p.ProcessName), process, StringComparison.OrdinalIgnoreCase)))
        {
            _message.ForeColor = Theme.Bad;
            _message.Text = $"{process}.exe already has a profile.";
            return;
        }

        // New game profiles start from the global settings.
        _profiles.Add(_profiles[0] with { Name = dialog.DisplayName, ProcessName = process + ".exe" });
        _dirty = true;
        RefreshList(_profiles.Count - 1);
        _message.ForeColor = Theme.Warn;
        _message.Text = "New profile added. Press Save to keep it.";
    }

    void RemoveGame()
    {
        if (_current <= 0)
            return;
        _profiles.RemoveAt(_current);
        _dirty = true;
        RefreshList(_current - 1);
        UpdateButtons();
    }

    void ResetCurrent()
    {
        var p = _profiles[_current];
        _profiles[_current] = new Profile { Name = p.Name, ProcessName = p.ProcessName };
        _dirty = true;
        ShowProfile(_current);
    }

    void UpdateButtons()
    {
        _remove.Enabled = _current > 0;
        _saveButton.Enabled = _dirty;
        _revert.Enabled = _dirty;
    }

    static string TargetLabel(X360Buttons value)
    {
        foreach (var t in Targets)
        {
            if (t.Value == value)
                return t.Label;
        }
        return "None";
    }

    static decimal Clamp(NumericUpDown n, decimal v) => Math.Clamp(Math.Round(v, n.DecimalPlaces), n.Minimum, n.Maximum);

    static NumericUpDown Number(decimal min, decimal max, decimal step, int decimals) => new()
    {
        Minimum = min, Maximum = max, Increment = step, DecimalPlaces = decimals, Width = 70,
    };

    static CheckBox Check(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 4, 3, 2) };

    static Control Row(string label, Control input)
    {
        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        row.Controls.Add(new Label { Text = label, Width = 130, Padding = new Padding(0, 5, 0, 0), ForeColor = Theme.Text });
        row.Controls.Add(input);
        return row;
    }

    static Control Group(string title, params Control[] children)
    {
        var box = new GroupBox { Text = title, AutoSize = true, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0, 0, 12, 6), ForeColor = Theme.Text };
        var stack = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, WrapContents = false };
        stack.Controls.AddRange(children);
        box.Controls.Add(stack);
        return box;
    }
}
