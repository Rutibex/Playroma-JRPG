using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.FormulaSandbox;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Validation;

public sealed class Phase9BDatabaseValidator : IProjectValidator
{
    public string Name => "Skills, items, equipment, and states";

    public async ValueTask<IReadOnlyList<ProjectValidationDiagnostic>> ValidateAsync(
        ProjectValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        List<ProjectValidationDiagnostic> diagnostics = [];
        foreach (ProjectDocumentHandle handle in context.Workspace.Documents.Where(item => item.Kind is
                     MvDocumentKind.Skills or MvDocumentKind.Items or MvDocumentKind.Weapons or MvDocumentKind.Armors or MvDocumentKind.States))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IMutableProjectDocument mutable = await context.Workspace.OpenDocumentAsync(handle.RelativePath, cancellationToken).ConfigureAwait(false);
            ReadOnlyMemory<byte> bytes = await mutable.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument json = JsonDocument.Parse(Utf8JsonInput.ToBomlessArray(bytes.Span));
            switch (handle.Kind)
            {
                case MvDocumentKind.Skills:
                case MvDocumentKind.Items:
                    ValidateUsableItems(json.RootElement, handle.RelativePath, handle.Kind, diagnostics);
                    break;
                case MvDocumentKind.Weapons:
                case MvDocumentKind.Armors:
                    ValidateEquipment(json.RootElement, handle.RelativePath, handle.Kind, diagnostics);
                    break;
                case MvDocumentKind.States:
                    ValidateStates(json.RootElement, handle.RelativePath, diagnostics);
                    break;
            }
        }
        return diagnostics;
    }

    private static void ValidateUsableItems(
        JsonElement root,
        string relativePath,
        MvDocumentKind kind,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Array) return;
        foreach ((int index, JsonElement record) in root.EnumerateIndexedArray())
        {
            if (index == 0 || record.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            if (record.ValueKind != JsonValueKind.Object)
            {
                AddError(DiagnosticCodes.Phase9BInvalidUsableItem, $"{kind} record {index} is not an object.", relativePath, $"/{index}", index);
                continue;
            }

            int id = ReadInt(record, "id", index);
            int scope = ReadInt(record, "scope", 0);
            int occasion = ReadInt(record, "occasion", 0);
            int hitType = ReadInt(record, "hitType", 0);
            int successRate = ReadInt(record, "successRate", 100);
            int repeats = ReadInt(record, "repeats", 1);
            if (scope is < 0 or > 11 || occasion is < 0 or > 3 || hitType is < 0 or > 2 || successRate is < 0 or > 100 || repeats < 1)
            {
                AddError(DiagnosticCodes.Phase9BInvalidUsableItem, $"{kind} {id} has invalid invocation settings.", relativePath, $"/{index}", id);
            }

            if (!record.TryGetProperty("damage", out JsonElement damage) || damage.ValueKind != JsonValueKind.Object)
            {
                AddError(DiagnosticCodes.Phase9BInvalidDamage, $"{kind} {id} has no valid damage object.", relativePath, $"/{index}/damage", id);
            }
            else
            {
                int damageType = ReadInt(damage, "type", 0);
                int variance = ReadInt(damage, "variance", 0);
                string formula = ReadString(damage, "formula");
                if (damageType is < 0 or > 6 || variance is < 0 or > 100 || string.IsNullOrWhiteSpace(formula))
                {
                    AddError(DiagnosticCodes.Phase9BInvalidDamage, $"{kind} {id} has invalid damage settings.", relativePath, $"/{index}/damage", id);
                }
                else if (damageType != 0)
                {
                    FormulaSandboxResult formulaResult = FormulaSandboxEvaluator.Evaluate(new FormulaSandboxRequest(
                        formula,
                        SampleBattler("a"),
                        SampleBattler("b"),
                        new FormulaItemSnapshot(id, ReadString(record, "name"), damageType, ReadInt(damage, "elementId", 0), variance, ReadBool(damage, "critical")),
                        new Dictionary<int, double> { [1] = 1 },
                        SampleCount: 1));
                    if (!formulaResult.Success)
                    {
                        diagnostics.Add(new ProjectValidationDiagnostic(
                            ProjectDiagnostic.Warning(
                                DiagnosticCodes.Phase9BFormulaApproximationWarning,
                                $"{kind} {id} formula could not be evaluated by the MV Core Approximation sandbox: {formulaResult.Error}",
                                new ProjectLocation(relativePath, $"/{index}/damage/formula", RecordId: id)),
                            DocumentationKey: "phase9b/formula-sandbox"));
                    }
                }
            }

            if (!record.TryGetProperty("effects", out JsonElement effects) || effects.ValueKind != JsonValueKind.Array)
            {
                AddError(DiagnosticCodes.Phase9BInvalidEffect, $"{kind} {id} has no effects array.", relativePath, $"/{index}/effects", id);
            }
            else
            {
                int effectIndex = 0;
                foreach (JsonElement effect in effects.EnumerateArray())
                {
                    if (effect.ValueKind != JsonValueKind.Object
                        || !effect.TryGetProperty("code", out JsonElement code) || !code.TryGetInt32(out _)
                        || !effect.TryGetProperty("dataId", out JsonElement dataId) || !dataId.TryGetInt32(out _)
                        || !effect.TryGetProperty("value1", out JsonElement value1) || !value1.TryGetDouble(out _)
                        || !effect.TryGetProperty("value2", out JsonElement value2) || !value2.TryGetDouble(out _))
                    {
                        AddError(DiagnosticCodes.Phase9BInvalidEffect, $"{kind} {id} effect {effectIndex + 1} is malformed.", relativePath, $"/{index}/effects/{effectIndex}", id);
                    }
                    effectIndex++;
                }
            }
        }
        return;

        void AddError(string code, string message, string path, string jsonPath, int recordId) => diagnostics.Add(new ProjectValidationDiagnostic(
            ProjectDiagnostic.Error(code, message, new ProjectLocation(path, jsonPath, RecordId: recordId)),
            DocumentationKey: "phase9b/usable-items"));
    }

    private static void ValidateEquipment(
        JsonElement root,
        string relativePath,
        MvDocumentKind kind,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Array) return;
        foreach ((int index, JsonElement record) in root.EnumerateIndexedArray())
        {
            if (index == 0 || record.ValueKind != JsonValueKind.Object) continue;
            int id = ReadInt(record, "id", index);
            bool validParams = record.TryGetProperty("params", out JsonElement parameters)
                && parameters.ValueKind == JsonValueKind.Array
                && parameters.GetArrayLength() == 8
                && parameters.EnumerateArray().All(value => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _));
            if (!validParams)
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Error(
                        DiagnosticCodes.Phase9BInvalidEquipmentParameters,
                        $"{kind} {id} must contain exactly eight integer parameters.",
                        new ProjectLocation(relativePath, $"/{index}/params", RecordId: id)),
                    DocumentationKey: "phase9b/equipment-parameters"));
            }
            int etypeId = ReadInt(record, "etypeId", 0);
            int typeId = ReadInt(record, kind == MvDocumentKind.Weapons ? "wtypeId" : "atypeId", 0);
            if (etypeId <= 0 || typeId <= 0)
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Warning(
                        DiagnosticCodes.Phase9BInvalidEquipmentType,
                        $"{kind} {id} has an invalid equipment or item type ID.",
                        new ProjectLocation(relativePath, $"/{index}", RecordId: id)),
                    DocumentationKey: "phase9b/equipment-types"));
            }
        }
    }

    private static void ValidateStates(
        JsonElement root,
        string relativePath,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Array) return;
        HashSet<string> standard = new(StringComparer.Ordinal)
        {
            "id", "autoRemovalTiming", "chanceByDamage", "iconIndex", "maxTurns", "message1", "message2", "message3", "message4", "minTurns", "motion", "name", "note", "overlay", "priority", "removeAtBattleEnd", "removeByDamage", "removeByRestriction", "removeByWalking", "restriction", "stepsToRemove", "traits"
        };
        foreach ((int index, JsonElement record) in root.EnumerateIndexedArray())
        {
            if (index == 0 || record.ValueKind != JsonValueKind.Object) continue;
            int id = ReadInt(record, "id", index);
            int minTurns = ReadInt(record, "minTurns", 1);
            int maxTurns = ReadInt(record, "maxTurns", 1);
            int timing = ReadInt(record, "autoRemovalTiming", 0);
            int chance = ReadInt(record, "chanceByDamage", 100);
            if (minTurns < 0 || maxTurns < 0 || minTurns > maxTurns || timing is < 0 or > 2 || chance is < 0 or > 100)
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Error(
                        DiagnosticCodes.Phase9BInvalidStateRemoval,
                        $"State {id} has invalid removal conditions.",
                        new ProjectLocation(relativePath, $"/{index}", RecordId: id)),
                    DocumentationKey: "phase9b/state-removal"));
            }
            int restriction = ReadInt(record, "restriction", 0);
            int priority = ReadInt(record, "priority", 50);
            int motion = ReadInt(record, "motion", 0);
            int overlay = ReadInt(record, "overlay", 0);
            if (restriction is < 0 or > 4 || priority is < 0 or > 100 || motion is < 0 or > 3 || overlay is < 0 or > 10)
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Warning(
                        DiagnosticCodes.Phase9BInvalidStateVisual,
                        $"State {id} has a nonstandard restriction, priority, motion, or overlay value.",
                        new ProjectLocation(relativePath, $"/{index}", RecordId: id)),
                    DocumentationKey: "phase9b/state-visuals"));
            }
            foreach (JsonProperty property in record.EnumerateObject())
            {
                if (standard.Contains(property.Name)) continue;
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Information(
                        DiagnosticCodes.Phase9BPreservedExtensionField,
                        $"State {id} contains preserved extension field '{property.Name}'. Playroma will not remove or normalize it.",
                        new ProjectLocation(relativePath, $"/{index}/{property.Name}", RecordId: id)),
                    DocumentationKey: "phase9b/extension-fields"));
            }
        }
    }

    private static FormulaBattlerSnapshot SampleBattler(string name) => new(name, 10, 500, 500, 100, 100, 0, 50, 40, 50, 40, 30, 20);
    private static int ReadInt(JsonElement value, string property, int fallback) => value.TryGetProperty(property, out JsonElement item) && item.TryGetInt32(out int parsed) ? parsed : fallback;
    private static string ReadString(JsonElement value, string property) => value.TryGetProperty(property, out JsonElement item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : string.Empty;
    private static bool ReadBool(JsonElement value, string property) => value.TryGetProperty(property, out JsonElement item) && item.ValueKind is JsonValueKind.True;
}
