using Playroma.JRPG.Editing;

namespace Playroma.JRPG.Editor.WinForms.Dialogs;

/// <summary>Reusable semantic preview for validation fixes, raw data edits and plugin changes.</summary>
public sealed class TransactionPreviewForm : Form
{
    public TransactionPreviewForm(string title, TransactionPreview preview, string applyCaption = "Apply")
        : this(preview, applyCaption)
    {
        Text = title;
    }

    public TransactionPreviewForm(TransactionPreview preview, string applyCaption = "Apply")
    {
        ArgumentNullException.ThrowIfNull(preview);
        Text = preview.Description;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(720, 480);
        ClientSize = new Size(940, 650);
        Padding = new Padding(8);

        Label summary = new()
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(8),
            Text = $"{preview.ChangedDocumentCount} document(s), {preview.DifferenceCount} semantic difference(s), " +
                   $"{preview.ChangedFileCount} staged file operation(s)."
        };

        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateDifferencesPage(preview));
        tabs.TabPages.Add(CreateFilesPage(preview));

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(4)
        };
        Button apply = new() { Text = applyCaption, DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(100, 30) };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(90, 30) };
        buttons.Controls.AddRange([apply, cancel]);

        Controls.Add(tabs);
        Controls.Add(summary);
        Controls.Add(buttons);
        AcceptButton = apply;
        CancelButton = cancel;
    }

    private static TabPage CreateDifferencesPage(TransactionPreview preview)
    {
        DataGridView grid = ControlHelpers.CreateReadOnlyGrid();
        grid.Columns.Add("Document", "Document");
        grid.Columns.Add("Kind", "Kind");
        grid.Columns.Add("Path", "JSON Path");
        grid.Columns.Add("Before", "Before");
        grid.Columns.Add("After", "After");
        grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        foreach (TransactionDocumentPreview document in preview.Documents)
        {
            foreach (var difference in document.Differences)
            {
                grid.Rows.Add(
                    document.RelativePath,
                    difference.Kind,
                    difference.Path,
                    difference.Before ?? string.Empty,
                    difference.After ?? string.Empty);
            }
        }
        return new TabPage("Semantic Changes") { Controls = { grid } };
    }

    private static TabPage CreateFilesPage(TransactionPreview preview)
    {
        DataGridView grid = ControlHelpers.CreateReadOnlyGrid();
        grid.Columns.Add("Kind", "Operation");
        grid.Columns.Add("Source", "Source");
        grid.Columns.Add("Destination", "Destination");
        grid.Columns.Add("Size", "Generated Size");
        grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        foreach (TransactionFilePreview file in preview.Files)
        {
            grid.Rows.Add(file.Kind, file.SourceRelativePath ?? string.Empty, file.DestinationRelativePath ?? string.Empty, ControlHelpers.FormatBytes(file.GeneratedLength));
        }
        return new TabPage("Files") { Controls = { grid } };
    }
}
