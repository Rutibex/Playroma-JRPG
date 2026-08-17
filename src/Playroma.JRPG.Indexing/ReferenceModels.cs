using Playroma.JRPG.Assets;

namespace Playroma.JRPG.Indexing;

public enum ReferenceConfidence
{
    Exact,
    Heuristic
}

public enum ReferenceTargetKind
{
    Unknown,
    Actor,
    Class,
    Skill,
    Item,
    Weapon,
    Armor,
    Equipment,
    Enemy,
    Troop,
    TroopMember,
    State,
    Animation,
    Tileset,
    CommonEvent,
    Map,
    Switch,
    Variable,
    Element,
    SkillType,
    WeaponType,
    ArmorType,
    EquipmentType,
    Parameter,
    Asset,
    Plugin
}

public enum ReferenceUsageKind
{
    DatabaseField,
    Trait,
    Effect,
    EventPageCondition,
    TroopPageCondition,
    TroopMemberIndex,
    EventCommand,
    MovementRoute,
    SystemSetting,
    MapProperty,
    MapEncounter,
    PluginParameter,
    Note,
    Formula,
    Script,
    PluginCommand,
    AssetName,
    Unknown
}

public sealed record ReferenceTarget(
    ReferenceTargetKind Kind,
    int? Id = null,
    string? Name = null,
    AssetCategory? AssetCategory = null)
{
    public override string ToString()
    {
        if (Kind == ReferenceTargetKind.Asset)
        {
            return AssetCategory.HasValue
                ? $"{Kind}:{AssetCategory}:{Name}"
                : $"{Kind}:{Name}";
        }

        if (Id.HasValue) return $"{Kind}:{Id.Value}";
        return Name is null ? Kind.ToString() : $"{Kind}:{Name}";
    }
}

public sealed record ReferenceSource(
    string RelativePath,
    string JsonPath,
    int? MapId = null,
    int? RecordId = null,
    int? EventId = null,
    int? PageIndex = null,
    int? CommandIndex = null,
    string? Label = null)
{
    public string NormalizedRelativePath { get; } = RelativePath.Replace('\\', '/');
}

public sealed record ProjectReference(
    ReferenceSource Source,
    ReferenceTarget Target,
    ReferenceUsageKind Usage,
    ReferenceConfidence Confidence,
    string? Context = null);

public sealed record ReferenceIndexProgress(
    int CompletedDocuments,
    int TotalDocuments,
    string? CurrentDocument,
    int ReferenceCount);

public sealed record ReferenceIndexStatistics(
    int DocumentCount,
    int ExactReferenceCount,
    int HeuristicReferenceCount,
    int TotalReferenceCount,
    DateTimeOffset BuiltUtc);
