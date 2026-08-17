namespace Playroma.Sprite.Editor.WinForms;

internal sealed class TextPromptDialog : Form
{
    private readonly TextBox textBox = new() { Dock = DockStyle.Top };

    public TextPromptDialog(string title, string prompt, string initialValue = "")
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(430, 130);
        Padding = new Padding(12);
        Label label = new() { Text = prompt, Dock = DockStyle.Top, Height = 30 };
        textBox.Text = initialValue;
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft };
        Button ok = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange([ok, cancel]);
        Controls.Add(buttons); Controls.Add(textBox); Controls.Add(label);
        AcceptButton = ok; CancelButton = cancel;
    }

    public string Value => textBox.Text.Trim();
}
