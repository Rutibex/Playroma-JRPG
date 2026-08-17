using Playroma.JRPG.Application;
using Playroma.JRPG.Core;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.Editor.WinForms.Documents;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Run();
            Console.WriteLine("PASS Phase 9E System and Tileset WinForms smoke test");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL Phase 9E WinForms smoke test: " + exception);
            return 1;
        }
    }

    private static void Run()
    {
        string root = FindRepositoryRoot();
        string source = Path.Combine(root, "testdata", "fixtures", "database-catalog-project");
        string temporary = Path.Combine(Path.GetTempPath(), "PlayromaJRPG-Phase9E-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(source, temporary);
        try
        {
            OperationResult<MvProjectSession> opened = MvProjectSession.OpenAsync(temporary, analysisLoadMode: ProjectAnalysisLoadMode.Deferred).AsTask().GetAwaiter().GetResult();
            MvProjectSession session = opened.Value ?? throw new InvalidOperationException(string.Join(Environment.NewLine, opened.Diagnostics.Select(item => item.Message)));
            try
            {
                VerifyRouting(session);
                SmokeSystem(session);
                SmokeTileset(session);
                SmokeCommonEvents(session);
            }
            finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        finally { try { Directory.Delete(temporary, recursive: true); } catch { } }
    }

    private static void VerifyRouting(MvProjectSession session)
    {
        MvDatabaseFileSummary system = session.Workspace.Snapshot.Database.Find(MvDocumentKind.System) ?? throw new InvalidOperationException("System fixture missing.");
        DocumentRequest request = MvDocumentRequestFactory.CreateDatabaseRequest(system);
        if (request.Kind != EditorDocumentKind.SystemData) throw new InvalidOperationException("System.json was not routed to the System editor.");
        MvDatabaseFileSummary mapInfos = session.Workspace.Snapshot.Database.Find(MvDocumentKind.MapInfos) ?? throw new InvalidOperationException("MapInfos fixture missing.");
        if (MvDocumentRequestFactory.CreateDatabaseRequest(mapInfos).Kind != EditorDocumentKind.MapHierarchy) throw new InvalidOperationException("MapInfos.json was not routed to Map Hierarchy.");
    }

    private static void SmokeSystem(MvProjectSession session)
    {
        MvDatabaseFileSummary summary = session.Workspace.Snapshot.Database.Find(MvDocumentKind.System) ?? throw new InvalidOperationException("System fixture missing.");
        IProjectDocumentLease lease = session.Workspace.AcquireDocumentAsync(summary.RelativePath).AsTask().GetAwaiter().GetResult();
        try
        {
            MutableJsonProjectDocument document = lease.Document as MutableJsonProjectDocument ?? throw new InvalidOperationException("System did not load as mutable JSON.");
            using SystemDocumentControl control = new(session, document) { Size = new Size(1280, 900) };
            using Bitmap image = new(control.Width, control.Height);
            control.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            if (image.Width != 1280 || control.Controls.Count == 0) throw new InvalidOperationException("System editor did not render.");
            int locationPickers = CountControls(control, candidate => string.Equals(candidate.AccessibleName, "Map location picker", StringComparison.Ordinal));
            if (locationPickers < 4) throw new InvalidOperationException("System editor did not expose visual map location pickers for the player and three vehicles.");
            if (!ContainsControlText(control, "Select Location…")) throw new InvalidOperationException("System editor map location button is missing.");
        }
        finally { lease.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private static void SmokeTileset(MvProjectSession session)
    {
        MvDatabaseFileSummary summary = session.Workspace.Snapshot.Database.Find(MvDocumentKind.Tilesets) ?? throw new InvalidOperationException("Tileset fixture missing.");
        IProjectDocumentLease lease = session.Workspace.AcquireDocumentAsync(summary.RelativePath).AsTask().GetAwaiter().GetResult();
        try
        {
            MutableJsonProjectDocument document = lease.Document as MutableJsonProjectDocument ?? throw new InvalidOperationException("Tilesets did not load as mutable JSON.");
            IDatabaseEditorDefinition definition = DatabaseEditorRegistry.CreateBuiltIn().Get(MvDocumentKind.Tilesets);
            using TilesetEditorControl control = new(session, document, definition) { Size = new Size(1280, 900) };
            control.BindAsync([new DatabaseRecordHandle(MvDocumentKind.Tilesets, summary.RelativePath, 1)]).AsTask().GetAwaiter().GetResult();
            using Bitmap image = new(control.Width, control.Height);
            control.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            if (image.Width != 1280 || !control.Enabled) throw new InvalidOperationException("Tileset Studio did not bind or render.");
            string[] requiredTexts = ["General Settings", "Images", "Passage", "Passage (4 dir)", "Ladder", "Bush", "Counter", "Damage Floor", "Terrain Tag", "Note", "A", "B", "C"];
            foreach (string required in requiredTexts)
            {
                if (!ContainsControlText(control, required)) throw new InvalidOperationException($"Tileset Studio layout is missing '{required}'.");
            }
        }
        finally { lease.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }


    private static void SmokeCommonEvents(MvProjectSession session)
    {
        MvDatabaseFileSummary summary = session.Workspace.Snapshot.Database.Find(MvDocumentKind.CommonEvents) ?? throw new InvalidOperationException("CommonEvents fixture missing.");
        IProjectDocumentLease lease = session.Workspace.AcquireDocumentAsync(summary.RelativePath).AsTask().GetAwaiter().GetResult();
        try
        {
            MutableJsonProjectDocument document = lease.Document as MutableJsonProjectDocument ?? throw new InvalidOperationException("CommonEvents did not load as mutable JSON.");
            IDatabaseEditorDefinition definition = DatabaseEditorRegistry.CreateBuiltIn().Get(MvDocumentKind.CommonEvents);
            using CommonEventEditorControl control = new(session, document, definition) { Size = new Size(1280, 900) };
            control.BindAsync([new DatabaseRecordHandle(MvDocumentKind.CommonEvents, summary.RelativePath, 1)]).AsTask().GetAwaiter().GetResult();
            using Bitmap image = new(control.Width, control.Height);
            control.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            if (image.Width != 1280 || !control.Enabled) throw new InvalidOperationException("Common Event editor did not bind or render.");
            string[] requiredTexts = ["General Settings", "Contents", "Name:", "Trigger:", "Switch:", "Open Full Event Command Editor…"];
            foreach (string required in requiredTexts)
            {
                if (!ContainsControlText(control, required)) throw new InvalidOperationException($"Common Event editor layout is missing '{required}'.");
            }
        }
        finally { lease.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private static int CountControls(Control root, Func<Control, bool> predicate)
    {
        int count = predicate(root) ? 1 : 0;
        foreach (Control child in root.Controls) count += CountControls(child, predicate);
        return count;
    }

    private static bool ContainsControlText(Control root, string text)
    {
        if (string.Equals(root.Text, text, StringComparison.Ordinal)) return true;
        foreach (Control child in root.Controls)
            if (ContainsControlText(child, text)) return true;
        return false;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Playroma.JRPG.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Playroma.JRPG.sln.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
