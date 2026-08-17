using Playroma.JRPG.Assets;

namespace Playroma.JRPG.Editor.WinForms.Dialogs;

public sealed class AssetImportDialog : Form
{
    private readonly TextBox _source = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly ComboBox _category = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _logicalName = new() { Dock = DockStyle.Fill };
    private readonly Label _detected = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label _destination = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Button _ok = new() { Text = "Stage Import", DialogResult = DialogResult.OK, AutoSize = true };

    public AssetImportDialog(string sourcePath, AssetCategory suggestedCategory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        Text = "Import RPG Maker MV Asset";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(600, 270);
        ClientSize = new Size(720, 300);
        Padding = new Padding(12);

        foreach (AssetCategory category in Enum.GetValues<AssetCategory>().Where(category => category != AssetCategory.Unknown && AssetCategoryRegistry.Directories.ContainsKey(category)))
        {
            _category.Items.Add(category);
        }
        _category.SelectedItem = suggestedCategory != AssetCategory.Unknown ? suggestedCategory : GuessCategory(sourcePath);
        if (_category.SelectedIndex < 0) _category.SelectedIndex = 0;
        _source.Text = Path.GetFullPath(sourcePath);
        _logicalName.Text = Path.GetFileNameWithoutExtension(sourcePath);
        _detected.Text = $"{AssetCategoryRegistry.ResolveMediaKind(Path.GetExtension(sourcePath))}, {new FileInfo(sourcePath).Length:N0} bytes, {Path.GetExtension(sourcePath)}";

        TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, AutoSize = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(table, 0, "Source", _source);
        AddRow(table, 1, "Detected", _detected);
        AddRow(table, 2, "Category", _category);
        AddRow(table, 3, "Logical name", _logicalName);
        AddRow(table, 4, "Destination", _destination);

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange([_ok, cancel]);
        Controls.Add(table);
        Controls.Add(buttons);
        AcceptButton = _ok;
        CancelButton = cancel;
        _category.SelectedIndexChanged += (_, _) => UpdateDestination();
        _logicalName.TextChanged += (_, _) => UpdateDestination();
        FormClosing += ValidateClosing;
        UpdateDestination();
    }

    public string SourcePath => _source.Text;
    public AssetKey Destination => new((AssetCategory)_category.SelectedItem!, _logicalName.Text);

    private void UpdateDestination()
    {
        bool valid = _category.SelectedItem is AssetCategory && IsValidLogicalName(_logicalName.Text);
        _ok.Enabled = valid;
        if (!valid)
        {
            _destination.Text = "Enter a project-relative asset name without an extension.";
            return;
        }
        AssetKey key = new((AssetCategory)_category.SelectedItem!, _logicalName.Text);
        _destination.Text = AssetCategoryRegistry.BuildRuntimeRelativePath(key, Path.GetExtension(SourcePath));
    }

    private void ValidateClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK) return;
        if (!IsValidLogicalName(_logicalName.Text))
        {
            MessageBox.Show(this, "The logical name must be relative, must not contain an extension, and must not contain invalid path characters.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
        }
    }

    private static bool IsValidLogicalName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || Path.HasExtension(value)) return false;
        string normalized = value.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0 || normalized.Split('/').Any(part => part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return false;
        return true;
    }

    private static AssetCategory GuessCategory(string sourcePath) => AssetCategoryRegistry.ResolveMediaKind(Path.GetExtension(sourcePath)) switch
    {
        AssetMediaKind.Audio => AssetCategory.SoundEffects,
        AssetMediaKind.Font => AssetCategory.Fonts,
        AssetMediaKind.Movie => AssetCategory.Movies,
        _ => AssetCategory.Pictures
    };

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        table.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        table.Controls.Add(control, 1, row);
    }
}
