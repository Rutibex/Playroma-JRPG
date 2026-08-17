using Playroma.Sprite.Core;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class AnimationTagDialog : Form
{
    private readonly TextBox nameBox = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown fromBox = new() { Minimum = 0, Dock = DockStyle.Fill };
    private readonly NumericUpDown toBox = new() { Minimum = 0, Dock = DockStyle.Fill };
    private readonly ComboBox directionBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Button colorButton = new() { Text = "Choose color...", Dock = DockStyle.Fill };
    private string? color;

    public AnimationTagDialog(int frameCount, SpriteAnimationTag? tag = null)
    {
        Text = tag is null ? "New Animation Tag" : "Edit Animation Tag";
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false; Width = 430; Height = 300;
        fromBox.Maximum = toBox.Maximum = Math.Max(0, frameCount - 1);
        directionBox.Items.AddRange(Enum.GetNames<AnimationPlaybackDirection>().Cast<object>().ToArray());
        nameBox.Text = tag?.Name ?? "Animation";
        fromBox.Value = Math.Clamp(tag?.FromFrame ?? 0, 0, Math.Max(0, frameCount - 1));
        toBox.Value = Math.Clamp(tag?.ToFrame ?? Math.Max(0, frameCount - 1), 0, Math.Max(0, frameCount - 1));
        directionBox.SelectedItem = (tag?.Direction ?? AnimationPlaybackDirection.Forward).ToString();
        color = tag?.Color; RefreshColorButton();
        colorButton.Click += (_, _) => PickColor();

        TableLayoutPanel table = new() { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 6 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(table, 0, "Name", nameBox); AddRow(table, 1, "First frame", fromBox); AddRow(table, 2, "Last frame", toBox);
        AddRow(table, 3, "Direction", directionBox); AddRow(table, 4, "Timeline color", colorButton);
        FlowLayoutPanel buttons = new() { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        Button ok = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel); table.Controls.Add(buttons, 0, 5); table.SetColumnSpan(buttons, 2);
        Controls.Add(table); AcceptButton = ok; CancelButton = cancel;
        FormClosing += (_, e) => { if (DialogResult == DialogResult.OK && ToFrame < FromFrame) { MessageBox.Show(this, "The last frame cannot precede the first frame.", "Invalid tag", MessageBoxButtons.OK, MessageBoxIcon.Warning); e.Cancel = true; } };
    }

    public string TagName => string.IsNullOrWhiteSpace(nameBox.Text) ? "Animation" : nameBox.Text.Trim();
    public int FromFrame => Decimal.ToInt32(fromBox.Value);
    public int ToFrame => Decimal.ToInt32(toBox.Value);
    public AnimationPlaybackDirection Direction => Enum.TryParse(directionBox.SelectedItem?.ToString(), out AnimationPlaybackDirection value) ? value : AnimationPlaybackDirection.Forward;
    public string? TagColor => color;

    private void PickColor()
    {
        using ColorDialog dialog = new() { FullOpen = true, Color = ParseColor(color) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        color = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}"; RefreshColorButton();
    }

    private void RefreshColorButton()
    {
        Color parsed = ParseColor(color); colorButton.BackColor = parsed; colorButton.ForeColor = parsed.GetBrightness() < .5f ? Color.White : Color.Black;
        colorButton.Text = color ?? "Choose color...";
    }

    private static Color ParseColor(string? value)
    {
        try { return string.IsNullOrWhiteSpace(value) ? Color.SteelBlue : ColorTranslator.FromHtml(value); }
        catch { return Color.SteelBlue; }
    }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 10, 3) }, 0, row);
        table.Controls.Add(control, 1, row);
    }
}
