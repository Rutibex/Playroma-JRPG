using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Music;
using Playroma.JRPG.Editor.WinForms.Windowing;

namespace Playroma.JRPG.Editor.WinForms.Forms;

public sealed class MusicStudioForm : EditorToolForm
{
    private readonly IWorkspaceService _workspace;
    private MusicStudioControl? _studio;

    public MusicStudioForm(WindowRequest request, IWorkspaceService workspace)
        : base(request)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Name = nameof(MusicStudioForm);
        Text = "Playroma Music Studio";
        MinimumSize = new Size(1100, 700);
        Size = new Size(1500, 900);
    }

    public override string Title
    {
        get
        {
            string project = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectTitle ?? string.Empty;
            return string.IsNullOrWhiteSpace(project)
                ? "Playroma Music Studio"
                : $"Playroma Music Studio — {project}";
        }
    }

    public override bool IsDirty => _studio?.IsDirty == true;

    protected override ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string projectRoot = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectRoot
            ?? throw new InvalidOperationException("Playroma Music Studio requires an open RPG Maker MV project.");
        _studio = new MusicStudioControl(projectRoot);
        InstallContent(_studio);
        Text = Title;
        return ValueTask.CompletedTask;
    }

    public override ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_studio?.PromptSave(this) != false);
    }

    protected override ValueTask OnDisposeWindowAsync()
    {
        _studio?.Dispose();
        _studio = null;
        return ValueTask.CompletedTask;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S) && _studio is not null)
        {
            _studio.SaveProject(this);
            return true;
        }
        if (keyData == (Keys.Control | Keys.W))
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
