using Playroma.JRPG.Application;
using Playroma.JRPG.Core;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;
using System.Reflection;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Run();
            Console.WriteLine("PASS Phase 9D Animation WinForms smoke test");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL Phase 9D WinForms smoke test: " + exception);
            return 1;
        }
    }

    private static void Run()
    {
        string root = FindRepositoryRoot();
        string source = Path.Combine(root, "testdata", "fixtures", "database-catalog-project");
        string temporary = Path.Combine(Path.GetTempPath(), "PlayromaJRPG-Phase9D-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(source, temporary);
        try
        {
            OperationResult<MvProjectSession> opened = MvProjectSession.OpenAsync(temporary, analysisLoadMode: ProjectAnalysisLoadMode.Deferred).AsTask().GetAwaiter().GetResult();
            MvProjectSession session = opened.Value ?? throw new InvalidOperationException(string.Join(Environment.NewLine, opened.Diagnostics.Select(item => item.Message)));
            try
            {
                MvDatabaseFileSummary summary = session.Workspace.Snapshot.Database.Find(MvDocumentKind.Animations) ?? throw new InvalidOperationException("Animation fixture is missing.");
                IProjectDocumentLease lease = session.Workspace.AcquireDocumentAsync(summary.RelativePath).AsTask().GetAwaiter().GetResult();
                try
                {
                    MutableJsonProjectDocument document = lease.Document as MutableJsonProjectDocument ?? throw new InvalidOperationException("Animations did not load as mutable JSON.");
                    IDatabaseEditorDefinition definition = DatabaseEditorRegistry.CreateBuiltIn().Get(MvDocumentKind.Animations);
                    using AnimationEditorControl body = new(session, document, definition);
                    body.BindAsync([new DatabaseRecordHandle(MvDocumentKind.Animations, summary.RelativePath, 1)]).AsTask().GetAwaiter().GetResult();
                    if (body.IsDisposed || !body.Enabled) throw new InvalidOperationException("Animation editor did not bind.");
                    body.Size = new Size(1200, 800);
                    using Bitmap image = new(body.Size.Width, body.Size.Height);
                    body.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    if (image.Width != 1200) throw new InvalidOperationException("Animation editor did not render its smoke-test surface.");
                    string[] required = ["General Settings", "SE and Flash Timing", "Frames", "Change Target…", "Paste Last", "Tween…", "Batch…", "Shift…", "Edit Cells…", "Play"];
                    foreach (string text in required)
                    {
                        if (!ContainsText(body, text)) throw new InvalidOperationException($"Animation studio layout is missing '{text}'.");
                    }
                    SmokeWorkspaceSizing(body);
                    SmokeSelectionSynchronization(body);
                }
                finally { lease.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            }
            finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        finally { try { Directory.Delete(temporary, recursive: true); } catch { } }
    }


    private static bool ContainsText(Control root, string text)
    {
        if (string.Equals(root.Text, text, StringComparison.Ordinal)) return true;
        if (root is ToolStrip strip)
        {
            foreach (ToolStripItem item in strip.Items)
                if (ContainsText(item, text)) return true;
        }
        foreach (Control child in root.Controls)
            if (ContainsText(child, text)) return true;
        return false;
    }

    private static bool ContainsText(ToolStripItem item, string text)
    {
        if (string.Equals(item.Text, text, StringComparison.Ordinal)) return true;
        if (item is ToolStripDropDownItem dropDown)
        {
            foreach (ToolStripItem child in dropDown.DropDownItems)
                if (ContainsText(child, text)) return true;
        }
        return false;
    }


    private static void SmokeWorkspaceSizing(AnimationEditorControl body)
    {
        FieldInfo? canvasField = typeof(AnimationEditorControl).GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo? paletteField = typeof(AnimationEditorControl).GetField("_palette", BindingFlags.Instance | BindingFlags.NonPublic);
        if (canvasField?.GetValue(body) is not Control canvas || paletteField?.GetValue(body) is not Control palette)
        {
            throw new InvalidOperationException("Animation workspace controls were not available for sizing checks.");
        }

        Application.DoEvents();
        if (canvas.Height < 180)
        {
            throw new InvalidOperationException($"Animation preview is still vertically cramped ({canvas.Height}px high).");
        }
        if (palette.Height < 180)
        {
            throw new InvalidOperationException($"Animation pattern browser is still vertically cramped ({palette.Height}px high).");
        }
    }

    private static void SmokeSelectionSynchronization(AnimationEditorControl body)
    {
        FieldInfo? cellsField = typeof(AnimationEditorControl).GetField("_cells", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo? canvasField = typeof(AnimationEditorControl).GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic);
        if (cellsField?.GetValue(body) is not DataGridView cells || canvasField?.GetValue(body) is not AnimationCanvas canvas)
        {
            throw new InvalidOperationException("Animation selection controls were not available for smoke testing.");
        }

        cells.Rows.Clear();
        cells.Rows.Add(1, 0, 0, 0, 100, 0, false, 255, "Normal", "Visible lane");
        cells.ClearSelection();
        cells.Rows[0].Selected = true;
        Application.DoEvents();
        if (!canvas.SelectedLanes.Contains(0))
        {
            throw new InvalidOperationException("Animation grid selection did not synchronize to the canvas.");
        }

        canvas.SelectLane(-1);
        Application.DoEvents();
        if (cells.SelectedRows.Count != 0)
        {
            throw new InvalidOperationException("Animation canvas deselection did not synchronize to the grid.");
        }

        canvas.SelectLane(0);
        Application.DoEvents();
        if (!cells.Rows[0].Selected)
        {
            throw new InvalidOperationException("Animation canvas selection did not synchronize to the grid.");
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
