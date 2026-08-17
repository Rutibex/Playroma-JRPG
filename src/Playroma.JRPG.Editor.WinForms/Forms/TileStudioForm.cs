using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Windowing;
using Playroma.Tile.Editor.WinForms.Integration;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>Embeds the complete Playroma Tile authoring suite in the jRPG multi-window shell.</summary>
public sealed class TileStudioForm : EditorToolForm
{
    private readonly IWorkspaceService _workspace;
    private Playroma.Tile.Editor.WinForms.MainForm? _studio;
    private bool _hostClosePrepared;

    public TileStudioForm(WindowRequest request, IWorkspaceService workspace)
        : base(request)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Name = nameof(TileStudioForm);
        Text = "Playroma Tile Studio";
        MinimumSize = new Size(1100, 720);
        Size = new Size(1520, 920);
    }

    public override string Title
    {
        get
        {
            var project = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectTitle ?? string.Empty;
            return string.IsNullOrWhiteSpace(project)
                ? "Playroma Tile Studio"
                : $"Playroma Tile Studio — {project}";
        }
    }

    public override bool IsDirty => _studio?.HasUnsavedChanges == true;

    protected override ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var projectRoot = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectRoot
            ?? throw new InvalidOperationException("Playroma Tile Studio requires an open RPG Maker MV project.");
        var projectTitle = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectTitle ?? "RPG Maker MV Project";
        var authoringDirectory = Path.Combine(projectRoot, "data", "PlayromaTile");
        Directory.CreateDirectory(authoringDirectory);
        var authoringProject = Path.Combine(authoringDirectory, "ProjectTiles.playromatile.json");

        _studio = new Playroma.Tile.Editor.WinForms.MainForm(new TileEditorIntegrationOptions(
            projectRoot,
            projectTitle,
            authoringProject,
            RequestHostClose,
            ImportDatabaseOnFirstOpen: true))
        {
            TopLevel = false,
            FormBorderStyle = FormBorderStyle.None,
            Dock = DockStyle.Fill,
            ShowInTaskbar = false,
            WindowState = FormWindowState.Normal
        };
        InstallContent(_studio);
        _studio.Show();
        Text = Title;
        return ValueTask.CompletedTask;
    }

    public override ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_hostClosePrepared || _studio is null) return ValueTask.FromResult(true);
        _hostClosePrepared = _studio.PrepareForHostClose();
        return ValueTask.FromResult(_hostClosePrepared);
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
            _studio.SaveIntegratedProject();
            return true;
        }
        if (keyData == (Keys.Control | Keys.W))
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void RequestHostClose()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            BeginInvoke((Action)RequestHostClose);
            return;
        }
        Close();
    }
}
