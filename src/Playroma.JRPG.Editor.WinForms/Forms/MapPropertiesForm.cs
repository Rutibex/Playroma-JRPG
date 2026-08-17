using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Documents;
using Playroma.JRPG.Editor.WinForms.Windowing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>
/// Owned map-properties window. The existing properties control remains transactional and
/// applies changes through the shared project session; the form owns its document lease.
/// </summary>
public sealed class MapPropertiesForm : EditorToolForm
{
    private readonly IWorkspaceService _workspace;
    private readonly IWinFormsWindowService _windows;
    private IProjectDocumentLease? _lease;
    private MapPropertiesControl? _control;

    public MapPropertiesForm(WindowRequest request, IWorkspaceService workspace, IWinFormsWindowService windows)
        : base(request)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        MinimumSize = new Size(720, 560);
        Size = new Size(900, 720);
        ShowInTaskbar = false;
    }

    public override bool IsDirty =>
        _lease?.Document.State == MutableDocumentState.Modified;

    protected override async ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        var session = _workspace.CurrentSession
            ?? throw new InvalidOperationException("No project is open.");
        DocumentRequest document = Request.Document
            ?? throw new InvalidOperationException("Map properties require a document request.");
        int mapId = document.MapId
            ?? throw new InvalidOperationException("Map properties require a map ID.");
        MvMapDescriptor descriptor = session.Workspace.Snapshot.Maps.FirstOrDefault(map => map.Id == mapId)
            ?? throw new KeyNotFoundException($"Map {mapId} was not found.");

        _lease = await session.Workspace.AcquireDocumentAsync(
            descriptor.RelativePath,
            cancellationToken).ConfigureAwait(true);
        _control = new MapPropertiesControl(_lease.Document, descriptor);
        _control.TextGenerationRequested += OnTextGenerationRequested;
        await _control.LoadAsync(session, cancellationToken).ConfigureAwait(true);

        Panel root = new() { Dock = DockStyle.Fill };
        FlowLayoutPanel footer = new()
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        Button close = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        Label note = new()
        {
            AutoSize = true,
            Text = "Changes are applied by the Apply or Resize controls above.",
            Padding = new Padding(4, 6, 12, 0)
        };
        footer.Controls.Add(close);
        footer.Controls.Add(note);
        root.Controls.Add(_control);
        root.Controls.Add(footer);
        AcceptButton = close;
        InstallContent(root);
    }


    private async void OnTextGenerationRequested(object? sender, MapTextGenerationRequestedEventArgs e)
    {
        try
        {
            IEditorWindow window = await _windows.OpenAsync(new WindowRequest(
                EditorWindowKind.TextStudio, "text-studio", "Playroma Text Studio",
                Scope: WindowScope.Project, OwnerKey: Key, PlacementId: "PlayromaTextStudio")).ConfigureAwait(true);
            if (window is not TextStudioForm studio) throw new InvalidOperationException("Text Studio window could not be opened.");
            await studio.ActivateMapTargetAsync(e.MapId, e.RelativePath, e.FieldKey).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not open Text Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override async ValueTask OnDisposeWindowAsync()
    {
        if (_control is not null) _control.TextGenerationRequested -= OnTextGenerationRequested;
        _control = null;
        if (_lease is not null)
        {
            await _lease.DisposeAsync().ConfigureAwait(true);
            _lease = null;
        }
    }
}
