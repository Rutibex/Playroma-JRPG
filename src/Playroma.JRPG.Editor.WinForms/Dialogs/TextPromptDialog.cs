namespace Playroma.JRPG.Editor.WinForms.Dialogs;

public sealed class TextPromptDialog : Form
{
    private readonly TextBox _value = new() { Dock = DockStyle.Top };

    public TextPromptDialog(string title, string prompt, string initialValue = "")
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(430, 130);
        Padding = new Padding(12);
        _value.Text = initialValue;
        _value.SelectAll();

        Label label = new() { Text = prompt, Dock = DockStyle.Top, Height = 28, AutoEllipsis = true };
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        Button ok = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange([ok, cancel]);
        Controls.Add(_value);
        Controls.Add(label);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Value => _value.Text.Trim();

    public static string? Show(IWin32Window owner, string title, string prompt, string initialValue = "")
    {
        using TextPromptDialog dialog = new(title, prompt, initialValue);
        return dialog.ShowDialog(owner) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.Value) ? dialog.Value : null;
    }
}
