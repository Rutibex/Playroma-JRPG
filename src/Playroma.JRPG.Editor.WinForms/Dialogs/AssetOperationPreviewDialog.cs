using Playroma.JRPG.Application;

namespace Playroma.JRPG.Editor.WinForms.Dialogs;

public sealed class AssetOperationPreviewDialog : Form
{
    public AssetOperationPreviewDialog(AssetManagementPreview preview, string applyCaption = "Stage Operation")
    {
        ArgumentNullException.ThrowIfNull(preview);
        Text = preview.Description;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 480);
        ClientSize = new Size(880, 620);
        Padding = new Padding(8);

        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateTextPage("Files", preview.FileChanges.Count == 0 ? "No physical file changes." : string.Join(Environment.NewLine, preview.FileChanges)));
        tabs.TabPages.Add(CreateTextPage("JSON Changes", BuildJsonChanges(preview)));
        tabs.TabPages.Add(CreateTextPage("Exact References", BuildReferences(preview.ExactReferences)));
        tabs.TabPages.Add(CreateTextPage("Heuristic References", BuildReferences(preview.HeuristicReferences, heuristic: true)));

        Label summary = new()
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(8),
            Text = $"{preview.Transaction.ChangedFileCount} file change(s), {preview.Transaction.ChangedDocumentCount} JSON document(s), {preview.ExactReferences.Count} exact reference(s), {preview.HeuristicReferences.Count} heuristic mention(s).\r\nHeuristic references are reported but are never rewritten automatically."
        };
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        Button apply = new() { Text = applyCaption, DialogResult = DialogResult.OK, AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange([apply, cancel]);
        Controls.Add(tabs);
        Controls.Add(summary);
        Controls.Add(buttons);
        AcceptButton = apply;
        CancelButton = cancel;
    }

    private static TabPage CreateTextPage(string title, string text) => new(title)
    {
        Controls =
        {
            new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 9f),
                Text = text
            }
        }
    };

    private static string BuildJsonChanges(AssetManagementPreview preview)
    {
        if (preview.Transaction.Documents.Count == 0) return "No structured JSON references will change.";
        System.Text.StringBuilder text = new();
        foreach (var document in preview.Transaction.Documents)
        {
            text.AppendLine(document.RelativePath);
            foreach (var difference in document.Differences) text.AppendLine($"  {difference.Kind}: {difference.Path}");
            text.AppendLine();
        }
        return text.ToString();
    }

    private static string BuildReferences(IEnumerable<Playroma.JRPG.Assets.AssetReferenceHint> references, bool heuristic = false)
    {
        var list = references.ToArray();
        if (list.Length == 0) return heuristic ? "No heuristic references were detected." : "No exact references were detected.";
        return string.Join(Environment.NewLine, list.Select(reference => $"{reference.SourceRelativePath} {reference.SourceLocation}  {reference.Context}"));
    }
}
