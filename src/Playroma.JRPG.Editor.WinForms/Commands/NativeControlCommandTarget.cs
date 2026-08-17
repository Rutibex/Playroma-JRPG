using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Commands;

internal sealed class NativeControlCommandTarget(Control control) : IEditorCommandTarget
{
    public event EventHandler? CommandStateChanged { add { } remove { } }

    public EditorCommandStatus QueryCommand(EditorCommandId command, EditorCommandContext context)
    {
        if (control is TextBoxBase text)
        {
            bool editable = !text.ReadOnly && text.Enabled;
            if (command == EditorCommandIds.EditUndo)
                return text.CanUndo ? EditorCommandStatus.Enabled("Undo Typing") : EditorCommandStatus.NotHandled(command);
            if (command == EditorCommandIds.EditCut)
                return editable ? Status("Cut", text.SelectionLength > 0) : EditorCommandStatus.Disabled("Cut", "Text is read-only.");
            if (command == EditorCommandIds.EditCopy)
                return Status("Copy", text.SelectionLength > 0);
            if (command == EditorCommandIds.EditPaste)
                return editable ? Status("Paste", Clipboard.ContainsText()) : EditorCommandStatus.Disabled("Paste", "Text is read-only.");
            if (command == EditorCommandIds.EditDelete)
                return editable ? EditorCommandStatus.Enabled("Delete Text") : EditorCommandStatus.Disabled("Delete", "Text is read-only.");
            if (command == EditorCommandIds.EditSelectAll) return EditorCommandStatus.Enabled("Select All");
        }
        if (control is ComboBox combo && combo.DropDownStyle != ComboBoxStyle.DropDownList)
        {
            if (command == EditorCommandIds.EditCut) return Status("Cut", combo.SelectionLength > 0);
            if (command == EditorCommandIds.EditCopy) return Status("Copy", combo.SelectionLength > 0);
            if (command == EditorCommandIds.EditPaste) return Status("Paste", Clipboard.ContainsText());
            if (command == EditorCommandIds.EditDelete) return EditorCommandStatus.Enabled("Delete Text");
            if (command == EditorCommandIds.EditSelectAll) return EditorCommandStatus.Enabled("Select All");
        }
        return EditorCommandStatus.NotHandled(command);
    }

    public ValueTask ExecuteCommandAsync(EditorCommandId command, EditorCommandContext context)
    {
        if (control is TextBoxBase text)
        {
            if (command == EditorCommandIds.EditUndo) text.Undo();
            else if (command == EditorCommandIds.EditCut) text.Cut();
            else if (command == EditorCommandIds.EditCopy) text.Copy();
            else if (command == EditorCommandIds.EditPaste) text.Paste();
            else if (command == EditorCommandIds.EditDelete) text.SelectedText = string.Empty;
            else if (command == EditorCommandIds.EditSelectAll) text.SelectAll();
        }
        else if (control is ComboBox combo)
        {
            if (command == EditorCommandIds.EditCut)
            {
                if (combo.SelectionLength > 0) Clipboard.SetText(combo.SelectedText);
                combo.SelectedText = string.Empty;
            }
            else if (command == EditorCommandIds.EditCopy && combo.SelectionLength > 0)
            {
                Clipboard.SetText(combo.SelectedText);
            }
            else if (command == EditorCommandIds.EditPaste && Clipboard.ContainsText())
            {
                combo.SelectedText = Clipboard.GetText();
            }
            else if (command == EditorCommandIds.EditDelete)
            {
                combo.SelectedText = string.Empty;
            }
            else if (command == EditorCommandIds.EditSelectAll)
            {
                combo.SelectAll();
            }
        }
        return ValueTask.CompletedTask;
    }

    private static EditorCommandStatus Status(string name, bool enabled) =>
        enabled ? EditorCommandStatus.Enabled(name) : EditorCommandStatus.Disabled(name);
}
