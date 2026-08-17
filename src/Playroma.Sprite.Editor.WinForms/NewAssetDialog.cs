using Playroma.Sprite.RpgMakerMV;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class NewAssetDialog : Form
{
    private readonly ComboBox profileBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox nameBox = new() { Text = "New Sprite", Dock = DockStyle.Fill };
    private readonly NumericUpDown widthBox = new() { Minimum = 1, Maximum = 16384, Value = 144, Dock = DockStyle.Fill };
    private readonly NumericUpDown heightBox = new() { Minimum = 1, Maximum = 16384, Value = 192, Dock = DockStyle.Fill };
    private readonly Label hint = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText };

    public NewAssetDialog(IReadOnlyCollection<RpgMakerMvImageProfile> profiles)
    {
        Text = "New RPG Maker MV Sprite Asset";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(590, 300);
        AutoScaleMode = AutoScaleMode.Dpi;

        profileBox.DisplayMember = nameof(ProfileChoice.DisplayName);
        profileBox.ValueMember = nameof(ProfileChoice.Id);
        profileBox.Items.Add(new ProfileChoice(string.Empty, "General Sprite", null));
        foreach (RpgMakerMvImageProfile profile in profiles.OrderBy(item => item.Category).ThenBy(item => item.DisplayName))
            profileBox.Items.Add(new ProfileChoice(profile.Id, profile.DisplayName, profile));
        profileBox.SelectedIndex = profileBox.Items.Cast<ProfileChoice>().ToList().FindIndex(choice => choice.Id == "mv-character-single");
        if (profileBox.SelectedIndex < 0) profileBox.SelectedIndex = 0;
        profileBox.SelectedIndexChanged += (_, _) => ApplyProfileDefaults();

        TableLayoutPanel fields = new() { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 5 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddRow(fields, 0, "Asset type", profileBox);
        AddRow(fields, 1, "Document name", nameBox);
        AddRow(fields, 2, "Width", widthBox);
        AddRow(fields, 3, "Height", heightBox);
        fields.Controls.Add(hint, 1, 4);

        Button create = new() { Text = "Create", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(14, 5, 14, 5) };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(14, 5, 14, 5) };
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.Add(create); buttons.Controls.Add(cancel);
        Controls.Add(fields); Controls.Add(buttons);
        AcceptButton = create;
        CancelButton = cancel;
        ApplyProfileDefaults();
    }

    public string DocumentName => string.IsNullOrWhiteSpace(nameBox.Text) ? "New Sprite" : nameBox.Text.Trim();
    public int CanvasWidth => Decimal.ToInt32(widthBox.Value);
    public int CanvasHeight => Decimal.ToInt32(heightBox.Value);
    public string? ProfileId => (profileBox.SelectedItem as ProfileChoice)?.Profile?.Id;

    private static void AddRow(TableLayoutPanel panel, int row, string label, Control control)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private void ApplyProfileDefaults()
    {
        ProfileChoice? choice = profileBox.SelectedItem as ProfileChoice;
        RpgMakerMvImageProfile? profile = choice?.Profile;
        if (profile is null)
        {
            hint.Text = "Creates an unrestricted transparent RGBA sprite.";
            return;
        }
        (int width, int height) = SuggestedSize(profile);
        widthBox.Value = Math.Clamp(width, (int)widthBox.Minimum, (int)widthBox.Maximum);
        heightBox.Value = Math.Clamp(height, (int)heightBox.Minimum, (int)heightBox.Maximum);
        hint.Text = $"Exports to {profile.AssetFolder}. Blue guides show the {GridDescription(profile)} layout.";
    }

    private static (int Width, int Height) SuggestedSize(RpgMakerMvImageProfile profile)
    {
        if (MvSystemAssetCatalog.TryGet(profile.Id, out MvSystemAssetDefinition? systemDefinition))
            return (systemDefinition.StockWidth, systemDefinition.StockHeight);
        if (profile.Dimensions.FixedWidth is int fw && profile.Dimensions.FixedHeight is int fh) return (fw, fh);
        if (profile.Dimensions.RecommendedWidth is int rw && profile.Dimensions.RecommendedHeight is int rh) return (rw, rh);
        if (profile.Grid is MvGridRule grid)
        {
            int cellW = grid.CellWidth ?? DefaultCellSize(profile);
            int cellH = grid.CellHeight ?? DefaultCellSize(profile);
            int columns = grid.Columns ?? 1;
            int rows = grid.Rows ?? grid.MinimumRows ?? 1;
            return (columns * cellW, rows * cellH);
        }
        return profile.Category switch
        {
            "battleback" => (1000, 740),
            "title" => (816, 624),
            "character" => (144, 192),
            "battler" => (576, 384),
            _ => (256, 256)
        };
    }

    private static int DefaultCellSize(RpgMakerMvImageProfile profile) => profile.Category switch
    {
        "character" => 48,
        "tileset" => 48,
        "icon" => 32,
        _ => 64
    };

    private static string GridDescription(RpgMakerMvImageProfile profile)
    {
        if (profile.Grid is not MvGridRule grid) return "free-size";
        return grid.Columns is int columns && grid.Rows is int rows ? $"{columns}×{rows}" : "profile-defined";
    }

    private sealed record ProfileChoice(string Id, string DisplayName, RpgMakerMvImageProfile? Profile);
}
