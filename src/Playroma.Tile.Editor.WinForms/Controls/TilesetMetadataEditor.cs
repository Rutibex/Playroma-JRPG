using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Editor.WinForms.Controls;

public sealed class TilesetMetadataEditor : UserControl
{
    private readonly ComboBox _passage = new();
    private readonly CheckedListBox _directions = new();
    private readonly CheckBox _ladder = new() { Text = "Ladder" };
    private readonly CheckBox _bush = new() { Text = "Bush" };
    private readonly CheckBox _counter = new() { Text = "Counter" };
    private readonly CheckBox _damage = new() { Text = "Damage floor" };
    private readonly NumericUpDown _terrainTag = new() { Minimum = 0, Maximum = 7 };
    private readonly TextBox _semantic = new();
    private readonly TextBox _footstep = new();
    private readonly TextBox _notes = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private bool _loading;
    private TileGameplayMetadata? _metadata;

    public TilesetMetadataEditor()
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        _passage.DropDownStyle = ComboBoxStyle.DropDownList;
        _passage.Items.AddRange(Enum.GetNames<PassageMode>());
        _directions.Items.AddRange(new object[] { "Down", "Left", "Right", "Up" });
        _directions.Height = 76;
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(panel, "Passage", _passage);
        AddRow(panel, "Blocked directions", _directions);
        var flags = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        flags.Controls.AddRange(new Control[] { _ladder, _bush, _counter, _damage });
        AddRow(panel, "Flags", flags);
        AddRow(panel, "Terrain tag", _terrainTag);
        AddRow(panel, "Semantic type", _semantic);
        AddRow(panel, "Footstep type", _footstep);
        _notes.Height = 90;
        AddRow(panel, "Notes", _notes);
        Controls.Add(panel);

        _passage.SelectedIndexChanged += (_, _) => Apply();
        _directions.ItemCheck += (_, _) => BeginInvoke(new Action(Apply));
        _ladder.CheckedChanged += (_, _) => Apply();
        _bush.CheckedChanged += (_, _) => Apply();
        _counter.CheckedChanged += (_, _) => Apply();
        _damage.CheckedChanged += (_, _) => Apply();
        _terrainTag.ValueChanged += (_, _) => Apply();
        _semantic.TextChanged += (_, _) => Apply();
        _footstep.TextChanged += (_, _) => Apply();
        _notes.TextChanged += (_, _) => Apply();
        Enabled = false;
    }

    public event EventHandler? MetadataChanged;

    public TileGameplayMetadata? SelectedMetadata
    {
        get => _metadata;
        set
        {
            _metadata = value;
            LoadValues();
        }
    }

    private void LoadValues()
    {
        _loading = true;
        try
        {
            Enabled = _metadata is not null;
            if (_metadata is null) return;
            _passage.SelectedItem = _metadata.Passage.ToString();
            SetDirection(0, DirectionPassageFlags.Down);
            SetDirection(1, DirectionPassageFlags.Left);
            SetDirection(2, DirectionPassageFlags.Right);
            SetDirection(3, DirectionPassageFlags.Up);
            _ladder.Checked = _metadata.Ladder;
            _bush.Checked = _metadata.Bush;
            _counter.Checked = _metadata.Counter;
            _damage.Checked = _metadata.DamageFloor;
            _terrainTag.Value = Math.Clamp(_metadata.TerrainTag, 0, 7);
            _semantic.Text = _metadata.SemanticType;
            _footstep.Text = _metadata.FootstepType;
            _notes.Text = _metadata.Notes;
        }
        finally { _loading = false; }
    }

    private void SetDirection(int index, DirectionPassageFlags flag)
        => _directions.SetItemChecked(index, _metadata?.BlockedDirections.HasFlag(flag) == true);

    private void Apply()
    {
        if (_loading || _metadata is null) return;
        if (_passage.SelectedItem is string passage && Enum.TryParse<PassageMode>(passage, out var mode)) _metadata.Passage = mode;
        var directions = DirectionPassageFlags.None;
        if (_directions.GetItemChecked(0)) directions |= DirectionPassageFlags.Down;
        if (_directions.GetItemChecked(1)) directions |= DirectionPassageFlags.Left;
        if (_directions.GetItemChecked(2)) directions |= DirectionPassageFlags.Right;
        if (_directions.GetItemChecked(3)) directions |= DirectionPassageFlags.Up;
        _metadata.BlockedDirections = directions;
        _metadata.Ladder = _ladder.Checked;
        _metadata.Bush = _bush.Checked;
        _metadata.Counter = _counter.Checked;
        _metadata.DamageFloor = _damage.Checked;
        _metadata.TerrainTag = (int)_terrainTag.Value;
        _metadata.SemanticType = _semantic.Text.Trim();
        _metadata.FootstepType = _footstep.Text.Trim();
        _metadata.Notes = _notes.Text;
        MetadataChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void AddRow(TableLayoutPanel panel, string label, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, 0, row);
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control, 1, row);
    }
}
