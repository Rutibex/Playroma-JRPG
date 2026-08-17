using Playroma.Text.Core.Narrative;
using Playroma.Text.Core.Planning;

namespace Playroma.Text.Generators.Planning;

/// <summary>Original JRPG-oriented quest grammars demonstrating authored HTN and state-space planning.</summary>
public static class BuiltInQuestGrammars
{
    private static StoryletBindingDefinition B(string id, StoryletBindingKind kind, string source, bool distinct=true, params string[] tags) => new(id, kind, source, true, distinct, tags);
    private static QuestEffectDefinition Set(string key, object? value) => new(QuestEffectKind.Set, key, value);
    private static QuestEffectDefinition Tag(string key) => new(QuestEffectKind.AddTag, key);
    private static QuestActionDefinition A(string id, string name, string[]? pre=null, QuestEffectDefinition[]? effects=null, string? recipe=null, StoryletBindingDefinition[]? bindings=null, params string[] tags) => new(id,name,Preconditions:pre,Effects:effects,RealizationRecipeId:recipe ?? "phase12.quest.journal-update",Bindings:bindings,Tags:tags);
    private static QuestMethodDefinition M(string id,string task,int priority,params string[] subtasks)=>new(id,task,subtasks,Priority:priority);

    public static IReadOnlyList<QuestGrammarDefinition> All { get; } =
    [
        new("phase12.quest.fetch",1,"Fetch Quest","Find an object, obtain it, and return it to a giver.",RootTask:"fetch-quest",GoalPredicates:["tag(\"quest-complete\")"],
            Bindings:[B("giver",StoryletBindingKind.Character,"storylet.characters"),B("location",StoryletBindingKind.Location,"storylet.locations"),B("object",StoryletBindingKind.Item,"quest.items")],
            Actions:[
                A("learn-location","Learn where the item is",effects:[Tag("location-known")]),
                A("travel","Travel to the destination",["tag(\"location-known\")"],[Tag("at-destination")]),
                A("obtain","Obtain the requested item",["tag(\"at-destination\")"],[Tag("has-object")]),
                A("return","Return to the quest giver",["tag(\"has-object\")"],[Tag("quest-complete")])],
            Methods:[M("fetch.standard","fetch-quest",10,"learn-location","travel","obtain","return")],SummaryRecipeId:"phase12.quest.summary",Tags:["fetch","classic"]),

        new("phase12.quest.hunt",1,"Monster Hunt","Track a threat, confront it, and report success.",RootTask:"hunt-quest",GoalPredicates:["tag(\"quest-complete\")"],
            Bindings:[B("giver",StoryletBindingKind.Character,"storylet.characters"),B("location",StoryletBindingKind.Location,"storylet.locations"),B("target",StoryletBindingKind.Custom,"quest.monsters")],
            Actions:[A("investigate-signs","Investigate signs of the creature",effects:[Tag("trail-found")]),A("track-target","Track the creature",["tag(\"trail-found\")"],[Tag("target-found")]),A("defeat-target","Defeat the creature",["tag(\"target-found\")"],[Tag("target-defeated")]),A("report-hunt","Report the hunt",["tag(\"target-defeated\")"],[Tag("quest-complete")])],
            Methods:[M("hunt.standard","hunt-quest",10,"investigate-signs","track-target","defeat-target","report-hunt")],SummaryRecipeId:"phase12.quest.summary",Tags:["combat","hunt"]),

        new("phase12.quest.delivery",1,"Delivery Quest","Carry an item safely from one character to another.",RootTask:"delivery-quest",GoalPredicates:["tag(\"quest-complete\")"],
            Bindings:[B("giver",StoryletBindingKind.Character,"storylet.characters"),B("recipient",StoryletBindingKind.Character,"storylet.characters"),B("location",StoryletBindingKind.Location,"storylet.locations"),B("object",StoryletBindingKind.Item,"quest.items")],
            Actions:[A("receive-package","Receive the delivery",effects:[Tag("has-package")]),A("travel-recipient","Travel to the recipient",["tag(\"has-package\")"],[Tag("at-recipient")]),A("deliver-package","Deliver the package",["tag(\"at-recipient\")"],[Tag("quest-complete")])],
            Methods:[M("delivery.standard","delivery-quest",10,"receive-package","travel-recipient","deliver-package")],SummaryRecipeId:"phase12.quest.summary",Tags:["delivery"]),

        new("phase12.quest.escort",1,"Escort Quest","Meet a traveler, escort them through danger, and reach safety.",RootTask:"escort-quest",GoalPredicates:["tag(\"quest-complete\")"],
            Bindings:[B("escort",StoryletBindingKind.Character,"storylet.characters"),B("destination",StoryletBindingKind.Location,"storylet.locations")],
            Actions:[A("meet-escort","Meet the traveler",effects:[Tag("escort-joined")]),A("cross-danger","Cross the dangerous route",["tag(\"escort-joined\")"],[Tag("route-crossed")]),A("reach-safety","Reach the destination",["tag(\"route-crossed\")"],[Tag("quest-complete")])],Methods:[M("escort.standard","escort-quest",10,"meet-escort","cross-danger","reach-safety")],SummaryRecipeId:"phase12.quest.summary",Tags:["escort"]),

        new("phase12.quest.investigation",1,"Investigation Quest","Gather clues, identify the truth, and confront the responsible party.",RootTask:"investigation",GoalPredicates:["tag(\"quest-complete\")"],
            Bindings:[B("giver",StoryletBindingKind.Character,"storylet.characters"),B("location",StoryletBindingKind.Location,"storylet.locations"),B("suspect",StoryletBindingKind.Character,"storylet.characters")],
            Actions:[A("inspect-scene","Inspect the scene",effects:[Tag("clue-one")]),A("question-witness","Question a witness",["tag(\"clue-one\")"],[Tag("clue-two")]),A("deduce-truth","Connect the clues",["tag(\"clue-one\") && tag(\"clue-two\")"],[Tag("truth-known")]),A("confront-suspect","Confront the suspect",["tag(\"truth-known\")"],[Tag("quest-complete")])],Methods:[M("investigation.standard","investigation",10,"inspect-scene","question-witness","deduce-truth","confront-suspect")],SummaryRecipeId:"phase12.quest.summary",Tags:["investigation","dialogue"]),

        new("phase12.quest.rescue",1,"Rescue Quest","Locate a missing person, overcome the obstacle, and bring them home.",RootTask:"rescue",GoalPredicates:["tag(\"quest-complete\")"],
            Bindings:[B("missing",StoryletBindingKind.Character,"storylet.characters"),B("location",StoryletBindingKind.Location,"storylet.locations")],
            Actions:[A("learn-last-location","Learn where the missing person was last seen",effects:[Tag("trail-known")]),A("reach-rescue-site","Reach the rescue site",["tag(\"trail-known\")"],[Tag("at-rescue-site")]),A("free-missing","Free the missing person",["tag(\"at-rescue-site\")"],[Tag("rescued")]),A("return-home","Return safely",["tag(\"rescued\")"],[Tag("quest-complete")])],Methods:[M("rescue.standard","rescue",10,"learn-last-location","reach-rescue-site","free-missing","return-home")],SummaryRecipeId:"phase12.quest.summary",Tags:["rescue"]),

        new("phase12.quest.branching-search",1,"Planner Search Demonstration","Lets the forward planner choose between buying information or investigating before recovering a relic.",GoalPredicates:["tag(\"quest-complete\")"],
            Bindings:[B("giver",StoryletBindingKind.Character,"storylet.characters"),B("location",StoryletBindingKind.Location,"storylet.locations"),B("object",StoryletBindingKind.Item,"quest.items")],
            Actions:[
                new("ask-rumors","Ask around for rumors",Cost:1,Effects:[Tag("location-known")],RealizationRecipeId:"phase12.quest.journal-update"),
                new("buy-map","Buy a map",Cost:2,Effects:[Tag("location-known")],RealizationRecipeId:"phase12.quest.journal-update"),
                new("travel-search","Travel to the site",Cost:1,Preconditions:["tag(\"location-known\")"],Effects:[Tag("at-site")],RealizationRecipeId:"phase12.quest.journal-update"),
                new("recover-relic","Recover the relic",Cost:1,Preconditions:["tag(\"at-site\")"],Effects:[Tag("has-object")],RealizationRecipeId:"phase12.quest.journal-update"),
                new("return-relic","Return the relic",Cost:1,Preconditions:["tag(\"has-object\")"],Effects:[Tag("quest-complete")],RealizationRecipeId:"phase12.quest.journal-update")],
            SummaryRecipeId:"phase12.quest.summary",Planner:new(MaxPlanSteps:8,MaxSearchNodes:2000,PreferLowerCost:true),Tags:["planner","search"]),

        new("phase12.quest.multi-stage",1,"Multi-stage JRPG Quest","A longer investigate → retrieve → confrontation → return structure.",RootTask:"multi-stage",GoalPredicates:["tag(\"quest-complete\")"],
            Bindings:[B("giver",StoryletBindingKind.Character,"storylet.characters"),B("location",StoryletBindingKind.Location,"storylet.locations"),B("object",StoryletBindingKind.Item,"quest.items"),B("target",StoryletBindingKind.Custom,"quest.monsters")],
            Actions:[A("hear-problem","Hear the full problem",effects:[Tag("quest-understood")]),A("investigate-area","Investigate the area",["tag(\"quest-understood\")"],[Tag("route-known")]),A("retrieve-key","Retrieve a required object",["tag(\"route-known\")"],[Tag("key-obtained")]),A("enter-lair","Enter the hidden lair",["tag(\"key-obtained\")"],[Tag("lair-entered")]),A("resolve-threat","Resolve the central threat",["tag(\"lair-entered\")"],[Tag("threat-resolved")]),A("return-result","Return with the result",["tag(\"threat-resolved\")"],[Tag("quest-complete")])],Methods:[M("multi-stage.standard","multi-stage",10,"hear-problem","investigate-area","retrieve-key","enter-lair","resolve-threat","return-result")],SummaryRecipeId:"phase12.quest.summary",Tags:["chain","jrpg"])
    ];
}
