using Playroma.JRPG.RpgMakerMV;
using Playroma.Text.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Dialogs;

internal sealed class TextGenerationTargetPickerDialog : Form
{
    private readonly ListBox _fields = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Button _open = new() { Text = "Open Text Studio", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

    public TextGenerationTargetPickerDialog(MvDocumentKind kind)
    {
        Text = "Generate Text — Choose Database Field";
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
        Size = new Size(480, 410); MinimumSize = new Size(420, 340);
        var info = new Label { Dock = DockStyle.Top, Height = 56, Padding = new Padding(8), Text = "Choose the field Text Studio should treat as the primary generation target. Complete recipes may still populate additional compatible fields during Import." };
        foreach (var field in MvDatabaseTextTargetCatalog.ForKind(kind.ToString())) _fields.Items.Add(new FieldItem(field));
        if (_fields.Items.Count > 0) _fields.SelectedIndex = 0;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.AddRange([_open, _cancel]);
        Controls.Add(_fields); Controls.Add(info); Controls.Add(buttons);
        _open.Click += (_, _) => AcceptSelection();
        _fields.DoubleClick += (_, _) => AcceptSelection();
        AcceptButton = _open; CancelButton = _cancel;
    }

    public string? SelectedFieldKey { get; private set; }

    private void AcceptSelection()
    {
        if (_fields.SelectedItem is not FieldItem item) return;
        SelectedFieldKey = item.Spec.Key; DialogResult = DialogResult.OK; Close();
    }

    private sealed record FieldItem(MvTextFieldSpec Spec)
    {
        public override string ToString() => Spec.DisplayName + (Spec.IsNote ? "  (plugin-tag protected Note)" : string.Empty);
    }
}
