using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Validation;

/// <summary>Phase 9A consistency checks for Actors.json and Classes.json.</summary>
public sealed class ActorClassValidator : IProjectValidator
{
    public string Name => "Actors and classes";

    public async ValueTask<IReadOnlyList<ProjectValidationDiagnostic>> ValidateAsync(
        ProjectValidationContext context,
        CancellationToken cancellationToken = default)
    {
        List<ProjectValidationDiagnostic> diagnostics = [];
        HashSet<int> weapons = await ReadIdsAsync(context.Workspace, MvDocumentKind.Weapons, cancellationToken).ConfigureAwait(false);
        HashSet<int> armors = await ReadIdsAsync(context.Workspace, MvDocumentKind.Armors, cancellationToken).ConfigureAwait(false);
        HashSet<int> skills = await ReadIdsAsync(context.Workspace, MvDocumentKind.Skills, cancellationToken).ConfigureAwait(false);
        Dictionary<int, int> maximumActorLevelByClass = [];
        Dictionary<int, bool> dualWieldByClass = [];

        ProjectDocumentHandle? classes = context.Workspace.Documents.FirstOrDefault(item => item.Kind == MvDocumentKind.Classes);
        if (classes is not null)
        {
            using JsonDocument classTraits = await ReadJsonAsync(context.Workspace, classes, cancellationToken).ConfigureAwait(false);
            dualWieldByClass = ReadDualWieldClasses(classTraits.RootElement);
        }

        ProjectDocumentHandle? actors = context.Workspace.Documents.FirstOrDefault(item => item.Kind == MvDocumentKind.Actors);
        if (actors is not null)
        {
            using JsonDocument document = await ReadJsonAsync(context.Workspace, actors, cancellationToken).ConfigureAwait(false);
            ValidateActors(document.RootElement, actors.RelativePath, weapons, armors, dualWieldByClass, maximumActorLevelByClass, diagnostics);
        }

        if (classes is not null)
        {
            using JsonDocument document = await ReadJsonAsync(context.Workspace, classes, cancellationToken).ConfigureAwait(false);
            ValidateClasses(document.RootElement, classes.RelativePath, skills, maximumActorLevelByClass, diagnostics);
        }
        return diagnostics;
    }

    private static void ValidateActors(
        JsonElement root,
        string relativePath,
        IReadOnlySet<int> weapons,
        IReadOnlySet<int> armors,
        IReadOnlyDictionary<int, bool> dualWieldByClass,
        IDictionary<int, int> maximumActorLevelByClass,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Array) return;
        int recordId = 0;
        foreach (JsonElement actor in root.EnumerateArray())
        {
            if (actor.ValueKind != JsonValueKind.Object)
            {
                recordId++;
                continue;
            }
            int id = actor.TryGetProperty("id", out JsonElement idValue) && idValue.TryGetInt32(out int parsedId) ? parsedId : recordId;
            int initial = ReadInt(actor, "initialLevel", 1);
            int maximum = ReadInt(actor, "maxLevel", 99);
            int classId = ReadInt(actor, "classId", 0);
            if (classId > 0)
            {
                maximumActorLevelByClass[classId] = maximumActorLevelByClass.TryGetValue(classId, out int existingMaximum)
                    ? Math.Max(existingMaximum, maximum)
                    : maximum;
            }
            if (initial < 1 || maximum < 1 || initial > maximum || maximum > 99)
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Error(
                        DiagnosticCodes.Phase9AInvalidActorLevelRange,
                        $"Actor {id} has invalid level range {initial}–{maximum}.",
                        new ProjectLocation(relativePath, $"/{recordId}/initialLevel", RecordId: recordId)),
                    DocumentationKey: "phase9a/actor-level-range"));
            }

            if (actor.TryGetProperty("equips", out JsonElement equips) && equips.ValueKind == JsonValueKind.Array)
            {
                bool dualWield = HasTrait(actor, 55, 1)
                    || (dualWieldByClass.TryGetValue(classId, out bool classDualWield) && classDualWield);
                int slot = 0;
                foreach (JsonElement equipment in equips.EnumerateArray())
                {
                    int equipmentId = equipment.TryGetInt32(out int parsedEquipment) ? parsedEquipment : 0;
                    if (equipmentId > 0)
                    {
                        bool weaponSlot = slot == 0 || (slot == 1 && dualWield);
                        bool exists = weaponSlot ? weapons.Contains(equipmentId) : armors.Contains(equipmentId);
                        if (!exists)
                        {
                            string kind = weaponSlot ? "weapon" : "armor";
                            diagnostics.Add(new ProjectValidationDiagnostic(
                                ProjectDiagnostic.Warning(
                                    DiagnosticCodes.Phase9AMissingEquipment,
                                    $"Actor {id} initial equipment slot {slot + 1} references missing {kind} {equipmentId}.",
                                    new ProjectLocation(relativePath, $"/{recordId}/equips/{slot}", RecordId: recordId)),
                                DocumentationKey: "phase9a/initial-equipment"));
                        }
                    }
                    slot++;
                }
            }
            recordId++;
        }
    }

    private static void ValidateClasses(
        JsonElement root,
        string relativePath,
        IReadOnlySet<int> skills,
        IReadOnlyDictionary<int, int> maximumActorLevelByClass,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Array) return;
        int recordId = 0;
        foreach (JsonElement @class in root.EnumerateArray())
        {
            if (@class.ValueKind != JsonValueKind.Object)
            {
                recordId++;
                continue;
            }
            int id = ReadInt(@class, "id", recordId);
            bool hasExpParameters = @class.TryGetProperty("expParams", out JsonElement exp);
            bool hasParameterCurves = @class.TryGetProperty("params", out JsonElement parameters);
            bool invalidCurve = !hasExpParameters
                || exp.ValueKind != JsonValueKind.Array
                || exp.GetArrayLength() != 4
                || !hasParameterCurves
                || parameters.ValueKind != JsonValueKind.Array
                || parameters.GetArrayLength() != 8;
            if (!invalidCurve)
            {
                foreach (JsonElement curve in parameters.EnumerateArray())
                {
                    if (curve.ValueKind != JsonValueKind.Array || curve.GetArrayLength() < 100)
                    {
                        invalidCurve = true;
                        break;
                    }
                }
            }
            if (invalidCurve)
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Error(
                        DiagnosticCodes.Phase9AInvalidClassCurve,
                        $"Class {id} does not contain four EXP parameters and eight 100-entry parameter curves.",
                        new ProjectLocation(relativePath, $"/{recordId}/params", RecordId: recordId)),
                    DocumentationKey: "phase9a/class-curves"));
            }

            if (@class.TryGetProperty("learnings", out JsonElement learnings) && learnings.ValueKind == JsonValueKind.Array)
            {
                HashSet<(int Level, int SkillId)> seen = [];
                int learningIndex = 0;
                foreach (JsonElement learning in learnings.EnumerateArray())
                {
                    if (learning.ValueKind == JsonValueKind.Object)
                    {
                        (int Level, int SkillId) key = (ReadInt(learning, "level", 1), ReadInt(learning, "skillId", 0));
                        ProjectLocation location = new(relativePath, $"/{recordId}/learnings/{learningIndex}", RecordId: recordId);
                        if (key.Level < 1 || key.Level > 99)
                        {
                            diagnostics.Add(new ProjectValidationDiagnostic(
                                ProjectDiagnostic.Error(
                                    DiagnosticCodes.Phase9AInvalidLearningLevel,
                                    $"Class {id} has a skill learning at invalid level {key.Level}.",
                                    location),
                                DocumentationKey: "phase9a/class-learnings"));
                        }
                        if (key.SkillId > 0 && !skills.Contains(key.SkillId))
                        {
                            diagnostics.Add(new ProjectValidationDiagnostic(
                                ProjectDiagnostic.Warning(
                                    DiagnosticCodes.Phase9AMissingLearnedSkill,
                                    $"Class {id} learns missing skill {key.SkillId} at level {key.Level}.",
                                    location),
                                DocumentationKey: "phase9a/class-learnings"));
                        }
                        if (maximumActorLevelByClass.TryGetValue(id, out int reachableLevel) && key.Level > reachableLevel)
                        {
                            diagnostics.Add(new ProjectValidationDiagnostic(
                                ProjectDiagnostic.Warning(
                                    DiagnosticCodes.Phase9AUnreachableLearning,
                                    $"Class {id} learns skill {key.SkillId} at level {key.Level}, but actors currently using this class can reach only level {reachableLevel}.",
                                    location),
                                DocumentationKey: "phase9a/class-learnings"));
                        }
                        if (!seen.Add(key))
                        {
                            diagnostics.Add(new ProjectValidationDiagnostic(
                                ProjectDiagnostic.Warning(
                                    DiagnosticCodes.Phase9ADuplicateLearning,
                                    $"Class {id} learns skill {key.SkillId} more than once at level {key.Level}.",
                                    location),
                                DocumentationKey: "phase9a/class-learnings"));
                        }
                    }
                    learningIndex++;
                }
            }
            recordId++;
        }
    }


    private static Dictionary<int, bool> ReadDualWieldClasses(JsonElement root)
    {
        Dictionary<int, bool> result = [];
        if (root.ValueKind != JsonValueKind.Array) return result;
        int recordId = 0;
        foreach (JsonElement item in root.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                int id = ReadInt(item, "id", recordId);
                result[id] = HasTrait(item, 55, 1);
            }
            recordId++;
        }
        return result;
    }

    private static bool HasTrait(JsonElement owner, int code, int dataId)
    {
        if (!owner.TryGetProperty("traits", out JsonElement traits) || traits.ValueKind != JsonValueKind.Array) return false;
        foreach (JsonElement trait in traits.EnumerateArray())
        {
            if (trait.ValueKind != JsonValueKind.Object) continue;
            if (ReadInt(trait, "code", 0) == code && ReadInt(trait, "dataId", 0) == dataId) return true;
        }
        return false;
    }

    private static async ValueTask<HashSet<int>> ReadIdsAsync(
        IMvProjectWorkspace workspace,
        MvDocumentKind kind,
        CancellationToken cancellationToken)
    {
        ProjectDocumentHandle? handle = workspace.Documents.FirstOrDefault(item => item.Kind == kind);
        if (handle is null) return [];
        using JsonDocument document = await ReadJsonAsync(workspace, handle, cancellationToken).ConfigureAwait(false);
        HashSet<int> result = [];
        if (document.RootElement.ValueKind != JsonValueKind.Array) return result;
        int index = 0;
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object) result.Add(ReadInt(item, "id", index));
            index++;
        }
        return result;
    }

    private static async ValueTask<JsonDocument> ReadJsonAsync(
        IMvProjectWorkspace workspace,
        ProjectDocumentHandle handle,
        CancellationToken cancellationToken)
    {
        await using IProjectDocumentLease lease = await workspace.AcquireDocumentAsync(handle.RelativePath, cancellationToken).ConfigureAwait(false);
        ReadOnlyMemory<byte> bytes = await lease.Document.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(bytes);
    }

    private static int ReadInt(JsonElement owner, string property, int fallback) =>
        owner.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int parsed) ? parsed : fallback;
}
