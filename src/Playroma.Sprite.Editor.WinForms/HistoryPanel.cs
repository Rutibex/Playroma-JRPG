using Playroma.Sprite.Formats;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class HistoryPanel : UserControl
{
    private readonly SpriteEditorSession session;
    private readonly ListBox entries = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label memory = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(5) };

    public HistoryPanel(SpriteEditorSession editorSession)
    {
        session = editorSession ?? throw new ArgumentNullException(nameof(editorSession));
        Dock = DockStyle.Fill;
        FlowLayoutPanel commands = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        Button undo = new() { Text = "Undo", AutoSize = true };
        Button redo = new() { Text = "Redo", AutoSize = true };
        undo.Click += (_, _) => session.Undo(); redo.Click += (_, _) => session.Redo();
        commands.Controls.AddRange([undo, redo]);
        Controls.Add(entries); Controls.Add(memory); Controls.Add(commands);
        session.Changed += (_, _) => RefreshState(); RefreshState();
    }

    private void RefreshState()
    {
        if (IsDisposed) return;
        entries.BeginUpdate(); entries.Items.Clear();
        foreach (string item in session.History.RedoDescriptions.Reverse()) entries.Items.Add("Redo  " + item);
        entries.Items.Add("Current document");
        foreach (string item in session.History.UndoDescriptions) entries.Items.Add("Undo  " + item);
        entries.EndUpdate();
        memory.Text = $"{session.History.UndoCount} undo / {session.History.RedoCount} redo; approximately {FormatBytes(session.History.MemoryBytes)}";
    }

    private static string FormatBytes(long value) => value >= 1024 * 1024 ? $"{value / 1024d / 1024d:N1} MiB" : value >= 1024 ? $"{value / 1024d:N1} KiB" : $"{value} bytes";
}
