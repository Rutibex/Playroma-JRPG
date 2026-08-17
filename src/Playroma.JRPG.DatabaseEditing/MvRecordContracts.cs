using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.DatabaseEditing;

/// <summary>
/// Describes the standard RPG Maker MV properties for a database record independently
/// from the fields currently exposed by the editor UI. This prevents incomplete forms
/// from misclassifying ordinary MV data as plugin-created data.
/// </summary>
public sealed record MvRecordContract(
    MvDocumentKind Kind,
    IReadOnlySet<string> StandardProperties,
    IReadOnlySet<string> RequiredProperties)
{
    public bool IsStandard(string propertyName) => StandardProperties.Contains(propertyName);
    public bool IsRequired(string propertyName) => RequiredProperties.Contains(propertyName);
}

public static class MvRecordContractCatalog
{
    private static readonly IReadOnlyDictionary<MvDocumentKind, MvRecordContract> Contracts =
        CreateContracts();

    public static MvRecordContract Get(MvDocumentKind kind) => Contracts.TryGetValue(kind, out MvRecordContract? contract)
        ? contract
        : new MvRecordContract(kind, Set("id"), Set("id"));

    public static bool TryGet(MvDocumentKind kind, out MvRecordContract? contract) =>
        Contracts.TryGetValue(kind, out contract);

    private static IReadOnlyDictionary<MvDocumentKind, MvRecordContract> CreateContracts()
    {
        Dictionary<MvDocumentKind, MvRecordContract> result = [];
        Add(MvDocumentKind.Actors,
            ["id", "battlerName", "characterIndex", "characterName", "classId", "equips", "faceIndex", "faceName", "traits", "initialLevel", "maxLevel", "name", "nickname", "note", "profile"],
            ["id", "classId", "equips", "traits", "initialLevel", "maxLevel", "name"]);
        Add(MvDocumentKind.Classes,
            ["id", "expParams", "traits", "learnings", "name", "note", "params"],
            ["id", "expParams", "traits", "learnings", "name", "params"]);
        Add(MvDocumentKind.Skills,
            ["id", "animationId", "damage", "description", "effects", "hitType", "iconIndex", "message1", "message2", "mpCost", "name", "note", "occasion", "repeats", "requiredWtypeId1", "requiredWtypeId2", "scope", "speed", "stypeId", "successRate", "tpCost", "tpGain"],
            ["id", "damage", "effects", "name"]);
        Add(MvDocumentKind.Items,
            ["id", "animationId", "consumable", "damage", "description", "effects", "hitType", "iconIndex", "itypeId", "name", "note", "occasion", "price", "repeats", "scope", "speed", "successRate", "tpGain"],
            ["id", "damage", "effects", "name"]);
        Add(MvDocumentKind.Weapons,
            ["id", "animationId", "description", "etypeId", "traits", "iconIndex", "name", "note", "params", "price", "wtypeId"],
            ["id", "etypeId", "traits", "name", "params", "wtypeId"]);
        Add(MvDocumentKind.Armors,
            ["id", "atypeId", "description", "etypeId", "traits", "iconIndex", "name", "note", "params", "price"],
            ["id", "atypeId", "etypeId", "traits", "name", "params"]);
        Add(MvDocumentKind.Enemies,
            ["id", "actions", "battlerHue", "battlerName", "dropItems", "exp", "traits", "gold", "name", "note", "params"],
            ["id", "actions", "dropItems", "traits", "name", "params"]);
        Add(MvDocumentKind.Troops,
            ["id", "members", "name", "pages"],
            ["id", "members", "name", "pages"]);
        Add(MvDocumentKind.States,
            ["id", "autoRemovalTiming", "chanceByDamage", "iconIndex", "maxTurns", "message1", "message2", "message3", "message4", "minTurns", "motion", "name", "note", "overlay", "priority", "removeAtBattleEnd", "removeByDamage", "removeByRestriction", "removeByWalking", "restriction", "stepsToRemove", "traits"],
            ["id", "name", "traits"]);
        Add(MvDocumentKind.Animations,
            ["id", "animation1Hue", "animation1Name", "animation2Hue", "animation2Name", "frames", "name", "position", "timings"],
            ["id", "frames", "name", "timings"]);
        Add(MvDocumentKind.Tilesets,
            ["id", "flags", "mode", "name", "note", "tilesetNames"],
            ["id", "flags", "mode", "name", "tilesetNames"]);
        Add(MvDocumentKind.CommonEvents,
            ["id", "list", "name", "switchId", "trigger"],
            ["id", "list", "name", "switchId", "trigger"]);
        return result;

        void Add(MvDocumentKind kind, IEnumerable<string> standard, IEnumerable<string> required) =>
            result.Add(kind, new MvRecordContract(kind, Set(standard), Set(required)));
    }

    private static IReadOnlySet<string> Set(params string[] values) => Set((IEnumerable<string>)values);

    private static IReadOnlySet<string> Set(IEnumerable<string> values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
