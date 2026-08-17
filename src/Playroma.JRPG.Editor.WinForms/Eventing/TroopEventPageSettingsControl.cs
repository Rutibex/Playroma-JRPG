using Playroma.JRPG.Eventing;

namespace Playroma.JRPG.Editor.WinForms.Eventing;

/// <summary>Passive troop battle-event page settings editor with optional legacy Apply button.</summary>
public sealed class TroopEventPageSettingsControl : UserControl
{
    private readonly CheckBox _turnEnding = Check("Turn End");
    private readonly CheckBox _turnValid = Check("Turn");
    private readonly NumericUpDown _turnA = Number(0, 999999);
    private readonly NumericUpDown _turnB = Number(0, 999999);
    private readonly CheckBox _enemyValid = Check("Enemy HP ≤");
    private readonly NumericUpDown _enemyIndex = Number(0, 999);
    private readonly NumericUpDown _enemyHp = Number(0, 100);
    private readonly CheckBox _actorValid = Check("Actor HP ≤");
    private readonly NumericUpDown _actorId = Number(1, 999999);
    private readonly NumericUpDown _actorHp = Number(0, 100);
    private readonly CheckBox _switchValid = Check("Switch");
    private readonly NumericUpDown _switchId = Number(1, 999999);
    private readonly ComboBox _span = Combo(["Battle", "Turn", "Moment"]);
    private readonly Button _apply = new() { Text = "Apply Battle Event Settings", AutoSize = true };
    private TroopEventPageSettingsSnapshot? _snapshot;
    private bool _loading;

    public TroopEventPageSettingsControl()
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        Padding = new Padding(8);
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Add(table, _turnEnding, new Label(), new Label());
        Add(table, _turnValid, Pair(_turnA, new Label { Text = "+", AutoSize = true, Margin = new Padding(4, 7, 4, 0) }), _turnB);
        Add(table, _enemyValid, _enemyIndex, Pair(_enemyHp, new Label { Text = "%", AutoSize = true, Margin = new Padding(4, 7, 0, 0) }));
        Add(table, _actorValid, _actorId, Pair(_actorHp, new Label { Text = "%", AutoSize = true, Margin = new Padding(4, 7, 0, 0) }));
        Add(table, _switchValid, _switchId, new Label());
        Add(table, new Label { Text = "Span", AutoSize = true, Margin = new Padding(3, 8, 8, 3) }, _span, new Label());
        Add(table, _apply, new Label(), new Label());
        GroupBox conditions = new() { Text = "Conditions and Span", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        conditions.Controls.Add(table);
        Controls.Add(conditions);

        _apply.Click += (_, _) =>
        {
            if (_snapshot is not null) ApplyRequested?.Invoke(this, BuildSnapshot());
        };
        WireValueEvents(this);
    }

    public bool ShowApplyButton
    {
        get => _apply.Visible;
        set => _apply.Visible = value;
    }

    public TroopEventPageSettingsSnapshot? CurrentSnapshot => _snapshot is null ? null : BuildSnapshot();

    public event EventHandler<TroopEventPageSettingsSnapshot>? ApplyRequested;
    public event EventHandler<TroopEventPageSettingsSnapshot>? SnapshotChanged;

    public void LoadSnapshot(TroopEventPageSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _loading = true;
        try
        {
            _snapshot = snapshot;
            _turnEnding.Checked = snapshot.TurnEnding;
            _turnValid.Checked = snapshot.TurnValid;
            _turnA.Value = Clamp(_turnA, snapshot.TurnA);
            _turnB.Value = Clamp(_turnB, snapshot.TurnB);
            _enemyValid.Checked = snapshot.EnemyValid;
            _enemyIndex.Value = Clamp(_enemyIndex, snapshot.EnemyIndex);
            _enemyHp.Value = Clamp(_enemyHp, snapshot.EnemyHp);
            _actorValid.Checked = snapshot.ActorValid;
            _actorId.Value = Clamp(_actorId, snapshot.ActorId);
            _actorHp.Value = Clamp(_actorHp, snapshot.ActorHp);
            _switchValid.Checked = snapshot.SwitchValid;
            _switchId.Value = Clamp(_switchId, snapshot.SwitchId);
            _span.SelectedIndex = Math.Clamp(snapshot.Span, 0, _span.Items.Count - 1);
            UpdateEnabledStates();
        }
        finally
        {
            _loading = false;
        }
    }

    private TroopEventPageSettingsSnapshot BuildSnapshot()
    {
        TroopEventPageSettingsSnapshot source = _snapshot
            ?? throw new InvalidOperationException("No troop event page is loaded.");
        return source with
        {
            TurnEnding = _turnEnding.Checked,
            TurnValid = _turnValid.Checked,
            TurnA = (int)_turnA.Value,
            TurnB = (int)_turnB.Value,
            EnemyValid = _enemyValid.Checked,
            EnemyIndex = (int)_enemyIndex.Value,
            EnemyHp = (int)_enemyHp.Value,
            ActorValid = _actorValid.Checked,
            ActorId = (int)_actorId.Value,
            ActorHp = (int)_actorHp.Value,
            SwitchValid = _switchValid.Checked,
            SwitchId = (int)_switchId.Value,
            Span = Math.Max(0, _span.SelectedIndex)
        };
    }

    private void WireValueEvents(Control root)
    {
        foreach (Control control in root.Controls)
        {
            WireValueEvents(control);
            switch (control)
            {
                case CheckBox check:
                    check.CheckedChanged += (_, _) => RaiseSnapshotChanged();
                    break;
                case NumericUpDown number:
                    number.ValueChanged += (_, _) => RaiseSnapshotChanged();
                    break;
                case ComboBox combo:
                    combo.SelectedIndexChanged += (_, _) => RaiseSnapshotChanged();
                    break;
            }
        }
    }

    private void RaiseSnapshotChanged()
    {
        UpdateEnabledStates();
        if (_loading || _snapshot is null) return;
        SnapshotChanged?.Invoke(this, BuildSnapshot());
    }

    private void UpdateEnabledStates()
    {
        _turnA.Enabled = _turnValid.Checked;
        _turnB.Enabled = _turnValid.Checked;
        _enemyIndex.Enabled = _enemyValid.Checked;
        _enemyHp.Enabled = _enemyValid.Checked;
        _actorId.Enabled = _actorValid.Checked;
        _actorHp.Enabled = _actorValid.Checked;
        _switchId.Enabled = _switchValid.Checked;
    }

    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 7, 8, 3) };
    private static NumericUpDown Number(int min, int max) => new() { Minimum = min, Maximum = max, Width = 86 };
    private static ComboBox Combo(IEnumerable<string> values)
    {
        ComboBox result = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        result.Items.AddRange(values.Cast<object>().ToArray());
        result.SelectedIndex = result.Items.Count > 0 ? 0 : -1;
        return result;
    }
    private static Control Pair(Control first, Control second)
    {
        FlowLayoutPanel panel = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        panel.Controls.Add(first);
        panel.Controls.Add(second);
        return panel;
    }
    private static void Add(TableLayoutPanel table, Control first, Control second, Control third)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(first, 0, row);
        table.Controls.Add(second, 1, row);
        table.Controls.Add(third, 2, row);
    }
    private static decimal Clamp(NumericUpDown control, int value) => Math.Clamp(value, (int)control.Minimum, (int)control.Maximum);
}
