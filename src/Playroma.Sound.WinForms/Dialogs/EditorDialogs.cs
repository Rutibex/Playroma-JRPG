using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Authoring.Projects;
using Playroma.Sound.Authoring.Services;
using Playroma.Sound.Generators.Contracts;

namespace Playroma.Sound.WinForms.Dialogs;

public sealed class NewProjectDialog : Form
{
    private readonly TextBox _nameBox;
    private readonly TextBox _idBox;
    private readonly TextBox _folderBox;

    public NewProjectDialog()
    {
        Text = "New Playroma Sound Project";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(560, 205);

        _nameBox = new TextBox { Dock = DockStyle.Fill, Text = "New Sound Project" };
        _idBox = new TextBox { Dock = DockStyle.Fill, Text = "new.sound.project" };
        _folderBox = new TextBox { Dock = DockStyle.Fill, Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Playroma Sound Projects") };
        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = _folderBox.Text, ShowNewFolderButton = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) _folderBox.Text = dialog.SelectedPath;
        };

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 3, RowCount = 4 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddRow(table, 0, "Display name:", _nameBox);
        AddRow(table, 1, "Project ID:", _idBox);
        table.Controls.Add(new Label { Text = "Folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        table.Controls.Add(_folderBox, 1, 2);
        table.Controls.Add(browse, 2, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var create = new Button { Text = "Create", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(create);
        buttons.Controls.Add(cancel);
        table.Controls.Add(buttons, 0, 3);
        table.SetColumnSpan(buttons, 3);
        Controls.Add(table);
        AcceptButton = create;
        CancelButton = cancel;

        create.Click += (_, eventArgs) =>
        {
            if (ValidateInputs()) return;
            DialogResult = DialogResult.None;
        };
    }

    public string ProjectDisplayName => _nameBox.Text.Trim();
    public string ProjectId => _idBox.Text.Trim();
    public string ProjectDirectory => Path.GetFullPath(_folderBox.Text.Trim());
    public string ProjectFilePath => Path.Combine(ProjectDirectory, MakeFileName(ProjectDisplayName) + ".playproject");

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(ProjectDisplayName) || string.IsNullOrWhiteSpace(ProjectId) || string.IsNullOrWhiteSpace(_folderBox.Text))
        {
            MessageBox.Show(this, "Display name, project ID, and folder are required.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        try { _ = Path.GetFullPath(_folderBox.Text); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }

    private static string MakeFileName(string value)
    {
        string result = new(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray());
        result = result.Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(result) ? "PlayromaSoundProject" : result;
    }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(control, 1, row);
        table.SetColumnSpan(control, 2);
    }
}

public sealed class NewAssetDialog : Form
{
    private readonly ComboBox _kindBox;
    private readonly TextBox _nameBox;
    private readonly TextBox _idBox;
    private readonly TextBox _pathBox;
    private readonly ComboBox _generatorBox;

    public NewAssetDialog(AuthoringDocumentKind initialKind = AuthoringDocumentKind.SoundGraph)
    {
        Text = "New Playroma Sound Asset";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(570, 270);

        _kindBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _kindBox.Items.AddRange(Enum.GetValues<AuthoringDocumentKind>().Cast<object>().ToArray());
        _kindBox.SelectedItem = initialKind;
        _nameBox = new TextBox { Dock = DockStyle.Fill, Text = "New Sound" };
        _idBox = new TextBox { Dock = DockStyle.Fill, Text = "sound.new" };
        _pathBox = new TextBox { Dock = DockStyle.Fill };
        _generatorBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (IUntypedSoundGenerator generator in SoundGeneratorCatalog.CreateDefault().Generators.OrderBy(item => item.Descriptor.DisplayName))
            _generatorBox.Items.Add(new GeneratorItem(generator.GeneratorId, generator.Descriptor.DisplayName));
        if (_generatorBox.Items.Count > 0) _generatorBox.SelectedIndex = 0;

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 6 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(table, 0, "Asset type:", _kindBox);
        AddRow(table, 1, "Display name:", _nameBox);
        AddRow(table, 2, "Asset ID:", _idBox);
        AddRow(table, 3, "Relative path:", _pathBox);
        AddRow(table, 4, "Generator:", _generatorBox);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var create = new Button { Text = "Create", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(create);
        buttons.Controls.Add(cancel);
        table.Controls.Add(buttons, 0, 5);
        table.SetColumnSpan(buttons, 2);
        Controls.Add(table);
        AcceptButton = create;
        CancelButton = cancel;

        _kindBox.SelectedIndexChanged += (_, _) => UpdateKindFields();
        _nameBox.TextChanged += (_, _) => UpdateSuggestedPath(false);
        _idBox.TextChanged += (_, _) => UpdateSuggestedPath(false);
        create.Click += (_, _) =>
        {
            if (ValidateInputs()) return;
            DialogResult = DialogResult.None;
        };
        UpdateKindFields();
        UpdateSuggestedPath(true);
    }

    public AuthoringDocumentKind DocumentKind => (AuthoringDocumentKind)(_kindBox.SelectedItem ?? AuthoringDocumentKind.SoundGraph);
    public string DisplayNameValue => _nameBox.Text.Trim();
    public string AssetId => _idBox.Text.Trim();
    public string RelativePath => _pathBox.Text.Trim().Replace('\\', '/');
    public string GeneratorId => (_generatorBox.SelectedItem as GeneratorItem)?.Id ?? "interface";

    private void UpdateKindFields()
    {
        _generatorBox.Enabled = DocumentKind == AuthoringDocumentKind.GeneratorRequest;
        UpdateSuggestedPath(true);
    }

    private void UpdateSuggestedPath(bool force)
    {
        if (!force && _pathBox.Modified) return;
        string file = string.IsNullOrWhiteSpace(AssetId) ? "new-sound" : AssetId.Replace('.', '-');
        (string folder, string extension) = DocumentKind switch
        {
            AuthoringDocumentKind.SoundGraph => ("Sounds", ".playsound"),
            AuthoringDocumentKind.GeneratorRequest => ("Generators", ".playgen"),
            AuthoringDocumentKind.StreamingScene => ("StreamingScenes", ".playstream"),
            _ => ("Assets", ".json")
        };
        _pathBox.Text = $"{folder}/{file}{extension}";
        _pathBox.Modified = false;
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(DisplayNameValue) || string.IsNullOrWhiteSpace(AssetId) || string.IsNullOrWhiteSpace(RelativePath))
        {
            MessageBox.Show(this, "Display name, asset ID, and relative path are required.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (Path.IsPathRooted(RelativePath) || RelativePath.Split('/').Any(part => part == ".."))
        {
            MessageBox.Show(this, "The asset path must remain inside the project and cannot contain '..'.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private sealed record GeneratorItem(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}

public sealed class RecoveryDialog : Form
{
    private readonly CheckedListBox _list;
    public RecoveryDialog(IReadOnlyList<AutosaveEnvelope> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Text = "Recover Autosaved Documents";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(700, 390);

        var explanation = new Label
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(10),
            Text = "Playroma Sound found recovery copies for this project. Selected copies will be applied as unsaved edits; source files are not overwritten until you save."
        };
        _list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
        foreach (AutosaveEnvelope entry in entries)
        {
            _list.Items.Add(new RecoveryItem(entry), true);
        }

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
        var recover = new Button { Text = "Recover Selected", DialogResult = DialogResult.OK, AutoSize = true };
        var skip = new Button { Text = "Skip", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(recover);
        buttons.Controls.Add(skip);

        Controls.Add(_list);
        Controls.Add(explanation);
        Controls.Add(buttons);
        AcceptButton = recover;
        CancelButton = skip;
    }

    public IReadOnlyList<AutosaveEnvelope> SelectedEntries => _list.CheckedItems.Cast<RecoveryItem>().Select(item => item.Entry).ToArray();

    private sealed record RecoveryItem(AutosaveEnvelope Entry)
    {
        public override string ToString() => $"{Entry.DocumentId} [{Entry.DocumentKind}] — revision {Entry.Revision}, {Entry.SavedAtUtc.LocalDateTime:g}";
    }
}

public sealed class AboutDialog : Form
{
    public AboutDialog()
    {
        Text = "About Playroma Sound";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(500, 260);

        var title = new Label
        {
            Text = "Playroma Sound",
            Dock = DockStyle.Top,
            Height = 54,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 18f, FontStyle.Bold)
        };
        var body = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            TextAlign = ContentAlignment.TopCenter,
            Text = "Procedural audio engine and authoring environment\n\nVersion 0.8.0\n\nCore engine, DSP, graph, generators, streaming, analysis, authoring, and WASAPI playback are separated into reusable projects.\n\nMIT License — Copyright © 2026 Michael Robinson"
        };
        var close = new Button { Text = "Close", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 36 };
        Controls.Add(body);
        Controls.Add(title);
        Controls.Add(close);
        AcceptButton = close;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (Control control in Controls)
                if (control is Label label && label.Font.Size >= 18f) label.Font.Dispose();
        }
        base.Dispose(disposing);
    }
}
