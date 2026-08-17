using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.RpgMakerMV.Imaging;
using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Editor.WinForms;

public sealed class ObjectEditorForm : Form
{
    private readonly TileProject _project;
    private readonly TileObjectDefinition _object;
    private readonly PictureBox _preview = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(50, 50, 52) };
    private readonly TextBox _name = new();
    private readonly ComboBox _category = new();
    private readonly NumericUpDown _gridWidth = new() { Minimum = 1, Maximum = 16 };
    private readonly NumericUpDown _gridHeight = new() { Minimum = 1, Maximum = 16 };
    private readonly NumericUpDown _anchorX = new() { Minimum = 0, Maximum = 15 };
    private readonly NumericUpDown _anchorY = new() { Minimum = 0, Maximum = 15 };
    private readonly NumericUpDown _collisionX = new() { Minimum = 0, Maximum = 15 };
    private readonly NumericUpDown _collisionY = new() { Minimum = 0, Maximum = 15 };
    private readonly NumericUpDown _collisionWidth = new() { Minimum = 1, Maximum = 16 };
    private readonly NumericUpDown _collisionHeight = new() { Minimum = 1, Maximum = 16 };
    private readonly TextBox _tags = new();
    private readonly TextBox _notes = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _locked = new() { Text = "Lock object placement" };

    public ObjectEditorForm(TileProject project, TileObjectDefinition definition)
    {
        _project = project;
        _object = definition;
        Text = $"Object Editor — {definition.Name}";
        StartPosition = FormStartPosition.CenterParent;
        Width = 760;
        Height = 610;
        MinimumSize = new Size(640, 500);
        _category.DropDownStyle = ComboBoxStyle.DropDownList;
        _category.Items.AddRange(Enum.GetNames<TileObjectCategory>());

        var properties = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(8) };
        properties.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        properties.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(properties, "Name", _name);
        AddRow(properties, "Category", _category);
        AddRow(properties, "Grid width", _gridWidth);
        AddRow(properties, "Grid height", _gridHeight);
        AddRow(properties, "Anchor X", _anchorX);
        AddRow(properties, "Anchor Y", _anchorY);
        AddRow(properties, "Collision X", _collisionX);
        AddRow(properties, "Collision Y", _collisionY);
        AddRow(properties, "Collision width", _collisionWidth);
        AddRow(properties, "Collision height", _collisionHeight);
        AddRow(properties, "Tags", _tags);
        _notes.Height = 90;
        AddRow(properties, "Notes", _notes);
        AddRow(properties, string.Empty, _locked);

        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(700, 500), SplitterDistance = 320, Panel1MinSize = 240, Panel2MinSize = 260 };
        split.Panel1.Controls.Add(_preview);
        _preview.Dock = DockStyle.Fill;
        split.Panel2.Controls.Add(properties);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        Controls.Add(split);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        ok.Click += (_, _) => SaveValues();
        LoadValues();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _preview.Image?.Dispose();
        base.Dispose(disposing);
    }

    private void LoadValues()
    {
        _name.Text = _object.Name;
        _category.SelectedItem = _object.Category.ToString();
        _gridWidth.Value = Math.Clamp(_object.GridWidth, 1, 16);
        _gridHeight.Value = Math.Clamp(_object.GridHeight, 1, 16);
        _anchorX.Value = Math.Clamp(_object.AnchorCellX, 0, 15);
        _anchorY.Value = Math.Clamp(_object.AnchorCellY, 0, 15);
        _collisionX.Value = Math.Clamp(_object.CollisionCellX, 0, 15);
        _collisionY.Value = Math.Clamp(_object.CollisionCellY, 0, 15);
        _collisionWidth.Value = Math.Clamp(_object.CollisionCellWidth, 1, 16);
        _collisionHeight.Value = Math.Clamp(_object.CollisionCellHeight, 1, 16);
        _tags.Text = string.Join(", ", _object.Tags);
        _notes.Text = _object.Notes;
        _locked.Checked = _object.IsLocked;
        var path = ProjectPath.Resolve(_project, _object.RelativeImagePath);
        if (File.Exists(path)) _preview.Image = BitmapFile.LoadEditable(path);
    }

    private void SaveValues()
    {
        _object.Name = string.IsNullOrWhiteSpace(_name.Text) ? "Object" : _name.Text.Trim();
        if (_category.SelectedItem is string category && Enum.TryParse<TileObjectCategory>(category, out var parsed)) _object.Category = parsed;
        _object.GridWidth = (int)_gridWidth.Value;
        _object.GridHeight = (int)_gridHeight.Value;
        _object.AnchorCellX = Math.Clamp((int)_anchorX.Value, 0, _object.GridWidth - 1);
        _object.AnchorCellY = Math.Clamp((int)_anchorY.Value, 0, _object.GridHeight - 1);
        _object.CollisionCellX = Math.Clamp((int)_collisionX.Value, 0, _object.GridWidth - 1);
        _object.CollisionCellY = Math.Clamp((int)_collisionY.Value, 0, _object.GridHeight - 1);
        _object.CollisionCellWidth = Math.Min((int)_collisionWidth.Value, _object.GridWidth - _object.CollisionCellX);
        _object.CollisionCellHeight = Math.Min((int)_collisionHeight.Value, _object.GridHeight - _object.CollisionCellY);
        _object.Tags = _tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _object.Notes = _notes.Text;
        _object.IsLocked = _locked.Checked;
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
