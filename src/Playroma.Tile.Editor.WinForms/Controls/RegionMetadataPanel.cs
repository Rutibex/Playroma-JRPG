using Playroma.Tile.Core.Models;
using Playroma.Tile.RpgMakerMV.Formats;

namespace Playroma.Tile.Editor.WinForms.Controls;

public sealed class RegionMetadataPanel : UserControl
{
    private readonly Label _heading = new();
    private readonly Label _bounds = new();
    private readonly Label _compileInfo = new();
    private readonly TextBox _name = new();
    private readonly TextBox _alternateName = new();
    private readonly TextBox _tags = new();
    private readonly TextBox _notes = new();
    private readonly CheckBox _locked = new();
    private readonly Button _apply = new();
    private TileRegionMetadata? _metadata;

    public RegionMetadataPanel()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(10);
        AutoScroll = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 15,
            Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _heading.Font = new Font(Font, FontStyle.Bold);
        _heading.AutoSize = true;
        _heading.Text = "No region selected";
        _bounds.AutoSize = true;
        _bounds.ForeColor = SystemColors.GrayText;
        _compileInfo.AutoSize = true;
        _compileInfo.ForeColor = SystemColors.GrayText;
        _compileInfo.MaximumSize = new Size(320, 0);

        ConfigureTextBox(_name);
        ConfigureTextBox(_alternateName);
        ConfigureTextBox(_tags);
        ConfigureTextBox(_notes);
        _notes.Multiline = true;
        _notes.ScrollBars = ScrollBars.Vertical;
        _notes.Height = 130;

        _locked.Text = "Lock region against image edits";
        _locked.AutoSize = true;
        _apply.Text = "Apply metadata";
        _apply.AutoSize = true;
        _apply.Click += (_, _) => ApplyChanges();

        Add(layout, _heading);
        Add(layout, _bounds);
        Add(layout, _compileInfo);
        AddLabel(layout, "Name");
        Add(layout, _name);
        AddLabel(layout, "Alternate / localized name");
        Add(layout, _alternateName);
        AddLabel(layout, "Tags (comma separated)");
        Add(layout, _tags);
        AddLabel(layout, "Notes");
        Add(layout, _notes);
        Add(layout, _locked);
        Add(layout, _apply);

        Controls.Add(layout);
        SetEditorEnabled(false);
    }

    public event EventHandler? MetadataChanged;

    public void ShowLogicalRegion(MvRegionDefinition region, TileRegionMetadata metadata)
    {
        _metadata = metadata;
        _heading.Text = $"Region {region.Index + 1}: {region.Kind}";
        _bounds.Text = $"X {region.Bounds.X}, Y {region.Bounds.Y}, {region.Bounds.Width} × {region.Bounds.Height} px";
        var compiledText = metadata.CompiledUtc.HasValue
            ? metadata.CompiledUtc.Value.ToLocalTime().ToString("g")
            : "unknown";

        _compileInfo.Text = metadata.SourceAutotileId.HasValue
            ? $"Compiled from autotile {metadata.SourceAutotileId.Value:N}, revision {metadata.SourceAutotileRevision}\n" +
              $"Compiled: {compiledText}"
            : "No autotile source binding.";
        _name.Text = metadata.Name;
        _alternateName.Text = metadata.AlternateName;
        _tags.Text = string.Join(", ", metadata.Tags);
        _notes.Text = metadata.Notes;
        _locked.Checked = metadata.IsLocked;
        SetEditorEnabled(true);
    }

    public void ShowRawCell(MvRegionDefinition region)
    {
        _metadata = null;
        _heading.Text = $"Raw source cell {region.Index + 1}";
        _bounds.Text = $"X {region.Bounds.X}, Y {region.Bounds.Y}, {region.Bounds.Width} × {region.Bounds.Height} px";
        _compileInfo.Text = "Compilation information is attached to logical regions.";
        _name.Clear();
        _alternateName.Clear();
        _tags.Clear();
        _notes.Text = "Names are attached to MV logical assets. Switch to “MV logical assets” to edit metadata.";
        _locked.Checked = false;
        SetEditorEnabled(false);
    }

    public void ClearSelection()
    {
        _metadata = null;
        _heading.Text = "No region selected";
        _bounds.Text = string.Empty;
        _compileInfo.Text = string.Empty;
        _name.Clear();
        _alternateName.Clear();
        _tags.Clear();
        _notes.Clear();
        _locked.Checked = false;
        SetEditorEnabled(false);
    }

    private void ApplyChanges()
    {
        if (_metadata is null)
        {
            return;
        }

        _metadata.Name = _name.Text.Trim();
        _metadata.AlternateName = _alternateName.Text.Trim();
        _metadata.Tags = _tags.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _metadata.Notes = _notes.Text.Trim();
        _metadata.IsLocked = _locked.Checked;
        MetadataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetEditorEnabled(bool enabled)
    {
        _name.Enabled = enabled;
        _alternateName.Enabled = enabled;
        _tags.Enabled = enabled;
        _notes.Enabled = enabled;
        _locked.Enabled = enabled;
        _apply.Enabled = enabled;
    }

    private static void ConfigureTextBox(TextBox textBox)
    {
        textBox.Dock = DockStyle.Top;
        textBox.Margin = new Padding(0, 2, 0, 8);
    }

    private static void AddLabel(TableLayoutPanel layout, string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        Add(layout, label);
    }

    private static void Add(TableLayoutPanel layout, Control control)
    {
        control.Dock = control is Button or CheckBox ? DockStyle.Left : DockStyle.Top;
        layout.Controls.Add(control);
    }
}
