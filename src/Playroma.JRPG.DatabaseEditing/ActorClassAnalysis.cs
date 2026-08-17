using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.DatabaseEditing;

/// <summary>Standard RPG Maker MV trait codes used by Phase 9A analysis.</summary>
public static class MvTraitCodes
{
    public const int EquipWeaponType = 51;
    public const int EquipArmorType = 52;
    public const int EquipLock = 53;
    public const int EquipSeal = 54;
    public const int SlotType = 55;
    public const int DualWieldSlotType = 1;
}

public sealed record ActorEquipmentLegalityEntry(
    int SlotIndex,
    int ExpectedEquipTypeId,
    string ExpectedEquipTypeName,
    int ItemId,
    string ItemName,
    string ItemKind,
    bool IsLegal,
    string Message);

public sealed record ActorEquipmentLegalityReport(
    int ActorId,
    int ClassId,
    bool IsDualWield,
    IReadOnlyList<ActorEquipmentLegalityEntry> Entries)
{
    public int ProblemCount => Entries.Count(entry => !entry.IsLegal);
}

/// <summary>
/// Produces a read-only equipment compatibility report using the same core rules MV uses:
/// equip slots are derived from System.equipTypes, dual wield converts slot two to weapon,
/// and class/actor traits grant weapon and armor types or seal equipment types.
/// </summary>
public static class ActorEquipmentLegalityAnalyzer
{
    public static ActorEquipmentLegalityReport Analyze(
        MutableJsonProjectDocument actors,
        MutableJsonProjectDocument classes,
        MutableJsonProjectDocument? system,
        MutableJsonProjectDocument? weapons,
        MutableJsonProjectDocument? armors,
        int actorId)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(classes);
        if (actorId <= 0) throw new ArgumentOutOfRangeException(nameof(actorId));

        MvActorRecordSnapshot actor = ActorClassRecordReader.ReadActor(actors, actorId);
        JsonPath actorRoot = JsonPath.Root.Append(actorId);
        JsonPath classRoot = JsonPath.Root.Append(actor.ClassId);
        HashSet<int> weaponTypes = ReadTraitDataIds(actors, actorRoot.Append("traits"), MvTraitCodes.EquipWeaponType);
        HashSet<int> armorTypes = ReadTraitDataIds(actors, actorRoot.Append("traits"), MvTraitCodes.EquipArmorType);
        HashSet<int> sealedEquipTypes = ReadTraitDataIds(actors, actorRoot.Append("traits"), MvTraitCodes.EquipSeal);
        bool dualWield = HasTrait(actors, actorRoot.Append("traits"), MvTraitCodes.SlotType, MvTraitCodes.DualWieldSlotType);

        if (classes.Syntax.Resolve(classRoot) is JsonObjectSyntax)
        {
            weaponTypes.UnionWith(ReadTraitDataIds(classes, classRoot.Append("traits"), MvTraitCodes.EquipWeaponType));
            armorTypes.UnionWith(ReadTraitDataIds(classes, classRoot.Append("traits"), MvTraitCodes.EquipArmorType));
            sealedEquipTypes.UnionWith(ReadTraitDataIds(classes, classRoot.Append("traits"), MvTraitCodes.EquipSeal));
            dualWield |= HasTrait(classes, classRoot.Append("traits"), MvTraitCodes.SlotType, MvTraitCodes.DualWieldSlotType);
        }

        IReadOnlyList<string> equipTypeNames = ReadEquipTypeNames(system, actor.EquipmentIds.Count);
        int slotCount = Math.Max(actor.EquipmentIds.Count, equipTypeNames.Count);
        List<int> expectedEquipTypes = Enumerable.Range(1, Math.Max(1, slotCount)).ToList();
        if (dualWield && expectedEquipTypes.Count >= 2) expectedEquipTypes[1] = 1;

        List<ActorEquipmentLegalityEntry> entries = new(slotCount);
        for (int slot = 0; slot < slotCount; slot++)
        {
            int expectedEquipType = slot < expectedEquipTypes.Count ? expectedEquipTypes[slot] : slot + 1;
            string expectedName = expectedEquipType > 0 && expectedEquipType <= equipTypeNames.Count
                ? equipTypeNames[expectedEquipType - 1]
                : $"Equipment Type {expectedEquipType}";
            int itemId = slot < actor.EquipmentIds.Count ? actor.EquipmentIds[slot] : 0;
            bool weaponSlot = expectedEquipType == 1;
            if (itemId <= 0)
            {
                entries.Add(new ActorEquipmentLegalityEntry(
                    slot,
                    expectedEquipType,
                    expectedName,
                    0,
                    "None",
                    weaponSlot ? "Weapon" : "Armor",
                    true,
                    "Empty slot"));
                continue;
            }

            MutableJsonProjectDocument? source = weaponSlot ? weapons : armors;
            DatabaseItem item = ReadItem(source, itemId, weaponSlot);
            if (!item.Exists)
            {
                entries.Add(new ActorEquipmentLegalityEntry(
                    slot,
                    expectedEquipType,
                    expectedName,
                    itemId,
                    $"Missing record {itemId}",
                    weaponSlot ? "Weapon" : "Armor",
                    false,
                    $"The referenced {(weaponSlot ? "weapon" : "armor")} does not exist."));
                continue;
            }

            List<string> problems = [];
            if (item.EquipTypeId != expectedEquipType)
            {
                problems.Add($"item equipment type is {item.EquipTypeId}, expected {expectedEquipType}");
            }
            if (sealedEquipTypes.Contains(expectedEquipType))
            {
                problems.Add("this equipment type is sealed by actor/class traits");
            }
            if (weaponSlot && item.TypeId > 0 && !weaponTypes.Contains(item.TypeId))
            {
                problems.Add($"weapon type {item.TypeId} is not granted by actor/class traits");
            }
            if (!weaponSlot && item.TypeId > 0 && !armorTypes.Contains(item.TypeId))
            {
                problems.Add($"armor type {item.TypeId} is not granted by actor/class traits");
            }

            entries.Add(new ActorEquipmentLegalityEntry(
                slot,
                expectedEquipType,
                expectedName,
                itemId,
                item.Name,
                weaponSlot ? "Weapon" : "Armor",
                problems.Count == 0,
                problems.Count == 0 ? "Legal" : string.Join("; ", problems)));
        }

        return new ActorEquipmentLegalityReport(actorId, actor.ClassId, dualWield, entries);
    }

    private static IReadOnlyList<string> ReadEquipTypeNames(MutableJsonProjectDocument? system, int fallbackCount)
    {
        if (system is not null && system.Syntax.Resolve(new JsonPath("/equipTypes")) is JsonArraySyntax array)
        {
            List<string> result = [];
            for (int id = 1; id < array.Elements.Count; id++)
            {
                result.Add(system.GetString(new JsonPath("/equipTypes").Append(id)) ?? $"Equipment Type {id}");
            }
            if (result.Count > 0) return result;
        }

        string[] standard = ["Weapon", "Shield", "Head", "Body", "Accessory"];
        int count = Math.Max(fallbackCount, standard.Length);
        return Enumerable.Range(0, count)
            .Select(index => index < standard.Length ? standard[index] : $"Equipment Type {index + 1}")
            .ToArray();
    }

    private static HashSet<int> ReadTraitDataIds(
        MutableJsonProjectDocument document,
        JsonPath path,
        int code)
    {
        HashSet<int> result = [];
        if (document.Syntax.Resolve(path) is not JsonArraySyntax traits) return result;
        for (int index = 0; index < traits.Elements.Count; index++)
        {
            JsonPath trait = path.Append(index);
            if (document.GetInt32(trait.Append("code")) == code
                && document.GetInt32(trait.Append("dataId")) is int dataId)
            {
                result.Add(dataId);
            }
        }
        return result;
    }

    private static bool HasTrait(
        MutableJsonProjectDocument document,
        JsonPath path,
        int code,
        int dataId) => ReadTraitDataIds(document, path, code).Contains(dataId);

    private static DatabaseItem ReadItem(MutableJsonProjectDocument? document, int id, bool weapon)
    {
        if (document is null || document.Syntax.Resolve(JsonPath.Root.Append(id)) is not JsonObjectSyntax)
        {
            return new DatabaseItem(false, string.Empty, 0, 0);
        }
        JsonPath root = JsonPath.Root.Append(id);
        return new DatabaseItem(
            true,
            document.GetString(root.Append("name")) ?? $"{(weapon ? "Weapon" : "Armor")} {id}",
            document.GetInt32(root.Append("etypeId")) ?? 0,
            document.GetInt32(root.Append(weapon ? "wtypeId" : "atypeId")) ?? 0);
    }

    private sealed record DatabaseItem(bool Exists, string Name, int EquipTypeId, int TypeId);
}
