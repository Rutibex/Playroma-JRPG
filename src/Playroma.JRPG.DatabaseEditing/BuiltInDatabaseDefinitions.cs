using System.Globalization;
using System.Text;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.DatabaseEditing;

public static class BuiltInDatabaseDefinitions
{
    public static DatabaseEditorRegistry CreateRegistry()
    {
        DatabaseEditorRegistry registry = new();
        registry.Register(Actor());
        registry.Register(Class());
        registry.Register(Skill());
        registry.Register(Item());
        registry.Register(Weapon());
        registry.Register(Armor());
        registry.Register(Enemy());
        registry.Register(Troop());
        registry.Register(State());
        registry.Register(Animation());
        registry.Register(Tileset());
        registry.Register(CommonEvent());
        return registry;
    }

    private static DatabaseEditorDefinition Definition(
        MvDocumentKind kind,
        string plural,
        string singular,
        params DatabaseEditorSection[] sections)
    {
        MvRecordContract contract = MvRecordContractCatalog.Get(kind);
        return new DatabaseEditorDefinition(
            kind,
            plural,
            singular,
            new DatabaseRecordSchema(sections, contract.StandardProperties.OrderBy(name => name, StringComparer.Ordinal).ToArray())
            {
                Contract = contract
            },
            id => DatabaseRecordTemplateFactory.Create(kind, id));
    }

    private static DatabaseFieldDescriptor<string> Text(string key, string label, string path, bool multiline = false) =>
        new(key, label, new JsonPath("/" + path), multiline ? DatabaseFieldValueKind.MultilineString : DatabaseFieldValueKind.String, EncodeString);

    private static DatabaseFieldDescriptor<string> Note(string key = "note", string label = "Notes", string path = "note") =>
        new(key, label, new JsonPath("/" + path), DatabaseFieldValueKind.Note, EncodeString) { SupportsBatchEditing = true };

    private static DatabaseFieldDescriptor<string> Formula(string key, string label, string path) =>
        new(key, label, new JsonPath("/" + path), DatabaseFieldValueKind.Formula, EncodeString);

    private static DatabaseFieldDescriptor<int> Number(string key, string label, string path, int min = int.MinValue, int max = int.MaxValue) =>
        new(key, label, new JsonPath("/" + path), DatabaseFieldValueKind.Integer, value => value.ToString(CultureInfo.InvariantCulture)) { Minimum = min, Maximum = max };

    private static DatabaseFieldDescriptor<bool> Boolean(string key, string label, string path) =>
        new(key, label, new JsonPath("/" + path), DatabaseFieldValueKind.Boolean, value => value == true ? "true" : "false");

    private static DatabaseFieldDescriptor<int> Reference(string key, string label, string path, MvDocumentKind target, ReferenceTargetKind targetKind, bool allowNone = true) =>
        new(key, label, new JsonPath("/" + path), DatabaseFieldValueKind.DatabaseReference, value => value.ToString(CultureInfo.InvariantCulture))
        {
            Minimum = allowNone ? 0 : 1,
            ReferenceKind = target,
            ReferenceTargetKind = targetKind,
            EmptyDisplayText = allowNone ? "None" : null
        };

    private static DatabaseFieldDescriptor<int> EnumField(string key, string label, string path, IReadOnlyDictionary<int, string> options) =>
        new(key, label, new JsonPath("/" + path), DatabaseFieldValueKind.Enum, value => value.ToString(CultureInfo.InvariantCulture)) { Options = options };

    private static DatabaseFieldDescriptor<string> Asset(string key, string label, string path, AssetCategory category) =>
        new(key, label, new JsonPath("/" + path), DatabaseFieldValueKind.AssetReference, EncodeString) { AssetCategory = category };


    private static DatabaseFieldDescriptor<string> RawArray(string key, string label, string path) =>
        new(key, label, new JsonPath("/" + path), DatabaseFieldValueKind.Array, value => value ?? "[]")
        {
            SupportsBatchEditing = false
        };

    private static DatabaseFieldDescriptor<int> Icon(string key = "iconIndex", string label = "Icon", string path = "iconIndex") =>
        new(key, label, new JsonPath("/" + path), DatabaseFieldValueKind.Icon, value => value.ToString(CultureInfo.InvariantCulture)) { Minimum = 0 };

    private static string EncodeString(string? value) => Encoding.UTF8.GetString(JsonValueEncoder.EncodeString(value ?? string.Empty));

    private static DatabaseEditorDefinition Actor() => Definition(MvDocumentKind.Actors, "Actors", "Actor",
        new("identity", "Identity",
        [
            Text("name", "Name", "name"),
            Text("nickname", "Nickname", "nickname"),
            Text("profile", "Profile", "profile", true)
        ]),
        new("progression", "Progression",
        [
            Reference("classId", "Class", "classId", MvDocumentKind.Classes, ReferenceTargetKind.Class, false),
            Number("initialLevel", "Initial Level", "initialLevel", 1, 99),
            Number("maxLevel", "Maximum Level", "maxLevel", 1, 99)
        ]),
        new("graphics", "Graphics",
        [
            Asset("faceName", "Face Sheet", "faceName", AssetCategory.Faces),
            Number("faceIndex", "Face Index", "faceIndex", 0, 7),
            Asset("characterName", "Character Sheet", "characterName", AssetCategory.Characters),
            Number("characterIndex", "Character Index", "characterIndex", 0, 7),
            Asset("battlerName", "Side-view Battler", "battlerName", AssetCategory.SideViewActors)
        ]),
        new("structured", "Structured Data",
        [
            RawArray("equips", "Initial Equipment", "equips"),
            RawArray("traits", "Traits", "traits")
        ]),
        new("notes", "Notes", [Note()]));

    private static DatabaseEditorDefinition Class() => Definition(MvDocumentKind.Classes, "Classes", "Class",
        new("general", "General", [Text("name", "Name", "name")]),
        new("progression", "Progression",
        [
            RawArray("expParams", "EXP Parameters", "expParams"),
            RawArray("params", "Parameter Curves", "params"),
            RawArray("learnings", "Skill Learnings", "learnings")
        ]),
        new("structured", "Structured Data", [RawArray("traits", "Traits", "traits")]),
        new("notes", "Notes", [Note()]));

    private static DatabaseEditorDefinition Skill() => Definition(MvDocumentKind.Skills, "Skills", "Skill",
        new("identity", "Identity",
        [
            Text("name", "Name", "name"),
            Text("description", "Description", "description", true),
            Icon()
        ]),
        new("cost", "Skill Settings",
        [
            Number("stypeId", "Skill Type", "stypeId", 1, 9999),
            Number("mpCost", "MP Cost", "mpCost", 0, 999999),
            Number("tpCost", "TP Cost", "tpCost", 0, 100),
            Reference("animationId", "Animation", "animationId", MvDocumentKind.Animations, ReferenceTargetKind.Animation)
        ]),
        new("invocation", "Invocation",
        [
            EnumField("scope", "Scope", "scope", ScopeOptions),
            EnumField("occasion", "Occasion", "occasion", OccasionOptions),
            Number("speed", "Speed", "speed", -9999, 9999),
            Number("successRate", "Success Rate", "successRate", 0, 100),
            Number("repeats", "Repeats", "repeats", 1, 99),
            Number("tpGain", "TP Gain", "tpGain", 0, 100),
            EnumField("hitType", "Hit Type", "hitType", HitTypeOptions)
        ]),
        new("damage", "Damage",
        [
            EnumField("damage.type", "Damage Type", "damage/type", DamageTypeOptions),
            Number("damage.elementId", "Element", "damage/elementId", -1, 9999),
            Formula("damage.formula", "Formula", "damage/formula"),
            Number("damage.variance", "Variance", "damage/variance", 0, 100),
            Boolean("damage.critical", "Critical", "damage/critical")
        ]),
        new("messages", "Messages and Requirements",
        [
            Text("message1", "Message 1", "message1"),
            Text("message2", "Message 2", "message2"),
            Number("requiredWtypeId1", "Required Weapon Type 1", "requiredWtypeId1", 0, 9999),
            Number("requiredWtypeId2", "Required Weapon Type 2", "requiredWtypeId2", 0, 9999)
        ]),
        new("structured", "Effects", [RawArray("effects", "Effects", "effects")]),
        new("notes", "Notes", [Note()]));

    private static DatabaseEditorDefinition Item() => Definition(MvDocumentKind.Items, "Items", "Item",
        new("identity", "Identity",
        [
            Text("name", "Name", "name"),
            Text("description", "Description", "description", true),
            Icon(),
            Number("itypeId", "Item Type", "itypeId", 1, 2),
            Number("price", "Price", "price", 0, 99999999),
            Boolean("consumable", "Consumable", "consumable")
        ]),
        new("invocation", "Invocation",
        [
            EnumField("scope", "Scope", "scope", ScopeOptions),
            EnumField("occasion", "Occasion", "occasion", OccasionOptions),
            Number("speed", "Speed", "speed", -9999, 9999),
            Number("successRate", "Success Rate", "successRate", 0, 100),
            Number("repeats", "Repeats", "repeats", 1, 99),
            Number("tpGain", "TP Gain", "tpGain", 0, 100),
            EnumField("hitType", "Hit Type", "hitType", HitTypeOptions),
            Reference("animationId", "Animation", "animationId", MvDocumentKind.Animations, ReferenceTargetKind.Animation)
        ]),
        new("damage", "Damage",
        [
            EnumField("damage.type", "Damage Type", "damage/type", DamageTypeOptions),
            Number("damage.elementId", "Element", "damage/elementId", -1, 9999),
            Formula("damage.formula", "Formula", "damage/formula"),
            Number("damage.variance", "Variance", "damage/variance", 0, 100),
            Boolean("damage.critical", "Critical", "damage/critical")
        ]),
        new("structured", "Effects", [RawArray("effects", "Effects", "effects")]),
        new("notes", "Notes", [Note()]));

    private static DatabaseEditorDefinition Weapon() => Definition(MvDocumentKind.Weapons, "Weapons", "Weapon",
        new("general", "General",
        [
            Text("name", "Name", "name"),
            Text("description", "Description", "description", true),
            Icon(),
            Number("price", "Price", "price", 0, 99999999),
            Number("etypeId", "Equipment Type", "etypeId", 1, 9999),
            Number("wtypeId", "Weapon Type", "wtypeId", 1, 9999),
            Reference("animationId", "Animation", "animationId", MvDocumentKind.Animations, ReferenceTargetKind.Animation)
        ]),
        new("structured", "Parameters and Traits", [RawArray("params", "Parameters", "params"), RawArray("traits", "Traits", "traits")]),
        new("notes", "Notes", [Note()]));

    private static DatabaseEditorDefinition Armor() => Definition(MvDocumentKind.Armors, "Armors", "Armor",
        new("general", "General",
        [
            Text("name", "Name", "name"),
            Text("description", "Description", "description", true),
            Icon(),
            Number("price", "Price", "price", 0, 99999999),
            Number("etypeId", "Equipment Type", "etypeId", 1, 9999),
            Number("atypeId", "Armor Type", "atypeId", 1, 9999)
        ]),
        new("structured", "Parameters and Traits", [RawArray("params", "Parameters", "params"), RawArray("traits", "Traits", "traits")]),
        new("notes", "Notes", [Note()]));

    private static DatabaseEditorDefinition Enemy() => Definition(MvDocumentKind.Enemies, "Enemies", "Enemy",
        new("general", "General", [Text("name", "Name", "name"), Asset("battlerName", "Battler", "battlerName", AssetCategory.Enemies), Number("battlerHue", "Hue", "battlerHue", 0, 360), Number("exp", "Experience", "exp", 0, int.MaxValue), Number("gold", "Gold", "gold", 0, int.MaxValue)]),
        new("notes", "Notes", [Note()]));

    private static DatabaseEditorDefinition Troop() => Definition(MvDocumentKind.Troops, "Troops", "Troop", new DatabaseEditorSection("general", "General", [Text("name", "Name", "name")]));
    private static DatabaseEditorDefinition Animation() => Definition(MvDocumentKind.Animations, "Animations", "Animation", new DatabaseEditorSection("general", "General", [Text("name", "Name", "name"), Asset("animation1Name", "Sheet 1", "animation1Name", AssetCategory.Animations), Asset("animation2Name", "Sheet 2", "animation2Name", AssetCategory.Animations), EnumField("position", "Position", "position", new Dictionary<int, string> { [0] = "Head", [1] = "Center", [2] = "Feet", [3] = "Screen" })]));
    private static DatabaseEditorDefinition Tileset() => Definition(MvDocumentKind.Tilesets, "Tilesets", "Tileset", new DatabaseEditorSection("general", "General", [Text("name", "Name", "name"), EnumField("mode", "Mode", "mode", new Dictionary<int, string> { [0] = "World", [1] = "Area" }), Note()]));
    private static DatabaseEditorDefinition CommonEvent() => Definition(MvDocumentKind.CommonEvents, "Common Events", "Common Event", new DatabaseEditorSection("general", "General", [Text("name", "Name", "name"), EnumField("trigger", "Trigger", "trigger", new Dictionary<int, string> { [0] = "None", [1] = "Autorun", [2] = "Parallel" }), Number("switchId", "Condition Switch", "switchId", 1, 999999)]));

    private static DatabaseEditorDefinition State() => Definition(MvDocumentKind.States, "States", "State",
        new("general", "General",
        [
            Text("name", "Name", "name"),
            Icon(),
            Number("priority", "Priority", "priority", 0, 100),
            EnumField("restriction", "Restriction", "restriction", StateRestrictionOptions),
            Number("motion", "Side-view Motion", "motion", 0, 3),
            Number("overlay", "Side-view Overlay", "overlay", 0, 10)
        ]),
        new("removal", "Removal",
        [
            Boolean("removeAtBattleEnd", "Remove at Battle End", "removeAtBattleEnd"),
            Boolean("removeByRestriction", "Remove by Restriction", "removeByRestriction"),
            EnumField("autoRemovalTiming", "Auto-removal Timing", "autoRemovalTiming", AutoRemovalTimingOptions),
            Number("minTurns", "Minimum Turns", "minTurns", 0, 999999),
            Number("maxTurns", "Maximum Turns", "maxTurns", 0, 999999),
            Boolean("removeByDamage", "Remove by Damage", "removeByDamage"),
            Number("chanceByDamage", "Damage Chance", "chanceByDamage", 0, 100),
            Boolean("removeByWalking", "Remove by Walking", "removeByWalking"),
            Number("stepsToRemove", "Steps", "stepsToRemove", 0, 999999)
        ]),
        new("messages", "Messages",
        [
            Text("message1", "Actor Infliction", "message1"),
            Text("message2", "Enemy Infliction", "message2"),
            Text("message3", "Persistence", "message3"),
            Text("message4", "Removal", "message4")
        ]),
        new("structured", "Traits", [RawArray("traits", "Traits", "traits")]),
        new("notes", "Notes", [Note()]));

    public static IReadOnlyDictionary<int, string> ScopeOptions { get; } = new Dictionary<int, string>
    {
        [0] = "None", [1] = "One Enemy", [2] = "All Enemies", [3] = "1 Random Enemy", [4] = "2 Random Enemies", [5] = "3 Random Enemies", [6] = "4 Random Enemies", [7] = "One Ally", [8] = "All Allies", [9] = "One Ally (Dead)", [10] = "All Allies (Dead)", [11] = "User"
    };
    public static IReadOnlyDictionary<int, string> OccasionOptions { get; } = new Dictionary<int, string> { [0] = "Always", [1] = "Battle", [2] = "Menu", [3] = "Never" };
    public static IReadOnlyDictionary<int, string> HitTypeOptions { get; } = new Dictionary<int, string> { [0] = "Certain Hit", [1] = "Physical", [2] = "Magical" };
    public static IReadOnlyDictionary<int, string> DamageTypeOptions { get; } = new Dictionary<int, string> { [0] = "None", [1] = "HP Damage", [2] = "MP Damage", [3] = "HP Recovery", [4] = "MP Recovery", [5] = "HP Drain", [6] = "MP Drain" };

    public static IReadOnlyDictionary<int, string> StateRestrictionOptions { get; } = new Dictionary<int, string>
    {
        [0] = "None", [1] = "Attack Enemy", [2] = "Attack Anyone", [3] = "Attack Ally", [4] = "Cannot Move"
    };
    public static IReadOnlyDictionary<int, string> AutoRemovalTimingOptions { get; } = new Dictionary<int, string>
    {
        [0] = "None", [1] = "Action End", [2] = "Turn End"
    };
}
