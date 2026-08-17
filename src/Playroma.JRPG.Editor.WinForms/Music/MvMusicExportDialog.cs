namespace Playroma.JRPG.Editor.WinForms.Music;

public sealed class MvMusicExportDialog : Form
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _ogg = new() { Text = "OGG for desktop and browser", Checked = true, AutoSize = true };
    private readonly CheckBox _m4a = new() { Text = "M4A for mobile compatibility", Checked = true, AutoSize = true };
    private readonly CheckBox _authoring = new() { Text = "Also save an editable Playroma Music project", Checked = true, AutoSize = true };
    private readonly CheckBox _loop = new() { Text = "Make this a seamless repeating loop", Checked = true, AutoSize = true };
    private readonly NumericUpDown _bitRate = new() { Minimum = 64, Maximum = 320, Value = 192, Increment = 16, Width = 90 };
    private readonly NumericUpDown _quality = new() { Minimum = -10, Maximum = 100, Value = 55, Increment = 5, Width = 90 };
    private readonly Panel _advancedPanel = new() { Dock = DockStyle.Top, Height = 82, Visible = false };
    private MvAudioDatabaseKind _selectedKind = MvAudioDatabaseKind.Bgm;
    private readonly Dictionary<MvAudioDatabaseKind, RadioButton> _kindButtons = [];

    public MvMusicExportDialog(string suggestedName)
    {
        Text = "Export Music to Game";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(700, 520);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;

        _name.Text = MusicExportService.SanitizeFileName(suggestedName);

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            RowCount = 8,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        root.Controls.Add(new Label
        {
            Text = "WHERE SHOULD THIS MUSIC BE USED?",
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 16f, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 10)
        }, 0, 0);

        FlowLayoutPanel destinations = new() { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 2, 0, 8) };
        destinations.Controls.AddRange([
            DestinationButton(MvAudioDatabaseKind.Bgm, "Background Music", "Exploration, towns, battles, and scenes", isChecked: true),
            DestinationButton(MvAudioDatabaseKind.Bgs, "Ambient Sound", "Rain, machinery, wind, and long ambience"),
            DestinationButton(MvAudioDatabaseKind.Me, "Victory / Jingle", "Short fanfares and musical effects"),
            DestinationButton(MvAudioDatabaseKind.Se, "Sound Effect", "Very short musical stings")
        ]);
        root.Controls.Add(destinations, 0, 1);

        TableLayoutPanel nameRow = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 6, 0, 6) };
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        nameRow.Controls.Add(new Label { Text = "Song file name", AutoSize = true, Margin = new Padding(0, 7, 8, 0) }, 0, 0);
        nameRow.Controls.Add(_name, 1, 0);
        root.Controls.Add(nameRow, 0, 2);

        FlowLayoutPanel simpleOptions = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 8, 0, 8) };
        simpleOptions.Controls.Add(_loop);
        simpleOptions.Controls.Add(_authoring);
        root.Controls.Add(simpleOptions, 0, 3);

        Panel compatibility = new() { Dock = DockStyle.Top, Height = 64, Padding = new Padding(10), BackColor = Color.FromArgb(240, 246, 240) };
        compatibility.Controls.Add(new Label
        {
            Text = "✓ Best RPG Maker MV compatibility selected: OGG + M4A",
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10f, FontStyle.Bold)
        });
        root.Controls.Add(compatibility, 0, 4);

        CheckBox showAdvanced = new() { Text = "Show format and quality options", AutoSize = true, Padding = new Padding(0, 6, 0, 4) };
        showAdvanced.CheckedChanged += (_, _) => _advancedPanel.Visible = showAdvanced.Checked;
        root.Controls.Add(showAdvanced, 0, 5);

        FlowLayoutPanel advanced = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(10) };
        advanced.Controls.Add(_ogg);
        advanced.Controls.Add(_m4a);
        advanced.Controls.Add(new Label { Text = "AAC kbps", AutoSize = true, Margin = new Padding(16, 6, 3, 0) });
        advanced.Controls.Add(_bitRate);
        advanced.Controls.Add(new Label { Text = "OGG quality %", AutoSize = true, Margin = new Padding(16, 6, 3, 0) });
        advanced.Controls.Add(_quality);
        _advancedPanel.Controls.Add(advanced);
        root.Controls.Add(_advancedPanel, 0, 6);

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 7, 0, 0) };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(95, 34) };
        Button export = new() { Text = "Export and Add to Game", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(190, 34) };
        export.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                MessageBox.Show(this, "Enter a song file name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        root.Controls.Add(buttons, 0, 7);

        Controls.Add(root);
        AcceptButton = export;
        CancelButton = cancel;
    }

    private Control DestinationButton(MvAudioDatabaseKind kind, string title, string description, bool isChecked = false)
    {
        RadioButton button = new()
        {
            Appearance = Appearance.Button,
            AutoCheck = true,
            Checked = isChecked,
            Text = title + Environment.NewLine + description,
            TextAlign = ContentAlignment.MiddleLeft,
            Width = 330,
            Height = 58,
            Padding = new Padding(10),
            Margin = new Padding(4),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = true,
            Tag = kind
        };
        button.CheckedChanged += (_, _) =>
        {
            if (!button.Checked) return;
            _selectedKind = kind;
            _loop.Checked = kind is MvAudioDatabaseKind.Bgm or MvAudioDatabaseKind.Bgs;
            foreach ((MvAudioDatabaseKind otherKind, RadioButton other) in _kindButtons)
                if (otherKind != kind) other.Checked = false;
        };
        _kindButtons[kind] = button;
        return button;
    }

    public string FileNameValue => MusicExportService.SanitizeFileName(_name.Text);
    public MvAudioDatabaseKind DatabaseKind => _selectedKind;
    public MvAudioExportFormats Formats => (_ogg.Checked ? MvAudioExportFormats.Ogg : MvAudioExportFormats.None)
        | (_m4a.Checked ? MvAudioExportFormats.M4A : MvAudioExportFormats.None);
    public bool SaveAuthoringProject => _authoring.Checked;
    public bool MakeSeamlessLoop => _loop.Checked;
    public int AacBitRate => decimal.ToInt32(_bitRate.Value) * 1000;
    public float VorbisQuality => decimal.ToSingle(_quality.Value) / 100f;
}
