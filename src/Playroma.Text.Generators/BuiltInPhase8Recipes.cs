using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Stages;

namespace Playroma.Text.Generators;

/// <summary>
/// RPG Maker MV database-oriented bundles. Each recipe deliberately emits semantic field names
/// that Phase 8 target adapters can map to several database properties in one preview/transaction.
/// </summary>
public static class BuiltInPhase8Recipes
{
    public static IReadOnlyList<TextRecipe> All { get; } =
    [
        CompleteActorText(), CompleteClassText(), CompleteSkillText(), CompleteItemText(), CompleteWeaponText(), CompleteArmorText(),
        CompleteEnemyText(), CompleteTroopText(), CompleteStateText(), CompleteAnimationText(), CompleteTilesetText(), CompleteCommonEventText(),
        SystemTypeName(), SystemTermText()
    ];

    public static TextRecipe CompleteActorText() => new(
        "phase8.mv.actor-text", 1, "MV Actor Text Bundle", TextContentKind.Custom,
        [
            Stage("given", NameGeneratorStage.StageKind, new { profile="common", domain="personGiven", strategy="hybrid", candidateCount=40, field="given", isPrimary=false }),
            Stage("family", NameGeneratorStage.StageKind, new { profile="common", domain="personFamily", strategy="hybrid", candidateCount=40, field="family", isPrimary=false }),
            Stage("name", TemplateStage.StageKind, new { template="{{given}} {{family}}", field="name", isPrimary=true }),
            Stage("nickname", RandomListStage.StageKind, new { items=new[] { "the Quiet", "Red", "Sparrow", "Stonehand", "Lucky", "Wayfarer", "the Younger", "Bright-Eyes" }, field="nickname", isPrimary=false }),
            Stage("profile", AdLibStage.StageKind, new { template="A {{temperament}} {{calling}} from {{origin}} who {{motive}}.", slots=new Dictionary<string,object> { ["temperament"]=new[]{"steadfast","curious","soft-spoken","reckless","practical","idealistic"}, ["calling"]=new[]{"wanderer","soldier","scholar","courier","hunter","apprentice"}, ["origin"]=new[]{"a river town","the northern marches","a merchant family","a remote island","the old capital","a border village"}, ["motive"]=new[]{"seeks a missing friend","owes an old debt","wants to see the wider world","guards a dangerous secret","hopes to restore a family name","follows a half-remembered prophecy"} }, field="profile", isPrimary=false })
        ],
        Description: "Generates Actor name, nickname and profile as one importable RPG Maker MV bundle.");


    public static TextRecipe CompleteClassText() => new(
        "phase8.mv.class-text", 1, "MV Class Text Bundle", TextContentKind.Custom,
        [
            Stage("name", AdLibStage.StageKind, new { template="{{descriptor|title}} {{role|title}}", slots=new Dictionary<string,object> { ["descriptor"]=new[]{"arcane","battle","dawn","dragon","elder","iron","moon","shadow","spirit","storm"}, ["role"]=new[]{"adept","guardian","hunter","knight","mage","mystic","ranger","scholar","warden","warrior"} }, field="name", isPrimary=true }),
            Stage("note", AdLibStage.StageKind, new { template="A {{tradition}} discipline emphasizing {{focus}}.", slots=new Dictionary<string,object> { ["tradition"]=new[]{"formal","wandering","ancient","military","mystical","regional"}, ["focus"]=new[]{"mobility","defense","elemental magic","precision","support","endurance"} }, field="note", isPrimary=false })
        ],
        Description: "Generates a Class name and optional prose Note as one RPG Maker MV text bundle.");

    public static TextRecipe CompleteSkillText() => new(
        "phase8.mv.skill-text", 1, "MV Skill Text Bundle", TextContentKind.Skill,
        [
            Stage("name", AdLibStage.StageKind, new { template="{{adjective|title}} {{noun|title}}[[30%: of {{concept|title}}]]", slots=new Dictionary<string,object> { ["adjective"]=new[]{"arcane","ashen","crimson","frozen","luminous","silent","spectral","stormbound","umbral"}, ["noun"]=new[]{"aegis","bolt","crown","lance","requiem","seal","shroud","torrent","ward"}, ["concept"]=new[]{"ashes","dreams","frost","memory","night","stars","winter"} }, field="name", isPrimary=true }),
            Stage("description", AdLibStage.StageKind, new { template="{{verb|sentence}} {{target}} with {{effect}}.", slots=new Dictionary<string,object> { ["verb"]=new[]{"strike","surround","pierce","bind","shield","sear","freeze"}, ["target"]=new[]{"one enemy","all enemies","the chosen target","the party","the caster"}, ["effect"]=new[]{"concentrated elemental force","a lingering arcane mark","a sudden burst of power","a protective veil","unstable spectral energy"} }, field="description", isPrimary=false }),
            Stage("message1", TemplateStage.StageKind, new { template="%1 invokes {{name}}!", field="message1", isPrimary=false }),
            Stage("message2", RandomListStage.StageKind, new { items=new[]{"The air trembles with power!","A brilliant arc flashes across the battlefield!","Ancient force answers the call!","The battlefield bends beneath the spell!"}, field="message2", isPrimary=false })
        ],
        Description: "Generates Skill name, database description and both invocation message fields together.");

    public static TextRecipe CompleteItemText() => ItemLike(
        "phase8.mv.item-text", "MV Item Text Bundle", TextContentKind.Item,
        ["tonic","charm","vial","salve","stone","medallion","powder","draught"]);

    public static TextRecipe CompleteWeaponText() => ItemLike(
        "phase8.mv.weapon-text", "MV Weapon Text Bundle", TextContentKind.Weapon,
        ["blade","spear","hammer","bow","staff","axe","saber","lance"]);

    public static TextRecipe CompleteArmorText() => ItemLike(
        "phase8.mv.armor-text", "MV Armor Text Bundle", TextContentKind.Armor,
        ["mail","cuirass","mantle","shield","coat","helm","vestment","plate"]);

    public static TextRecipe CompleteEnemyText() => new(
        "phase8.mv.enemy-text", 1, "MV Enemy Text Bundle", TextContentKind.Enemy,
        [
            Stage("name", AdLibStage.StageKind, new { template="{{descriptor|title}} {{creature|title}}", slots=new Dictionary<string,object> { ["descriptor"]=new[]{"ash","bog","bone","carrion","dusk","ember","frost","gloom","mire","shadow","thorn"}, ["creature"]=new[]{"crawler","fiend","hound","lurker","maw","reaver","stalker","whelp","wight","wyrm"} }, field="name", isPrimary=true }),
            Stage("note", AdLibStage.StageKind, new { template="A {{behavior}} creature associated with {{habitat}}. {{detail|sentence}}.", slots=new Dictionary<string,object> { ["behavior"]=new[]{"territorial","nocturnal","pack-hunting","ambush-hunting","restless","unnaturally patient"}, ["habitat"]=new[]{"ruins","deep forest","marshes","caves","old battlefields","mountain passes"}, ["detail"]=new[]{"locals recognize its tracks immediately","it avoids open flame","it becomes aggressive near nesting grounds","older specimens are rarely seen alone"} }, field="note", isPrimary=false })
        ],
        Description: "Generates Enemy name plus optional prose Note. Existing plugin-tag Notes remain protected from whole-field replacement.");


    public static TextRecipe CompleteTroopText() => new(
        "phase8.mv.troop-text", 1, "MV Troop Name", TextContentKind.Enemy,
        [Stage("name", AdLibStage.StageKind, new { template="{{group|title}} — {{place|title}}", slots=new Dictionary<string,object> { ["group"]=new[]{"ambush","boss encounter","hunting pack","patrol","raiding party","wandering monsters"}, ["place"]=new[]{"ancient road","black marsh","deep cave","forest edge","old ruins","mountain pass"} }, field="name", isPrimary=true })],
        Description: "Generates descriptive Troop names for easier RPG Maker MV database navigation.");

    public static TextRecipe CompleteAnimationText() => new(
        "phase8.mv.animation-text", 1, "MV Animation Name", TextContentKind.Custom,
        [Stage("name", AdLibStage.StageKind, new { template="{{element|title}} {{motion|title}}", slots=new Dictionary<string,object> { ["element"]=new[]{"arcane","dark","earth","fire","frost","holy","lightning","wind"}, ["motion"]=new[]{"burst","impact","pulse","ring","slash","spiral","surge","wave"} }, field="name", isPrimary=true })],
        Description: "Generates concise Animation names for the RPG Maker MV database.");

    public static TextRecipe CompleteTilesetText() => new(
        "phase8.mv.tileset-text", 1, "MV Tileset Text Bundle", TextContentKind.PlaceName,
        [
            Stage("name", AdLibStage.StageKind, new { template="{{theme|title}} {{set|title}}", slots=new Dictionary<string,object> { ["theme"]=new[]{"castle","coastal","desert","forest","ice","ruins","swamp","village","volcanic"}, ["set"]=new[]{"interior","exterior","dungeon","field","settlement","temple"} }, field="name", isPrimary=true }),
            Stage("note", TemplateStage.StageKind, new { template="Tileset authoring notes for {{name}}.", field="note", isPrimary=false })
        ],
        Description: "Generates a Tileset name and optional authoring Note.");

    public static TextRecipe CompleteStateText() => new(
        "phase8.mv.state-text", 1, "MV State Text Bundle", TextContentKind.Custom,
        [
            Stage("name", RandomListStage.StageKind, new { items=new[]{"Burning","Frozen","Dazed","Silenced","Cursed","Blessed","Bleeding","Shrouded","Weakened","Inspired"}, field="name", isPrimary=true }),
            Stage("m1", TemplateStage.StageKind, new { template="%1 is afflicted with {{name}}!", field="message1", isPrimary=false }),
            Stage("m2", TemplateStage.StageKind, new { template="%1 is afflicted with {{name}}!", field="message2", isPrimary=false }),
            Stage("m3", TemplateStage.StageKind, new { template="%1 remains {{name}}.", field="message3", isPrimary=false }),
            Stage("m4", TemplateStage.StageKind, new { template="%1 is no longer {{name}}.", field="message4", isPrimary=false })
        ],
        Description: "Generates a State name and all four RPG Maker MV state-message fields as one bundle.");

    public static TextRecipe CompleteCommonEventText() => new(
        "phase8.mv.common-event-text", 1, "MV Common Event Name", TextContentKind.Event,
        [Stage("name", AdLibStage.StageKind, new { template="{{action|title}} — {{subject|title}}", slots=new Dictionary<string,object> { ["action"]=new[]{"update","check","resolve","advance","initialize","refresh","handle"}, ["subject"]=new[]{"quest state","day cycle","party camp","weather","reputation","travel","encounter table","shop inventory"} }, field="name", isPrimary=true })],
        Description: "Generates descriptive Common Event names that are easier to navigate in the MV database.");


    public static TextRecipe SystemTypeName() => new(
        "phase8.mv.system-type-name", 1, "MV System Type Name", TextContentKind.Custom,
        [Stage("name", RandomListStage.StageKind, new { items=new[]{"Arcane","Beast","Blunt","Frost","Heavy","Light","Mystic","Piercing","Shadow","Spirit","Storm","Support"}, field="name", isPrimary=true })],
        Description: "Generates short labels suitable for System type arrays such as elements or equipment types.");

    public static TextRecipe SystemTermText() => new(
        "phase8.mv.system-term", 1, "MV System Term Text", TextContentKind.Custom,
        [Stage("term", RandomListStage.StageKind, new { items=new[]{"Attack","Defend","Escape","Fight","Item","Magic","Options","Save","Status","Victory"}, field="term", isPrimary=true })],
        Description: "Generates short System vocabulary. Import validation preserves required RPG Maker placeholders.");

    private static TextRecipe ItemLike(string id, string name, TextContentKind kind, string[] nouns) => new(
        id, 1, name, kind,
        [
            Stage("name", AdLibStage.StageKind, new { template="{{adjective|title}} {{noun|title}}[[25%: of {{concept|title}}]]", slots=new Dictionary<string,object> { ["adjective"]=new[]{"ashen","bright","cracked","elder","gilded","iron","moonlit","runed","silver","storm"}, ["noun"]=nouns, ["concept"]=new[]{"dawn","embers","kings","night","oaths","stars","winter"} }, field="name", isPrimary=true }),
            Stage("description", AdLibStage.StageKind, new { template="A {{quality}} {{object}} {{detail}}.", slots=new Dictionary<string,object> { ["quality"]=new[]{"carefully made","weathered","unusually light","rune-marked","well-balanced","strange"}, ["object"]=nouns, ["detail"]=new[]{"favored by veteran adventurers","bearing marks of an older age","that reacts faintly to magic","made for long journeys","with a history no merchant can verify"} }, field="description", isPrimary=false })
        ],
        Description: "Generates the database Name and Description fields as one RPG Maker MV bundle.");

    private static TextRecipeStage Stage(string id, string kind, object settings)
    {
        JsonElement element = JsonSerializer.SerializeToElement(settings, RecipeJson.Options);
        return new TextRecipeStage(id, kind, Settings: element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase));
    }
}
