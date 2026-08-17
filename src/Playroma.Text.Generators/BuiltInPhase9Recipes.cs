using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Stages;

namespace Playroma.Text.Generators;

/// <summary>Map and event-message recipes for RPG Maker MV authoring.</summary>
public static class BuiltInPhase9Recipes
{
    public static IReadOnlyList<TextRecipe> All { get; } =
    [
        MapText(), NpcDialogue(), ShopGreeting(), WarningDialogue(), ScrollingLore(), EventComment(),
        ChoicePrompt(), BranchingDialogue(), SignText(), TreasureClue(), QuestDialogue(), AmbientNpcChatter()
    ];

    public static TextRecipe MapText() => new(
        "phase9.mv.map-text", 1, "MV Map Text Bundle", TextContentKind.PlaceName,
        [
            Stage("name", AdLibStage.StageKind, new { template="{{descriptor|title}} {{place|title}}", slots=new Dictionary<string,object> { ["descriptor"]=new[]{"amber","ashen","black","bright","elder","frost","green","moon","old","silver","sunken","whispering"}, ["place"]=new[]{"crossing","falls","haven","heights","hollow","marsh","pass","reach","ruins","vale","village","wood"} }, field="name", isPrimary=true }),
            Stage("display", TemplateStage.StageKind, new { template="{{name}}", field="displayName", isPrimary=false }),
            Stage("note", AdLibStage.StageKind, new { template="A {{mood}} area known for {{feature}}. {{hook|sentence}}.", slots=new Dictionary<string,object> { ["mood"]=new[]{"quiet","dangerous","remote","prosperous","weathered","mysterious"}, ["feature"]=new[]{"an old road","unusual wildlife","ruined foundations","a local shrine","merchant traffic","strange weather"}, ["hook"]=new[]{"travelers exchange rumors here","locals avoid the area after dark","an old story is tied to this place","the route becomes important later"} }, field="note", isPrimary=false })
        ], Description:"Generates editor name, display name and optional map note as one importable MV map bundle.");

    public static TextRecipe NpcDialogue() => new(
        "phase9.mv.show-text", 1, "MV NPC Dialogue", TextContentKind.Dialogue,
        [Stage("text", AdLibStage.StageKind, new { template="{{opening|sentence}}. {{detail|sentence}}.", slots=new Dictionary<string,object> { ["opening"]=new[]{"you look new around here","the roads have been strange lately","good timing, traveler","I would keep your voice down","there is work for someone capable","the weather is turning"}, ["detail"]=new[]{"the innkeeper hears everything","the north gate closes at dusk","merchants came through this morning","someone has been asking about the old ruins","the watch doubled its patrols last night","people are trying not to talk about it"} }, field="text", isPrimary=true })],
        Description:"Generates Show Text-ready NPC dialogue. The MV adapter automatically wraps and paginates it into message commands.");

    public static TextRecipe ShopGreeting() => new(
        "phase9.mv.shop-greeting", 1, "MV Shop Greeting", TextContentKind.Dialogue,
        [Stage("text", RandomListStage.StageKind, new { items=new[]{"Welcome! Take your time and look around.","Need supplies? You came to the right place.","Fresh stock came in this morning. See anything you like?","If you are heading beyond the walls, do not leave without supplies.","Back again? I saved the good merchandise for you."}, field="text", isPrimary=true })],
        Description:"Short shopkeeper greetings formatted for Show Text.");

    public static TextRecipe WarningDialogue() => new(
        "phase9.mv.warning", 1, "MV Warning Dialogue", TextContentKind.Dialogue,
        [Stage("text", AdLibStage.StageKind, new { template="{{warning|sentence}}! {{reason|sentence}}.", slots=new Dictionary<string,object> { ["warning"]=new[]{"do not go through that gate","stay off the eastern road","keep away from the old tower","turn back before nightfall","do not touch the altar"}, ["reason"]=new[]{"people who ignore the warning rarely return","the guards have already lost two patrols","something there has been waking after sunset","the ground itself is unstable","whatever is inside notices strangers"} }, field="text", isPrimary=true })],
        Description:"Generates warnings and hazard dialogue for map events.");

    public static TextRecipe ScrollingLore() => new(
        "phase9.mv.scrolling-lore", 1, "MV Scrolling Lore", TextContentKind.Lore,
        [Stage("text", AdLibStage.StageKind, new { template="In the age of {{age}}, {{event}}.\n\nThe chronicles claim {{claim}}.\n\nEven now, {{legacy}}.", slots=new Dictionary<string,object> { ["age"]=new[]{"Ash","Crowns","Falling Stars","Glass","Kings","Storms"}, ["event"]=new[]{"the western kingdoms broke their ancient pact","a nameless fleet crossed the inland sea","the first sky-temple fell","the old road vanished beneath the forest","three cities closed their gates forever"}, ["claim"]=new[]{"the disaster began with a single broken oath","no army ever found the true battlefield","the victors erased their own names","one witness survived for another century","the final warning was deliberately ignored"}, ["legacy"]=new[]{"travelers leave offerings at ruined milestones","the old symbols still appear on sealed doors","children know fragments of the forbidden song","the border stones remain warm in winter","some families refuse to speak the ancient names"} }, field="text", isPrimary=true })],
        Description:"Generates multi-paragraph text suitable for RPG Maker MV Scrolling Text.");

    public static TextRecipe EventComment() => new(
        "phase9.mv.comment", 1, "MV Event Comment", TextContentKind.Note,
        [Stage("text", AdLibStage.StageKind, new { template="{{purpose}}\nGenerated authoring note: {{detail}}", slots=new Dictionary<string,object> { ["purpose"]=new[]{"Quest progression branch","Ambient NPC behavior","One-time treasure interaction","Area transition setup","Boss encounter trigger","Dialogue state check"}, ["detail"]=new[]{"review switches before release","keep this block together when moving commands","safe to rewrite prose but preserve control flow","used by another event on this map","intended as an editor-facing note only"} }, field="text", isPrimary=true })],
        Description:"Generates editor-facing Event Comments using MV codes 108/408.");

    public static TextRecipe ChoicePrompt() => new(
        "phase9.mv.choices", 1, "MV Choice Set", TextContentKind.Dialogue,
        [
            Stage("choice1", RandomListStage.StageKind, new { items=new[]{"Ask about the town","Ask about the ruins","Ask about work","Ask about the road"}, field="choice1", isPrimary=true }),
            Stage("choice2", RandomListStage.StageKind, new { items=new[]{"Trade rumors","Request directions","Ask who is in charge","Ask what happened"}, field="choice2", isPrimary=false }),
            Stage("choice3", RandomListStage.StageKind, new { items=new[]{"Say goodbye","Leave","Change the subject","Thank them"}, field="choice3", isPrimary=false })
        ], Description:"Generates up to six Show Choices labels. The MV adapter enforces MV's six-choice maximum.");

    public static TextRecipe BranchingDialogue() => new(
        "phase9.mv.branching-dialogue", 1, "MV Branching Conversation", TextContentKind.Dialogue,
        [
            Stage("prompt", RandomListStage.StageKind, new { items=new[]{"You wanted to ask me something?","What do you need, traveler?","I can spare a moment. What is it?","Go on. I am listening."}, field="prompt", isPrimary=true }),
            Stage("c1", RandomListStage.StageKind, new { items=new[]{"What happened here?","Tell me about this place.","Why is everyone nervous?"}, field="choice1", isPrimary=false }),
            Stage("r1", RandomListStage.StageKind, new { items=new[]{"It started three nights ago. Nobody agrees on what they saw.","This used to be a quiet stop on the trade road. That changed quickly.","Because the watch found tracks outside the wall that belong to nothing local."}, field="response1", isPrimary=false }),
            Stage("c2", RandomListStage.StageKind, new { items=new[]{"Is there any work?","Can I help?","What needs doing?"}, field="choice2", isPrimary=false }),
            Stage("r2", RandomListStage.StageKind, new { items=new[]{"If you are serious, speak to the captain near the north gate.","Maybe. Check the notice board first; the dangerous jobs are marked in red.","There is always work. The question is whether you want the kind nobody else accepts."}, field="response2", isPrimary=false }),
            Stage("c3", RandomListStage.StageKind, new { items=new[]{"Goodbye.","That is all.","I should go."}, field="choice3", isPrimary=false }),
            Stage("r3", RandomListStage.StageKind, new { items=new[]{"Safe travels.","Keep your eyes open out there.","Then do not let me keep you."}, field="response3", isPrimary=false })
        ], Description:"Generates a prompt, choices and branch responses that the MV adapter compiles into a complete Show Text + Show Choices command block.");

    public static TextRecipe SignText() => Simple("phase9.mv.sign", "MV Sign / Plaque", TextContentKind.Dialogue,
        ["NORTH GATE — CLOSED AFTER SUNSET","MARKET SQUARE →","NO ENTRY WITHOUT CAPTAIN'S ORDER","OLD ROAD CLOSED — USE EAST BRIDGE","TRAVELERS: REPORT MONSTER SIGHTINGS TO THE WATCH"]);
    public static TextRecipe TreasureClue() => Simple("phase9.mv.treasure-clue", "MV Treasure Clue", TextContentKind.Dialogue,
        ["Where the broken bell faces west, count three stones from the wall.","The key sleeps beneath the tree that never flowers.","Follow the river until the old road crosses it for the second time.","Only the statue without a shadow points toward the hidden door."]);
    public static TextRecipe QuestDialogue() => Simple("phase9.mv.quest-dialogue", "MV Quest Dialogue", TextContentKind.Dialogue,
        ["I need someone to check the old road. Three caravans are overdue.","A courier never reached the next village. Find out what stopped them.","The shrine keeper asked for help, but nobody here wants to go near the ruins.","Bring back proof that the creature in the marsh is gone and the town will pay you."]);
    public static TextRecipe AmbientNpcChatter() => Simple("phase9.mv.ambient-chatter", "MV Ambient NPC Chatter", TextContentKind.Dialogue,
        ["Rain again tomorrow, if the clouds are telling the truth.","The baker sold out before noon. Never seen that happen before.","Someone repaired the south bridge at last.","The inn is full tonight. Merchants, mostly.","I heard bells from the hill after midnight. There is no chapel up there."]);

    private static TextRecipe Simple(string id, string name, TextContentKind kind, string[] lines) => new(
        id, 1, name, kind, [Stage("text", RandomListStage.StageKind, new { items=lines, field="text", isPrimary=true })], Description:name);

    private static TextRecipeStage Stage(string id, string kind, object settings)
    {
        JsonElement element = JsonSerializer.SerializeToElement(settings, RecipeJson.Options);
        return new TextRecipeStage(id, kind, Settings: element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase));
    }
}
