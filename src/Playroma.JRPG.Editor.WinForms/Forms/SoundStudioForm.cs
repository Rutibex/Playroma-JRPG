using System.Security.Cryptography;
using System.Text;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Windowing;
using Playroma.Sound.Authoring.Workspace;
using Playroma.Sound.WinForms.Application;
using Playroma.Sound.WinForms.Integration;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>
/// Hosts the complete Playroma Sound WinForms application inside the Playroma jRPG
/// multi-window shell while preserving the standalone editor's project, graph, generator,
/// streaming-scene, analysis, validation, playback, recovery, and export workflows.
/// </summary>
public sealed class SoundStudioForm : EditorToolForm
{
    private readonly IWorkspaceService _workspace;
    private Playroma.Sound.WinForms.MainForm? _studio;
    private bool _hostClosePrepared;

    public SoundStudioForm(WindowRequest request, IWorkspaceService workspace)
        : base(request)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Name = nameof(SoundStudioForm);
        Text = "Playroma Sound Studio";
        MinimumSize = new Size(1100, 720);
        Size = new Size(1520, 920);
    }

    public override string Title
    {
        get
        {
            string project = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectTitle ?? string.Empty;
            return string.IsNullOrWhiteSpace(project)
                ? "Playroma Sound Studio"
                : $"Playroma Sound Studio — {project}";
        }
    }

    public override bool IsDirty => _studio?.HasUnsavedChanges == true;

    protected override ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string projectRoot = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectRoot
            ?? throw new InvalidOperationException("Playroma Sound Studio requires an open RPG Maker MV project.");
        string projectTitle = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectTitle ?? "RPG Maker MV Project";

        string soundProjectDirectory = Path.Combine(projectRoot, "data", "PlayromaSound", "Project");
        Directory.CreateDirectory(soundProjectDirectory);
        string defaultSoundProject = Path.Combine(soundProjectDirectory, "ProjectSounds.playproject");
        if (!File.Exists(defaultSoundProject))
        {
            using AuthoringWorkspace created = AuthoringWorkspace.Create(
                defaultSoundProject,
                CreateStableProjectId(projectRoot),
                projectTitle + " Sounds");
        }

        string settingsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Playroma",
            "jRPG",
            "SoundStudio",
            CreateStableDirectoryKey(projectRoot));
        EditorApplicationPaths paths = new(
            settingsRoot,
            Path.Combine(settingsRoot, "editor-settings.json"),
            Path.Combine(settingsRoot, "recent-projects.json"),
            Path.Combine(settingsRoot, "Autosave"));
        var settingsStore = new EditorSettingsStore(paths.SettingsFilePath);
        Playroma.Sound.WinForms.Application.EditorSettings settings = settingsStore.Load();
        settings.LastProjectPath = defaultSoundProject;

        var integration = new SoundEditorIntegrationOptions(
            projectRoot,
            projectTitle,
            RequestHostClose);
        _studio = new Playroma.Sound.WinForms.MainForm(
            paths,
            settingsStore,
            settings,
            [defaultSoundProject],
            integration)
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
        if (_studio is not null)
        {
            _studio.Dispose();
            _studio = null;
        }
        return ValueTask.CompletedTask;
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

    private static string CreateStableProjectId(string projectRoot)
    {
        string key = CreateStableDirectoryKey(projectRoot).ToLowerInvariant();
        return "playroma.jrpg.sound." + key;
    }

    private static string CreateStableDirectoryKey(string projectRoot)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(projectRoot).ToUpperInvariant()));
        return Convert.ToHexString(hash)[..16];
    }
}
