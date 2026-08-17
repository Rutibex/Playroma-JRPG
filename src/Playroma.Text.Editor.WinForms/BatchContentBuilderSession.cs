using System.Globalization;
using System.Text;
using Playroma.Text.Core.Batch;
using Playroma.Text.Core.Generation;
using Playroma.Text.RpgMakerMV;
using Playroma.Text.Generators.Names;

namespace Playroma.Text.Editor.WinForms;

/// <summary>Stateful, UI-agnostic Phase 13 batch authoring session.</summary>
public sealed class BatchContentBuilderSession
{
    private readonly TextStudioSession _studio;
    private readonly ITextBatchHost? _host;
    private readonly List<BatchContentRow> _rows = [];
    private TextBatchTargetSnapshot? _snapshot;

    public BatchContentBuilderSession(TextStudioSession studio, ITextBatchHost? host)
    {
        _studio = studio ?? throw new ArgumentNullException(nameof(studio));
        _host = host;
    }

    public IReadOnlyList<BatchContentRow> Rows => _rows;
    public TextBatchTargetSnapshot? Snapshot => _snapshot;
    public bool IsHostAvailable => _host is not null;
    public string? RecipeId { get; private set; }
    public BatchContentMode Mode { get; private set; }
    public string PrimarySemanticField { get; private set; } = "name";

    public event EventHandler? RowsChanged;

    public async ValueTask<TextBatchTargetSnapshot> LoadSnapshotAsync(string adapterKind, int requestedCreateSlots, CancellationToken cancellationToken = default)
    {
        if (_host is null) throw new InvalidOperationException("Batch database integration is not available in this host.");
        _snapshot = await _host.BuildSnapshotAsync(adapterKind, requestedCreateSlots, cancellationToken).ConfigureAwait(false);
        return _snapshot;
    }

    public async Task PrepareAndGenerateAsync(
        string adapterKind,
        string recipeId,
        BatchContentMode mode,
        int requestedRows,
        string primarySemanticField = "name",
        ulong? seed = null,
        CancellationToken cancellationToken = default)
    {
        requestedRows = Math.Clamp(requestedRows, 1, 999);
        RecipeId = recipeId;
        Mode = mode;
        PrimarySemanticField = string.IsNullOrWhiteSpace(primarySemanticField) ? "name" : primarySemanticField;
        TextBatchTargetSnapshot snapshot = await LoadSnapshotAsync(adapterKind, mode == BatchContentMode.AppendNew ? requestedRows : 0, cancellationToken).ConfigureAwait(false);
        _rows.Clear();

        IReadOnlyList<TextRecordTargetDescriptor> targets;
        if (mode == BatchContentMode.AppendNew)
        {
            targets = snapshot.SuggestedCreateRecordIds.Take(requestedRows)
                .Select(id => CreateTargetForId(snapshot.Template, id))
                .ToArray();
        }
        else
        {
            IEnumerable<TextRecordTargetDescriptor> query = snapshot.Records;
            if (mode == BatchContentMode.FillBlankExisting)
                query = query.Where(t => TargetHasBlankText(t, PrimarySemanticField));
            targets = query.Take(requestedRows).ToArray();
        }
        targets = targets.Select(t => t with { SelectedSemanticField = PrimarySemanticField }).ToArray();

        ulong rootSeed = seed ?? _studio.Seed;
        var diversity = new List<string>();
        for (int i = 0; i < targets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TextRecordTargetDescriptor target = targets[i];
            ulong rowSeed = Playroma.Text.Core.Random.SeedFactory.Derive(rootSeed, $"batch:{adapterKind}:{recipeId}:{i}");
            GeneratedTextBundle bundle = await GenerateForTargetAsync(target, recipeId, rowSeed, diversity, i, cancellationToken).ConfigureAwait(false);
            BatchContentRow row = BuildRow(target, bundle, rowSeed, mode == BatchContentMode.AppendNew, i + 1);
            _rows.Add(row);
            string primary = row.FindCell(PrimarySemanticField)?.Value ?? bundle.PrimaryText;
            if (!string.IsNullOrWhiteSpace(primary)) diversity.Add(primary);
        }
        ValidateDuplicates();
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RegenerateRowsAsync(IEnumerable<Guid> rowIds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(RecipeId)) throw new InvalidOperationException("No batch recipe is selected.");
        var ids = rowIds.ToHashSet();
        for (int index = 0; index < _rows.Count; index++)
        {
            BatchContentRow row = _rows[index];
            if (!ids.Contains(row.Id) || row.IsRowLocked) continue;
            ulong seed = Playroma.Text.Core.Random.SeedFactory.Derive(row.Seed, "batch-row-regenerate");
            TextRecordTargetDescriptor target = FindTarget(row);
            string[] references = _rows.Where(x => x.Id != row.Id).Select(x => x.FindCell(PrimarySemanticField)?.Value).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();
            GeneratedTextBundle bundle = await GenerateForTargetAsync(target, RecipeId!, seed, references, index, cancellationToken).ConfigureAwait(false);
            BatchContentRow fresh = BuildRow(target, bundle, seed, row.CreateRecord, row.DisplayIndex);
            var cells = fresh.Cells.Select(cell => row.FindCell(cell.SemanticField) is { IsLocked: true } old ? old : cell).ToArray();
            _rows[index] = fresh with { Id = row.Id, Cells = cells, IsRowLocked = row.IsRowLocked };
        }
        ValidateDuplicates();
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RegenerateCellsAsync(IEnumerable<(Guid RowId, string SemanticField)> cells, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(RecipeId)) throw new InvalidOperationException("No batch recipe is selected.");
        foreach ((Guid rowId, string semanticField) in cells)
        {
            int index = _rows.FindIndex(x => x.Id == rowId);
            if (index < 0) continue;
            BatchContentRow row = _rows[index];
            BatchCellState? current = row.FindCell(semanticField);
            if (row.IsRowLocked || current is null || current.IsLocked) continue;
            ulong seed = Playroma.Text.Core.Random.SeedFactory.Derive(row.Seed, $"batch-cell:{semanticField}");
            TextRecordTargetDescriptor target = FindTarget(row);
            string[] references = _rows.Where(x => x.Id != row.Id).Select(x => x.FindCell(PrimarySemanticField)?.Value).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();
            GeneratedTextBundle bundle = await GenerateForTargetAsync(target, RecipeId!, seed, references, index, cancellationToken).ConfigureAwait(false);
            TextTargetDescriptor? fieldTarget = target.Fields.FirstOrDefault(f => f.SemanticField.Equals(semanticField, StringComparison.OrdinalIgnoreCase));
            if (fieldTarget is null) continue;
            string generated = TextImportPlanner.ResolveGeneratedText(bundle, semanticField, fieldTarget, target.SelectedField);
            if (string.IsNullOrEmpty(generated) && !semanticField.Equals(target.SelectedField?.SemanticField, StringComparison.OrdinalIgnoreCase)) continue;
            var updatedCells = row.Cells.Select(c => c.SemanticField.Equals(semanticField, StringComparison.OrdinalIgnoreCase) ? c with { Value = generated } : c).ToArray();
            _rows[index] = row with { Cells = updatedCells, Seed = seed, SourceBundle = bundle };
        }
        ValidateDuplicates();
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCellValue(Guid rowId, string semanticField, string value)
    {
        int index = _rows.FindIndex(x => x.Id == rowId);
        if (index < 0) return;
        BatchContentRow row = _rows[index];
        _rows[index] = row with { Cells = row.Cells.Select(c => c.SemanticField.Equals(semanticField, StringComparison.OrdinalIgnoreCase) ? c with { Value = value ?? string.Empty } : c).ToArray() };
        ValidateDuplicates();
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleCellLock(Guid rowId, string semanticField)
    {
        int index = _rows.FindIndex(x => x.Id == rowId);
        if (index < 0) return;
        BatchContentRow row = _rows[index];
        _rows[index] = row with { Cells = row.Cells.Select(c => c.SemanticField.Equals(semanticField, StringComparison.OrdinalIgnoreCase) ? c with { IsLocked = !c.IsLocked } : c).ToArray() };
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleRowLock(Guid rowId)
    {
        int index = _rows.FindIndex(x => x.Id == rowId);
        if (index < 0) return;
        _rows[index] = _rows[index] with { IsRowLocked = !_rows[index].IsRowLocked };
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    public BatchValidationSummary Validate()
    {
        ValidateDuplicates();
        int warningRows = _rows.Count(r => r.HasWarnings);
        int errors = _rows.Count(r => string.Equals(r.Status, "Error", StringComparison.OrdinalIgnoreCase));
        int duplicates = _rows.Sum(r => r.EffectiveWarnings.Count(w => w.Contains("duplicate", StringComparison.OrdinalIgnoreCase)));
        var messages = _rows.SelectMany(r => r.EffectiveWarnings.Select(w => $"Row {r.DisplayIndex}: {w}"))
            .Concat(_rows.SelectMany(r => r.Cells.SelectMany(c => c.EffectiveWarnings.Select(w => $"Row {r.DisplayIndex} / {c.DisplayName}: {w}"))))
            .ToArray();
        return new BatchValidationSummary(_rows.Count, warningRows, errors, duplicates, messages);
    }

    public async ValueTask<TextBatchImportPlan> BuildImportPlanAsync(CancellationToken cancellationToken = default)
    {
        if (_host is null) throw new InvalidOperationException("Batch database integration is not available in this host.");
        var requests = new List<TextBatchImportRequest>(_rows.Count);
        foreach (BatchContentRow row in _rows)
        {
            TextRecordTargetDescriptor target = FindTarget(row);
            GeneratedTextBundle bundle = BundleFromCells(row);
            TextImportMode importMode = Mode == BatchContentMode.FillBlankExisting ? TextImportMode.FillIfBlank : TextImportMode.Replace;
            var selections = row.Cells.Select(c => new TextImportFieldSelection(c.SemanticField, Include: true, Mode: importMode, GeneratedField: c.SemanticField)).ToArray();
            requests.Add(new TextBatchImportRequest(target, bundle, selections, row.CreateRecord, row.RecordId, $"Row {row.DisplayIndex} — {row.TargetDisplayName}"));
        }
        return await _host.BuildBatchPlanAsync(requests, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ApplyImportAsync(TextBatchImportPlan plan, CancellationToken cancellationToken = default)
    {
        if (_host is null) throw new InvalidOperationException("Batch database integration is not available in this host.");
        await _host.ApplyBatchAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    public void Reset()
    {
        _rows.Clear();
        _snapshot = null;
        RecipeId = null;
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    public string ExportCsv() => BatchCsvCodec.Write(_rows);

    public void ImportCsv(string csv)
    {
        IReadOnlyList<IReadOnlyDictionary<string, string>> records = BatchCsvCodec.Read(csv);
        if (records.Count == 0) return;
        int count = Math.Min(records.Count, _rows.Count);
        for (int i = 0; i < count; i++)
        {
            BatchContentRow row = _rows[i];
            IReadOnlyDictionary<string, string> record = records[i];
            var cells = row.Cells.Select(cell => record.TryGetValue(cell.SemanticField, out string? value) || record.TryGetValue(cell.DisplayName, out value) ? cell with { Value = value ?? string.Empty } : cell).ToArray();
            _rows[i] = row with { Cells = cells };
        }
        ValidateDuplicates();
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async ValueTask<GeneratedTextBundle> GenerateForTargetAsync(TextRecordTargetDescriptor target, string recipeId, ulong seed, IReadOnlyList<string> references, int rowIndex, CancellationToken cancellationToken)
    {
        var context = new Dictionary<string, object?>(target.EffectiveContextValues, StringComparer.OrdinalIgnoreCase)
        {
            ["batch.index"] = rowIndex,
            ["batch.row"] = rowIndex + 1,
            ["batch.mode"] = Mode.ToString(),
            ["target.recordId"] = target.RecordId,
            ["target.recordDisplayName"] = target.DisplayName
        };
        return await _studio.GenerateBatchBundleAsync(recipeId, seed, context, references, cancellationToken).ConfigureAwait(false);
    }

    private BatchContentRow BuildRow(TextRecordTargetDescriptor target, GeneratedTextBundle bundle, ulong seed, bool create, int displayIndex)
    {
        var cells = new List<BatchCellState>();
        foreach (TextTargetDescriptor field in target.Fields)
        {
            string generated = TextImportPlanner.ResolveGeneratedText(bundle, field.SemanticField, field, target.SelectedField);
            if (string.IsNullOrEmpty(generated) && Mode != BatchContentMode.AppendNew) generated = field.CurrentValue;
            cells.Add(new BatchCellState(field.SemanticField, field.DisplayName, generated, GeneratedField: field.SemanticField));
        }
        int? id = int.TryParse(target.RecordId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
        return new BatchContentRow(Guid.NewGuid(), displayIndex, target.RecordId, target.DisplayName, id, create, seed, bundle, cells, Status: "Ready");
    }

    private TextRecordTargetDescriptor FindTarget(BatchContentRow row)
    {
        if (_snapshot is null) throw new InvalidOperationException("No batch target snapshot is loaded.");
        if (row.CreateRecord && row.RecordId is int id) return CreateTargetForId(_snapshot.Template with { SelectedSemanticField = PrimarySemanticField }, id);
        TextRecordTargetDescriptor target = _snapshot.Records.FirstOrDefault(r => r.RecordId.Equals(row.TargetKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Batch target '{row.TargetKey}' no longer exists.");
        return target with { SelectedSemanticField = PrimarySemanticField };
    }

    private static TextRecordTargetDescriptor CreateTargetForId(TextRecordTargetDescriptor template, int id) => template with
    {
        RecordId = id.ToString(CultureInfo.InvariantCulture),
        DisplayName = $"New record #{id}",
        Fields = template.Fields.Select(f => f with { RecordId = id.ToString(CultureInfo.InvariantCulture), CurrentValue = string.Empty, RecordDisplayName = $"New record #{id}" }).ToArray(),
        CanCreateRecord = true,
        SuggestedCreateRecordId = id
    };

    private static bool TargetHasBlankText(TextRecordTargetDescriptor target, string semanticField) => string.IsNullOrWhiteSpace(target.Fields.FirstOrDefault(f => f.SemanticField.Equals(semanticField, StringComparison.OrdinalIgnoreCase))?.CurrentValue);

    private void ValidateDuplicates()
    {
        if (_snapshot is null) return;
        var batchGroups = _rows.Select(r => (Row: r, Value: Normalize(r.FindCell(PrimarySemanticField)?.Value)))
            .Where(x => x.Value.Length > 0).GroupBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var existing = _snapshot.EffectiveExistingPrimaryValues.Select(Normalize).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < _rows.Count; i++)
        {
            BatchContentRow row = _rows[i];
            string value = Normalize(row.FindCell(PrimarySemanticField)?.Value);
            var warnings = new List<string>();
            if (value.Length == 0) warnings.Add($"{PrimarySemanticField} is blank.");
            if (value.Length > 0 && batchGroups.TryGetValue(value, out int count) && count > 1) warnings.Add($"Batch duplicate {PrimarySemanticField}: '{row.FindCell(PrimarySemanticField)?.Value}'.");
            if (value.Length > 0 && existing.Contains(value))
            {
                TextRecordTargetDescriptor? target = _snapshot.Records.FirstOrDefault(r => r.RecordId.Equals(row.TargetKey, StringComparison.OrdinalIgnoreCase));
                string current = Normalize(target?.Fields.FirstOrDefault(f => f.SemanticField.Equals(PrimarySemanticField, StringComparison.OrdinalIgnoreCase))?.CurrentValue);
                if (row.CreateRecord || !value.Equals(current, StringComparison.OrdinalIgnoreCase)) warnings.Add($"Project collision: '{row.FindCell(PrimarySemanticField)?.Value}' already exists.");
            }
            if (PrimarySemanticField.Equals("name", StringComparison.OrdinalIgnoreCase) && value.Length >= 4)
            {
                string raw = row.FindCell(PrimarySemanticField)?.Value ?? string.Empty;
                string? nearBatch = _rows.Where(r => r.Id != row.Id).Select(r => r.FindCell(PrimarySemanticField)?.Value).FirstOrDefault(other => !string.IsNullOrWhiteSpace(other) && NameSimilarity.Composite(raw, other!) >= 0.92);
                if (nearBatch is not null && !Normalize(nearBatch).Equals(value, StringComparison.OrdinalIgnoreCase)) warnings.Add($"Near-duplicate batch name: '{nearBatch}'.");
                string? nearProject = _snapshot.EffectiveExistingPrimaryValues.FirstOrDefault(other => !string.IsNullOrWhiteSpace(other) && NameSimilarity.Composite(raw, other) >= 0.94);
                if (nearProject is not null && !Normalize(nearProject).Equals(value, StringComparison.OrdinalIgnoreCase)) warnings.Add($"Near project-name collision: '{nearProject}'.");
            }
            _rows[i] = row with { Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), Status = warnings.Count == 0 ? "Ready" : "Warning" };
        }
    }

    private GeneratedTextBundle BundleFromCells(BatchContentRow row)
    {
        string primary = row.FindCell(PrimarySemanticField)?.Value ?? row.Cells.FirstOrDefault()?.Value ?? string.Empty;
        return new GeneratedTextBundle
        {
            Kind = row.SourceBundle?.Kind ?? TextContentKind.Custom,
            PrimaryText = primary,
            Fields = row.Cells.Select(c => new GeneratedField(c.SemanticField, c.Value, c.SemanticField, c.SemanticField.Equals(PrimarySemanticField, StringComparison.OrdinalIgnoreCase))).ToArray(),
            Tags = row.SourceBundle?.Tags ?? new Dictionary<string, string>(),
            Features = row.SourceBundle?.Features ?? Playroma.Text.Core.Semantics.SemanticFeatureUnifier.Empty,
            Scores = row.SourceBundle?.Scores ?? [],
            Provenance = row.SourceBundle?.Provenance,
            Trace = row.SourceBundle?.Trace,
            Diagnostics = row.SourceBundle?.Diagnostics ?? []
        };
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }
}

internal static class BatchCsvCodec
{
    public static string Write(IReadOnlyList<BatchContentRow> rows)
    {
        if (rows.Count == 0) return string.Empty;
        string[] fields = rows.SelectMany(r => r.Cells.Select(c => c.SemanticField)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', new[] { "recordId", "target" }.Concat(fields).Select(Escape)));
        foreach (BatchContentRow row in rows)
        {
            var values = new List<string> { row.RecordId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, row.TargetDisplayName };
            values.AddRange(fields.Select(f => row.FindCell(f)?.Value ?? string.Empty));
            sb.AppendLine(string.Join(',', values.Select(Escape)));
        }
        return sb.ToString();
    }

    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Read(string csv)
    {
        List<string[]> rows = ParseRows(csv ?? string.Empty);
        if (rows.Count < 2) return [];
        string[] header = rows[0];
        var result = new List<IReadOnlyDictionary<string, string>>();
        foreach (string[] row in rows.Skip(1))
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++) dict[header[i]] = i < row.Length ? row[i] : string.Empty;
            result.Add(dict);
        }
        return result;
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? '"' + value.Replace("\"", "\"\"") + '"' : value;
    }

    private static List<string[]> ParseRows(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i <= text.Length; i++)
        {
            char c = i < text.Length ? text[i] : '\n';
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(field.ToString()); field.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n')
            {
                fields.Add(field.ToString()); field.Clear();
                if (fields.Any(x => x.Length > 0)) rows.Add(fields.ToArray());
                fields.Clear();
            }
            else field.Append(c);
        }
        return rows;
    }
}
