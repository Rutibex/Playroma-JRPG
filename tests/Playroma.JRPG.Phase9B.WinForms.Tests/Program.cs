using Playroma.JRPG.Application;
using Playroma.JRPG.Core;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editor.WinForms.Database;
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
            Console.WriteLine("PASS Phase 9B Skills, Items, Equipment, and States WinForms smoke test");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL Phase 9B WinForms smoke test: " + exception);
            return 1;
        }
    }

    private static void Run()
    {
        string root = FindRepositoryRoot();
        string source = Path.Combine(root, "testdata", "fixtures", "database-catalog-project");
        string temporary = Path.Combine(Path.GetTempPath(), "PlayromaJRPG-Phase9B-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(source, temporary);
        try
        {
            OperationResult<MvProjectSession> opened = MvProjectSession.OpenAsync(temporary, analysisLoadMode: ProjectAnalysisLoadMode.Deferred).AsTask().GetAwaiter().GetResult();
            MvProjectSession session = opened.Value ?? throw new InvalidOperationException(string.Join(Environment.NewLine, opened.Diagnostics.Select(item => item.Message)));
            try
            {
                foreach (MvDocumentKind kind in new[] { MvDocumentKind.Skills, MvDocumentKind.Items, MvDocumentKind.Weapons, MvDocumentKind.Armors, MvDocumentKind.States })
                {
                    SmokeCategory(session, kind, 1);
                }
            }
            finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        finally { try { Directory.Delete(temporary, recursive: true); } catch { } }
    }

    private static void SmokeCategory(MvProjectSession session, MvDocumentKind kind, int recordId)
    {
        MvDatabaseFileSummary summary = session.Workspace.Snapshot.Database.Find(kind) ?? throw new InvalidOperationException($"{kind} fixture is missing.");
        IProjectDocumentLease lease = session.Workspace.AcquireDocumentAsync(summary.RelativePath).AsTask().GetAwaiter().GetResult();
        try
        {
            MutableJsonProjectDocument document = lease.Document as MutableJsonProjectDocument ?? throw new InvalidOperationException($"{kind} did not load as mutable JSON.");
            IDatabaseEditorDefinition definition = DatabaseEditorRegistry.CreateBuiltIn().Get(kind);
            IDatabaseEditorBody body = kind switch
            {
                MvDocumentKind.Skills or MvDocumentKind.Items => new UsableItemEditorControl(session, document, definition, kind),
                MvDocumentKind.Weapons or MvDocumentKind.Armors => new EquipmentEditorControl(session, document, definition, kind),
                MvDocumentKind.States => new StateEditorControl(session, document, definition),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            try
            {
                body.BindAsync([new DatabaseRecordHandle(kind, summary.RelativePath, recordId)]).AsTask().GetAwaiter().GetResult();
                if (body.View.IsDisposed || !body.View.Enabled) throw new InvalidOperationException($"{kind} editor did not bind.");
                bool hasIconPreview = EnumerateControls(body.View).Any(control =>
                    string.Equals(control.AccessibleName, "Selected icon preview", StringComparison.Ordinal));
                if (!hasIconPreview) throw new InvalidOperationException($"{kind} editor is missing the selected icon preview control.");
                DatabaseFieldDescriptor name = definition.RecordSchema.FindField("name");
                DatabaseMutationService mutations = new(session.Editing);
                string original = document.GetString($"/{recordId}/name") ?? string.Empty;
                mutations.SetFieldAsync([new DatabaseRecordHandle(kind, summary.RelativePath, recordId)], name, original + " Smoke").AsTask().GetAwaiter().GetResult();
                session.Undo.UndoAsync().AsTask().GetAwaiter().GetResult();
                if (!string.Equals(original, document.GetString($"/{recordId}/name"), StringComparison.Ordinal)) throw new InvalidOperationException($"{kind} undo did not restore the original name.");
            }
            finally { body.View.Dispose(); }
        }
        finally { lease.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in EnumerateControls(child)) yield return descendant;
        }
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
