using System.Globalization;
using System.Text.Json;
using Playroma.JRPG.Assets;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Indexing;

internal static class MvReferenceExtractor
{
    public static IReadOnlyList<ProjectReference> Extract(
        string relativePath,
        MvDocumentKind kind,
        JsonElement root)
    {
        ExtractionContext context = new(relativePath, kind);
        switch (kind)
        {
            case MvDocumentKind.Actors:
                ScanDatabase(root, context, ScanActor);
                break;
            case MvDocumentKind.Classes:
                ScanDatabase(root, context, ScanClass);
                break;
            case MvDocumentKind.Skills:
                ScanDatabase(root, context, ScanSkill);
                break;
            case MvDocumentKind.Items:
                ScanDatabase(root, context, ScanItem);
                break;
            case MvDocumentKind.Weapons:
                ScanDatabase(root, context, ScanWeapon);
                break;
            case MvDocumentKind.Armors:
                ScanDatabase(root, context, ScanArmor);
                break;
            case MvDocumentKind.Enemies:
                ScanDatabase(root, context, ScanEnemy);
                break;
            case MvDocumentKind.Troops:
                ScanDatabase(root, context, ScanTroop);
                break;
            case MvDocumentKind.States:
                ScanDatabase(root, context, ScanState);
                break;
            case MvDocumentKind.Animations:
                ScanDatabase(root, context, ScanAnimation);
                break;
            case MvDocumentKind.Tilesets:
                ScanDatabase(root, context, ScanTileset);
                break;
            case MvDocumentKind.CommonEvents:
                ScanDatabase(root, context, ScanCommonEvent);
                break;
            case MvDocumentKind.MapInfos:
                ScanMapInfos(root, context);
                break;
            case MvDocumentKind.System:
                ScanSystem(root, context);
                break;
            case MvDocumentKind.Map:
                ScanMap(root, context);
                break;
        }

        HeuristicReferenceScanner.ScanJsonStrings(root, context);
        return context.References.Distinct().ToArray();
    }

    private static void ScanDatabase(
        JsonElement root,
        ExtractionContext context,
        Action<JsonElement, int, string, ExtractionContext> scanner)
    {
        if (root.ValueKind != JsonValueKind.Array) return;
        foreach ((int index, JsonElement record) in root.EnumerateIndexedArray())
        {
            if (index == 0 || record.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            if (record.ValueKind != JsonValueKind.Object) continue;
            int recordId = record.GetOptionalInt32("id") ?? index;
            scanner(record, recordId, "/" + index.ToString(CultureInfo.InvariantCulture), context);
        }
    }

    private static void ScanActor(JsonElement actor, int id, string path, ExtractionContext context)
    {
        AddId(context, actor.GetOptionalInt32("classId"), ReferenceTargetKind.Class, ReferenceUsageKind.DatabaseField, path + "/classId", id);
        AddAsset(context, actor.GetOptionalString("characterName"), AssetCategory.Characters, path + "/characterName", id);
        AddAsset(context, actor.GetOptionalString("faceName"), AssetCategory.Faces, path + "/faceName", id);
        AddAsset(context, actor.GetOptionalString("battlerName"), AssetCategory.SideViewActors, path + "/battlerName", id);
        if (actor.TryGetPropertyValue("equips", out JsonElement equips))
        {
            foreach ((int index, JsonElement value) in equips.EnumerateIndexedArray())
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int equipmentId) && equipmentId > 0)
                {
                    context.Add(
                        new ReferenceSource(context.RelativePath, $"{path}/equips/{index}", RecordId: id),
                        new ReferenceTarget(ReferenceTargetKind.Equipment, equipmentId),
                        ReferenceUsageKind.DatabaseField,
                        ReferenceConfidence.Exact,
                        "Initial equipment slot");
                }
            }
        }
        ScanTraits(actor, id, path, context);
    }

    private static void ScanClass(JsonElement record, int id, string path, ExtractionContext context)
    {
        if (record.TryGetPropertyValue("learnings", out JsonElement learnings))
        {
            foreach ((int index, JsonElement learning) in learnings.EnumerateIndexedArray())
            {
                AddId(context, learning.GetOptionalInt32("skillId"), ReferenceTargetKind.Skill, ReferenceUsageKind.DatabaseField,
                    $"{path}/learnings/{index}/skillId", id);
            }
        }
        ScanTraits(record, id, path, context);
    }

    private static void ScanSkill(JsonElement record, int id, string path, ExtractionContext context)
    {
        ScanUsableItem(record, id, path, context);
        AddId(context, record.GetOptionalInt32("stypeId"), ReferenceTargetKind.SkillType, ReferenceUsageKind.DatabaseField, path + "/stypeId", id);
        AddId(context, record.GetOptionalInt32("requiredWtypeId1"), ReferenceTargetKind.WeaponType, ReferenceUsageKind.DatabaseField, path + "/requiredWtypeId1", id);
        AddId(context, record.GetOptionalInt32("requiredWtypeId2"), ReferenceTargetKind.WeaponType, ReferenceUsageKind.DatabaseField, path + "/requiredWtypeId2", id);
    }

    private static void ScanItem(JsonElement record, int id, string path, ExtractionContext context) =>
        ScanUsableItem(record, id, path, context);

    private static void ScanUsableItem(JsonElement record, int id, string path, ExtractionContext context)
    {
        AddId(context, record.GetOptionalInt32("animationId"), ReferenceTargetKind.Animation, ReferenceUsageKind.DatabaseField,
            path + "/animationId", id);
        if (record.TryGetPropertyValue("damage", out JsonElement damage))
        {
            int? elementId = damage.GetOptionalInt32("elementId");
            if (elementId is > 0)
            {
                AddId(context, elementId, ReferenceTargetKind.Element, ReferenceUsageKind.DatabaseField, path + "/damage/elementId", id);
            }
        }
        ScanEffects(record, id, path, context);
    }

    private static void ScanWeapon(JsonElement record, int id, string path, ExtractionContext context)
    {
        AddId(context, record.GetOptionalInt32("wtypeId"), ReferenceTargetKind.WeaponType, ReferenceUsageKind.DatabaseField, path + "/wtypeId", id);
        AddId(context, record.GetOptionalInt32("etypeId"), ReferenceTargetKind.EquipmentType, ReferenceUsageKind.DatabaseField, path + "/etypeId", id);
        AddId(context, record.GetOptionalInt32("animationId"), ReferenceTargetKind.Animation, ReferenceUsageKind.DatabaseField,
            path + "/animationId", id);
        ScanTraits(record, id, path, context);
    }

    private static void ScanArmor(JsonElement record, int id, string path, ExtractionContext context)
    {
        AddId(context, record.GetOptionalInt32("atypeId"), ReferenceTargetKind.ArmorType, ReferenceUsageKind.DatabaseField, path + "/atypeId", id);
        AddId(context, record.GetOptionalInt32("etypeId"), ReferenceTargetKind.EquipmentType, ReferenceUsageKind.DatabaseField, path + "/etypeId", id);
        ScanTraits(record, id, path, context);
    }

    private static void ScanEnemy(JsonElement record, int id, string path, ExtractionContext context)
    {
        string battlerName = record.GetOptionalString("battlerName") ?? string.Empty;
        AddAsset(context, battlerName, AssetCategory.Enemies, path + "/battlerName", id);
        AddAsset(context, battlerName, AssetCategory.SideViewEnemies, path + "/battlerName", id, confidence: ReferenceConfidence.Heuristic);
        if (record.TryGetPropertyValue("actions", out JsonElement actions))
        {
            foreach ((int index, JsonElement action) in actions.EnumerateIndexedArray())
            {
                AddId(context, action.GetOptionalInt32("skillId"), ReferenceTargetKind.Skill, ReferenceUsageKind.DatabaseField,
                    $"{path}/actions/{index}/skillId", id);
                int conditionType = action.GetOptionalInt32("conditionType") ?? 0;
                if (conditionType == 4)
                {
                    AddId(context, ToIntegralId(action, "conditionParam1"), ReferenceTargetKind.State, ReferenceUsageKind.DatabaseField,
                        $"{path}/actions/{index}/conditionParam1", id);
                }
                else if (conditionType == 6)
                {
                    AddId(context, ToIntegralId(action, "conditionParam1"), ReferenceTargetKind.Switch, ReferenceUsageKind.DatabaseField,
                        $"{path}/actions/{index}/conditionParam1", id);
                }
            }
        }
        if (record.TryGetPropertyValue("dropItems", out JsonElement drops))
        {
            foreach ((int index, JsonElement drop) in drops.EnumerateIndexedArray())
            {
                int kind = drop.GetOptionalInt32("kind") ?? 0;
                ReferenceTargetKind targetKind = kind switch
                {
                    1 => ReferenceTargetKind.Item,
                    2 => ReferenceTargetKind.Weapon,
                    3 => ReferenceTargetKind.Armor,
                    _ => ReferenceTargetKind.Unknown
                };
                if (targetKind != ReferenceTargetKind.Unknown)
                {
                    AddId(context, drop.GetOptionalInt32("dataId"), targetKind, ReferenceUsageKind.DatabaseField,
                        $"{path}/dropItems/{index}/dataId", id);
                }
            }
        }
        ScanTraits(record, id, path, context);
    }

    private static void ScanTroop(JsonElement record, int id, string path, ExtractionContext context)
    {
        if (record.TryGetPropertyValue("members", out JsonElement members))
        {
            foreach ((int index, JsonElement member) in members.EnumerateIndexedArray())
            {
                AddId(context, member.GetOptionalInt32("enemyId"), ReferenceTargetKind.Enemy, ReferenceUsageKind.DatabaseField,
                    $"{path}/members/{index}/enemyId", id);
            }
        }
        if (record.TryGetPropertyValue("pages", out JsonElement pages))
        {
            foreach ((int pageIndex, JsonElement page) in pages.EnumerateIndexedArray())
            {
                string pagePath = $"{path}/pages/{pageIndex}";
                if (page.TryGetPropertyValue("conditions", out JsonElement conditions))
                {
                    if (conditions.GetOptionalBoolean("actorValid"))
                    {
                        AddId(context, conditions.GetOptionalInt32("actorId"), ReferenceTargetKind.Actor,
                            ReferenceUsageKind.TroopPageCondition, pagePath + "/conditions/actorId", id, pageIndex: pageIndex);
                    }
                    if (conditions.GetOptionalBoolean("switchValid"))
                    {
                        AddId(context, conditions.GetOptionalInt32("switchId"), ReferenceTargetKind.Switch,
                            ReferenceUsageKind.TroopPageCondition, pagePath + "/conditions/switchId", id, pageIndex: pageIndex);
                    }
                    if (conditions.GetOptionalBoolean("enemyValid"))
                    {
                        int? memberIndex = conditions.GetOptionalInt32("enemyIndex");
                        if (memberIndex is >= 0)
                        {
                            context.Add(
                                new ReferenceSource(context.RelativePath, pagePath + "/conditions/enemyIndex", RecordId: id, PageIndex: pageIndex),
                                new ReferenceTarget(ReferenceTargetKind.TroopMember, memberIndex, $"Troop {id} member {memberIndex + 1}"),
                                ReferenceUsageKind.TroopMemberIndex,
                                ReferenceConfidence.Exact,
                                "Troop page enemy HP condition");
                        }
                    }
                }
                if (page.TryGetPropertyValue("list", out JsonElement list))
                {
                    ScanEventCommands(list, id, null, pageIndex, pagePath + "/list", context);
                }
            }
        }
    }

    private static int? ToIntegralId(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.Number) return null;
        if (property.TryGetInt32(out int integer)) return integer;
        return property.TryGetDouble(out double number) ? (int)Math.Round(number) : null;
    }

    private static void ScanState(JsonElement record, int id, string path, ExtractionContext context) =>
        ScanTraits(record, id, path, context);

    private static void ScanAnimation(JsonElement record, int id, string path, ExtractionContext context)
    {
        AddAsset(context, record.GetOptionalString("animation1Name"), AssetCategory.Animations, path + "/animation1Name", id);
        AddAsset(context, record.GetOptionalString("animation2Name"), AssetCategory.Animations, path + "/animation2Name", id);
        if (record.TryGetPropertyValue("timings", out JsonElement timings))
        {
            foreach ((int index, JsonElement timing) in timings.EnumerateIndexedArray())
            {
                if (timing.TryGetPropertyValue("se", out JsonElement se))
                {
                    AddAsset(context, se.GetOptionalString("name"), AssetCategory.SoundEffects,
                        $"{path}/timings/{index}/se/name", id);
                }
            }
        }
    }

    private static void ScanTileset(JsonElement record, int id, string path, ExtractionContext context)
    {
        if (!record.TryGetPropertyValue("tilesetNames", out JsonElement names)) return;
        foreach ((int index, JsonElement value) in names.EnumerateIndexedArray())
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                AddAsset(context, value.GetString(), AssetCategory.Tilesets, $"{path}/tilesetNames/{index}", id);
            }
        }
    }

    private static void ScanCommonEvent(JsonElement record, int id, string path, ExtractionContext context)
    {
        int trigger = record.GetOptionalInt32("trigger") ?? 0;
        if (trigger > 0)
        {
            AddId(context, record.GetOptionalInt32("switchId"), ReferenceTargetKind.Switch, ReferenceUsageKind.DatabaseField,
                path + "/switchId", id);
        }
        if (record.TryGetPropertyValue("list", out JsonElement list))
        {
            ScanEventCommands(list, id, null, null, path + "/list", context);
        }
    }

    private static void ScanMapInfos(JsonElement root, ExtractionContext context)
    {
        if (root.ValueKind != JsonValueKind.Array) return;
        foreach ((int index, JsonElement record) in root.EnumerateIndexedArray())
        {
            if (index == 0 || record.ValueKind != JsonValueKind.Object) continue;
            int id = record.GetOptionalInt32("id") ?? index;
            AddId(context, record.GetOptionalInt32("parentId"), ReferenceTargetKind.Map, ReferenceUsageKind.DatabaseField,
                $"/{index}/parentId", id);
        }
    }

    private static void ScanSystem(JsonElement root, ExtractionContext context)
    {
        if (root.ValueKind != JsonValueKind.Object) return;
        AddId(context, root.GetOptionalInt32("startMapId"), ReferenceTargetKind.Map, ReferenceUsageKind.SystemSetting,
            "/startMapId", null);
        ScanVehicle(root, "boat", context);
        ScanVehicle(root, "ship", context);
        ScanVehicle(root, "airship", context);

        if (root.TryGetPropertyValue("partyMembers", out JsonElement partyMembers))
        {
            foreach ((int index, JsonElement value) in partyMembers.EnumerateIndexedArray())
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int actorId))
                {
                    AddId(context, actorId, ReferenceTargetKind.Actor, ReferenceUsageKind.SystemSetting,
                        $"/partyMembers/{index}", null);
                }
            }
        }

        AddId(context, root.GetOptionalInt32("testTroopId"), ReferenceTargetKind.Troop, ReferenceUsageKind.SystemSetting,
            "/testTroopId", null);

        if (root.TryGetPropertyValue("magicSkills", out JsonElement magicSkills))
        {
            foreach ((int index, JsonElement value) in magicSkills.EnumerateIndexedArray())
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int skillTypeId))
                {
                    AddId(context, skillTypeId, ReferenceTargetKind.SkillType, ReferenceUsageKind.SystemSetting,
                        $"/magicSkills/{index}", null);
                }
            }
        }

        if (root.TryGetPropertyValue("testBattlers", out JsonElement testBattlers))
        {
            foreach ((int index, JsonElement battler) in testBattlers.EnumerateIndexedArray())
            {
                AddId(context, battler.GetOptionalInt32("actorId"), ReferenceTargetKind.Actor, ReferenceUsageKind.SystemSetting,
                    $"/testBattlers/{index}/actorId", null);
                if (battler.TryGetPropertyValue("equips", out JsonElement equips))
                {
                    foreach ((int equipIndex, JsonElement value) in equips.EnumerateIndexedArray())
                    {
                        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int equipmentId) && equipmentId > 0)
                        {
                            ReferenceTargetKind equipmentKind = equipIndex == 0 ? ReferenceTargetKind.Weapon : ReferenceTargetKind.Armor;
                            context.Add(
                                new ReferenceSource(context.RelativePath, $"/testBattlers/{index}/equips/{equipIndex}"),
                                new ReferenceTarget(equipmentKind, equipmentId),
                                ReferenceUsageKind.SystemSetting,
                                ReferenceConfidence.Exact,
                                equipIndex == 0 ? "Battle-test weapon" : "Battle-test armor");
                        }
                    }
                }
            }
        }

        ScanAudio(root, "titleBgm", AssetCategory.BackgroundMusic, context);
        ScanAudio(root, "battleBgm", AssetCategory.BackgroundMusic, context);
        ScanAudio(root, "victoryMe", AssetCategory.MusicEffects, context);
        ScanAudio(root, "defeatMe", AssetCategory.MusicEffects, context);
        ScanAudio(root, "gameoverMe", AssetCategory.MusicEffects, context);
        if (root.TryGetPropertyValue("sounds", out JsonElement sounds))
        {
            foreach ((int index, JsonElement sound) in sounds.EnumerateIndexedArray())
            {
                AddAsset(context, sound.GetOptionalString("name"), AssetCategory.SoundEffects, $"/sounds/{index}/name", null);
            }
        }
        AddAsset(context, root.GetOptionalString("title1Name"), AssetCategory.Titles1, "/title1Name", null);
        AddAsset(context, root.GetOptionalString("title2Name"), AssetCategory.Titles2, "/title2Name", null);
        AddAsset(context, root.GetOptionalString("battleback1Name"), AssetCategory.Battlebacks1, "/battleback1Name", null);
        AddAsset(context, root.GetOptionalString("battleback2Name"), AssetCategory.Battlebacks2, "/battleback2Name", null);
        AddAsset(context, root.GetOptionalString("battlerName"), AssetCategory.SideViewEnemies, "/battlerName", null);
    }

    private static void ScanVehicle(JsonElement root, string property, ExtractionContext context)
    {
        if (!root.TryGetPropertyValue(property, out JsonElement vehicle)) return;
        AddId(context, vehicle.GetOptionalInt32("startMapId"), ReferenceTargetKind.Map, ReferenceUsageKind.SystemSetting,
            $"/{property}/startMapId", null);
        AddAsset(context, vehicle.GetOptionalString("characterName"), AssetCategory.Characters,
            $"/{property}/characterName", null);
        if (vehicle.TryGetPropertyValue("bgm", out JsonElement bgm))
        {
            AddAsset(context, bgm.GetOptionalString("name"), AssetCategory.BackgroundMusic,
                $"/{property}/bgm/name", null);
        }
    }

    private static void ScanAudio(JsonElement root, string property, AssetCategory category, ExtractionContext context)
    {
        if (root.TryGetPropertyValue(property, out JsonElement audio))
        {
            AddAsset(context, audio.GetOptionalString("name"), category, $"/{property}/name", null);
        }
    }

    private static void ScanMap(JsonElement root, ExtractionContext context)
    {
        if (root.ValueKind != JsonValueKind.Object) return;
        int? mapId = TryParseMapId(context.RelativePath);
        context.MapId = mapId;
        AddId(context, root.GetOptionalInt32("tilesetId"), ReferenceTargetKind.Tileset, ReferenceUsageKind.MapProperty,
            "/tilesetId", null, mapId: mapId);
        AddAsset(context, root.GetOptionalString("battleback1Name"), AssetCategory.Battlebacks1, "/battleback1Name", null, mapId: mapId);
        AddAsset(context, root.GetOptionalString("battleback2Name"), AssetCategory.Battlebacks2, "/battleback2Name", null, mapId: mapId);
        AddAsset(context, root.GetOptionalString("parallaxName"), AssetCategory.Parallaxes, "/parallaxName", null, mapId: mapId);
        if (root.TryGetPropertyValue("bgm", out JsonElement bgm))
        {
            AddAsset(context, bgm.GetOptionalString("name"), AssetCategory.BackgroundMusic, "/bgm/name", null, mapId: mapId);
        }
        if (root.TryGetPropertyValue("bgs", out JsonElement bgs))
        {
            AddAsset(context, bgs.GetOptionalString("name"), AssetCategory.BackgroundSounds, "/bgs/name", null, mapId: mapId);
        }
        if (root.TryGetPropertyValue("encounterList", out JsonElement encounters))
        {
            foreach ((int index, JsonElement encounter) in encounters.EnumerateIndexedArray())
            {
                AddId(context, encounter.GetOptionalInt32("troopId"), ReferenceTargetKind.Troop, ReferenceUsageKind.MapEncounter,
                    $"/encounterList/{index}/troopId", null, mapId: mapId);
            }
        }
        if (!root.TryGetPropertyValue("events", out JsonElement events) || events.ValueKind != JsonValueKind.Array) return;
        foreach ((int eventIndex, JsonElement eventRecord) in events.EnumerateIndexedArray())
        {
            if (eventIndex == 0 || eventRecord.ValueKind != JsonValueKind.Object) continue;
            int eventId = eventRecord.GetOptionalInt32("id") ?? eventIndex;
            if (!eventRecord.TryGetPropertyValue("pages", out JsonElement pages)) continue;
            foreach ((int pageIndex, JsonElement page) in pages.EnumerateIndexedArray())
            {
                string pagePath = $"/events/{eventIndex}/pages/{pageIndex}";
                if (page.TryGetPropertyValue("conditions", out JsonElement conditions))
                {
                    ScanEventPageConditions(conditions, mapId, eventId, pageIndex, pagePath + "/conditions", context);
                }
                if (page.TryGetPropertyValue("image", out JsonElement image))
                {
                    AddAsset(context, image.GetOptionalString("characterName"), AssetCategory.Characters,
                        pagePath + "/image/characterName", null, mapId, eventId, pageIndex);
                }
                if (page.TryGetPropertyValue("moveRoute", out JsonElement route))
                {
                    ScanMoveRoute(route, mapId, eventId, pageIndex, pagePath + "/moveRoute", context);
                }
                if (page.TryGetPropertyValue("list", out JsonElement list))
                {
                    ScanEventCommands(list, null, eventId, pageIndex, pagePath + "/list", context, mapId);
                }
            }
        }
    }

    private static void ScanEventPageConditions(
        JsonElement conditions,
        int? mapId,
        int eventId,
        int pageIndex,
        string path,
        ExtractionContext context)
    {
        if (conditions.GetOptionalBoolean("actorValid"))
        {
            AddId(context, conditions.GetOptionalInt32("actorId"), ReferenceTargetKind.Actor, ReferenceUsageKind.EventPageCondition,
                path + "/actorId", null, mapId, eventId, pageIndex);
        }
        if (conditions.GetOptionalBoolean("itemValid"))
        {
            AddId(context, conditions.GetOptionalInt32("itemId"), ReferenceTargetKind.Item, ReferenceUsageKind.EventPageCondition,
                path + "/itemId", null, mapId, eventId, pageIndex);
        }
        if (conditions.GetOptionalBoolean("switch1Valid"))
        {
            AddId(context, conditions.GetOptionalInt32("switch1Id"), ReferenceTargetKind.Switch, ReferenceUsageKind.EventPageCondition,
                path + "/switch1Id", null, mapId, eventId, pageIndex);
        }
        if (conditions.GetOptionalBoolean("switch2Valid"))
        {
            AddId(context, conditions.GetOptionalInt32("switch2Id"), ReferenceTargetKind.Switch, ReferenceUsageKind.EventPageCondition,
                path + "/switch2Id", null, mapId, eventId, pageIndex);
        }
        if (conditions.GetOptionalBoolean("variableValid"))
        {
            AddId(context, conditions.GetOptionalInt32("variableId"), ReferenceTargetKind.Variable, ReferenceUsageKind.EventPageCondition,
                path + "/variableId", null, mapId, eventId, pageIndex);
        }
    }

    private static void ScanMoveRoute(
        JsonElement route,
        int? mapId,
        int eventId,
        int pageIndex,
        string path,
        ExtractionContext context)
    {
        if (!route.TryGetPropertyValue("list", out JsonElement list)) return;
        foreach ((int commandIndex, JsonElement command) in list.EnumerateIndexedArray())
        {
            int code = command.GetOptionalInt32("code") ?? 0;
            if (!command.TryGetPropertyValue("parameters", out JsonElement parameters)) continue;
            string commandPath = $"{path}/list/{commandIndex}/parameters";
            if (code == 41)
            {
                AddAsset(context, GetStringParameter(parameters, 0), AssetCategory.Characters, commandPath + "/0", null,
                    mapId, eventId, pageIndex, commandIndex);
            }
            else if (code == 44 && TryGetParameter(parameters, 0, out JsonElement se))
            {
                AddAsset(context, se.GetOptionalString("name"), AssetCategory.SoundEffects, commandPath + "/0/name", null,
                    mapId, eventId, pageIndex, commandIndex);
            }
            else if (code == 45)
            {
                string? script = GetStringParameter(parameters, 0);
                HeuristicReferenceScanner.ScanText(script, new ReferenceSource(context.RelativePath, commandPath + "/0", mapId,
                    EventId: eventId, PageIndex: pageIndex, CommandIndex: commandIndex), ReferenceUsageKind.Script, context);
            }
        }
    }

    private static void ScanTraits(JsonElement record, int id, string path, ExtractionContext context)
    {
        if (!record.TryGetPropertyValue("traits", out JsonElement traits)) return;
        foreach ((int index, JsonElement trait) in traits.EnumerateIndexedArray())
        {
            int code = trait.GetOptionalInt32("code") ?? 0;
            int? dataId = trait.GetOptionalInt32("dataId");
            ReferenceTargetKind kind = code switch
            {
                11 or 31 => ReferenceTargetKind.Element,
                13 or 14 or 32 => ReferenceTargetKind.State,
                41 or 42 => ReferenceTargetKind.SkillType,
                43 or 44 => ReferenceTargetKind.Skill,
                51 => ReferenceTargetKind.WeaponType,
                52 => ReferenceTargetKind.ArmorType,
                53 or 54 => ReferenceTargetKind.EquipmentType,
                _ => ReferenceTargetKind.Unknown
            };
            if (kind != ReferenceTargetKind.Unknown)
            {
                AddId(context, dataId, kind, ReferenceUsageKind.Trait, $"{path}/traits/{index}/dataId", id);
            }
        }
    }

    private static void ScanEffects(JsonElement record, int id, string path, ExtractionContext context)
    {
        if (!record.TryGetPropertyValue("effects", out JsonElement effects)) return;
        foreach ((int index, JsonElement effect) in effects.EnumerateIndexedArray())
        {
            int code = effect.GetOptionalInt32("code") ?? 0;
            int? dataId = effect.GetOptionalInt32("dataId");
            ReferenceTargetKind kind = code switch
            {
                21 or 22 => ReferenceTargetKind.State,
                43 => ReferenceTargetKind.Skill,
                44 => ReferenceTargetKind.CommonEvent,
                _ => ReferenceTargetKind.Unknown
            };
            if (kind != ReferenceTargetKind.Unknown)
            {
                AddId(context, dataId, kind, ReferenceUsageKind.Effect, $"{path}/effects/{index}/dataId", id);
            }
        }
    }

    private static void AddId(
        ExtractionContext context,
        int? id,
        ReferenceTargetKind kind,
        ReferenceUsageKind usage,
        string path,
        int? recordId,
        int? mapId = null,
        int? eventId = null,
        int? pageIndex = null,
        int? commandIndex = null,
        ReferenceConfidence confidence = ReferenceConfidence.Exact,
        string? textContext = null)
    {
        if (!id.HasValue || id.Value <= 0) return;
        context.Add(
            new ReferenceSource(context.RelativePath, path, mapId ?? context.MapId, recordId, eventId, pageIndex, commandIndex),
            new ReferenceTarget(kind, id.Value),
            usage,
            confidence,
            textContext);
    }

    private static void AddAsset(
        ExtractionContext context,
        string? name,
        AssetCategory category,
        string path,
        int? recordId,
        int? mapId = null,
        int? eventId = null,
        int? pageIndex = null,
        int? commandIndex = null,
        ReferenceConfidence confidence = ReferenceConfidence.Exact)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        context.Add(
            new ReferenceSource(context.RelativePath, path, mapId ?? context.MapId, recordId, eventId, pageIndex, commandIndex),
            new ReferenceTarget(ReferenceTargetKind.Asset, Name: name, AssetCategory: category),
            ReferenceUsageKind.AssetName,
            confidence);
    }

    private static int? TryParseMapId(string relativePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(relativePath);
        if (fileName.StartsWith("Map", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(fileName.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out int id)) return id;
        return null;
    }

    private static bool TryGetParameter(JsonElement parameters, int index, out JsonElement value)
    {
        if (parameters.ValueKind == JsonValueKind.Array && index >= 0 && index < parameters.GetArrayLength())
        {
            value = parameters[index];
            return true;
        }
        value = default;
        return false;
    }

    private static int? GetIntParameter(JsonElement parameters, int index)
    {
        if (!TryGetParameter(parameters, index, out JsonElement value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }

    private static string? GetStringParameter(JsonElement parameters, int index)
    {
        if (!TryGetParameter(parameters, index, out JsonElement value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static void ScanEventCommands(
        JsonElement list,
        int? recordId,
        int? eventId,
        int? pageIndex,
        string path,
        ExtractionContext context,
        int? mapId = null)
    {
        if (list.ValueKind != JsonValueKind.Array) return;
        foreach ((int commandIndex, JsonElement command) in list.EnumerateIndexedArray())
        {
            int code = command.GetOptionalInt32("code") ?? 0;
            if (!command.TryGetPropertyValue("parameters", out JsonElement parameters)) continue;
            string parameterPath = $"{path}/{commandIndex}/parameters";
            EventCommandReferenceExtractor.Scan(
                code,
                parameters,
                new EventCommandContext(context, parameters, parameterPath, mapId ?? context.MapId, recordId, eventId, pageIndex, commandIndex));
        }
    }

    internal sealed class ExtractionContext
    {
        public ExtractionContext(string relativePath, MvDocumentKind kind)
        {
            RelativePath = relativePath.Replace('\\', '/');
            Kind = kind;
        }

        public string RelativePath { get; }
        public MvDocumentKind Kind { get; }
        public int? MapId { get; set; }
        public List<ProjectReference> References { get; } = [];

        public void Add(
            ReferenceSource source,
            ReferenceTarget target,
            ReferenceUsageKind usage,
            ReferenceConfidence confidence,
            string? textContext = null)
        {
            References.Add(new ProjectReference(source, target, usage, confidence, textContext));
        }
    }

    internal sealed record EventCommandContext(
        ExtractionContext Extraction,
        JsonElement Parameters,
        string ParametersPath,
        int? MapId,
        int? RecordId,
        int? EventId,
        int? PageIndex,
        int CommandIndex)
    {
        public void AddId(int parameterIndex, ReferenceTargetKind kind, string? textContext = null, ReferenceConfidence confidence = ReferenceConfidence.Exact)
        {
            int? id = GetIntParameter(Parameters, parameterIndex);
            if (id is null || id.Value <= 0) return;
            Extraction.Add(
                Source(parameterIndex),
                new ReferenceTarget(kind, id.Value),
                ReferenceUsageKind.EventCommand,
                confidence,
                textContext);
        }

        public void AddIdValue(int parameterIndex, int? id, ReferenceTargetKind kind, string? textContext = null, ReferenceConfidence confidence = ReferenceConfidence.Exact)
        {
            if (id is null || id.Value <= 0) return;
            Extraction.Add(Source(parameterIndex), new ReferenceTarget(kind, id.Value), ReferenceUsageKind.EventCommand, confidence, textContext);
        }

        public void AddAsset(int parameterIndex, string? name, AssetCategory category, string? suffix = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            Extraction.Add(
                Source(parameterIndex, suffix),
                new ReferenceTarget(ReferenceTargetKind.Asset, Name: name, AssetCategory: category),
                ReferenceUsageKind.AssetName,
                ReferenceConfidence.Exact);
        }

        public void AddPlugin(string? pluginName)
        {
            if (string.IsNullOrWhiteSpace(pluginName)) return;
            Extraction.Add(
                Source(0),
                new ReferenceTarget(ReferenceTargetKind.Plugin, Name: pluginName),
                ReferenceUsageKind.PluginCommand,
                ReferenceConfidence.Heuristic,
                pluginName);
        }

        public ReferenceSource Source(int parameterIndex, string? suffix = null) => new(
            Extraction.RelativePath,
            ParametersPath + "/" + parameterIndex.ToString(CultureInfo.InvariantCulture) + suffix,
            MapId,
            RecordId,
            EventId,
            PageIndex,
            CommandIndex);

    }
}
