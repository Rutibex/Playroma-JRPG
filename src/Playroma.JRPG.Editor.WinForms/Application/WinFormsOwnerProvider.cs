using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Windowing;

namespace Playroma.JRPG.Editor.WinForms.Application;

public sealed class WinFormsOwnerProvider : IWinFormsOwnerProvider
{
    public IActiveWindowService? ActiveWindows { get; set; }
    public Form? ProjectHub { get; set; }
    public Form? ProjectWorkspace { get; set; }

    public IWin32Window? CurrentOwner
    {
        get
        {
            if (ActiveWindows?.ActiveWindow is IWinFormsEditorWindow editor
                && IsUsable(editor.Form))
            {
                return editor.Form;
            }

            if (IsUsable(ProjectWorkspace)) return ProjectWorkspace;
            if (IsUsable(ProjectHub)) return ProjectHub;
            return null;
        }
    }

    private static bool IsUsable(Form? form) =>
        form is { IsDisposed: false, Disposing: false, Visible: true };
}
