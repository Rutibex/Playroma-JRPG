using Playroma.Tile.Tilesets.Integration;

namespace Playroma.Tile.Editor.WinForms.Integration;

internal sealed class MvDatabaseImportDialog : Form
{
    private readonly CheckedListBox _records = new() { Dock = DockStyle.Fill, CheckOnClick = true };
    private readonly CheckBox _replace = new()
    {
        Text = "Refresh tilesets already linked to these database records",
        Checked = true,
        AutoSize = true
    };

    public MvDatabaseImportDialog(MvProjectInspection inspection)
    {
        Text = "Load Tilesets from jRPG Database";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(620, 460);
        Size = new Size(760, 620);

        foreach (var record in inspection.Records) _records.Items.Add(record, true);

        var selectAll = new Button { Text = "Select All", AutoSize = true };
        var selectNone = new Button { Text = "Select None", AutoSize = true };
        selectAll.Click += (_, _) => SetAll(true);
        selectNone.Click += (_, _) => SetAll(false);

        var okay = new Button { Text = "Load Selected", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        AcceptButton = okay;
        CancelButton = cancel;

        var header = new Label
        {
            Text = "Choose RPG Maker MV Tilesets.json records. Referenced PNG sheets and passage flags are copied into the editable Playroma Tile project.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(700, 0)
        };
        var topButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        topButtons.Controls.AddRange([selectAll, selectNone]);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };
        buttons.Controls.AddRange([cancel, okay]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(topButtons, 0, 1);
        layout.Controls.Add(_records, 0, 2);
        layout.Controls.Add(_replace, 0, 3);
        layout.Controls.Add(buttons, 0, 4);
        Controls.Add(layout);
    }

    public IReadOnlyList<int> SelectedRecordIds => _records.CheckedItems
        .OfType<MvTilesetRecordSummary>()
        .Select(x => x.Id)
        .ToArray();

    public bool ReplaceLinkedTilesets => _replace.Checked;

    private void SetAll(bool value)
    {
        for (var index = 0; index < _records.Items.Count; index++) _records.SetItemChecked(index, value);
    }
}

internal sealed class MvDatabaseExportDialog : Form
{
    private sealed record Choice(int? RecordId, string Text)
    {
        public override string ToString() => Text;
    }

    private readonly ComboBox _target = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly CheckBox _overwrite = new()
    {
        Text = "Overwrite referenced PNG files in img/tilesets (backups are created first)",
        Checked = true,
        AutoSize = true
    };

    public MvDatabaseExportDialog(MvProjectInspection inspection, int? linkedRecordId, string tileSetName)
    {
        Text = "Export Tileset to jRPG Database";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(620, 260);
        Size = new Size(700, 300);

        _target.Items.Add(new Choice(null, $"Create a new database record for {tileSetName}"));
        foreach (var record in inspection.Records)
            _target.Items.Add(new Choice(record.Id, $"Replace {record.Id:D4} — {record.Name}"));

        var selectedIndex = 0;
        if (linkedRecordId is int linked)
        {
            for (var index = 0; index < _target.Items.Count; index++)
            {
                if ((_target.Items[index] as Choice)?.RecordId == linked)
                {
                    selectedIndex = index;
                    break;
                }
            }
        }
        _target.SelectedIndex = selectedIndex;

        var okay = new Button { Text = "Export to Database", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        AcceptButton = okay;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };
        buttons.Controls.AddRange([cancel, okay]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "Choose the Tilesets.json record to update. Playroma Tile writes all A1–E PNG sheets and the construction studio's passage, ladder, bush, counter, damage-floor, and terrain-tag flags.",
            AutoSize = true,
            MaximumSize = new Size(650, 0)
        }, 0, 0);
        layout.Controls.Add(_target, 0, 1);
        layout.Controls.Add(_overwrite, 0, 2);
        layout.Controls.Add(buttons, 0, 4);
        Controls.Add(layout);
    }

    private Choice Selected => (Choice)_target.SelectedItem!;
    public bool CreateNewRecord => Selected.RecordId is null;
    public int? RecordId => Selected.RecordId;
    public bool OverwriteImages => _overwrite.Checked;
}
