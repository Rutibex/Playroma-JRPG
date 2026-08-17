using System.Text;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.Editor.WinForms.PluginTags;

internal sealed class PluginTagChangePreviewDialog : Form
{
    public PluginTagChangePreviewDialog(string title, PluginTagChangePreview change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(720, 500);
        Size = new Size(900, 660);

        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("Before / After") { Controls = { CreateDiffBox(change.Lines) } });
        tabs.TabPages.Add(new TabPage("Generated syntax") { Controls = { CreateTextBox(change.GeneratedSyntax) } });
        tabs.TabPages.Add(new TabPage("Complete result") { Controls = { CreateTextBox(change.After) } });

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            WrapContents = false
        };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        Button apply = new() { Text = "Apply to working notes", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(apply);
        Controls.Add(tabs);
        Controls.Add(buttons);
        AcceptButton = apply;
        CancelButton = cancel;
    }

    private static TextBox CreateTextBox(string value) => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Text = value,
        Font = new Font(FontFamily.GenericMonospace, 9f)
    };

    private static TextBox CreateDiffBox(IReadOnlyList<PluginTagLineChange> lines)
    {
        StringBuilder builder = new();
        foreach (PluginTagLineChange line in lines) builder.Append(line.Prefix).Append(' ').AppendLine(line.Text);
        return CreateTextBox(builder.ToString());
    }
}
