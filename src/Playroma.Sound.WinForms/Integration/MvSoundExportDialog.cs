using Playroma.Sound.Authoring.Documents;

namespace Playroma.Sound.WinForms.Integration;

public sealed class MvSoundExportDialog : Form
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly CheckBox _ogg = new() { Text = "Ogg Vorbis (.ogg)", Checked = true, AutoSize = true };
    private readonly CheckBox _m4a = new() { Text = "AAC in MPEG-4 (.m4a)", Checked = true, AutoSize = true };
    private readonly CheckBox _authoring = new()
    {
        Text = "Save an editable Playroma Sound source beside the RPG project",
        Checked = true,
        AutoSize = true
    };
    private readonly NumericUpDown _duration = new()
    {
        Minimum = 1,
        Maximum = 600,
        DecimalPlaces = 1,
        Increment = 1,
        Value = 10,
        Dock = DockStyle.Left,
        Width = 110
    };
    private readonly NumericUpDown _bitRate = new()
    {
        Minimum = 64,
        Maximum = 320,
        Value = 192,
        Increment = 16,
        Dock = DockStyle.Left,
        Width = 110
    };
    private readonly NumericUpDown _quality = new()
    {
        Minimum = -10,
        Maximum = 100,
        Value = 55,
        Increment = 5,
        Dock = DockStyle.Left,
        Width = 110
    };

    public MvSoundExportDialog(string suggestedName, AuthoringDocumentKind documentKind)
    {
        Text = "Export to RPG Maker MV Audio Database";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(610, 420);
        MinimumSize = new Size(570, 400);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;

        _name.Text = MvSoundExportService.SanitizeFileName(suggestedName);
        _kind.Items.AddRange(Enum.GetValues<MvSoundDatabaseKind>().Cast<object>().ToArray());
        _kind.SelectedItem = documentKind == AuthoringDocumentKind.StreamingScene
            ? MvSoundDatabaseKind.Bgs
            : MvSoundDatabaseKind.Se;
        _duration.Value = documentKind == AuthoringDocumentKind.StreamingScene ? 60 : 10;
        _kind.SelectedIndexChanged += (_, _) =>
        {
            if (_kind.SelectedItem is not MvSoundDatabaseKind selected) return;
            if (selected is MvSoundDatabaseKind.Bgm or MvSoundDatabaseKind.Bgs)
            {
                if (_duration.Value < 30) _duration.Value = 60;
            }
            else if (_duration.Value > 60)
            {
                _duration.Value = 10;
            }
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 10
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 195));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        layout.Controls.Add(LabelFor("Database file name:"), 0, 0);
        layout.Controls.Add(_name, 1, 0);
        layout.Controls.Add(LabelFor("MV audio category:"), 0, 1);
        layout.Controls.Add(_kind, 1, 1);
        layout.Controls.Add(LabelFor("Export formats:"), 0, 2);
        FlowLayoutPanel formats = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        formats.Controls.AddRange([_ogg, _m4a]);
        layout.Controls.Add(formats, 1, 2);
        layout.Controls.Add(LabelFor("Maximum render length (sec):"), 0, 3);
        layout.Controls.Add(_duration, 1, 3);
        layout.Controls.Add(LabelFor("AAC bitrate (kbps):"), 0, 4);
        layout.Controls.Add(_bitRate, 1, 4);
        layout.Controls.Add(LabelFor("Vorbis quality (%):"), 0, 5);
        layout.Controls.Add(_quality, 1, 5);
        layout.Controls.Add(_authoring, 0, 6);
        layout.SetColumnSpan(_authoring, 2);

        Label explanation = new()
        {
            Text = "RPG Maker MV uses OGG for desktop deployment and M4A for platforms that require AAC. " +
                   "Selecting both writes matching files into audio/se, audio/me, audio/bgs, or audio/bgm. " +
                   "Finite sounds stop naturally; the maximum length is used as a safety limit and as the fixed length for streaming scenes.",
            AutoSize = false,
            Dock = DockStyle.Fill
        };
        layout.Controls.Add(explanation, 0, 7);
        layout.SetColumnSpan(explanation, 2);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        Button export = new() { Text = "Export", DialogResult = DialogResult.OK, AutoSize = true };
        export.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                MessageBox.Show(this, "Enter a database file name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            if (!_ogg.Checked && !_m4a.Checked)
            {
                MessageBox.Show(this, "Choose OGG, M4A, or both.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
        };
        buttons.Controls.AddRange([cancel, export]);
        layout.Controls.Add(buttons, 0, 9);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        AcceptButton = export;
        CancelButton = cancel;
    }

    public string FileNameValue => MvSoundExportService.SanitizeFileName(_name.Text);
    public MvSoundDatabaseKind DatabaseKind => _kind.SelectedItem is MvSoundDatabaseKind value ? value : MvSoundDatabaseKind.Se;
    public MvSoundExportFormats Formats => (_ogg.Checked ? MvSoundExportFormats.Ogg : MvSoundExportFormats.None)
        | (_m4a.Checked ? MvSoundExportFormats.M4A : MvSoundExportFormats.None);
    public bool SaveAuthoringSource => _authoring.Checked;
    public double MaximumDurationSeconds => decimal.ToDouble(_duration.Value);
    public int AacBitRate => decimal.ToInt32(_bitRate.Value) * 1000;
    public float VorbisQuality => decimal.ToSingle(_quality.Value) / 100f;

    private static Label LabelFor(string text) => new()
    {
        Text = text,
        TextAlign = ContentAlignment.MiddleLeft,
        Dock = DockStyle.Fill
    };
}
