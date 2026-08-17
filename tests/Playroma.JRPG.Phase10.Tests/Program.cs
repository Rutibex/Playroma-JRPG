using Playroma.JRPG.Application;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Eventing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

internal static class Program
{
    private static int Main()
    {
        try
        {
            string root = FindRepositoryRoot();
            string source = Path.Combine(root, "testdata", "fixtures", "event-command-catalog-project");
            string temporary = Path.Combine(Path.GetTempPath(), "PlayromaJRPG-Phase10-" + Guid.NewGuid().ToString("N"));
            CopyDirectory(source, temporary);
            try { Run(temporary); }
            finally { try { Directory.Delete(temporary, true); } catch { } }
            Console.WriteLine("PASS Phase 10 transactional integration test"); return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine("FAIL Phase 10 integration test: " + exception); return 1; }
    }

    private static void Run(string project)
    {
        OperationResult<MvProjectSession> opened = MvProjectSession.OpenAsync(project, analysisLoadMode: ProjectAnalysisLoadMode.Deferred).AsTask().GetAwaiter().GetResult();
        MvProjectSession session = opened.Value ?? throw new InvalidOperationException(string.Join("; ", opened.Diagnostics.Select(item => item.Message)));
        try
        {
            MvDatabaseFileSummary summary = session.Workspace.Snapshot.Database.Find(MvDocumentKind.CommonEvents) ?? throw new InvalidOperationException("Common events missing.");
            IProjectDocumentLease lease = session.Workspace.AcquireDocumentAsync(summary.RelativePath).AsTask().GetAwaiter().GetResult();
            try
            {
                MutableJsonProjectDocument document = lease.Document as MutableJsonProjectDocument ?? throw new InvalidOperationException("Not mutable JSON.");
                EventListAddress address = EventListAddress.ForCommonEvent(summary.RelativePath, 2);
                EventListSnapshot before = EventListSnapshot.Capture(document, address);
                string inserted = RawEventCommand.CreateJson(230, 0, "[30]");
                ProjectTransaction transaction = session.BeginTransaction("Insert Wait");
                try
                {
                    transaction.Add(new ReplaceEventCommandSpanCommand(address, new EventCommandSpan(1, 0), [inserted], "Insert Wait"));
                    transaction.CommitAsync().AsTask().GetAwaiter().GetResult();
                }
                finally { transaction.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                EventListSnapshot edited = EventListSnapshot.Capture(document, address);
                if (edited.Count != before.Count + 1 || edited.Commands[1].Code != 230) throw new InvalidOperationException("Insert failed.");
                session.Undo.UndoAsync().AsTask().GetAwaiter().GetResult();
                EventListSnapshot restored = EventListSnapshot.Capture(document, address);
                if (restored.RawListJson != before.RawListJson) throw new InvalidOperationException("Undo did not restore exact raw list.");
            }
            finally { lease.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private static string FindRepositoryRoot() { DirectoryInfo? current = new(AppContext.BaseDirectory); while (current is not null) { if (File.Exists(Path.Combine(current.FullName, "Playroma.JRPG.sln"))) return current.FullName; current = current.Parent; } throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { Directory.CreateDirectory(destination); foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) { string target = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true); } }
}

