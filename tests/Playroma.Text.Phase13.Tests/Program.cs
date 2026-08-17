using Playroma.Text.Core.Batch;
using Playroma.Text.Core.Generation;
using Playroma.Text.Editor.WinForms;
using Playroma.Text.Generators;
using Playroma.Text.Generators.Stages;
using Playroma.Text.RpgMakerMV;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

Check(!string.IsNullOrWhiteSpace(TextEngineInfo.EngineVersion), "Phase 13 engine marker remains available on current engine");
var registry = GeneratorCatalog.CreatePhase13Registry();
Check(registry.TryGet(RandomListStage.StageKind, out _), "Phase 13 preserves complete generator stack");
Check(MvDatabaseTextTargetCatalog.SupportedKinds.Contains("Enemies"), "batch database target catalog exposes enemies");

var field = new TextTargetDescriptor("database.enemies", "data/Enemies.json", "4", "/name", "name", "Name", "Old Beast", false, false, 64,
    new HashSet<TextImportMode>([TextImportMode.Replace, TextImportMode.FillIfBlank, TextImportMode.CreateRecord]), "name", "Old Beast", ["starter.fantasy.monster-name"]);
var note = new TextTargetDescriptor("database.enemies", "data/Enemies.json", "4", "/note", "note", "Note", "", true, true, null,
    new HashSet<TextImportMode>([TextImportMode.Replace, TextImportMode.FillIfBlank, TextImportMode.CreateRecord]), "note", "Old Beast");
var template = new TextRecordTargetDescriptor("database.enemies", "data/Enemies.json", "4", "New Enemy", [field with { CurrentValue = "" }, note], "name", CanCreateRecord: true, SuggestedCreateRecordId: 4);
var existing = new TextRecordTargetDescriptor("database.enemies", "data/Enemies.json", "1", "Enemy 0001 — Old Beast", [field with { RecordId = "1" }, note with { RecordId = "1" }], "name");
var host = new FakeBatchHost(new TextBatchTargetSnapshot("database.enemies", "data/Enemies.json", "Enemies", [existing], template, [4,5,6,7], ["Old Beast"]));

string root = Path.Combine(Path.GetTempPath(), "playroma-phase13-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(root);
    using var studio = new TextStudioSession(new TextStudioOptions(root, "Phase 13 Test", BatchHost: host));
    await studio.InitializeAsync();
    var batch = new BatchContentBuilderSession(studio, host);
    await batch.PrepareAndGenerateAsync("database.enemies", "starter.fantasy.monster-name", BatchContentMode.AppendNew, 3, "name", 1234);
    Check(batch.Rows.Count == 3, "append batch produces requested rows");
    Check(batch.Rows.All(r => r.RecordId is >= 4 and <= 6), "append batch uses host-reserved IDs");
    Check(batch.Rows.All(r => !string.IsNullOrWhiteSpace(r.FindCell("name")?.Value)), "batch maps primary result into name cells");

    BatchContentRow first = batch.Rows[0];
    string original = first.FindCell("name")!.Value;
    batch.ToggleCellLock(first.Id, "name");
    await batch.RegenerateCellsAsync([(first.Id, "name")]);
    Check(batch.Rows[0].FindCell("name")!.Value == original, "locked cell survives cell regeneration");

    string secondName = batch.Rows[1].FindCell("name")!.Value;
    batch.SetCellValue(batch.Rows[2].Id, "name", secondName);
    Check(batch.Validate().DuplicateCount > 0, "batch duplicate validator catches in-grid duplicate");
    batch.SetCellValue(batch.Rows[2].Id, "name", "Unique Phase Thirteen Beast");

    TextBatchImportPlan plan = await batch.BuildImportPlanAsync();
    Check(plan.Creations.Count == 3, "batch import plan contains record creations");
    Check(plan.Changes.Count >= 3, "batch import plan contains generated field changes");
    Check(plan.CanApply, "batch import plan can apply");
    await batch.ApplyImportAsync(plan);
    Check(host.Applied, "batch host receives final import as one plan");

    var rowA = new TextBatchImportRowPlan("A", new TextImportPlan("A", [new("data/Enemies.json", "1", "/name", "A", "B", "name")], []));
    var rowB = new TextBatchImportRowPlan("B", new TextImportPlan("B", [new("data/Enemies.json", "1", "/name", "A", "C", "name")], []));
    TextBatchImportPlan conflict = TextBatchImportPlanner.Combine("Conflict", [rowA, rowB]);
    Check(conflict.Issues.Any(i => i.IsError && i.Code == "PTXTBATCH0002"), "cross-row conflicting write protection");
}
finally
{
    try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Playroma Text Phase 13 tests FAILED:");
    foreach (string failure in failures.Distinct()) Console.Error.WriteLine(" - " + failure);
    return 1;
}
Console.WriteLine("Playroma Text Phase 13 tests passed.");
return 0;

sealed class FakeBatchHost : ITextBatchHost
{
    private readonly TextBatchTargetSnapshot _snapshot;
    public FakeBatchHost(TextBatchTargetSnapshot snapshot) => _snapshot = snapshot;
    public bool Applied { get; private set; }

    public ValueTask<TextBatchTargetSnapshot> BuildSnapshotAsync(string adapterKind, int requestedCreateSlots = 0, CancellationToken cancellationToken = default) => ValueTask.FromResult(_snapshot);

    public ValueTask<TextBatchImportPlan> BuildBatchPlanAsync(IReadOnlyList<TextBatchImportRequest> requests, CancellationToken cancellationToken = default)
    {
        var rows = new List<TextBatchImportRowPlan>();
        foreach (TextBatchImportRequest request in requests)
        {
            TextImportRecordCreation? creation = request.CreateRecord && request.CreateRecordId is int id
                ? new TextImportRecordCreation(request.Target.DataFile, id, $"{{\"id\":{id},\"name\":\"\",\"note\":\"\"}}", "Enemy " + id)
                : null;
            TextRecordTargetDescriptor target = request.CreateRecordId is int rid ? request.Target with
            {
                RecordId = rid.ToString(),
                Fields = request.Target.Fields.Select(f => f with { RecordId = rid.ToString(), CurrentValue = "" }).ToArray()
            } : request.Target;
            rows.Add(new TextBatchImportRowPlan(request.RowLabel ?? target.DisplayName, TextImportPlanner.Build(target, request.Bundle, request.Selections, creation)));
        }
        return ValueTask.FromResult(TextBatchImportPlanner.Combine("Fake batch import", rows));
    }

    public ValueTask ApplyBatchAsync(TextBatchImportPlan plan, CancellationToken cancellationToken = default) { Applied = true; return ValueTask.CompletedTask; }
}
