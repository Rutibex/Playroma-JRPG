using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Commands;

public sealed class WinFormsCommandDispatcher : IMessageFilter, IDisposable
{
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private readonly IEditorCommandRouter _router;
    private bool _disposed;

    public WinFormsCommandDispatcher(IEditorCommandRouter router)
    {
        _router = router;
        System.Windows.Forms.Application.AddMessageFilter(this);
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg is not (WmKeyDown or WmSysKeyDown)) return false;
        Keys keyData = (Keys)(int)m.WParam | Control.ModifierKeys;
        if (!TryMap(keyData, out EditorCommandId command)) return false;
        EditorCommandStatus status = _router.Query(command);
        if (status.Handling == CommandHandling.NotHandled) return false;
        if (status.Handling == CommandHandling.Enabled)
            _ = _router.ExecuteAsync(command, CommandInvocationSource.Keyboard);
        return true;
    }

    public static bool TryMap(Keys keyData, out EditorCommandId command)
    {
        command = keyData switch
        {
            Keys.Control | Keys.S => EditorCommandIds.FileSaveProject,
            Keys.Control | Keys.Z => EditorCommandIds.EditUndo,
            Keys.Control | Keys.Y => EditorCommandIds.EditRedo,
            Keys.Control | Keys.X => EditorCommandIds.EditCut,
            Keys.Control | Keys.C => EditorCommandIds.EditCopy,
            Keys.Control | Keys.V => EditorCommandIds.EditPaste,
            Keys.Delete => EditorCommandIds.EditDelete,
            Keys.Control | Keys.A => EditorCommandIds.EditSelectAll,
            Keys.Control | Keys.F => EditorCommandIds.EditFind,
            Keys.F3 => EditorCommandIds.EditFindNext,
            Keys.Control | Keys.Enter => EditorCommandIds.SessionApply,
            Keys.Control | Keys.Shift | Keys.Enter => EditorCommandIds.SessionApplyAndClose,
            Keys.Alt | Keys.Left => EditorCommandIds.NavigateBack,
            Keys.Alt | Keys.Right => EditorCommandIds.NavigateForward,
            Keys.Control | Keys.Shift | Keys.P => EditorCommandIds.NavigateCommandPalette,
            Keys.F5 => EditorCommandIds.ViewRefresh,
            Keys.Control | Keys.W => EditorCommandIds.WindowClose,
            _ => default
        };
        return !string.IsNullOrEmpty(command.Value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        System.Windows.Forms.Application.RemoveMessageFilter(this);
    }
}
