using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;

List<(string Name, Func<Task> Test)> tests =
[
    ("Every standard array database has a reusable definition", RegistryCoverageAsync),
    ("Default record templates are legal JSON with matching IDs", TemplateCoverageAsync),
    ("Virtual index handles ten thousand records without detached clones", TenThousandRecordIndexAsync),
    ("Record filters preserve IDs while sorting visible rows", FilteringAndSortingAsync),
    ("Aggregate field binding distinguishes uniform and mixed values", AggregateValuesAsync),
    ("Known field edits preserve unknown raw properties and undo exactly", LosslessFieldEditAsync),
    ("Database maximum expansion and reduction undo exactly", MaximumResizeAsync),
    ("Maximum planner reports populated slots and exact references", MaximumPlanningAsync),
    ("Clipboard round trip preserves raw unknown data and assigns safe IDs", ClipboardRoundTripAsync),
    ("Trait and effect registries preserve unknown codes", StructuredCodeRegistryAsync),
    ("JavaScript and note analysis reports malformed text", TextAnalysisAsync),
    ("Numeric batch operations are deterministic", NumericBatchAsync),
    ("History index maps nested JSON targets to records", HistoryIndexAsync)
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
Console.WriteLine($"{tests.Count - failures}/{tests.Count} Phase 8 tests passed.");
return failures == 0 ? 0 : 1;

static Task RegistryCoverageAsync()
{
    DatabaseEditorRegistry registry = DatabaseEditorRegistry.CreateBuiltIn();
    MvDocumentKind[] expected =
    [
        MvDocumentKind.Actors, MvDocumentKind.Classes, MvDocumentKind.Skills,
        MvDocumentKind.Items, MvDocumentKind.Weapons, MvDocumentKind.Armors,
        MvDocumentKind.Enemies, MvDocumentKind.Troops, MvDocumentKind.States,
        MvDocumentKind.Animations, MvDocumentKind.Tilesets, MvDocumentKind.CommonEvents
    ];
    foreach (MvDocumentKind kind in expected)
    {
        IDatabaseEditorDefinition definition = registry.Get(kind);
        True(definition.RecordSchema.Sections.Count > 0);
        True(definition.RecordSchema.Fields.Select(field => field.Key).Distinct(StringComparer.Ordinal).Count() == definition.RecordSchema.Fields.Count);
    }
    return Task.CompletedTask;
}

static Task TemplateCoverageAsync()
{
    foreach (IDatabaseEditorDefinition definition in DatabaseEditorRegistry.CreateBuiltIn().Definitions)
    {
        string raw = definition.CreateDefaultRecordJson(37);
        using JsonDocument document = JsonDocument.Parse(raw);
        Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Equal(37, document.RootElement.GetProperty("id").GetInt32());
        string duplicate = definition.CreateDuplicateRecordJson(raw[..^1] + ",\"pluginUnknown\":{\"x\":1}}", 52);
        using JsonDocument copied = JsonDocument.Parse(duplicate);
        Equal(52, copied.RootElement.GetProperty("id").GetInt32());
        Equal(1, copied.RootElement.GetProperty("pluginUnknown").GetProperty("x").GetInt32());
    }
    return Task.CompletedTask;
}

static Task TenThousandRecordIndexAsync()
{
    StringBuilder json = new("[null");
    for (int id = 1; id <= 10_000; id++) json.Append(",{\"id\":").Append(id).Append(",\"name\":\"Record ").Append(id).Append("\",\"note\":\"searchable\"}");
    json.Append(']');
    MutableJsonProjectDocument document = Create("data/Skills.json", MvDocumentKind.Skills, Utf8(json.ToString()));
    DatabaseRecordIndex index = new(document);
    Equal(10_000, index.Records.Count);
    Equal(10_000, index.HighestId);
    Equal(1, index.Query(DatabaseRecordFilter.Parse("id:9999 searchable")).Count);
    return Task.CompletedTask;
}

static Task FilteringAndSortingAsync()
{
    MutableJsonProjectDocument document = Create("data/Actors.json", MvDocumentKind.Actors,
        Utf8("[null,{\"id\":1,\"name\":\"Zulu\",\"note\":\"\"},null,{\"id\":3,\"name\":\"Alpha\",\"note\":\"fire\"}]") );
    DatabaseRecordIndex index = new(document);
    IReadOnlyList<DatabaseRecordIndexEntry> visible = index.Query(new DatabaseRecordFilter(Text: "", Populated: true, Sort: DatabaseRecordSort.Name));
    Equal(2, visible.Count);
    Equal(3, visible[0].Id);
    Equal(1, visible[1].Id);
    Equal(3, index.Query(DatabaseRecordFilter.Parse("fire used:true")).Single().Id);
    return Task.CompletedTask;
}

static Task AggregateValuesAsync()
{
    MutableJsonProjectDocument document = Create("data/Actors.json", MvDocumentKind.Actors,
        Utf8("[null,{\"id\":1,\"name\":\"A\",\"note\":\"\"},{\"id\":2,\"name\":\"A\",\"note\":\"\"},{\"id\":3,\"name\":\"B\",\"note\":\"\"}]") );
    DatabaseFieldDescriptor field = DatabaseEditorRegistry.CreateBuiltIn().Get(MvDocumentKind.Actors).RecordSchema.FindField("name");
    AggregateValue<object?> uniform = DatabaseValueAccessor.ReadAggregate(document,
        [new DatabaseRecordHandle(MvDocumentKind.Actors, document.RelativePath, 1), new DatabaseRecordHandle(MvDocumentKind.Actors, document.RelativePath, 2)], field);
    Equal(AggregateValueKind.Uniform, uniform.Kind);
    Equal("A", Convert.ToString(uniform.Value));
    AggregateValue<object?> mixed = DatabaseValueAccessor.ReadAggregate(document,
        [new DatabaseRecordHandle(MvDocumentKind.Actors, document.RelativePath, 1), new DatabaseRecordHandle(MvDocumentKind.Actors, document.RelativePath, 3)], field);
    Equal(AggregateValueKind.Mixed, mixed.Kind);
    return Task.CompletedTask;
}

static async Task LosslessFieldEditAsync()
{
    byte[] original = Utf8("[null,{\"id\":1,\"name\":\"Old\",\"note\":\"<Tag:  x>\\n\",\"pluginUnknown\":{\"ordered\":[3,2,1]}}]");
    MutableJsonProjectDocument document = Create("data/Actors.json", MvDocumentKind.Actors, original);
    EditorCommandContext context = new(new InMemoryResolver(document));
    UndoManager undo = new(context);
    SetJsonValueCommand command = new(document.RelativePath, new JsonPath("/1/name"), "\"New\"", "Rename actor");
    await undo.ExecuteAsync(command).ConfigureAwait(false);
    Equal("New", document.GetString("/1/name")!);
    Equal("{\"ordered\":[3,2,1]}", document.GetRawJson("/1/pluginUnknown")!);
    await undo.UndoAsync().ConfigureAwait(false);
    Equal("Old", document.GetString("/1/name")!);
    byte[] restored = (await document.GenerateBytesAsync().ConfigureAwait(false)).ToArray();
    True(restored.AsSpan().SequenceEqual(original), "Undo did not restore exact original bytes.");
}

static async Task MaximumResizeAsync()
{
    byte[] original = Utf8("[null,{\"id\":1,\"name\":\"One\",\"unknown\":7},null,{\"id\":3,\"name\":\"Three\"}]");
    MutableJsonProjectDocument document = Create("data/Items.json", MvDocumentKind.Items, original);
    EditorCommandContext context = new(new InMemoryResolver(document));
    UndoManager undo = new(context);
    await undo.ExecuteAsync(new ResizeDatabaseArrayCommand(document.RelativePath, 1)).ConfigureAwait(false);
    Equal(2, (document.Syntax.Root as JsonArraySyntax)!.Elements.Count);
    await undo.UndoAsync().ConfigureAwait(false);
    True((await document.GenerateBytesAsync().ConfigureAwait(false)).Span.SequenceEqual(original));
    await undo.RedoAsync().ConfigureAwait(false);
    Equal(2, (document.Syntax.Root as JsonArraySyntax)!.Elements.Count);
    await undo.UndoAsync().ConfigureAwait(false);
    await undo.ExecuteAsync(new ResizeDatabaseArrayCommand(document.RelativePath, 6)).ConfigureAwait(false);
    Equal(7, (document.Syntax.Root as JsonArraySyntax)!.Elements.Count);
    await undo.UndoAsync().ConfigureAwait(false);
    True((await document.GenerateBytesAsync().ConfigureAwait(false)).Span.SequenceEqual(original));
}

static Task MaximumPlanningAsync()
{
    MutableJsonProjectDocument document = Create("data/Skills.json", MvDocumentKind.Skills,
        Utf8("[null,{\"id\":1,\"name\":\"One\"},{\"id\":2,\"name\":\"Two\",\"pluginUnknown\":true}]") );
    ProjectReferenceIndex references = new();
    references.ReplaceAll([
        new ProjectReference(new ReferenceSource("data/Actors.json", "/1/test", RecordId: 1), new ReferenceTarget(ReferenceTargetKind.Skill, 2), ReferenceUsageKind.DatabaseField, ReferenceConfidence.Exact),
        new ProjectReference(new ReferenceSource("data/Actors.json", "/1/note", RecordId: 1), new ReferenceTarget(ReferenceTargetKind.Skill, 2), ReferenceUsageKind.Note, ReferenceConfidence.Heuristic)
    ]);
    DatabaseMaximumChangePlan plan = new DatabaseMaximumChangePlanner().CreatePlan(document, MvDocumentKind.Skills, 1, references);
    Equal(1, plan.RemovedSlots.Count);
    True(plan.RemovedSlots[0].IsPopulated);
    Equal(1, plan.RemovedSlots[0].UnknownPropertyCount);
    Equal(1, plan.ExactReferences.Count);
    Equal(1, plan.HeuristicReferences.Count);
    False(plan.CanApplySafely);
    return Task.CompletedTask;
}

static Task ClipboardRoundTripAsync()
{
    DatabaseClipboardPayload payload = new()
    {
        FormatVersion = DatabaseClipboardPayload.CurrentFormatVersion,
        SourceProjectId = Guid.NewGuid(),
        SourceKind = MvDocumentKind.Skills,
        Records = [new ClipboardDatabaseRecord(7, "{\"id\":7,\"name\":\"Raw\",\"pluginUnknown\":[1,2]}")],
        References = [new ClipboardReference("/7/note", new ReferenceTarget(ReferenceTargetKind.Skill, 4), ReferenceConfidence.Heuristic, "script")],
        Assets = ["Magic/Flare"]
    };
    DatabaseClipboardPayload decoded = DatabaseClipboardPayload.Deserialize(payload.Serialize());
    DatabasePastePlan plan = DatabasePastePlanner.Create(decoded, MvDocumentKind.Skills, [1, 2, 3], availableAssets: []);
    Equal(4, plan.Assignments.Single().DestinationId);
    using JsonDocument record = JsonDocument.Parse(plan.Assignments.Single().RawJson);
    Equal(4, record.RootElement.GetProperty("id").GetInt32());
    Equal(2, record.RootElement.GetProperty("pluginUnknown").GetArrayLength());
    True(plan.RequiresReview);
    return Task.CompletedTask;
}

static Task StructuredCodeRegistryAsync()
{
    string known = MvTraitDefinitions.Create().FormatRaw("{\"code\":21,\"dataId\":2,\"value\":1.5}");
    True(known.Contains("Parameter 2", StringComparison.Ordinal));
    string unknown = MvEffectDefinitions.Create().FormatRaw("{\"code\":999,\"dataId\":8,\"value1\":0.5,\"value2\":2}");
    True(unknown.Contains("Unknown Code 999", StringComparison.Ordinal));
    return Task.CompletedTask;
}

static Task TextAnalysisAsync()
{
    CodeTextAnalysis analysis = CodeTextAnalyzer.AnalyzeJavaScript("a.hp * 2 + (v[1]", noteTags: false);
    True(analysis.Diagnostics.Any(item => item.Code == DiagnosticCodes.Phase8UnmatchedOpeningBracket));
    CodeTextAnalysis note = CodeTextAnalyzer.AnalyzeJavaScript("<Passive State: 7>\\n/* missing", noteTags: true);
    True(note.Tokens.Any(token => token.Kind == CodeTextTokenKind.NoteTag));
    True(note.Diagnostics.Any(item => item.Code == DiagnosticCodes.Phase8UnterminatedComment));
    Equal("Passive", CodeTextAnalyzer.ExtractNoteTags("<Passive: 7>").Single().Name);
    return Task.CompletedTask;
}

static Task NumericBatchAsync()
{
    Equal(15d, NumericBatchCalculator.Apply(10, NumericBatchOperation.Add, 5));
    Equal(25d, NumericBatchCalculator.Apply(10, NumericBatchOperation.Multiply, 2.5));
    Equal(10d, NumericBatchCalculator.Apply(20, NumericBatchOperation.ClampMaximum, 10));
    Equal(1.24d, NumericBatchCalculator.Apply(1.235, NumericBatchOperation.Round, 2));
    return Task.CompletedTask;
}

static Task HistoryIndexAsync()
{
    DatabaseHistoryIndex index = new();
    EditorCommandEvent notification = new(Guid.NewGuid(), "Changed formula", EditorCommandExecutionKind.Execute,
        [new EditorMutationTarget("data/Skills.json", new JsonPath("/42/damage/formula"))], DateTimeOffset.UtcNow);
    index.Observe(notification, MvDocumentKind.Skills);
    Equal(1, index.ForRecord("data/Skills.json", 42).Count);
    return Task.CompletedTask;
}

static MutableJsonProjectDocument Create(string path, MvDocumentKind kind, byte[] bytes) =>
    MutableJsonProjectDocument.Create(path, kind, bytes, Fingerprint(bytes));

static ContentFingerprint Fingerprint(byte[] bytes) => new(
    bytes.LongLength,
    DateTimeOffset.UnixEpoch,
    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
static void True(bool condition, string message = "Assertion was false.") { if (!condition) throw new InvalidOperationException(message); }
static void False(bool condition, string message = "Assertion was true.") => True(!condition, message);
static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
}

file sealed class InMemoryResolver : IEditorDocumentResolver
{
    private readonly Dictionary<string, IMutableProjectDocument> _documents;
    public InMemoryResolver(params IMutableProjectDocument[] documents) => _documents = documents.ToDictionary(document => document.RelativePath, StringComparer.OrdinalIgnoreCase);
    public ValueTask<IMutableProjectDocument> OpenDocumentAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_documents[relativePath.Replace('\\', '/')]);
    }
}
