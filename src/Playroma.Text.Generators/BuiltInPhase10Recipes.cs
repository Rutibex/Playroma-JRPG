using System.Text.Json;
using Playroma.Text.Core.Dialogue;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Stages;

namespace Playroma.Text.Generators;

/// <summary>Voice-aware dialogue recipes. Narrative intent is generated first; Phase 10 voice realization is applied last.</summary>
public static class BuiltInPhase10Recipes
{
    public static IReadOnlyList<TextRecipe> All { get; } =
    [
        Single("phase10.dialogue.greeting", "Character Greeting", DialogueAct.Greeting,
            ["Good to see you. What brings you this way?","Hello. I was wondering when someone would come by.","You made it. I hope the road treated you kindly.","Welcome. Is there something you need?"]),
        Single("phase10.dialogue.small-talk", "Character Small Talk", DialogueAct.SmallTalk,
            ["The weather has changed three times today. Nobody knows what to make of it.","The market is quieter than usual, but the inn is full every night.","I heard the south road finally reopened this morning.","People have been staying indoors after sunset lately."]),
        Single("phase10.dialogue.warning", "Character Warning", DialogueAct.Warning,
            ["Do not take the eastern road after dark. Something has been following travelers there.","If you hear bells near the old ruins, turn around and come back.","Keep away from the lower gate tonight. The watch is expecting trouble.","Whatever you do, do not open the sealed door beneath the shrine."]),
        Single("phase10.dialogue.threat", "Character Threat", DialogueAct.Threat,
            ["Walk away now and this ends without bloodshed.","You have one chance to leave before I decide for you.","Take another step and you will regret coming here.","You should have stayed out of this matter."]),
        Single("phase10.dialogue.quest-offer", "Character Quest Offer", DialogueAct.QuestOffer,
            ["A courier disappeared on the north road. I need someone to find out what happened.","There is an old shrine beyond the ridge. Bring back the ledger hidden there and I will reward you.","Three supply carts are overdue. Find them before the town runs short.","Something drove the miners out of the lower tunnels. I need someone willing to investigate."]),
        Single("phase10.dialogue.quest-complete", "Character Quest Completion", DialogueAct.QuestComplete,
            ["You actually did it. This will make a real difference here.","The missing supplies are back where they belong. You have my thanks.","I was beginning to think nobody would return from there. Well done.","That settles the matter. I promised you a reward, and I keep my promises."]),
        Single("phase10.dialogue.rumor", "Character Rumor", DialogueAct.Rumor,
            ["Someone saw lights moving through the abandoned watchtower last night.","They say a merchant vanished after buying a map from the old bookseller.","The river has been running warm near the ruined bridge, even before sunrise.","A patrol found fresh tracks outside the wall, but none of the hunters recognized them."]),
        Single("phase10.dialogue.shop-greeting", "Character Shop Greeting", DialogueAct.ShopGreeting,
            ["Welcome. Take a look around and let me know if you need anything.","Fresh stock came in this morning. You might find something useful.","If you are heading outside the walls, check your supplies before you go.","Come in. I have a few things that are hard to find anywhere else."]),
        Single("phase10.dialogue.battle-bark", "Character Battle Bark", DialogueAct.BattleBark,
            ["There! Move!","Stay together!","I have this one!","Do not let them surround us!","Now! Strike while they are open!"]),
        Single("phase10.dialogue.victory", "Character Victory Bark", DialogueAct.Victory,
            ["That is the last of them.","Everyone still standing? Good.","We did it. Keep moving before more arrive.","That went better than I expected."]),
        Single("phase10.dialogue.farewell", "Character Farewell", DialogueAct.Farewell,
            ["Safe travels. I hope we meet again under easier circumstances.","Take care of yourself out there.","That is all I have for now. Good luck.","Until next time."]),
        BranchingConversation()
    ];

    private static TextRecipe Single(string id, string name, DialogueAct act, IReadOnlyList<string> lines)
    {
        return new TextRecipe(id, 1, name, TextContentKind.Dialogue,
        [
            Stage("meaning", RandomListStage.StageKind, new { items=lines, field="text", isPrimary=true }),
            Voice("voice", act, ["text"])
        ], Description:$"Generates a {act} dialogue intent, then realizes it using the active character voice profile.");
    }

    private static TextRecipe BranchingConversation() => new(
        "phase10.dialogue.branching", 1, "Character-Voiced Branching Conversation", TextContentKind.Dialogue,
        [
            Stage("prompt", RandomListStage.StageKind, new { items=new[]{"What did you want to ask?","You look like you have questions.","I can spare a moment. What do you need?"}, field="prompt", isPrimary=true }),
            Stage("choice1", RandomListStage.StageKind, new { items=new[]{"Ask about the town","Ask about recent trouble","Ask what happened here"}, field="choice1", isPrimary=false }),
            Stage("response1", RandomListStage.StageKind, new { items=new[]{"Things were quiet until a few nights ago. Since then, nobody sleeps easily.","The town is old, stubborn, and harder to frighten than outsiders think.","It started with missing livestock. Then a patrol failed to return."}, field="response1", isPrimary=false }),
            Stage("choice2", RandomListStage.StageKind, new { items=new[]{"Ask about work","Offer to help","Ask who needs help"}, field="choice2", isPrimary=false }),
            Stage("response2", RandomListStage.StageKind, new { items=new[]{"Speak to the captain by the gate. There is more work than people willing to do it.","If you mean that, check the notice board near the inn.","The shrine keeper has been asking for someone capable all morning."}, field="response2", isPrimary=false }),
            Stage("choice3", RandomListStage.StageKind, new { items=new[]{"Say goodbye","Leave","Thank them"}, field="choice3", isPrimary=false }),
            Stage("response3", RandomListStage.StageKind, new { items=new[]{"Safe travels.","Keep your eyes open outside the walls.","You are welcome. Take care."}, field="response3", isPrimary=false }),
            Voice("voice", DialogueAct.Answer, ["prompt","response1","response2","response3"])
        ],
        Description:"Generates an MV-compatible prompt, choices, and responses, then applies one consistent character voice to all spoken lines while leaving player choice labels untouched.");

    private static TextRecipeStage Voice(string id, DialogueAct act, IReadOnlyList<string> fields) =>
        Stage(id, DialogueVoiceStage.StageKind, new
        {
            profileContextKey="voice.profileId",
            act=act.ToString(),
            fields,
            addActFraming=true,
            addEmotionFraming=true,
            addCatchphrase=true,
            applyDialect=true,
            applyVocabulary=true
        });

    private static TextRecipeStage Stage(string id, string kind, object settings) =>
        new(id, kind, Settings: JsonSerializer.SerializeToElement(settings).EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value.Clone(),StringComparer.OrdinalIgnoreCase));
}
