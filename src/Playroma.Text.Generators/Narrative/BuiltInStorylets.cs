using Playroma.Text.Core.Narrative;

namespace Playroma.Text.Generators.Narrative;

/// <summary>Original demonstration storylets focused on common JRPG authoring situations.</summary>
public static class BuiltInStorylets
{
    public static IReadOnlyList<StoryletDefinition> All { get; } =
    [
        new("phase11.storylet.guard-warning", 1, "Guard Warns the Party",
            "A guarded local warns the player about nearby danger.", Weight: 4, Salience: 1.2, CooldownTurns: 3,
            Effects: [new(StoryletEffectKind.Increment, "warningsHeard", 1)],
            Realization: new("phase10.dialogue.warning", DialogueAct: "Warning"), Tags: ["dialogue", "warning", "npc"]),

        new("phase11.storylet.tavern-rumor", 1, "Fresh Tavern Rumor",
            "Surfaces a local rumor without immediately repeating it.", Weight: 5, Salience: 1, CooldownTurns: 2,
            Effects: [new(StoryletEffectKind.Increment, "rumorsHeard", 1)],
            Realization: new("phase10.dialogue.rumor", DialogueAct: "Rumor"), Tags: ["dialogue", "rumor", "tavern"]),

        new("phase11.storylet.quest-offer", 1, "A Stranger Needs Help",
            "Introduces a lightweight quest offer and marks that an offer has been heard.", Weight: 2.5, Salience: 1.35, OnceOnly: true,
            Effects: [new(StoryletEffectKind.Set, "questOfferHeard", true), new(StoryletEffectKind.AddTag, "quest-offer-heard")],
            Realization: new("phase10.dialogue.quest-offer", DialogueAct: "QuestOffer"), Tags: ["quest", "dialogue"]),

        new("phase11.storylet.old-soldier-memory", 1, "Old Soldier Remembers",
            "A once-only reflective exposition beat.", Weight: 1.4, Salience: 1.1, OnceOnly: true,
            Effects: [new(StoryletEffectKind.AddTag, "old-soldier-memory-heard")],
            Realization: new("phase10.dialogue.small-talk", DialogueAct: "Exposition", Emotion: "Solemn"), Tags: ["lore", "memory", "dialogue"]),

        new("phase11.storylet.shop-greeting", 1, "Merchant Greets a Customer",
            "A repeatable shop greeting with a short cooldown.", Weight: 6, Salience: .8, CooldownTurns: 1,
            Realization: new("phase10.dialogue.shop-greeting", DialogueAct: "ShopGreeting"), Tags: ["shop", "dialogue"]),

        new("phase11.storylet.ominous-sign", 1, "Ominous Sign",
            "An environmental clue that can be realized as ordinary event text.", Weight: 2, Salience: .9, CooldownTurns: 4,
            Effects: [new(StoryletEffectKind.Increment, "omensSeen", 1)],
            Realization: new("phase9.mv.sign"), Tags: ["environment", "clue"]),

        new("phase11.storylet.hidden-treasure-clue", 1, "Hidden Treasure Clue",
            "A once-only clue that records discovery state.", Weight: 1, Salience: 1.5, OnceOnly: true,
            Effects: [new(StoryletEffectKind.Set, "treasureClueKnown", true), new(StoryletEffectKind.AddTag, "treasure-clue-known")],
            Realization: new("phase9.mv.treasure-clue"), Tags: ["clue", "treasure"]),

        new("phase11.storylet.chronicle-fragment", 1, "Chronicle Fragment",
            "Produces an optional piece of local history.", Weight: 1.6, Salience: .7, CooldownTurns: 5,
            Realization: new("phase5.markov.chronicle"), Tags: ["lore", "history"]),

        new("phase11.storylet.hostile-threat", 1, "Hostile NPC Escalates",
            "A threat that only becomes eligible once hostility has been established.", Weight: 3, Salience: 1.5, CooldownTurns: 2,
            Prerequisites: ["relationship.affinity < -0.25 || tag(\"hostile\")"],
            Realization: new("phase10.dialogue.threat", DialogueAct: "Threat", Emotion: "Hostile"), Tags: ["dialogue", "hostile"]),

        new("phase11.storylet.quest-complete", 1, "Quest Completion Thanks",
            "A completion beat gated by storylet runtime state.", Weight: 4, Salience: 1.8, OnceOnly: true,
            Prerequisites: ["questComplete == true"],
            Effects: [new(StoryletEffectKind.Set, "questRewardAcknowledged", true)],
            Realization: new("phase10.dialogue.quest-complete", DialogueAct: "QuestComplete", Emotion: "Warm"), Tags: ["quest", "dialogue", "completion"]),

        new("phase11.storylet.bound-character-rumor", 1, "Bound Character Rumor",
            "Demonstrates character binding from a host-supplied candidate collection.", Weight: 1.5, Salience: 1,
            Bindings: [new StoryletBindingDefinition("speaker", StoryletBindingKind.Character, "storylet.characters", Required: true)],
            Realization: new("phase10.dialogue.rumor", DialogueAct: "Rumor"), Tags: ["dialogue", "binding-demo"]),

        new("phase11.storylet.bound-location-hook", 1, "Bound Location Hook",
            "Demonstrates location binding from project or simulation candidates.", Weight: 1.5, Salience: 1,
            Bindings: [new StoryletBindingDefinition("location", StoryletBindingKind.Location, "storylet.locations", Required: true)],
            Effects: [new(StoryletEffectKind.Increment, "locationHooksSeen", 1)],
            Realization: new("phase6.semantic.quest-hook"), Tags: ["quest", "binding-demo", "location"])
    ];
}
