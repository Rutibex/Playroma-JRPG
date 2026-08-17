using System.Globalization;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.DatabaseEditing;

public sealed record NamedDatabaseValue(int Id, string Name)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? $"#{Id}" : $"{Id:D4} {Name}";
}

/// <summary>Names stored in System.json. Index zero is preserved because MV uses it for sentinel values.</summary>
public sealed record MvSystemTypeCatalog(
    IReadOnlyList<NamedDatabaseValue> Elements,
    IReadOnlyList<NamedDatabaseValue> SkillTypes,
    IReadOnlyList<NamedDatabaseValue> WeaponTypes,
    IReadOnlyList<NamedDatabaseValue> ArmorTypes,
    IReadOnlyList<NamedDatabaseValue> EquipmentTypes)
{
    public static MvSystemTypeCatalog Empty { get; } = new([], [], [], [], []);

    public static MvSystemTypeCatalog Read(MutableJsonProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Kind != MvDocumentKind.System)
        {
            throw new ArgumentException("A System.json document is required.", nameof(document));
        }

        return new MvSystemTypeCatalog(
            ReadNames(document, "elements"),
            ReadNames(document, "skillTypes"),
            ReadNames(document, "weaponTypes"),
            ReadNames(document, "armorTypes"),
            ReadNames(document, "equipTypes"));
    }

    public string Resolve(ReferenceTargetKind kind, int id) => kind switch
    {
        ReferenceTargetKind.Element => Resolve(Elements, id, "Element"),
        ReferenceTargetKind.SkillType => Resolve(SkillTypes, id, "Skill Type"),
        ReferenceTargetKind.WeaponType => Resolve(WeaponTypes, id, "Weapon Type"),
        ReferenceTargetKind.ArmorType => Resolve(ArmorTypes, id, "Armor Type"),
        ReferenceTargetKind.EquipmentType => Resolve(EquipmentTypes, id, "Equipment Type"),
        _ => $"#{id}"
    };

    private static IReadOnlyList<NamedDatabaseValue> ReadNames(MutableJsonProjectDocument document, string property)
    {
        JsonPath path = JsonPath.Root.Append(property);
        if (document.Syntax.Resolve(path) is not JsonArraySyntax array) return [];
        List<NamedDatabaseValue> values = new(array.Elements.Count);
        for (int index = 0; index < array.Elements.Count; index++)
        {
            values.Add(new NamedDatabaseValue(index, document.GetString(path.Append(index)) ?? string.Empty));
        }
        return values;
    }

    private static string Resolve(IReadOnlyList<NamedDatabaseValue> values, int id, string kind)
    {
        NamedDatabaseValue? value = values.FirstOrDefault(item => item.Id == id);
        return value is null || string.IsNullOrWhiteSpace(value.Name) ? $"Unknown {kind} #{id}" : value.Name;
    }
}

public sealed record MvDamageSnapshot(
    int Type,
    int ElementId,
    string Formula,
    int Variance,
    bool Critical,
    string RawJson);

public sealed record MvEffectSnapshot(
    int Code,
    int DataId,
    double Value1,
    double Value2,
    string RawJson);

public sealed record MvUsableItemSnapshot(
    int Id,
    string Name,
    string Description,
    int IconIndex,
    int Scope,
    int Occasion,
    int Speed,
    int SuccessRate,
    int Repeats,
    int TpGain,
    int HitType,
    int AnimationId,
    MvDamageSnapshot Damage,
    IReadOnlyList<MvEffectSnapshot> Effects,
    string Note);

public sealed record MvSkillRecordSnapshot(
    MvUsableItemSnapshot Common,
    int SkillTypeId,
    int MpCost,
    int TpCost,
    string Message1,
    string Message2,
    int RequiredWeaponType1,
    int RequiredWeaponType2);

public sealed record MvItemRecordSnapshot(
    MvUsableItemSnapshot Common,
    int ItemTypeId,
    int Price,
    bool Consumable);

public sealed record MvEquipmentRecordSnapshot(
    int Id,
    MvDocumentKind Kind,
    string Name,
    string Description,
    int IconIndex,
    int Price,
    int EquipmentTypeId,
    int TypeId,
    int AnimationId,
    IReadOnlyList<int> Parameters,
    int TraitCount,
    string Note);

public sealed record MvStateRecordSnapshot(
    int Id,
    string Name,
    int IconIndex,
    int Restriction,
    int Priority,
    int Motion,
    int Overlay,
    bool RemoveAtBattleEnd,
    bool RemoveByRestriction,
    int AutoRemovalTiming,
    int MinimumTurns,
    int MaximumTurns,
    bool RemoveByDamage,
    int ChanceByDamage,
    bool RemoveByWalking,
    int StepsToRemove,
    string Message1,
    string Message2,
    string Message3,
    string Message4,
    int TraitCount,
    string Note,
    IReadOnlyDictionary<string, string> ExtensionFields);

public static class Phase9BRecordReader
{
    public static MvSkillRecordSnapshot ReadSkill(MutableJsonProjectDocument document, int recordId)
    {
        MvUsableItemSnapshot common = ReadUsable(document, recordId);
        JsonPath root = JsonPath.Root.Append(Validate(recordId));
        return new MvSkillRecordSnapshot(
            common,
            document.GetInt32(root.Append("stypeId")) ?? 0,
            document.GetInt32(root.Append("mpCost")) ?? 0,
            document.GetInt32(root.Append("tpCost")) ?? 0,
            document.GetString(root.Append("message1")) ?? string.Empty,
            document.GetString(root.Append("message2")) ?? string.Empty,
            document.GetInt32(root.Append("requiredWtypeId1")) ?? 0,
            document.GetInt32(root.Append("requiredWtypeId2")) ?? 0);
    }

    public static MvItemRecordSnapshot ReadItem(MutableJsonProjectDocument document, int recordId)
    {
        MvUsableItemSnapshot common = ReadUsable(document, recordId);
        JsonPath root = JsonPath.Root.Append(Validate(recordId));
        return new MvItemRecordSnapshot(
            common,
            document.GetInt32(root.Append("itypeId")) ?? 1,
            document.GetInt32(root.Append("price")) ?? 0,
            document.GetBoolean(root.Append("consumable")) ?? true);
    }

    public static MvEquipmentRecordSnapshot ReadEquipment(MutableJsonProjectDocument document, int recordId)
    {
        if (document.Kind is not MvDocumentKind.Weapons and not MvDocumentKind.Armors)
        {
            throw new ArgumentException("A Weapons.json or Armors.json document is required.", nameof(document));
        }
        JsonPath root = JsonPath.Root.Append(Validate(recordId));
        EnsureObject(document, root, "equipment");
        return new MvEquipmentRecordSnapshot(
            recordId,
            document.Kind,
            document.GetString(root.Append("name")) ?? string.Empty,
            document.GetString(root.Append("description")) ?? string.Empty,
            document.GetInt32(root.Append("iconIndex")) ?? 0,
            document.GetInt32(root.Append("price")) ?? 0,
            document.GetInt32(root.Append("etypeId")) ?? 0,
            document.GetInt32(root.Append(document.Kind == MvDocumentKind.Weapons ? "wtypeId" : "atypeId")) ?? 0,
            document.Kind == MvDocumentKind.Weapons ? document.GetInt32(root.Append("animationId")) ?? 0 : 0,
            ReadIntArray(document, root.Append("params")),
            ArrayCount(document, root.Append("traits")),
            document.GetString(root.Append("note")) ?? string.Empty);
    }

    public static MvStateRecordSnapshot ReadState(MutableJsonProjectDocument document, int recordId)
    {
        JsonPath root = JsonPath.Root.Append(Validate(recordId));
        EnsureObject(document, root, "state");
        MvRecordContract contract = MvRecordContractCatalog.Get(MvDocumentKind.States);
        Dictionary<string, string> extensions = new(StringComparer.Ordinal);
        if (document.Syntax.Resolve(root) is JsonObjectSyntax state)
        {
            foreach (JsonPropertySyntax property in state.Properties)
            {
                if (!contract.IsStandard(property.Name))
                {
                    extensions[property.Name] = document.GetRawJson(root.Append(property.Name)) ?? "null";
                }
            }
        }
        return new MvStateRecordSnapshot(
            recordId,
            document.GetString(root.Append("name")) ?? string.Empty,
            document.GetInt32(root.Append("iconIndex")) ?? 0,
            document.GetInt32(root.Append("restriction")) ?? 0,
            document.GetInt32(root.Append("priority")) ?? 50,
            document.GetInt32(root.Append("motion")) ?? 0,
            document.GetInt32(root.Append("overlay")) ?? 0,
            document.GetBoolean(root.Append("removeAtBattleEnd")) ?? false,
            document.GetBoolean(root.Append("removeByRestriction")) ?? false,
            document.GetInt32(root.Append("autoRemovalTiming")) ?? 0,
            document.GetInt32(root.Append("minTurns")) ?? 1,
            document.GetInt32(root.Append("maxTurns")) ?? 1,
            document.GetBoolean(root.Append("removeByDamage")) ?? false,
            document.GetInt32(root.Append("chanceByDamage")) ?? 100,
            document.GetBoolean(root.Append("removeByWalking")) ?? false,
            document.GetInt32(root.Append("stepsToRemove")) ?? 100,
            document.GetString(root.Append("message1")) ?? string.Empty,
            document.GetString(root.Append("message2")) ?? string.Empty,
            document.GetString(root.Append("message3")) ?? string.Empty,
            document.GetString(root.Append("message4")) ?? string.Empty,
            ArrayCount(document, root.Append("traits")),
            document.GetString(root.Append("note")) ?? string.Empty,
            extensions);
    }

    public static IReadOnlyList<int> ReadIntArray(MutableJsonProjectDocument document, JsonPath path)
    {
        if (document.Syntax.Resolve(path) is not JsonArraySyntax array) return [];
        int[] result = new int[array.Elements.Count];
        for (int index = 0; index < result.Length; index++) result[index] = document.GetInt32(path.Append(index)) ?? 0;
        return result;
    }

    private static MvUsableItemSnapshot ReadUsable(MutableJsonProjectDocument document, int recordId)
    {
        if (document.Kind is not MvDocumentKind.Skills and not MvDocumentKind.Items)
        {
            throw new ArgumentException("A Skills.json or Items.json document is required.", nameof(document));
        }
        JsonPath root = JsonPath.Root.Append(Validate(recordId));
        EnsureObject(document, root, "usable item");
        JsonPath damagePath = root.Append("damage");
        MvDamageSnapshot damage = new(
            document.GetInt32(damagePath.Append("type")) ?? 0,
            document.GetInt32(damagePath.Append("elementId")) ?? 0,
            document.GetString(damagePath.Append("formula")) ?? "0",
            document.GetInt32(damagePath.Append("variance")) ?? 0,
            document.GetBoolean(damagePath.Append("critical")) ?? false,
            document.GetRawJson(damagePath) ?? "{}");
        return new MvUsableItemSnapshot(
            recordId,
            document.GetString(root.Append("name")) ?? string.Empty,
            document.GetString(root.Append("description")) ?? string.Empty,
            document.GetInt32(root.Append("iconIndex")) ?? 0,
            document.GetInt32(root.Append("scope")) ?? 0,
            document.GetInt32(root.Append("occasion")) ?? 0,
            document.GetInt32(root.Append("speed")) ?? 0,
            document.GetInt32(root.Append("successRate")) ?? 100,
            document.GetInt32(root.Append("repeats")) ?? 1,
            document.GetInt32(root.Append("tpGain")) ?? 0,
            document.GetInt32(root.Append("hitType")) ?? 0,
            document.GetInt32(root.Append("animationId")) ?? 0,
            damage,
            ReadEffects(document, root.Append("effects")),
            document.GetString(root.Append("note")) ?? string.Empty);
    }

    private static IReadOnlyList<MvEffectSnapshot> ReadEffects(MutableJsonProjectDocument document, JsonPath path)
    {
        if (document.Syntax.Resolve(path) is not JsonArraySyntax array) return [];
        List<MvEffectSnapshot> result = new(array.Elements.Count);
        for (int index = 0; index < array.Elements.Count; index++)
        {
            JsonPath entry = path.Append(index);
            result.Add(new MvEffectSnapshot(
                document.GetInt32(entry.Append("code")) ?? 0,
                document.GetInt32(entry.Append("dataId")) ?? 0,
                document.GetDouble(entry.Append("value1")) ?? 0,
                document.GetDouble(entry.Append("value2")) ?? 0,
                document.GetRawJson(entry) ?? "{}"));
        }
        return result;
    }

    private static int ArrayCount(MutableJsonProjectDocument document, JsonPath path) =>
        (document.Syntax.Resolve(path) as JsonArraySyntax)?.Elements.Count ?? 0;

    private static void EnsureObject(MutableJsonProjectDocument document, JsonPath path, string kind)
    {
        if (document.Syntax.Resolve(path) is not JsonObjectSyntax)
        {
            throw new InvalidDataException($"The selected {kind} record is empty or invalid.");
        }
    }

    private static int Validate(int id) => id > 0 ? id : throw new ArgumentOutOfRangeException(nameof(id));
}

public sealed class Phase9BMutationService
{
    private readonly MvEditingSession _session;

    public Phase9BMutationService(MvEditingSession session) =>
        _session = session ?? throw new ArgumentNullException(nameof(session));

    public ValueTask SetDamageAsync(
        string relativePath,
        int recordId,
        MvDamageSnapshot damage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(damage);
        JsonPath path = JsonPath.Root.Append(Validate(recordId)).Append("damage");
        List<IEditorCommand> commands =
        [
            new SetJsonValueCommand(relativePath, path.Append("type"), damage.Type.ToString(CultureInfo.InvariantCulture), "Set damage type"),
            new SetJsonValueCommand(relativePath, path.Append("elementId"), damage.ElementId.ToString(CultureInfo.InvariantCulture), "Set damage element"),
            new SetJsonValueCommand(relativePath, path.Append("formula"), EncodeString(damage.Formula), "Set damage formula"),
            new SetJsonValueCommand(relativePath, path.Append("variance"), damage.Variance.ToString(CultureInfo.InvariantCulture), "Set damage variance"),
            new SetJsonValueCommand(relativePath, path.Append("critical"), damage.Critical ? "true" : "false", "Set critical permission")
        ];
        return ExecuteAsync($"Change record {recordId} damage", commands, cancellationToken);
    }

    public ValueTask SetEquipmentParametersAsync(
        string relativePath,
        int recordId,
        IReadOnlyList<int> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Count != 8) throw new ArgumentException("MV equipment requires exactly eight parameters.", nameof(parameters));
        string raw = JsonSerializer.Serialize(parameters.Select(value => Math.Clamp(value, -999999, 999999)).ToArray());
        return ExecuteAsync(
            $"Change equipment {recordId} parameters",
            [new SetJsonValueCommand(relativePath, JsonPath.Root.Append(Validate(recordId)).Append("params"), raw)],
            cancellationToken);
    }

    public ValueTask SetStateRemovalAsync(
        string relativePath,
        int recordId,
        MvStateRemovalSnapshot removal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(removal);
        if (removal.MinimumTurns > removal.MaximumTurns)
        {
            throw new ArgumentException("Minimum turns cannot exceed maximum turns.", nameof(removal));
        }
        JsonPath root = JsonPath.Root.Append(Validate(recordId));
        List<IEditorCommand> commands =
        [
            Bool("removeAtBattleEnd", removal.RemoveAtBattleEnd),
            Bool("removeByRestriction", removal.RemoveByRestriction),
            Int("autoRemovalTiming", removal.AutoRemovalTiming),
            Int("minTurns", removal.MinimumTurns),
            Int("maxTurns", removal.MaximumTurns),
            Bool("removeByDamage", removal.RemoveByDamage),
            Int("chanceByDamage", removal.ChanceByDamage),
            Bool("removeByWalking", removal.RemoveByWalking),
            Int("stepsToRemove", removal.StepsToRemove)
        ];
        return ExecuteAsync($"Change state {recordId} removal rules", commands, cancellationToken);

        SetJsonValueCommand Bool(string property, bool value) =>
            new(relativePath, root.Append(property), value ? "true" : "false", $"Set {property}");
        SetJsonValueCommand Int(string property, int value) =>
            new(relativePath, root.Append(property), value.ToString(CultureInfo.InvariantCulture), $"Set {property}");
    }

    public ValueTask SetStateMessagesAsync(
        string relativePath,
        int recordId,
        IReadOnlyList<string> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count != 4) throw new ArgumentException("MV states require four message strings.", nameof(messages));
        JsonPath root = JsonPath.Root.Append(Validate(recordId));
        List<IEditorCommand> commands = [];
        for (int index = 0; index < 4; index++)
        {
            commands.Add(new SetJsonValueCommand(relativePath, root.Append($"message{index + 1}"), EncodeString(messages[index]), $"Set state message {index + 1}"));
        }
        return ExecuteAsync($"Change state {recordId} messages", commands, cancellationToken);
    }

    private async ValueTask ExecuteAsync(
        string description,
        IReadOnlyList<IEditorCommand> commands,
        CancellationToken cancellationToken)
    {
        await using ProjectTransaction transaction = _session.BeginTransaction(description);
        foreach (IEditorCommand command in commands) transaction.Add(command);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string EncodeString(string value) => Encoding.UTF8.GetString(JsonValueEncoder.EncodeString(value ?? string.Empty));
    private static int Validate(int id) => id > 0 ? id : throw new ArgumentOutOfRangeException(nameof(id));
}

public sealed record MvStateRemovalSnapshot(
    bool RemoveAtBattleEnd,
    bool RemoveByRestriction,
    int AutoRemovalTiming,
    int MinimumTurns,
    int MaximumTurns,
    bool RemoveByDamage,
    int ChanceByDamage,
    bool RemoveByWalking,
    int StepsToRemove);
