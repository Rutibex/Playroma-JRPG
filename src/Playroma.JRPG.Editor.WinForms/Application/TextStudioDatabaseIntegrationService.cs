using System.Globalization;
using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.Text.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Application;

/// <summary>
/// Host-side bridge between platform-neutral Text Studio import plans and Playroma's live MV
/// database documents/transaction history. No text project writes MV JSON directly.
/// </summary>
internal sealed class TextStudioDatabaseIntegrationService : ITextImportTransactionSink, ITextBatchHost
{
    private readonly MvProjectSession _session;

    public TextStudioDatabaseIntegrationService(MvProjectSession session) =>
        _session = session ?? throw new ArgumentNullException(nameof(session));

    public async ValueTask<TextRecordTargetDescriptor> BuildTargetAsync(
        MvDocumentKind kind,
        string relativePath,
        int recordId,
        string? fieldKey = null,
        CancellationToken cancellationToken = default)
    {
        if (recordId <= 0) throw new ArgumentOutOfRangeException(nameof(recordId));
        IDatabaseEditorDefinition definition = DatabaseEditorRegistry.CreateBuiltIn().Get(kind);
        await using var lease = await _session.Workspace.AcquireDocumentAsync(relativePath, cancellationToken).ConfigureAwait(false);
        var document = lease.Document as MutableJsonProjectDocument
            ?? throw new NotSupportedException($"{relativePath} is not a mutable JSON database document.");
        var record = new DatabaseRecordHandle(kind, relativePath, recordId);
        string recordName = ReadName(document, record);
        var specs = MvDatabaseTextTargetCatalog.ForKind(kind.ToString());
        var fields = new List<TextTargetDescriptor>();
        foreach (MvTextFieldSpec spec in specs)
        {
            DatabaseFieldDescriptor? schemaField = definition.RecordSchema.Fields.FirstOrDefault(f =>
                f.Key.Equals(spec.Key, StringComparison.OrdinalIgnoreCase));
            if (schemaField is null) continue;
            string current = Convert.ToString(DatabaseValueAccessor.Read(document, record, schemaField), CultureInfo.InvariantCulture) ?? string.Empty;
            fields.Add(new TextTargetDescriptor(
                "database." + kind.ToString().ToLowerInvariant(),
                relativePath,
                recordId.ToString(CultureInfo.InvariantCulture),
                spec.JsonPath,
                spec.SemanticField,
                spec.DisplayName,
                current,
                spec.Multiline,
                spec.IsNote,
                spec.SuggestedMaximumLength,
                SupportedModesFor(spec),
                spec.Key,
                string.IsNullOrWhiteSpace(recordName) ? $"{definition.SingularName} {recordId}" : recordName,
                spec.RecommendedRecipeIds));
        }
        if (fields.Count == 0) throw new InvalidOperationException($"{definition.DisplayName} does not expose any Text Studio-compatible fields.");

        TextTargetDescriptor? selected = fields.FirstOrDefault(f => string.Equals(f.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase)) ?? fields[0];
        var context = BuildRecordContext(document, definition, record, fields);
        int? nextId = FindCreateRecordId(document);
        return new TextRecordTargetDescriptor(
            "database." + kind.ToString().ToLowerInvariant(),
            relativePath,
            recordId.ToString(CultureInfo.InvariantCulture),
            $"{definition.SingularName} {recordId:D4}" + (string.IsNullOrWhiteSpace(recordName) ? string.Empty : $" — {recordName}"),
            fields,
            selected.SemanticField,
            context,
            CanCreateRecord: nextId.HasValue,
            SuggestedCreateRecordId: nextId);
    }

    public async ValueTask<TextRecordTargetDescriptor> BuildMapTargetAsync(
        int mapId,
        string mapRelativePath,
        string? fieldKey = null,
        CancellationToken cancellationToken = default)
    {
        if (mapId <= 0) throw new ArgumentOutOfRangeException(nameof(mapId));
        ArgumentException.ThrowIfNullOrWhiteSpace(mapRelativePath);
        string mapInfosPath = _session.Workspace.Snapshot.Database.Find(MvDocumentKind.MapInfos)?.RelativePath
            ?? Path.Combine("data", "MapInfos.json").Replace('\\', '/');

        await using var mapLease = await _session.Workspace.AcquireDocumentAsync(mapRelativePath, cancellationToken).ConfigureAwait(false);
        await using var infosLease = await _session.Workspace.AcquireDocumentAsync(mapInfosPath, cancellationToken).ConfigureAwait(false);
        var map = mapLease.Document as MutableJsonProjectDocument
            ?? throw new NotSupportedException($"{mapRelativePath} is not a mutable JSON map document.");
        var infos = infosLease.Document as MutableJsonProjectDocument
            ?? throw new NotSupportedException($"{mapInfosPath} is not a mutable MapInfos document.");

        string editorName = infos.GetString(JsonPath.Root.Append(mapId).Append("name")) ?? $"Map {mapId:D3}";
        string displayName = map.GetString(JsonPath.Root.Append("displayName")) ?? string.Empty;
        string note = map.GetString(JsonPath.Root.Append("note")) ?? string.Empty;
        string recordId = $"map:{mapId}"; // non-numeric: import sink does not prepend an array record ID.
        var fields = new List<TextTargetDescriptor>
        {
            new("map", mapInfosPath, recordId, $"/{mapId}/name", "name", "Editor Name", editorName, false, false, 100,
                SupportedModesFor(MvMapTextTargets.Specs[0]), "editorName", editorName, MvMapTextTargets.Specs[0].RecommendedRecipeIds),
            new("map", mapRelativePath, recordId, "/displayName", "displayName", "Display Name", displayName, false, false, 100,
                SupportedModesFor(MvMapTextTargets.Specs[1]), "displayName", editorName, MvMapTextTargets.Specs[1].RecommendedRecipeIds),
            new("map", mapRelativePath, recordId, "/note", "note", "Map Note", note, true, true, null,
                SupportedModesFor(MvMapTextTargets.Specs[2]), "note", editorName, MvMapTextTargets.Specs[2].RecommendedRecipeIds)
        };
        TextTargetDescriptor selected = fields.FirstOrDefault(x => x.FieldKey?.Equals(fieldKey, StringComparison.OrdinalIgnoreCase) == true)
            ?? fields.FirstOrDefault(x => x.SemanticField.Equals(fieldKey, StringComparison.OrdinalIgnoreCase))
            ?? fields[1];
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["map.id"] = mapId,
            ["map.name"] = editorName,
            ["map.displayName"] = displayName,
            ["map.note"] = note,
            ["target.kind"] = "Map",
            ["target.recordId"] = mapId,
            ["target.name"] = editorName,
            ["target.displayName"] = displayName,
            ["target.note"] = note
        };
        return new TextRecordTargetDescriptor("map", mapRelativePath, recordId,
            $"Map {mapId:D3} — {editorName}", fields, selected.SemanticField, context);
    }

    public async ValueTask<TextImportPlan> BuildPlanAsync(
        TextRecordTargetDescriptor target,
        Playroma.Text.Core.Generation.GeneratedTextBundle bundle,
        IReadOnlyList<TextImportFieldSelection> selections,
        bool createRecord,
        int? createRecordId,
        CancellationToken cancellationToken = default)
    {
        TextImportRecordCreation? creation = null;
        TextRecordTargetDescriptor effectiveTarget = target;
        if (createRecord)
        {
            MvDocumentKind kind = ParseKind(target.AdapterKind);
            IDatabaseEditorDefinition definition = DatabaseEditorRegistry.CreateBuiltIn().Get(kind);
            int id = createRecordId ?? target.SuggestedCreateRecordId ?? throw new InvalidOperationException("No free record slot is available.");
            string raw = definition.CreateDefaultRecordJson(id);
            creation = new TextImportRecordCreation(target.DataFile, id, raw, $"{definition.SingularName} {id}");
            // New records begin with default/empty text values, not the current source record's values.
            effectiveTarget = target with
            {
                RecordId = id.ToString(CultureInfo.InvariantCulture),
                DisplayName = $"{definition.SingularName} {id:D4}",
                Fields = target.Fields.Select(field => field with
                {
                    RecordId = id.ToString(CultureInfo.InvariantCulture),
                    CurrentValue = string.Empty,
                    RecordDisplayName = $"{definition.SingularName} {id}"
                }).ToArray()
            };
        }
        TextImportPlan plan = TextImportPlanner.Build(effectiveTarget, bundle, selections, creation);
        var issues = plan.Issues.ToList();

        if (creation is not null)
            await ValidateCreationSlotAsync(target.DataFile, creation.RecordId, issues, cancellationToken).ConfigureAwait(false);

        if (target.AdapterKind.Equals("system.terms", StringComparison.OrdinalIgnoreCase))
        {
            foreach (TextImportChange change in plan.Changes)
            {
                IReadOnlyList<string> missing = MvTermPlaceholderValidator.Missing(change.OldValue, change.NewValue);
                if (missing.Count > 0)
                    issues.Add(new TextImportValidationIssue(
                        "PTXTMV0201",
                        $"{change.DisplayName ?? change.SemanticField} removes RPG Maker placeholder(s): {string.Join(", ", missing)}.",
                        IsError: true,
                        change.SemanticField));
            }
        }

        return plan with { Issues = issues };
    }

    public async ValueTask ApplyAsync(TextImportPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanApply) throw new InvalidOperationException("The Text Studio import plan contains blocking validation issues or no changes.");
        await using ProjectTransaction transaction = _session.BeginTransaction(plan.Description);
        if (plan.Creation is { } creation)
        {
            transaction.Add(new InsertDatabaseRecordCommand(
                creation.DataFile,
                creation.RecordId,
                creation.RawJson,
                $"Create {creation.DisplayName}"));
        }
        foreach (TextImportChange change in plan.Changes)
        {
            JsonPath relative = new(change.JsonPath);
            JsonPath absolute = JsonPath.Root;
            if (int.TryParse(change.RecordId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int recordId))
                absolute = absolute.Append(recordId);
            foreach (string segment in relative.Segments())
                absolute = int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int index) ? absolute.Append(index) : absolute.Append(segment);
            transaction.Add(new SetJsonValueCommand(
                change.DataFile,
                absolute,
                JsonSerializer.Serialize(change.NewValue),
                $"Set generated {change.DisplayName ?? change.SemanticField}"));
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TextBatchTargetSnapshot> BuildSnapshotAsync(
        string adapterKind,
        int requestedCreateSlots = 0,
        CancellationToken cancellationToken = default)
    {
        if (adapterKind.Equals("map", StringComparison.OrdinalIgnoreCase) || adapterKind.Equals("maps", StringComparison.OrdinalIgnoreCase))
        {
            var mapRecords = new List<TextRecordTargetDescriptor>();
            foreach (MvMapDescriptor map in _session.Workspace.Snapshot.Maps.Where(m => m.FileExists).OrderBy(m => m.Order).ThenBy(m => m.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                mapRecords.Add(await BuildMapTargetAsync(map.Id, map.RelativePath, "editorName", cancellationToken).ConfigureAwait(false));
            }
            TextRecordTargetDescriptor mapTemplate = mapRecords.FirstOrDefault() ?? new TextRecordTargetDescriptor(
                "map", "data/Map001.json", "map:1", "Map", MvMapTextTargets.Specs.Select(spec => new TextTargetDescriptor(
                    "map", spec.Key == "editorName" ? "data/MapInfos.json" : "data/Map001.json", "map:1", spec.JsonPath,
                    spec.SemanticField, spec.DisplayName, string.Empty, spec.Multiline, spec.IsNote, spec.SuggestedMaximumLength,
                    SupportedModesFor(spec), spec.Key, "Map", spec.RecommendedRecipeIds)).ToArray(), "name");
            string[] mapExisting = mapRecords.Select(r => r.Fields.FirstOrDefault(f => f.SemanticField.Equals("name", StringComparison.OrdinalIgnoreCase))?.CurrentValue ?? string.Empty)
                .Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
            string mapInfos = _session.Workspace.Snapshot.Database.Find(MvDocumentKind.MapInfos)?.RelativePath ?? "data/MapInfos.json";
            return new TextBatchTargetSnapshot("map", mapInfos, "Maps", mapRecords, mapTemplate, [], mapExisting);
        }

        MvDocumentKind kind = ParseKind(adapterKind);
        if (kind == MvDocumentKind.Unknown || !MvDatabaseTextTargetCatalog.SupportsKind(kind.ToString()))
            throw new NotSupportedException($"{adapterKind} is not a batch-capable RPG Maker MV text database.");
        MvDatabaseFileSummary summary = _session.Workspace.Snapshot.Database.Find(kind)
            ?? throw new InvalidOperationException($"The open project does not contain {kind}.");
        IDatabaseEditorDefinition definition = DatabaseEditorRegistry.CreateBuiltIn().Get(kind);

        await using var lease = await _session.Workspace.AcquireDocumentAsync(summary.RelativePath, cancellationToken).ConfigureAwait(false);
        var document = lease.Document as MutableJsonProjectDocument
            ?? throw new NotSupportedException($"{summary.RelativePath} is not a mutable JSON database document.");
        if (document.Syntax.Root is not JsonArraySyntax array)
            throw new NotSupportedException($"{summary.RelativePath} is not an RPG Maker MV record array.");

        var records = new List<TextRecordTargetDescriptor>();
        for (int id = 1; id < array.Elements.Count; id++)
        {
            JsonPath path = JsonPath.Root.Append(id);
            if (!document.ContainsPath(path) || string.Equals(document.GetRawJson(path), "null", StringComparison.OrdinalIgnoreCase)) continue;
            records.Add(BuildRecordTarget(document, definition, kind, summary.RelativePath, id));
        }

        TextRecordTargetDescriptor template = BuildEmptyTarget(definition, kind, summary.RelativePath, Math.Max(1, array.Elements.Count));
        int count = Math.Clamp(requestedCreateSlots, 0, 9999);
        var createIds = new List<int>(count);
        for (int id = 1; id < array.Elements.Count && createIds.Count < count; id++)
        {
            JsonPath path = JsonPath.Root.Append(id);
            if (!document.ContainsPath(path) || string.Equals(document.GetRawJson(path), "null", StringComparison.OrdinalIgnoreCase)) createIds.Add(id);
        }
        int append = array.Elements.Count;
        while (createIds.Count < count) createIds.Add(append++);

        string primarySemantic = MvDatabaseTextTargetCatalog.ForKind(kind.ToString()).FirstOrDefault(x => x.SemanticField.Equals("name", StringComparison.OrdinalIgnoreCase))?.SemanticField
            ?? template.Fields.First().SemanticField;
        string[] existing = records.Select(r => r.Fields.FirstOrDefault(f => f.SemanticField.Equals(primarySemantic, StringComparison.OrdinalIgnoreCase))?.CurrentValue ?? string.Empty)
            .Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        return new TextBatchTargetSnapshot("database." + kind.ToString().ToLowerInvariant(), summary.RelativePath, definition.DisplayName, records, template, createIds, existing);
    }

    public async ValueTask<TextBatchImportPlan> BuildBatchPlanAsync(
        IReadOnlyList<TextBatchImportRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0) return new TextBatchImportPlan("Import generated batch content", [], [new("PTXTBATCH0100", "The batch contains no rows.", true)]);

        var rowPlans = new List<TextBatchImportRowPlan>(requests.Count);
        foreach (TextBatchImportRequest request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TextImportRecordCreation? creation = null;
            TextRecordTargetDescriptor effectiveTarget = request.Target;
            if (request.CreateRecord)
            {
                MvDocumentKind kind = ParseKind(request.Target.AdapterKind);
                IDatabaseEditorDefinition definition = DatabaseEditorRegistry.CreateBuiltIn().Get(kind);
                int id = request.CreateRecordId ?? throw new InvalidOperationException("A batch create row has no target record ID.");
                creation = new TextImportRecordCreation(request.Target.DataFile, id, definition.CreateDefaultRecordJson(id), $"{definition.SingularName} {id}");
                effectiveTarget = request.Target with
                {
                    RecordId = id.ToString(CultureInfo.InvariantCulture),
                    DisplayName = $"{definition.SingularName} {id:D4}",
                    Fields = request.Target.Fields.Select(field => field with
                    {
                        RecordId = id.ToString(CultureInfo.InvariantCulture),
                        CurrentValue = string.Empty,
                        RecordDisplayName = $"{definition.SingularName} {id}"
                    }).ToArray()
                };
            }
            TextImportPlan rowPlan = TextImportPlanner.Build(effectiveTarget, request.Bundle, request.Selections, creation);
            var rowIssues = rowPlan.Issues.ToList();
            if (effectiveTarget.AdapterKind.Equals("system.terms", StringComparison.OrdinalIgnoreCase))
            {
                foreach (TextImportChange change in rowPlan.Changes)
                {
                    IReadOnlyList<string> missing = MvTermPlaceholderValidator.Missing(change.OldValue, change.NewValue);
                    if (missing.Count > 0) rowIssues.Add(new("PTXTMV0201", $"{change.DisplayName ?? change.SemanticField} removes RPG Maker placeholder(s): {string.Join(", ", missing)}.", true, change.SemanticField));
                }
            }
            rowPlans.Add(new TextBatchImportRowPlan(request.RowLabel ?? effectiveTarget.DisplayName, rowPlan with { Issues = rowIssues }));
        }

        TextBatchImportPlan combined = TextBatchImportPlanner.Combine($"Import {requests.Count:N0} generated Text Studio rows", rowPlans);
        var issues = combined.Issues.ToList();
        await ValidateBatchCreationsAsync(combined.Creations, issues, cancellationToken).ConfigureAwait(false);
        return combined with { Issues = issues };
    }

    public async ValueTask ApplyBatchAsync(TextBatchImportPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanApply) throw new InvalidOperationException("The Text Studio batch import contains blocking validation issues or no changes.");
        await using ProjectTransaction transaction = _session.BeginTransaction(plan.Description);
        foreach (TextImportRecordCreation creation in plan.Creations.OrderBy(c => c.DataFile, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.RecordId))
        {
            transaction.Add(new InsertDatabaseRecordCommand(creation.DataFile, creation.RecordId, creation.RawJson, $"Create {creation.DisplayName}"));
        }
        foreach (TextImportChange change in plan.Changes)
        {
            JsonPath relative = new(change.JsonPath);
            JsonPath absolute = JsonPath.Root;
            if (int.TryParse(change.RecordId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int recordId)) absolute = absolute.Append(recordId);
            foreach (string segment in relative.Segments())
                absolute = int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int index) ? absolute.Append(index) : absolute.Append(segment);
            transaction.Add(new SetJsonValueCommand(change.DataFile, absolute, JsonSerializer.Serialize(change.NewValue), $"Set generated {change.DisplayName ?? change.SemanticField}"));
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ValidateBatchCreationsAsync(
        IReadOnlyList<TextImportRecordCreation> creations,
        ICollection<TextImportValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        foreach (IGrouping<string, TextImportRecordCreation> group in creations.GroupBy(c => c.DataFile, StringComparer.OrdinalIgnoreCase))
        {
            await using var lease = await _session.Workspace.AcquireDocumentAsync(group.Key, cancellationToken).ConfigureAwait(false);
            if (lease.Document is not MutableJsonProjectDocument document || document.Syntax.Root is not JsonArraySyntax array)
            {
                issues.Add(new("PTXTBATCH0101", $"{group.Key} does not support batch record creation.", true));
                continue;
            }
            var used = new HashSet<int>();
            int nextAppend = array.Elements.Count;
            foreach (TextImportRecordCreation creation in group.OrderBy(c => c.RecordId))
            {
                int id = creation.RecordId;
                if (id <= 0 || !used.Add(id)) { issues.Add(new("PTXTBATCH0102", $"Record #{id} is an invalid or duplicate batch creation ID.", true)); continue; }
                if (id < array.Elements.Count)
                {
                    JsonPath path = JsonPath.Root.Append(id);
                    if (document.ContainsPath(path) && !string.Equals(document.GetRawJson(path), "null", StringComparison.OrdinalIgnoreCase))
                        issues.Add(new("PTXTBATCH0103", $"Record #{id} in {group.Key} is already populated.", true));
                }
                else if (id != nextAppend)
                {
                    issues.Add(new("PTXTBATCH0104", $"Record #{id} would create an unsafe ID gap in {group.Key}; expected append ID #{nextAppend}.", true));
                }
                else nextAppend++;
            }
        }
    }

    private static TextRecordTargetDescriptor BuildRecordTarget(
        MutableJsonProjectDocument document,
        IDatabaseEditorDefinition definition,
        MvDocumentKind kind,
        string relativePath,
        int recordId)
    {
        var record = new DatabaseRecordHandle(kind, relativePath, recordId);
        string recordName = ReadName(document, record);
        var fields = new List<TextTargetDescriptor>();
        foreach (MvTextFieldSpec spec in MvDatabaseTextTargetCatalog.ForKind(kind.ToString()))
        {
            DatabaseFieldDescriptor? schemaField = definition.RecordSchema.Fields.FirstOrDefault(f => f.Key.Equals(spec.Key, StringComparison.OrdinalIgnoreCase));
            if (schemaField is null) continue;
            string current = Convert.ToString(DatabaseValueAccessor.Read(document, record, schemaField), CultureInfo.InvariantCulture) ?? string.Empty;
            fields.Add(new TextTargetDescriptor("database." + kind.ToString().ToLowerInvariant(), relativePath, recordId.ToString(CultureInfo.InvariantCulture), spec.JsonPath, spec.SemanticField, spec.DisplayName, current, spec.Multiline, spec.IsNote, spec.SuggestedMaximumLength, SupportedModesFor(spec), spec.Key, string.IsNullOrWhiteSpace(recordName) ? $"{definition.SingularName} {recordId}" : recordName, spec.RecommendedRecipeIds));
        }
        return new TextRecordTargetDescriptor("database." + kind.ToString().ToLowerInvariant(), relativePath, recordId.ToString(CultureInfo.InvariantCulture), $"{definition.SingularName} {recordId:D4}" + (string.IsNullOrWhiteSpace(recordName) ? string.Empty : $" — {recordName}"), fields, fields.FirstOrDefault()?.SemanticField, BuildRecordContext(document, definition, record, fields));
    }

    private static TextRecordTargetDescriptor BuildEmptyTarget(IDatabaseEditorDefinition definition, MvDocumentKind kind, string relativePath, int suggestedId)
    {
        var fields = MvDatabaseTextTargetCatalog.ForKind(kind.ToString()).Select(spec => new TextTargetDescriptor(
            "database." + kind.ToString().ToLowerInvariant(), relativePath, suggestedId.ToString(CultureInfo.InvariantCulture), spec.JsonPath,
            spec.SemanticField, spec.DisplayName, string.Empty, spec.Multiline, spec.IsNote, spec.SuggestedMaximumLength,
            SupportedModesFor(spec), spec.Key, $"New {definition.SingularName}", spec.RecommendedRecipeIds)).ToArray();
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["database.kind"] = kind.ToString(), ["database.recordId"] = suggestedId, ["database.recordName"] = string.Empty,
            ["target.recordId"] = suggestedId, ["target.kind"] = kind.ToString()
        };
        foreach (TextTargetDescriptor field in fields) { context["target." + field.SemanticField] = string.Empty; context["record." + (field.FieldKey ?? field.SemanticField)] = string.Empty; }
        return new TextRecordTargetDescriptor("database." + kind.ToString().ToLowerInvariant(), relativePath, suggestedId.ToString(CultureInfo.InvariantCulture), $"New {definition.SingularName}", fields, fields.FirstOrDefault()?.SemanticField, context, true, suggestedId);
    }

    private static IReadOnlySet<TextImportMode> SupportedModesFor(MvTextFieldSpec spec)
    {
        var result = new HashSet<TextImportMode>
        {
            TextImportMode.Replace,
            TextImportMode.Append,
            TextImportMode.Prepend,
            TextImportMode.FillIfBlank,
            TextImportMode.CreateRecord
        };
        if (spec.Multiline || spec.IsNote)
        {
            result.Add(TextImportMode.InsertAtCursor);
            result.Add(TextImportMode.ReplaceSelection);
        }
        return result;
    }

    private static IReadOnlyDictionary<string, object?> BuildRecordContext(
        MutableJsonProjectDocument document,
        IDatabaseEditorDefinition definition,
        DatabaseRecordHandle record,
        IReadOnlyList<TextTargetDescriptor> fields)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["database.kind"] = definition.Kind.ToString(),
            ["database.recordId"] = record.Id,
            ["database.recordName"] = ReadName(document, record),
            ["target.recordId"] = record.Id,
            ["target.kind"] = definition.Kind.ToString()
        };
        foreach (TextTargetDescriptor field in fields)
        {
            values["target." + field.SemanticField] = field.CurrentValue;
            values["record." + (field.FieldKey ?? field.SemanticField)] = field.CurrentValue;
        }
        return values;
    }

    private static string ReadName(MutableJsonProjectDocument document, DatabaseRecordHandle record) =>
        document.GetString(record.Path.Append("name")) ?? string.Empty;

    private static int? FindCreateRecordId(MutableJsonProjectDocument document)
    {
        if (document.Syntax.Root is not JsonArraySyntax array) return null;
        for (int id = 1; id < array.Elements.Count; id++)
        {
            JsonPath path = JsonPath.Root.Append(id);
            if (!document.ContainsPath(path) || string.Equals(document.GetRawJson(path), "null", StringComparison.OrdinalIgnoreCase)) return id;
        }
        // Appending one slot is safe; InsertDatabaseRecordCommand handles array expansion semantics used by the database editor.
        return array.Elements.Count;
    }

    private async ValueTask ValidateCreationSlotAsync(
        string relativePath,
        int recordId,
        ICollection<TextImportValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        if (recordId <= 0)
        {
            issues.Add(new TextImportValidationIssue("PTXTMV0202", "New RPG Maker database records require a positive ID.", true));
            return;
        }

        await using var lease = await _session.Workspace.AcquireDocumentAsync(relativePath, cancellationToken).ConfigureAwait(false);
        if (lease.Document is not MutableJsonProjectDocument document || document.Syntax.Root is not JsonArraySyntax array)
        {
            issues.Add(new TextImportValidationIssue("PTXTMV0203", "The selected database does not support record creation.", true));
            return;
        }

        if (recordId > array.Elements.Count)
        {
            issues.Add(new TextImportValidationIssue(
                "PTXTMV0204",
                $"Record #{recordId} would create an unsafe ID gap. Choose an existing null slot or the next ID ({array.Elements.Count}).",
                true));
            return;
        }

        if (recordId < array.Elements.Count)
        {
            JsonPath path = JsonPath.Root.Append(recordId);
            if (document.ContainsPath(path) && !string.Equals(document.GetRawJson(path), "null", StringComparison.OrdinalIgnoreCase))
                issues.Add(new TextImportValidationIssue("PTXTMV0205", $"Record #{recordId} is already populated and will not be overwritten by Create New Record.", true));
        }
    }

    private static MvDocumentKind ParseKind(string adapterKind)
    {
        string token = adapterKind.StartsWith("database.", StringComparison.OrdinalIgnoreCase)
            ? adapterKind["database.".Length..]
            : adapterKind;
        return Enum.TryParse(token, ignoreCase: true, out MvDocumentKind kind) ? kind : MvDocumentKind.Unknown;
    }
}
