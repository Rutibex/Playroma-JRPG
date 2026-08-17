using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Validation;

public sealed class Phase9CEnemyTroopValidator : IProjectValidator
{
    public string Name => "Enemies and troops";

    public async ValueTask<IReadOnlyList<ProjectValidationDiagnostic>> ValidateAsync(
        ProjectValidationContext context,
        CancellationToken cancellationToken = default)
    {
        List<ProjectValidationDiagnostic> diagnostics = [];
        foreach (ProjectDocumentHandle handle in context.Workspace.Documents.Where(item => item.Kind is MvDocumentKind.Enemies or MvDocumentKind.Troops))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IMutableProjectDocument document = await context.Workspace.OpenDocumentAsync(handle.RelativePath, cancellationToken).ConfigureAwait(false);
            ReadOnlyMemory<byte> bytes = await document.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument json = JsonDocument.Parse(Utf8JsonInput.ToBomlessArray(bytes.Span));
            if (handle.Kind == MvDocumentKind.Enemies) ValidateEnemies(json.RootElement, handle.RelativePath, diagnostics);
            else ValidateTroops(json.RootElement, handle.RelativePath, diagnostics);
        }
        return diagnostics;
    }

    private static void ValidateEnemies(JsonElement root, string relativePath, ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Array) return;
        foreach ((int index, JsonElement enemy) in root.EnumerateIndexedArray())
        {
            if (index == 0 || enemy.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            if (enemy.ValueKind != JsonValueKind.Object)
            {
                Add(diagnostics, DiagnosticCodes.Phase9CInvalidEnemyParameters, DiagnosticSeverity.Error, $"Enemy record {index} is not an object.", relativePath, $"/{index}", index);
                continue;
            }
            int id = ReadInt(enemy, "id", index);
            bool validParameters = enemy.TryGetProperty("params", out JsonElement parameters)
                && parameters.ValueKind == JsonValueKind.Array
                && parameters.GetArrayLength() == 8
                && parameters.EnumerateArray().All(value => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _));
            if (!validParameters)
            {
                Add(diagnostics, DiagnosticCodes.Phase9CInvalidEnemyParameters, DiagnosticSeverity.Error, $"Enemy {id} must contain exactly eight integer parameters.", relativePath, $"/{index}/params", id);
            }

            if (!enemy.TryGetProperty("dropItems", out JsonElement drops) || drops.ValueKind != JsonValueKind.Array)
            {
                Add(diagnostics, DiagnosticCodes.Phase9CInvalidEnemyDrop, DiagnosticSeverity.Error, $"Enemy {id} has no valid dropItems array.", relativePath, $"/{index}/dropItems", id);
            }
            else
            {
                int dropIndex = 0;
                foreach (JsonElement drop in drops.EnumerateArray())
                {
                    int kind = ReadInt(drop, "kind", -1);
                    int dataId = ReadInt(drop, "dataId", 0);
                    int denominator = ReadInt(drop, "denominator", 0);
                    if (drop.ValueKind != JsonValueKind.Object || kind is < 0 or > 3 || denominator <= 0 || kind > 0 && dataId <= 0)
                    {
                        Add(diagnostics, DiagnosticCodes.Phase9CInvalidEnemyDrop, DiagnosticSeverity.Error, $"Enemy {id} drop slot {dropIndex + 1} is malformed.", relativePath, $"/{index}/dropItems/{dropIndex}", id);
                    }
                    dropIndex++;
                }
            }

            if (!enemy.TryGetProperty("actions", out JsonElement actions) || actions.ValueKind != JsonValueKind.Array)
            {
                Add(diagnostics, DiagnosticCodes.Phase9CInvalidEnemyAction, DiagnosticSeverity.Error, $"Enemy {id} has no valid actions array.", relativePath, $"/{index}/actions", id);
            }
            else
            {
                int actionIndex = 0;
                bool hasPotentialAction = false;
                foreach (JsonElement action in actions.EnumerateArray())
                {
                    int skillId = ReadInt(action, "skillId", 0);
                    int conditionType = ReadInt(action, "conditionType", -1);
                    double first = ReadDouble(action, "conditionParam1", 0);
                    double second = ReadDouble(action, "conditionParam2", 0);
                    int rating = ReadInt(action, "rating", 0);
                    bool valid = action.ValueKind == JsonValueKind.Object && skillId > 0 && conditionType is >= 0 and <= 6 && rating is >= 1 and <= 9;
                    if (conditionType is 2 or 3 && (first < 0 || second > 1 || first > second)) valid = false;
                    if (conditionType == 1 && (first < 0 || second < 0)) valid = false;
                    if (!valid)
                    {
                        Add(diagnostics, DiagnosticCodes.Phase9CInvalidEnemyAction, DiagnosticSeverity.Error, $"Enemy {id} action {actionIndex + 1} has invalid skill, condition, or rating data.", relativePath, $"/{index}/actions/{actionIndex}", id);
                    }
                    else hasPotentialAction = true;
                    actionIndex++;
                }
                if (!hasPotentialAction)
                {
                    Add(diagnostics, DiagnosticCodes.Phase9CEnemyHasNoAction, DiagnosticSeverity.Warning, $"Enemy {id} has no potentially usable action pattern.", relativePath, $"/{index}/actions", id);
                }
            }
        }
    }

    private static void ValidateTroops(JsonElement root, string relativePath, ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Array) return;
        foreach ((int index, JsonElement troop) in root.EnumerateIndexedArray())
        {
            if (index == 0 || troop.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            if (troop.ValueKind != JsonValueKind.Object) continue;
            int id = ReadInt(troop, "id", index);
            int memberCount = 0;
            bool hasHidden = false;
            if (!troop.TryGetProperty("members", out JsonElement members) || members.ValueKind != JsonValueKind.Array)
            {
                Add(diagnostics, DiagnosticCodes.Phase9CInvalidTroopMember, DiagnosticSeverity.Error, $"Troop {id} has no valid members array.", relativePath, $"/{index}/members", id);
            }
            else
            {
                foreach ((int memberIndex, JsonElement member) in members.EnumerateIndexedArray())
                {
                    memberCount++;
                    bool valid = member.ValueKind == JsonValueKind.Object
                        && ReadInt(member, "enemyId", 0) > 0
                        && member.TryGetProperty("x", out JsonElement x) && x.TryGetInt32(out _)
                        && member.TryGetProperty("y", out JsonElement y) && y.TryGetInt32(out _)
                        && member.TryGetProperty("hidden", out JsonElement hidden) && hidden.ValueKind is JsonValueKind.True or JsonValueKind.False;
                    if (!valid)
                    {
                        Add(diagnostics, DiagnosticCodes.Phase9CInvalidTroopMember, DiagnosticSeverity.Error, $"Troop {id} member {memberIndex + 1} is malformed.", relativePath, $"/{index}/members/{memberIndex}", id);
                    }
                    hasHidden |= member.ValueKind == JsonValueKind.Object && ReadBool(member, "hidden");
                }
            }

            bool hasAppearCommand = false;
            if (!troop.TryGetProperty("pages", out JsonElement pages) || pages.ValueKind != JsonValueKind.Array || pages.GetArrayLength() == 0)
            {
                Add(diagnostics, DiagnosticCodes.Phase9CInvalidTroopPage, DiagnosticSeverity.Error, $"Troop {id} must contain at least one battle-event page.", relativePath, $"/{index}/pages", id);
                continue;
            }
            foreach ((int pageIndex, JsonElement page) in pages.EnumerateIndexedArray())
            {
                if (page.ValueKind != JsonValueKind.Object)
                {
                    Add(diagnostics, DiagnosticCodes.Phase9CInvalidTroopPage, DiagnosticSeverity.Error, $"Troop {id} page {pageIndex + 1} is not an object.", relativePath, $"/{index}/pages/{pageIndex}", id);
                    continue;
                }
                int span = ReadInt(page, "span", -1);
                if (span is < 0 or > 2)
                {
                    Add(diagnostics, DiagnosticCodes.Phase9CInvalidTroopPage, DiagnosticSeverity.Error, $"Troop {id} page {pageIndex + 1} has invalid span {span}.", relativePath, $"/{index}/pages/{pageIndex}/span", id);
                }
                if (!page.TryGetProperty("conditions", out JsonElement conditions) || conditions.ValueKind != JsonValueKind.Object)
                {
                    Add(diagnostics, DiagnosticCodes.Phase9CInvalidTroopPage, DiagnosticSeverity.Error, $"Troop {id} page {pageIndex + 1} has no valid conditions object.", relativePath, $"/{index}/pages/{pageIndex}/conditions", id);
                }
                else
                {
                    bool any = ReadBool(conditions, "turnEnding") || ReadBool(conditions, "turnValid") || ReadBool(conditions, "enemyValid") || ReadBool(conditions, "actorValid") || ReadBool(conditions, "switchValid");
                    if (!any)
                    {
                        Add(diagnostics, DiagnosticCodes.Phase9CTroopPageWillNotRun, DiagnosticSeverity.Warning, $"Troop {id} page {pageIndex + 1} has no enabled conditions and will not execute.", relativePath, $"/{index}/pages/{pageIndex}/conditions", id);
                    }
                    if (ReadBool(conditions, "enemyValid"))
                    {
                        int enemyIndex = ReadInt(conditions, "enemyIndex", -1);
                        if (enemyIndex < 0 || enemyIndex >= memberCount)
                        {
                            Add(diagnostics, DiagnosticCodes.Phase9CMemberIndexReference, DiagnosticSeverity.Error, $"Troop {id} page {pageIndex + 1} refers to missing member index {enemyIndex}.", relativePath, $"/{index}/pages/{pageIndex}/conditions/enemyIndex", id);
                        }
                    }
                }
                if (span == 2)
                {
                    Add(diagnostics, DiagnosticCodes.Phase9CMomentPageRepeatRisk, DiagnosticSeverity.Warning, $"Troop {id} page {pageIndex + 1} uses Moment span and may repeat continuously while its conditions remain true.", relativePath, $"/{index}/pages/{pageIndex}/span", id);
                }
                if (!page.TryGetProperty("list", out JsonElement list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
                {
                    Add(diagnostics, DiagnosticCodes.Phase9CInvalidTroopPage, DiagnosticSeverity.Error, $"Troop {id} page {pageIndex + 1} has no command-list terminator.", relativePath, $"/{index}/pages/{pageIndex}/list", id);
                }
                else
                {
                    foreach (JsonElement command in list.EnumerateArray())
                    {
                        if (command.ValueKind == JsonValueKind.Object && ReadInt(command, "code", 0) == 335) hasAppearCommand = true;
                    }
                    JsonElement last = list.EnumerateArray().Last();
                    if (last.ValueKind != JsonValueKind.Object || ReadInt(last, "code", -1) != 0)
                    {
                        Add(diagnostics, DiagnosticCodes.Phase9CInvalidTroopPage, DiagnosticSeverity.Error, $"Troop {id} page {pageIndex + 1} command list does not end with code 0.", relativePath, $"/{index}/pages/{pageIndex}/list", id);
                    }
                }
            }
            if (hasHidden && !hasAppearCommand)
            {
                Add(diagnostics, DiagnosticCodes.Phase9CHiddenMemberNeverAppears, DiagnosticSeverity.Warning, $"Troop {id} contains hidden members but no standard Appear Enemy command was found.", relativePath, $"/{index}/members", id);
            }
        }
    }

    private static void Add(
        ICollection<ProjectValidationDiagnostic> diagnostics,
        string code,
        DiagnosticSeverity severity,
        string message,
        string relativePath,
        string jsonPath,
        int recordId)
    {
        ProjectDiagnostic diagnostic = new(code, severity, message, new ProjectLocation(relativePath, jsonPath, RecordId: recordId));
        diagnostics.Add(new ProjectValidationDiagnostic(diagnostic, DocumentationKey: "phase9c/enemies-troops"));
    }

    private static int ReadInt(JsonElement element, string name, int fallback) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement property) && property.TryGetInt32(out int value) ? value : fallback;

    private static double ReadDouble(JsonElement element, string name, double fallback) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement property) && property.TryGetDouble(out double value) ? value : fallback;

    private static bool ReadBool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.True;
}
