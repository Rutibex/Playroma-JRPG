namespace Playroma.Text.RpgMakerMV;

public sealed record MvTextFieldSpec(
    string Key,
    string SemanticField,
    string DisplayName,
    string JsonPath,
    bool Multiline = false,
    bool IsNote = false,
    int? SuggestedMaximumLength = null,
    IReadOnlyList<string>? RecommendedRecipeIds = null);

/// <summary>
/// RPG Maker MV text-bearing database fields. This catalogue intentionally contains only text
/// semantics; the Playroma host remains authoritative for reading, validating and mutating JSON.
/// </summary>
public static class MvDatabaseTextTargetCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<MvTextFieldSpec>> Specs =
        new Dictionary<string, IReadOnlyList<MvTextFieldSpec>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Actors"] =
            [
                F("name", "name", "Name", "/name", false, false, 64, "phase8.mv.actor-text", "starter.names.person-full", "starter.fantasy.person-name"),
                F("nickname", "nickname", "Nickname", "/nickname", false, false, 64, "phase8.mv.actor-text", "starter.fantasy.person-name"),
                F("profile", "profile", "Profile", "/profile", true, false, 500, "phase8.mv.actor-text", "phase6.semantic.creature-description", "phase5.markov.item-lore"),
                F("note", "note", "Note", "/note", true, true, null, "phase6.semantic.item-lore", "phase5.markov.eldritch-note")
            ],
            ["Classes"] =
            [
                F("name", "name", "Name", "/name", false, false, 64, "phase8.mv.class-text", "starter.names.person-given", "starter.fantasy.faction-name"),
                F("note", "note", "Note", "/note", true, true, null, "phase8.mv.class-text", "phase6.semantic.item-lore")
            ],
            ["Skills"] =
            [
                F("name", "name", "Name", "/name", false, false, 64, "phase8.mv.skill-text", "starter.names.spell", "starter.fantasy.spell-name"),
                F("description", "description", "Description", "/description", true, false, 300, "phase8.mv.skill-text", "phase6.semantic.spell-invocation", "starter.fantasy.item-description"),
                F("message1", "message1", "Message 1", "/message1", false, false, 160, "phase8.mv.skill-text", "phase6.semantic.spell-invocation"),
                F("message2", "message2", "Message 2", "/message2", false, false, 160, "phase8.mv.skill-text", "phase6.semantic.spell-invocation"),
                F("note", "note", "Note", "/note", true, true, null, "phase6.semantic.item-lore")
            ],
            ["Items"] = BasicItem("item", "phase8.mv.item-text", "starter.fantasy.key-item-name"),
            ["Weapons"] = BasicItem("weapon", "phase8.mv.weapon-text", "starter.names.weapon", "starter.fantasy.weapon-name"),
            ["Armors"] = BasicItem("armor", "phase8.mv.armor-text", "starter.names.armor", "starter.fantasy.armor-name"),
            ["Enemies"] =
            [
                F("name", "name", "Name", "/name", false, false, 64, "phase8.mv.enemy-text", "starter.names.monster", "starter.fantasy.monster-name"),
                F("note", "note", "Note", "/note", true, true, null, "phase8.mv.enemy-text", "phase6.semantic.creature-description", "phase6.semantic.item-lore")
            ],
            ["Troops"] = [F("name", "name", "Name", "/name", false, false, 80, "phase8.mv.troop-text", "starter.fantasy.monster-name")],
            ["States"] =
            [
                F("name", "name", "Name", "/name", false, false, 64, "phase8.mv.state-text", "starter.fantasy.spell-name"),
                F("message1", "message1", "Actor Infliction", "/message1", false, false, 160, "phase8.mv.state-text", "phase6.semantic.npc-greeting"),
                F("message2", "message2", "Enemy Infliction", "/message2", false, false, 160, "phase8.mv.state-text", "phase6.semantic.npc-greeting"),
                F("message3", "message3", "Persistence", "/message3", false, false, 160, "phase8.mv.state-text", "phase6.semantic.npc-greeting"),
                F("message4", "message4", "Removal", "/message4", false, false, 160, "phase8.mv.state-text", "phase6.semantic.npc-greeting"),
                F("note", "note", "Note", "/note", true, true, null, "phase6.semantic.item-lore")
            ],
            ["Animations"] = [F("name", "name", "Name", "/name", false, false, 80, "phase8.mv.animation-text", "starter.fantasy.spell-name")],
            ["Tilesets"] =
            [
                F("name", "name", "Name", "/name", false, false, 80, "phase8.mv.tileset-text", "starter.names.region", "starter.fantasy.settlement-name"),
                F("note", "note", "Note", "/note", true, true, null, "phase8.mv.tileset-text", "phase6.semantic.item-lore")
            ],
            ["CommonEvents"] = [F("name", "name", "Name", "/name", false, false, 100, "phase8.mv.common-event-text", "phase6.semantic.quest-hook", "starter.fantasy.event-hook")]
        };

    public static IReadOnlyList<string> SupportedKinds => Specs.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<MvTextFieldSpec> ForKind(string kind) =>
        Specs.TryGetValue(kind, out var fields) ? fields : [];

    public static bool SupportsKind(string kind) => Specs.ContainsKey(kind);

    public static MvTextFieldSpec? Find(string kind, string? fieldKeyOrSemantic)
    {
        if (string.IsNullOrWhiteSpace(fieldKeyOrSemantic)) return ForKind(kind).FirstOrDefault();
        return ForKind(kind).FirstOrDefault(field =>
            field.Key.Equals(fieldKeyOrSemantic, StringComparison.OrdinalIgnoreCase)
            || field.SemanticField.Equals(fieldKeyOrSemantic, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<MvTextFieldSpec> BasicItem(string kind, params string[] nameRecipes) =>
    [
        F("name", "name", "Name", "/name", false, false, 64, nameRecipes),
        F("description", "description", "Description", "/description", true, false, 300, nameRecipes.Concat(["starter.fantasy.item-description", "phase6.semantic.item-lore"]).ToArray()),
        F("note", "note", "Note", "/note", true, true, null, nameRecipes.Where(id => !id.StartsWith("phase8.mv.", StringComparison.OrdinalIgnoreCase)).Concat(["phase6.semantic.item-lore", "phase5.markov.item-lore"]).ToArray())
    ];

    private static MvTextFieldSpec F(string key, string semantic, string display, string path, bool multiline, bool note, int? max, params string[] recipes) =>
        new(key, semantic, display, path, multiline, note, max, recipes);
}
