using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Validation;

List<(string Name, Func<Task> Test)> tests =
[
    ("Single-command undo and redo", SingleCommandUndoRedoAsync),
    ("Cross-document transaction", CrossDocumentTransactionAsync),
    ("Transaction preview restores state", TransactionPreviewAsync),
    ("Command events are atomic", CommandEventPublicationAsync),
    ("Partial command rollback", PartialCommandRollbackAsync),
    ("Sparse map stroke", SparseMapStrokeAsync),
    ("Database deletion preserves IDs", DatabaseDeletionPreservesIdsAsync),
    ("History merge and saved marker", HistoryMergeAsync),
    ("Duplicate-property semantic diff", DuplicatePropertyDiffAsync),
    ("Reference indexing", ReferenceIndexingAsync),
    ("Exact and heuristic references", ExactAndHeuristicReferencesAsync),
    ("Integrated project session", IntegratedProjectSessionAsync),
    ("Missing-reference validation", MissingReferenceValidationAsync),
    ("Safe diagnostic fix is undoable", SafeDiagnosticFixAsync)
];

int failures = 0;
foreach ((string name, Func<Task> test) in tests)
{
    try
    {
        await test().ConfigureAwait(false);
        Console.WriteLine("PASS " + name);
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + exception);
    }
}

Console.WriteLine($"{tests.Count - failures}/{tests.Count} Phase 3 tests passed.");
return failures == 0 ? 0 : 1;

static async Task SingleCommandUndoRedoAsync()
{
    byte[] actorsBytes = Utf8("[null,{\"id\":1,\"name\":\"Hero\",\"classId\":1,\"note\":\"\"}]");
    using MutableJsonProjectDocument actors = Create("data/Actors.json", MvDocumentKind.Actors, actorsBytes);
    InMemoryResolver resolver = new(actors);
    EditorCommandContext context = new(resolver);
    UndoManager undo = new(context);
    await using ProjectTransaction transaction = new("Rename actor", context, undo);
    transaction.Add(new SetJsonStringCommand("data/Actors.json", "/1/name", "Alicia"));
    await transaction.CommitAsync().ConfigureAwait(false);
    Equal("Alicia", ReadString(actors, "/1/name"));
    True(undo.CanUndo);

    await undo.UndoAsync().ConfigureAwait(false);
    True(actors.CaptureGeneratedBytes().AsSpan().SequenceEqual(actorsBytes), "Undo did not restore exact bytes.");
    await undo.RedoAsync().ConfigureAwait(false);
    Equal("Alicia", ReadString(actors, "/1/name"));
}

static async Task CrossDocumentTransactionAsync()
{
    using MutableJsonProjectDocument actors = Create("data/Actors.json", MvDocumentKind.Actors,
        Utf8("[null,{\"id\":1,\"name\":\"Hero\",\"classId\":1}]") );
    using MutableJsonProjectDocument classes = Create("data/Classes.json", MvDocumentKind.Classes,
        Utf8("[null,{\"id\":1,\"name\":\"General\",\"learnings\":[],\"traits\":[]}]") );
    InMemoryResolver resolver = new(actors, classes);
    EditorCommandContext context = new(resolver);
    UndoManager undo = new(context);
    await using ProjectTransaction transaction = new("Rename actor and class", context, undo);
    transaction.Add(new SetJsonStringCommand("data/Actors.json", "/1/name", "Alicia"));
    transaction.Add(new SetJsonStringCommand("data/Classes.json", "/1/name", "Vanguard"));
    await transaction.CommitAsync().ConfigureAwait(false);
    Equal("Alicia", ReadString(actors, "/1/name"));
    Equal("Vanguard", ReadString(classes, "/1/name"));
    Equal(1, undo.UndoCount);

    await undo.UndoAsync().ConfigureAwait(false);
    Equal("Hero", ReadString(actors, "/1/name"));
    Equal("General", ReadString(classes, "/1/name"));
}


static async Task TransactionPreviewAsync()
{
    byte[] actorsBytes = Utf8("[null,{\"id\":1,\"name\":\"Hero\",\"classId\":1}]");
    using MutableJsonProjectDocument actors = Create("data/Actors.json", MvDocumentKind.Actors, actorsBytes);
    InMemoryResolver resolver = new(actors);
    EditorCommandContext context = new(resolver);
    UndoManager undo = new(context);
    await using ProjectTransaction transaction = new("Preview rename", context, undo);
    transaction.Add(new SetJsonStringCommand("data/Actors.json", "/1/name", "Alicia"));

    TransactionPreview preview = await transaction.PreviewAsync().ConfigureAwait(false);
    Equal(1, preview.ChangedDocumentCount);
    True(preview.DifferenceCount > 0, "Preview did not report the changed actor name.");
    True(actors.CaptureGeneratedBytes().AsSpan().SequenceEqual(actorsBytes),
        "Preview did not restore the document's exact pre-preview bytes.");
    Equal(0, undo.UndoCount);

    await transaction.CommitAsync().ConfigureAwait(false);
    Equal("Alicia", ReadString(actors, "/1/name"));
}

static async Task CommandEventPublicationAsync()
{
    using MutableJsonProjectDocument actors = Create("data/Actors.json", MvDocumentKind.Actors,
        Utf8("[null,{\"id\":1,\"name\":\"Hero\"}]"));
    using MutableJsonProjectDocument classes = Create("data/Classes.json", MvDocumentKind.Classes,
        Utf8("[null,{\"id\":1,\"name\":\"General\",\"learnings\":[],\"traits\":[]}]") );
    EditorCommandContext context = new(new InMemoryResolver(actors, classes));
    UndoManager undo = new(context);
    List<EditorCommandEvent> events = [];
    context.CommandApplied += (_, e) => events.Add(e);

    await using (ProjectTransaction transaction = new("Atomic transaction", context, undo))
    {
        transaction.Add(new SetJsonStringCommand("data/Actors.json", "/1/name", "Alicia"));
        transaction.Add(new SetJsonStringCommand("data/Classes.json", "/1/name", "Vanguard"));
        await transaction.CommitAsync().ConfigureAwait(false);
    }

    Equal(1, events.Count);
    Equal(EditorCommandExecutionKind.Execute, events[0].Kind);
    Equal(2, events[0].Targets.Select(target => target.NormalizedRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    await undo.UndoAsync().ConfigureAwait(false);
    Equal(2, events.Count);
    Equal(EditorCommandExecutionKind.Undo, events[1].Kind);
}

static async Task PartialCommandRollbackAsync()
{
    byte[] actorsBytes = Utf8("[null,{\"id\":1,\"name\":\"Hero\",\"classId\":1}]");
    byte[] classesBytes = Utf8("[null,{\"id\":1,\"name\":\"General\",\"learnings\":[],\"traits\":[]}]");
    using MutableJsonProjectDocument actors = Create("data/Actors.json", MvDocumentKind.Actors, actorsBytes);
    using MutableJsonProjectDocument classes = Create("data/Classes.json", MvDocumentKind.Classes, classesBytes);
    InMemoryResolver resolver = new(actors, classes);
    EditorCommandContext context = new(resolver);
    UndoManager undo = new(context);
    await using ProjectTransaction transaction = new("Failing transaction", context, undo);
    transaction.Add(new SetJsonStringCommand("data/Actors.json", "/1/name", "Changed"));
    transaction.Add(new PartiallyFailingCommand("data/Classes.json", "/1/name"));
    bool failed = false;
    try
    {
        await transaction.CommitAsync().ConfigureAwait(false);
    }
    catch (EditorTransactionException)
    {
        failed = true;
    }

    True(failed, "The intentionally failing transaction unexpectedly succeeded.");
    True(actors.CaptureGeneratedBytes().AsSpan().SequenceEqual(actorsBytes), "Actors document was not restored byte-for-byte.");
    True(classes.CaptureGeneratedBytes().AsSpan().SequenceEqual(classesBytes), "Classes document was not restored byte-for-byte.");
    Equal(0, undo.UndoCount);
}

static async Task SparseMapStrokeAsync()
{
    int[] values = Enumerable.Range(0, 12).ToArray();
    string mapJson = "{\"displayName\":\"Test\",\"width\":2,\"height\":1,\"tilesetId\":1,\"data\":["
        + string.Join(',', values) + "],\"events\":[null]}";
    using MutableJsonProjectDocument map = Create("data/Map001.json", MvDocumentKind.Map, Utf8(mapJson));
    InMemoryResolver resolver = new(map);
    EditorCommandContext context = new(resolver);
    UndoManager undo = new(context);
    await using ProjectTransaction transaction = new("Paint", context, undo);
    transaction.Add(new MapTileStrokeCommand("data/Map001.json", [new TileDelta(0, 99), new TileDelta(7, 88)]));
    await transaction.CommitAsync().ConfigureAwait(false);
    Equal(99, map.GetInt32("/data/0") ?? -1);
    Equal(88, map.GetInt32("/data/7") ?? -1);
    Equal(1, undo.UndoCount);
    await undo.UndoAsync().ConfigureAwait(false);
    Equal(0, map.GetInt32("/data/0") ?? -1);
    Equal(7, map.GetInt32("/data/7") ?? -1);
}

static async Task DatabaseDeletionPreservesIdsAsync()
{
    byte[] before = Utf8("[null,{\"id\":1,\"name\":\"One\"},{\"id\":2,\"name\":\"Two\"}]");
    using MutableJsonProjectDocument actors = Create("data/Actors.json", MvDocumentKind.Actors, before);
    EditorCommandContext context = new(new InMemoryResolver(actors));
    UndoManager undo = new(context);
    await using ProjectTransaction transaction = new("Delete actor slot", context, undo);
    transaction.Add(new RemoveDatabaseRecordCommand("data/Actors.json", 1));
    await transaction.CommitAsync().ConfigureAwait(false);

    Equal("null", actors.GetRawJson("/1") ?? string.Empty);
    Equal(2, actors.GetInt32("/2/id") ?? -1);
    await undo.UndoAsync().ConfigureAwait(false);
    True(actors.CaptureGeneratedBytes().AsSpan().SequenceEqual(before),
        "Undo did not restore the database array exactly.");
}

static async Task HistoryMergeAsync()
{
    using MutableJsonProjectDocument actors = Create("data/Actors.json", MvDocumentKind.Actors,
        Utf8("[null,{\"id\":1,\"name\":\"Hero\"}]") );
    InMemoryResolver resolver = new(actors);
    EditorCommandContext context = new(resolver);
    UndoManager undo = new(context);
    undo.MarkSaved();

    await using (ProjectTransaction first = new("Type name", context, undo, allowHistoryMerge: true))
    {
        first.Add(new SetJsonStringCommand("data/Actors.json", "/1/name", "Her"));
        await first.CommitAsync().ConfigureAwait(false);
    }
    await using (ProjectTransaction second = new("Type name", context, undo, allowHistoryMerge: true))
    {
        second.Add(new SetJsonStringCommand("data/Actors.json", "/1/name", "Heroine"));
        await second.CommitAsync().ConfigureAwait(false);
    }

    Equal(1, undo.UndoCount);
    True(!undo.IsAtSavedState, "Editing after the saved marker incorrectly remained clean.");
    await undo.UndoAsync().ConfigureAwait(false);
    Equal("Hero", ReadString(actors, "/1/name"));
}


static Task DuplicatePropertyDiffAsync()
{
    byte[] before = Utf8("{\"value\":1,\"value\":2}");
    byte[] after = Utf8("{\"value\":9,\"value\":2}");
    IReadOnlyList<JsonSemanticDifference> differences = JsonSemanticDiffer.Compare(before, after);
    True(differences.Any(item => item.Path.ToString().Contains("occurrence=0", StringComparison.Ordinal)),
        "The semantic differ concealed a changed earlier duplicate property occurrence.");
    return Task.CompletedTask;
}

static async Task ReferenceIndexingAsync()
{
    string root = RepositoryRootLocator.Find();
    string fixture = Path.Combine(root, "testdata", "fixtures", "database-catalog-project");
    OperationResult<Playroma.JRPG.Workspaces.MvProjectWorkspace> opened =
        await Playroma.JRPG.Workspaces.MvProjectWorkspace.OpenAsync(fixture).ConfigureAwait(false);
    Playroma.JRPG.Workspaces.MvProjectWorkspace workspace = opened.Value
        ?? throw new InvalidOperationException(string.Join("; ", opened.Diagnostics.Select(item => item.Message)));
    await using (workspace)
    {
        ProjectReferenceIndex index = await new MvProjectReferenceIndexer().BuildAsync(workspace).ConfigureAwait(false);
        True(index.Statistics.DocumentCount >= 15);
        True(index.Statistics.ExactReferenceCount > 0, "No exact references were indexed.");
        True(index.FindInbound(new ReferenceTarget(ReferenceTargetKind.Class, 1)).Count > 0,
            "Actor-to-class reference was not indexed.");
    }
}


static async Task ExactAndHeuristicReferencesAsync()
{
    string root = RepositoryRootLocator.Find();
    string source = Path.Combine(root, "testdata", "fixtures", "database-catalog-project");
    using TemporaryDirectory temporary = new();
    CopyDirectory(source, temporary.Path);
    OperationResult<MvEditingSession> opened = await MvEditingSession.OpenAsync(temporary.Path).ConfigureAwait(false);
    MvEditingSession editing = opened.Value
        ?? throw new InvalidOperationException(string.Join("; ", opened.Diagnostics.Select(item => item.Message)));
    await using (editing)
    {
        await using ProjectTransaction transaction = editing.BeginTransaction("Add heuristic note reference");
        transaction.Add(new SetDatabaseFieldCommand(
            "data/Actors.json",
            1,
            "note",
            "\"$dataSkills[1] and switch: 1\""));
        await transaction.CommitAsync().ConfigureAwait(false);

        ProjectReferenceIndex index = await new MvProjectReferenceIndexer().BuildAsync(editing.Workspace).ConfigureAwait(false);
        True(index.FindInbound(new ReferenceTarget(ReferenceTargetKind.Class, 1), includeHeuristic: false).Count > 0,
            "Exact actor-to-class reference was lost.");
        True(index.FindInbound(new ReferenceTarget(ReferenceTargetKind.Skill, 1), includeHeuristic: true)
                .Any(reference => reference.Confidence == ReferenceConfidence.Heuristic),
            "The note-contained skill reference was not classified as heuristic.");
        True(index.FindInbound(new ReferenceTarget(ReferenceTargetKind.Switch, 1), includeHeuristic: true)
                .Any(reference => reference.Confidence == ReferenceConfidence.Heuristic),
            "The note-contained switch reference was not classified as heuristic.");
    }
}

static async Task IntegratedProjectSessionAsync()
{
    string root = RepositoryRootLocator.Find();
    string fixture = Path.Combine(root, "testdata", "fixtures", "database-catalog-project");
    OperationResult<MvProjectSession> opened = await MvProjectSession.OpenAsync(fixture).ConfigureAwait(false);
    MvProjectSession session = opened.Value
        ?? throw new InvalidOperationException(string.Join("; ", opened.Diagnostics.Select(item => item.Message)));
    await using (session)
    {
        True(session.References.Statistics.DocumentCount >= 15);
        DependencyReport dependencies = session.GetDependencies(new ReferenceTarget(ReferenceTargetKind.Class, 1));
        True(dependencies.ExactCount > 0, "Integrated dependency report did not find actor-to-class references.");
        ProjectValidationResult validation = await session.ValidateAsync().ConfigureAwait(false);
        True(validation.CompletedUtc > DateTimeOffset.UnixEpoch);
    }
}

static async Task MissingReferenceValidationAsync()
{
    string root = RepositoryRootLocator.Find();
    string source = Path.Combine(root, "testdata", "fixtures", "database-catalog-project");
    using TemporaryDirectory temporary = new();
    CopyDirectory(source, temporary.Path);
    OperationResult<MvEditingSession> opened = await MvEditingSession.OpenAsync(temporary.Path).ConfigureAwait(false);
    MvEditingSession session = opened.Value
        ?? throw new InvalidOperationException(string.Join("; ", opened.Diagnostics.Select(item => item.Message)));
    await using (session)
    {
        await using ProjectTransaction transaction = session.BeginTransaction("Break actor class reference");
        transaction.Add(new SetDatabaseFieldCommand("data/Actors.json", 1, "classId", "999999"));
        await transaction.CommitAsync().ConfigureAwait(false);
        ProjectReferenceIndex references = await new MvProjectReferenceIndexer().BuildAsync(session.Workspace).ConfigureAwait(false);
        ProjectValidationResult validation = await new ProjectValidationEngine().ValidateAsync(session.Workspace, references).ConfigureAwait(false);
        True(validation.Diagnostics.Any(item =>
            item.Code == DiagnosticCodes.Phase3MissingReference
            && item.Diagnostic.Location.RelativePath?.EndsWith("Actors.json", StringComparison.OrdinalIgnoreCase) == true),
            "Missing class reference was not diagnosed.");
    }
}

static async Task SafeDiagnosticFixAsync()
{
    string root = RepositoryRootLocator.Find();
    string source = Path.Combine(root, "testdata", "fixtures", "database-catalog-project");
    using TemporaryDirectory temporary = new();
    CopyDirectory(source, temporary.Path);
    OperationResult<MvProjectSession> opened = await MvProjectSession.OpenAsync(temporary.Path).ConfigureAwait(false);
    MvProjectSession session = opened.Value
        ?? throw new InvalidOperationException(string.Join("; ", opened.Diagnostics.Select(item => item.Message)));
    await using (session)
    {
        await using (ProjectTransaction transaction = session.BeginTransaction("Break animation reference"))
        {
            transaction.Add(new SetDatabaseFieldCommand("data/Skills.json", 1, "animationId", "999999"));
            await transaction.CommitAsync().ConfigureAwait(false);
        }

        ProjectValidationResult validation = await session.ValidateAsync().ConfigureAwait(false);
        ProjectValidationDiagnostic diagnostic = validation.Diagnostics.First(item =>
            item.Code == DiagnosticCodes.Phase3MissingReference
            && item.Diagnostic.Location.RelativePath?.EndsWith("Skills.json", StringComparison.OrdinalIgnoreCase) == true
            && item.Fix is not null);
        TransactionPreview preview = await session.PreviewFixAsync(diagnostic).ConfigureAwait(false);
        True(preview.DifferenceCount > 0, "Safe-fix preview reported no change.");

        await session.ApplyFixAsync(diagnostic).ConfigureAwait(false);
        MutableJsonProjectDocument skills = await session.Editing.Context
            .OpenJsonDocumentAsync("data/Skills.json")
            .ConfigureAwait(false);
        Equal(0, skills.GetInt32("/1/animationId") ?? -1);
        await session.Undo.UndoAsync().ConfigureAwait(false);
        Equal(999999, skills.GetInt32("/1/animationId") ?? -1);
    }
}

static MutableJsonProjectDocument Create(string path, MvDocumentKind kind, byte[] bytes) =>
    MutableJsonProjectDocument.Create(path, kind, bytes, Fingerprint(bytes));

static ContentFingerprint Fingerprint(byte[] bytes) => new(
    bytes.LongLength,
    DateTimeOffset.UnixEpoch,
    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

static string ReadString(MutableJsonProjectDocument document, JsonPath path) =>
    document.GetString(path) ?? throw new InvalidOperationException("Missing string at " + path);

static void CopyDirectory(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
    {
        Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
    }
    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        string target = Path.Combine(destination, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, overwrite: true);
    }
}

static void True(bool condition, string message = "Assertion was false.")
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }
}

file sealed class InMemoryResolver : IEditorDocumentResolver
{
    private readonly Dictionary<string, IMutableProjectDocument> _documents;

    public InMemoryResolver(params IMutableProjectDocument[] documents)
    {
        _documents = documents.ToDictionary(
            document => document.RelativePath.Replace('\\', '/'),
            StringComparer.OrdinalIgnoreCase);
    }

    public ValueTask<IMutableProjectDocument> OpenDocumentAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_documents.TryGetValue(relativePath.Replace('\\', '/'), out IMutableProjectDocument document))
        {
            throw new FileNotFoundException("In-memory test document not found.", relativePath);
        }
        return ValueTask.FromResult(document);
    }
}

file sealed class PartiallyFailingCommand : IEditorCommand
{
    private readonly string _relativePath;
    private readonly JsonPath _path;

    public PartiallyFailingCommand(string relativePath, JsonPath path)
    {
        _relativePath = relativePath;
        _path = path;
        Targets = [new EditorMutationTarget(relativePath, path)];
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Description => "Mutate then fail";
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }

    public async ValueTask ExecuteAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(_relativePath, cancellationToken).ConfigureAwait(false);
        document.SetString(_path, "Corrupted");
        throw new InvalidOperationException("Intentional test failure.");
    }

    public ValueTask UndoAsync(EditorCommandContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}

file sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PlayromaJRPG-Phase3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
        catch
        {
        }
    }
}
