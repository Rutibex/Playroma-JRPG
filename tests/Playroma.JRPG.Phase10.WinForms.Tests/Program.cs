using Playroma.JRPG.Application;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Saving;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms;
using Playroma.JRPG.Editor.WinForms.Documents;
using Playroma.JRPG.Editor.WinForms.Forms;
using Playroma.JRPG.Editor.WinForms.Windowing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Validation;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try { ApplicationConfiguration.Initialize(); Run(); Console.WriteLine("PASS Phase 12 dedicated Event Editor smoke test"); return 0; }
        catch (Exception exception) { Console.Error.WriteLine("FAIL Phase 12 dedicated Event Editor smoke test: " + exception); return 1; }
    }

    private static void Run()
    {
        string root = FindRepositoryRoot();
        string source = Path.Combine(root, "testdata", "fixtures", "event-command-catalog-project");
        string temporary = Path.Combine(Path.GetTempPath(), "PlayromaJRPG-Phase12-WinForms-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(source, temporary);
        try
        {
            OperationResult<MvProjectSession> opened = MvProjectSession.OpenAsync(
                temporary,
                analysisLoadMode: ProjectAnalysisLoadMode.Deferred).AsTask().GetAwaiter().GetResult();
            MvProjectSession session = opened.Value
                ?? throw new InvalidOperationException(string.Join("; ", opened.Diagnostics.Select(item => item.Message)));
            try
            {
                MvDatabaseFileSummary common = session.Workspace.Snapshot.Database.Find(MvDocumentKind.CommonEvents)
                    ?? throw new InvalidOperationException("CommonEvents missing.");
                DocumentRequest document = MvDocumentRequestFactory.CreateDatabaseRequest(common) with
                {
                    Identity = "common-event:2",
                    RecordId = 2,
                    EventId = 2,
                    EventSurface = "CommonEvent",
                    DisplayTitle = "Common Event 0002"
                };
                if (document.Kind != EditorDocumentKind.EventEditor)
                    throw new InvalidOperationException("CommonEvents was not routed to the dedicated Event Editor.");
                WindowRequest request = new(
                    EditorWindowKind.EventEditor,
                    document.Identity,
                    document.DisplayTitle,
                    Presentation: WindowPresentation.Modal,
                    Scope: WindowScope.Project,
                    PlacementId: "EventEditor",
                    Document: document,
                    Location: new NavigationLocation(document, RecordId: 2));
                SessionWorkspaceService workspace = new(session);
                NotificationService notifications = new();
                JsonSettingsService settings = new(Path.Combine(temporary, "phase10-winforms-settings.json"));
                using Control dispatcher = new();
                using WinFormsThemeService theme = new(settings);
                WinFormsWindowPlacementService placement = new(settings);
                WinFormsWindowService windows = new(dispatcher, theme, placement, notifications);
                EventEditorForm form = new(request, workspace, windows, notifications) { Size = new Size(1500, 900) };
                try
                {
                    form.LoadAsync().AsTask().GetAwaiter().GetResult();
                    using Bitmap image = new(form.Width, form.Height);
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    if (form.Controls.Count == 0 || image.Width != 1500)
                        throw new InvalidOperationException("Dedicated Event Editor did not render.");
                }
                finally
                {
                    form.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    windows.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
            finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        finally { try { Directory.Delete(temporary, true); } catch { } }
    }

    private sealed class SessionWorkspaceService(MvProjectSession session) : IWorkspaceService
    {
        public MvProjectSession? CurrentSession { get; private set; } = session;
        public ProjectSessionState? State => CurrentSession?.State;
        public ProjectValidationResult? LastValidation => CurrentSession?.LastValidation;
        public event EventHandler<WorkspaceStateChangedEventArgs>? StateChanged { add { } remove { } }
        public event EventHandler<ValidationChangedEventArgs>? ValidationChanged { add { } remove { } }
        public ValueTask<OperationResult<MvProjectSession>> OpenAsync(string path, IProgress<ProjectOpenProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ProjectSaveResult> SaveAsync(ProjectSaveOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ProjectValidationResult> ValidateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> RefreshReferencesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask DiscardAllChangesAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public void RequestRecoverySnapshot() { }
        public ValueTask CloseAsync(bool discardUnsaved, CancellationToken cancellationToken = default) { CurrentSession = null; return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { CurrentSession = null; return ValueTask.CompletedTask; }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Playroma.JRPG.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }
}
