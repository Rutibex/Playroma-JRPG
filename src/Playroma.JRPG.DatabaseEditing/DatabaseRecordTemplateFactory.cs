using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.DatabaseEditing;

public static class DatabaseRecordTemplateFactory
{
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    public static string Create(MvDocumentKind kind, int id)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        JsonObject record = kind switch
        {
            MvDocumentKind.Actors => Actor(id),
            MvDocumentKind.Classes => Class(id),
            MvDocumentKind.Skills => Skill(id),
            MvDocumentKind.Items => Item(id),
            MvDocumentKind.Weapons => Weapon(id),
            MvDocumentKind.Armors => Armor(id),
            MvDocumentKind.Enemies => Enemy(id),
            MvDocumentKind.Troops => Troop(id),
            MvDocumentKind.States => State(id),
            MvDocumentKind.Animations => Animation(id),
            MvDocumentKind.Tilesets => Tileset(id),
            MvDocumentKind.CommonEvents => CommonEvent(id),
            _ => Basic(id)
        };
        return record.ToJsonString(Compact);
    }

    public static string DuplicateWithId(string sourceRawJson, int destinationId)
    {
        if (destinationId <= 0) throw new ArgumentOutOfRangeException(nameof(destinationId));
        JsonNode? node = JsonNode.Parse(sourceRawJson);
        if (node is not JsonObject record) throw new InvalidDataException("A database record must be a JSON object.");
        record["id"] = destinationId;
        return record.ToJsonString(Compact);
    }

    private static JsonObject Basic(int id) => new()
    {
        ["id"] = id,
        ["name"] = $"Record {id:D4}",
        ["note"] = ""
    };

    private static JsonArray Params8(int value = 0) => new(value, value, value, value, value, value, value, value);
    private static JsonObject Audio() => new() { ["name"] = "", ["pan"] = 0, ["pitch"] = 100, ["volume"] = 90 };
    private static JsonObject Damage() => new() { ["critical"] = false, ["elementId"] = 0, ["formula"] = "0", ["type"] = 0, ["variance"] = 20 };

    private static JsonObject Actor(int id) => new()
    {
        ["id"] = id,
        ["battlerName"] = "",
        ["characterIndex"] = 0,
        ["characterName"] = "",
        ["classId"] = 1,
        ["equips"] = new JsonArray(0, 0, 0, 0, 0),
        ["faceIndex"] = 0,
        ["faceName"] = "",
        ["traits"] = new JsonArray(),
        ["initialLevel"] = 1,
        ["maxLevel"] = 99,
        ["name"] = $"Actor {id:D4}",
        ["nickname"] = "",
        ["note"] = "",
        ["profile"] = ""
    };

    private static JsonObject Class(int id)
    {
        JsonArray parameters = new();
        for (int parameter = 0; parameter < 8; parameter++)
        {
            JsonArray curve = new();
            for (int level = 0; level < 100; level++) curve.Add(parameter == 0 ? 400 + level * 10 : parameter == 1 ? 80 + level * 2 : 10 + level);
            parameters.Add(curve);
        }
        return new JsonObject
        {
            ["id"] = id,
            ["expParams"] = new JsonArray(30, 20, 30, 30),
            ["traits"] = new JsonArray(),
            ["learnings"] = new JsonArray(),
            ["name"] = $"Class {id:D4}",
            ["note"] = "",
            ["params"] = parameters
        };
    }

    private static JsonObject Skill(int id) => new()
    {
        ["id"] = id,
        ["animationId"] = 0,
        ["damage"] = Damage(),
        ["description"] = "",
        ["effects"] = new JsonArray(),
        ["hitType"] = 0,
        ["iconIndex"] = 0,
        ["message1"] = "",
        ["message2"] = "",
        ["mpCost"] = 0,
        ["name"] = $"Skill {id:D4}",
        ["note"] = "",
        ["occasion"] = 0,
        ["repeats"] = 1,
        ["requiredWtypeId1"] = 0,
        ["requiredWtypeId2"] = 0,
        ["scope"] = 0,
        ["speed"] = 0,
        ["stypeId"] = 1,
        ["successRate"] = 100,
        ["tpCost"] = 0,
        ["tpGain"] = 0
    };

    private static JsonObject Item(int id)
    {
        JsonObject result = Skill(id);
        result["name"] = $"Item {id:D4}";
        result.Remove("mpCost");
        result.Remove("tpCost");
        result.Remove("stypeId");
        result.Remove("requiredWtypeId1");
        result.Remove("requiredWtypeId2");
        result["consumable"] = true;
        result["itypeId"] = 1;
        result["price"] = 0;
        return result;
    }

    private static JsonObject Weapon(int id) => new()
    {
        ["id"] = id,
        ["animationId"] = 0,
        ["description"] = "",
        ["etypeId"] = 1,
        ["traits"] = new JsonArray(),
        ["iconIndex"] = 0,
        ["name"] = $"Weapon {id:D4}",
        ["note"] = "",
        ["params"] = Params8(),
        ["price"] = 0,
        ["wtypeId"] = 1
    };

    private static JsonObject Armor(int id)
    {
        JsonObject result = Weapon(id);
        result["name"] = $"Armor {id:D4}";
        result.Remove("animationId");
        result.Remove("wtypeId");
        result["atypeId"] = 1;
        result["etypeId"] = 2;
        return result;
    }

    private static JsonObject Enemy(int id) => new()
    {
        ["id"] = id,
        ["actions"] = new JsonArray(
            new JsonObject
            {
                ["skillId"] = 1,
                ["conditionType"] = 0,
                ["conditionParam1"] = 0,
                ["conditionParam2"] = 0,
                ["rating"] = 5
            }),
        ["battlerHue"] = 0,
        ["battlerName"] = "",
        ["dropItems"] = new JsonArray(
            new JsonObject { ["dataId"] = 1, ["denominator"] = 1, ["kind"] = 0 },
            new JsonObject { ["dataId"] = 1, ["denominator"] = 1, ["kind"] = 0 },
            new JsonObject { ["dataId"] = 1, ["denominator"] = 1, ["kind"] = 0 }),
        ["exp"] = 0,
        ["traits"] = new JsonArray(),
        ["gold"] = 0,
        ["name"] = $"Enemy {id:D4}",
        ["note"] = "",
        ["params"] = new JsonArray(100, 0, 10, 10, 10, 10, 10, 10)
    };

    private static JsonObject Troop(int id) => new()
    {
        ["id"] = id,
        ["members"] = new JsonArray(),
        ["name"] = $"Troop {id:D4}",
        ["pages"] = new JsonArray(
            new JsonObject
            {
                ["conditions"] = new JsonObject
                {
                    ["actorHp"] = 50,
                    ["actorId"] = 1,
                    ["actorValid"] = false,
                    ["enemyHp"] = 50,
                    ["enemyIndex"] = 0,
                    ["enemyValid"] = false,
                    ["switchId"] = 1,
                    ["switchValid"] = false,
                    ["turnA"] = 0,
                    ["turnB"] = 0,
                    ["turnEnding"] = false,
                    ["turnValid"] = false
                },
                ["list"] = new JsonArray(
                    new JsonObject
                    {
                        ["code"] = 0,
                        ["indent"] = 0,
                        ["parameters"] = new JsonArray()
                    }),
                ["span"] = 0
            })
    };

    private static JsonObject State(int id) => new()
    {
        ["id"] = id,
        ["autoRemovalTiming"] = 0,
        ["chanceByDamage"] = 100,
        ["iconIndex"] = 0,
        ["maxTurns"] = 1,
        ["message1"] = "",
        ["message2"] = "",
        ["message3"] = "",
        ["message4"] = "",
        ["minTurns"] = 1,
        ["motion"] = 0,
        ["name"] = $"State {id:D4}",
        ["note"] = "",
        ["overlay"] = 0,
        ["priority"] = 50,
        ["removeAtBattleEnd"] = false,
        ["removeByDamage"] = false,
        ["removeByRestriction"] = false,
        ["removeByWalking"] = false,
        ["restriction"] = 0,
        ["stepsToRemove"] = 100,
        ["traits"] = new JsonArray()
    };

    private static JsonObject Animation(int id) => new()
    {
        ["id"] = id,
        ["animation1Hue"] = 0,
        ["animation1Name"] = "",
        ["animation2Hue"] = 0,
        ["animation2Name"] = "",
        ["frames"] = new JsonArray(new JsonArray(Enumerable.Range(0, 16).Select(_ => (JsonNode?)new JsonArray(-1, 0, 0, 100, 0, false, 255, 0)).ToArray())),
        ["name"] = $"Animation {id:D4}",
        ["position"] = 1,
        ["timings"] = new JsonArray()
    };

    private static JsonObject Tileset(int id) => new()
    {
        ["id"] = id,
        ["flags"] = new JsonArray(Enumerable.Range(0, 8192).Select(_ => (JsonNode?)JsonValue.Create(0)).ToArray()),
        ["mode"] = 0,
        ["name"] = $"Tileset {id:D4}",
        ["note"] = "",
        ["tilesetNames"] = new JsonArray("", "", "", "", "", "", "", "", "")
    };

    private static JsonObject CommonEvent(int id) => new()
    {
        ["id"] = id,
        ["list"] = new JsonArray(new JsonObject { ["code"] = 0, ["indent"] = 0, ["parameters"] = new JsonArray() }),
        ["name"] = $"Common Event {id:D4}",
        ["switchId"] = 1,
        ["trigger"] = 0
    };
}
