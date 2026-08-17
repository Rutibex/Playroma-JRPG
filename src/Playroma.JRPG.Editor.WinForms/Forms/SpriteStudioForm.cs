using Playroma.JRPG.Application;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Windowing;
using Playroma.Sprite.Editor.WinForms;
using Playroma.Sprite.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>Embeds Playroma Sprite Studio in the jRPG multi-window shell.</summary>
public sealed class SpriteStudioForm : EditorToolForm
{
    private readonly IWorkspaceService _workspace;
    private MainForm? _studio;
    private bool _hostClosePrepared;

    public SpriteStudioForm(WindowRequest request, IWorkspaceService workspace) : base(request)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Name = nameof(SpriteStudioForm);
        Text = "Playroma Sprite Studio";
        MinimumSize = new Size(1000, 680);
        Size = new Size(1480, 920);
    }

    public override string Title
    {
        get
        {
            string project = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectTitle ?? string.Empty;
            return string.IsNullOrWhiteSpace(project) ? "Playroma Sprite Studio" : $"Playroma Sprite Studio — {project}";
        }
    }

    public override bool IsDirty => _studio?.HasUnsavedChanges == true;

    protected override ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string projectRoot = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectRoot
            ?? throw new InvalidOperationException("Playroma Sprite Studio requires an open RPG Maker MV project.");
        string projectTitle = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectTitle ?? "RPG Maker MV Project";
        string authoringDirectory = Path.Combine(projectRoot, "data", "PlayromaSprite");
        Directory.CreateDirectory(authoringDirectory);
        string authoringProject = Path.Combine(authoringDirectory, "ProjectSprites.playromasprite");

        MvSpriteEditorDeepLink? deepLink = BuildDeepLink(projectRoot);
        _studio = new MainForm(new SpriteEditorIntegrationOptions(
            ProjectRoot: projectRoot,
            ProjectTitle: projectTitle,
            AuthoringProjectPath: authoringProject,
            RequestHostClose: RequestHostClose,
            InitialImagePath: deepLink?.ImagePath,
            DeepLink: deepLink,
            AssetInstalled: OnAssetInstalledAsync))
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


    private MvSpriteEditorDeepLink? BuildDeepLink(string projectRoot)
    {
        DocumentRequest? document = Request.Location?.Document ?? Request.Document;
        if (document is null || string.IsNullOrWhiteSpace(document.RelativePath)) return null;
        string root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(projectRoot, document.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(root, comparison) ||
            !Path.GetExtension(candidate).Equals(".png", StringComparison.OrdinalIgnoreCase)) return null;
        return new MvSpriteEditorDeepLink(
            ImagePath: candidate,
            CellIndex: document.EventPageIndex,
            DatabaseSection: document.DatabaseKind?.ToString(),
            DatabaseRecordId: Request.Location?.RecordId ?? document.RecordId,
            PropertyName: Request.Location?.SelectionPath);
    }

    private async ValueTask OnAssetInstalledAsync(MvRuntimeInstallResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        MvProjectSession? session = _workspace.CurrentSession;
        if (session is null) return;
        await session.RefreshAssetCatalogueAsync(cancellationToken).ConfigureAwait(true);
        await _workspace.RefreshReferencesAsync(cancellationToken).ConfigureAwait(true);
    }

    private void RequestHostClose()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke((Action)RequestHostClose); return; }
        Close();
    }
}
