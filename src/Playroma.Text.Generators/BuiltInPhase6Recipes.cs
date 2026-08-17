using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Semantics;
using Playroma.Text.Core.Tracery;
using Playroma.Text.Generators.Stages;

namespace Playroma.Text.Generators;

/// <summary>Original Phase 6 recipes demonstrating semantic features, agreement, filtering and hidden-candidate ranking.</summary>
public static class BuiltInPhase6Recipes
{
    public static IReadOnlyList<TextRecipe> All { get; } =
    [
        SemanticTraceryRecipe(
            "phase6.semantic.spell-invocation", "Semantic Spell Invocation", TextContentKind.Spell,
            "Generate a spell invocation whose semantic tone matches the selected authoring intent, then rank hidden candidates for concise, varied phrasing.",
            "tone", ["ominous", "heroic", "strange"], "ominous",
            new Dictionary<string, IReadOnlyList<TraceryRuleDefinition>>(StringComparer.OrdinalIgnoreCase)
            {
                ["origin"] =
                [
                    Rule("By #omen#, #effect#.", ("tone", S("ominous")), ("intensity", N(.88))),
                    Rule("#caller.capitalize# commands #effect#.", ("tone", S("heroic")), ("intensity", N(.74))),
                    Rule("A crooked cadence folds the air; #effect#.", ("tone", S("strange")), ("intensity", N(.63)))
                ],
                ["omen"] = [new("the last star"), new("the ash beneath the moon"), new("the bell below the earth")],
                ["caller"] = [new("the caster"), new("the oathbound hand"), new("the silver voice")],
                ["effect"] = [new("let the frozen dark awaken"), new("let every ward remember its maker"), new("open the road no torch can find"), new("break the silence between worlds")]
            },
            candidateCount: 24,
            scorers:
            [
                new("length", CandidateScorerKind.CharacterLength, .8, Ideal: 72, Minimum: 30, Maximum: 125),
                new("variety", CandidateScorerKind.UniqueWordRatio, .35),
                new("features", CandidateScorerKind.FeatureCoverage, .2, Target: "tone,intensity")
            ]),

        SemanticTraceryRecipe(
            "phase6.semantic.npc-greeting", "Semantic NPC Greeting", TextContentKind.Dialogue,
            "Mood-aware NPC greeting. Hidden candidates are filtered by semantic mood rather than hard-wiring separate generators.",
            "mood", ["friendly", "neutral", "hostile"], "friendly",
            new Dictionary<string, IReadOnlyList<TraceryRuleDefinition>>(StringComparer.OrdinalIgnoreCase)
            {
                ["origin"] =
                [
                    Rule("Welcome, traveler. #friendlyTail#", ("mood", S("friendly")), ("dialogue.act", S("greeting"))),
                    Rule("You're here. #neutralTail#", ("mood", S("neutral")), ("dialogue.act", S("greeting"))),
                    Rule("Stop there. #hostileTail#", ("mood", S("hostile")), ("dialogue.act", S("warning")))
                ],
                ["friendlyTail"] = [new("The fire is warm if you need it."), new("Tell me what brings you this way."), new("You've chosen a good road today.")],
                ["neutralTail"] = [new("State your business when you're ready."), new("The road behind you is still open."), new("I have work to finish.")],
                ["hostileTail"] = [new("One more step and the guards will hear of it."), new("You were not invited past this gate."), new("Turn around while that's still your choice.")]
            },
            candidateCount: 18,
            scorers:
            [
                new("brevity", CandidateScorerKind.WordCount, .5, Ideal: 12, Minimum: 5, Maximum: 24),
                new("variety", CandidateScorerKind.UniqueWordRatio, .25),
                new("features", CandidateScorerKind.FeatureCoverage, .2, Target: "mood,dialogue.act")
            ]),

        SemanticTraceryRecipe(
            "phase6.semantic.quest-hook", "Semantic Quest Hook", TextContentKind.Quest,
            "Quest hooks tagged by urgency and scope, filtered to the selected urgency and ranked for useful length.",
            "urgency", ["low", "medium", "high"], "medium",
            new Dictionary<string, IReadOnlyList<TraceryRuleDefinition>>(StringComparer.OrdinalIgnoreCase)
            {
                ["origin"] =
                [
                    Rule("A farmer needs someone to recover #smallThing# before market day.", ("urgency", S("low")), ("scope", S("local"))),
                    Rule("The road patrol vanished near #place#, and the magistrate needs answers before nightfall.", ("urgency", S("medium")), ("scope", S("local"))),
                    Rule("Unless the seal beneath #place# is restored tonight, the entire valley will be exposed.", ("urgency", S("high")), ("scope", S("regional")))
                ],
                ["smallThing"] = [new("a stolen medicine chest"), new("three missing pack animals"), new("a bundle of survey maps")],
                ["place"] = [new("Blackwater Ford"), new("the ruined observatory"), new("the old quarry"), new("Saint Orra's bridge")]
            },
            candidateCount: 24,
            scorers:
            [
                new("length", CandidateScorerKind.WordCount, .65, Ideal: 20, Minimum: 10, Maximum: 36),
                new("lexical", CandidateScorerKind.UniqueWordRatio, .3),
                new("features", CandidateScorerKind.FeatureCoverage, .2, Target: "urgency,scope")
            ]),

        SemanticTraceryRecipe(
            "phase6.semantic.faction-motto", "Semantic Faction Motto", TextContentKind.Faction,
            "Faction mottos selected by ethos while retaining a single reusable grammar.",
            "ethos", ["duty", "freedom", "mystery"], "duty",
            new Dictionary<string, IReadOnlyList<TraceryRuleDefinition>>(StringComparer.OrdinalIgnoreCase)
            {
                ["origin"] =
                [
                    Rule("#duty#", ("ethos", S("duty")), ("tone", S("formal"))),
                    Rule("#freedom#", ("ethos", S("freedom")), ("tone", S("defiant"))),
                    Rule("#mystery#", ("ethos", S("mystery")), ("tone", S("esoteric")))
                ],
                ["duty"] = [new("We Stand Where Others Yield"), new("Oath Before Glory"), new("The Watch Does Not End")],
                ["freedom"] = [new("No Crown Owns the Horizon"), new("Open Roads, Unbroken Hands"), new("We Choose Our Own Dawn")],
                ["mystery"] = [new("What Is Hidden Still Watches"), new("Beyond the Veil, Remember"), new("The Ninth Door Has No Key")]
            },
            candidateCount: 16,
            scorers:
            [
                new("brevity", CandidateScorerKind.WordCount, .6, Ideal: 5, Minimum: 3, Maximum: 9),
                new("variety", CandidateScorerKind.UniqueWordRatio, .3)
            ]),

        AgreementRecipe(),

        SemanticTraceryRecipe(
            "phase6.semantic.creature-description", "Semantic Creature Description", TextContentKind.Monster,
            "Habitat-aware creature description with typed numeric threat metadata.",
            "habitat", ["forest", "cave", "swamp"], "forest",
            new Dictionary<string, IReadOnlyList<TraceryRuleDefinition>>(StringComparer.OrdinalIgnoreCase)
            {
                ["origin"] =
                [
                    Rule("A long-limbed hunter that moves beneath #forestCover# and freezes whenever birds fall silent.", ("habitat", S("forest")), ("threat", N(.62))),
                    Rule("A pale burrower whose plated forelimbs scrape sparks from #caveStone#.", ("habitat", S("cave")), ("threat", N(.71))),
                    Rule("A reed-colored ambusher that exhales bubbles just before it rises from #swampWater#.", ("habitat", S("swamp")), ("threat", N(.79)))
                ],
                ["forestCover"] = [new("cedar boughs"), new("thorn hedges"), new("the fern-dark understory")],
                ["caveStone"] = [new("basalt walls"), new("old mine braces"), new("crystal seams")],
                ["swampWater"] = [new("black water"), new("algae mats"), new("the drowned reeds")]
            },
            candidateCount: 20,
            scorers:
            [
                new("length", CandidateScorerKind.WordCount, .5, Ideal: 18, Minimum: 10, Maximum: 32),
                new("features", CandidateScorerKind.FeatureCoverage, .3, Target: "habitat,threat"),
                new("variety", CandidateScorerKind.UniqueWordRatio, .25)
            ]),

        RankedMarkovRumor(),

        SemanticTraceryRecipe(
            "phase6.semantic.item-lore", "Semantic Item Lore", TextContentKind.Lore,
            "Item lore categorized by tone and ranked from a hidden pool for readable RPG database descriptions.",
            "tone", ["scholarly", "ominous", "personal"], "scholarly",
            new Dictionary<string, IReadOnlyList<TraceryRuleDefinition>>(StringComparer.OrdinalIgnoreCase)
            {
                ["origin"] =
                [
                    Rule("Catalogues from #archive# describe this #object# as #detail#.", ("tone", S("scholarly")), ("lore.kind", S("catalogue"))),
                    Rule("No owner kept this #object# for more than a season; #detail#.", ("tone", S("ominous")), ("lore.kind", S("warning"))),
                    Rule("A faded note tied to the #object# reads, ‘#memory#’", ("tone", S("personal")), ("lore.kind", S("memory")))
                ],
                ["archive"] = [new("the eastern monastery"), new("the royal survey office"), new("an abandoned guildhall")],
                ["object"] = [new("bronze clasp"), new("river-stone charm"), new("narrow silver knife")],
                ["detail"] = [new("older than the road beneath the capital"), new("made with a technique no surviving workshop claims"), new("warm to the touch whenever rain approaches")],
                ["memory"] = [new("Bring it home before winter."), new("I kept my promise. I hope you kept yours."), new("The hill looked smaller when we returned.")]
            },
            candidateCount: 24,
            scorers:
            [
                new("length", CandidateScorerKind.CharacterLength, .6, Ideal: 120, Minimum: 55, Maximum: 210),
                new("variety", CandidateScorerKind.UniqueWordRatio, .3),
                new("features", CandidateScorerKind.FeatureCoverage, .2, Target: "tone,lore.kind")
            ])
    ];

    private static TextRecipe SemanticTraceryRecipe(
        string id,
        string name,
        TextContentKind kind,
        string description,
        string parameterName,
        IReadOnlyList<string> choices,
        string defaultChoice,
        IReadOnlyDictionary<string, IReadOnlyList<TraceryRuleDefinition>> symbols,
        int candidateCount,
        IReadOnlyList<CandidateScorerDefinition> scorers)
    {
        var parameter = new Dictionary<string, RecipeParameterDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [parameterName] = new(parameterName, RecipeParameterType.Choice, false, JsonSerializer.SerializeToElement(defaultChoice),
                $"Semantic {parameterName} used to filter hidden candidates.", choices)
        };
        var grammar = new TraceryGrammarDocument(id + ".grammar", 1, name + " Grammar", symbols, Compatibility: TraceryCompatibilityMode.PlayromaExtended);
        using var grammarDoc = JsonDocument.Parse(TraceryGrammarJson.SerializePlayroma(grammar, false));
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["grammar"] = grammarDoc.RootElement.Clone(),
            ["field"] = JsonSerializer.SerializeToElement("text"),
            ["isPrimary"] = JsonSerializer.SerializeToElement(true),
            ["emitMetadataTags"] = JsonSerializer.SerializeToElement(true),
            ["emitSemanticFeatures"] = JsonSerializer.SerializeToElement(true)
        };
        var pipeline = new CandidatePipelineDefinition(candidateCount,
            [new CandidateFilterDefinition("semantic-match", $"feature.{parameterName} == context.{parameterName}")],
            scorers, DiversityWeight: .2, MinimumDiversity: 0);
        return new TextRecipe(id, 1, name, kind, [new TextRecipeStage("grammar", TraceryGrammarStage.StageKind, Settings: settings)], parameter,
            Description: description, CandidatePipeline: pipeline);
    }

    private static TextRecipe AgreementRecipe()
    {
        var parameters = new Dictionary<string, RecipeParameterDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["gender"] = new("gender", RecipeParameterType.Choice, false, JsonSerializer.SerializeToElement("female"), "Pronoun feature.", ["female", "male", "neutral", "nonbinary"]),
            ["number"] = new("number", RecipeParameterType.Choice, false, JsonSerializer.SerializeToElement("singular"), "Grammatical number.", ["singular", "plural"]),
            ["person"] = new("person", RecipeParameterType.Integer, false, JsonSerializer.SerializeToElement(3), "Grammatical person (1-3).")
        };
        var template = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["template"] = JsonSerializer.SerializeToElement("At the old gate, {pronoun:subject} {verb:watch} the road and {be} ready to defend {pronoun:possessive} people."),
            ["field"] = JsonSerializer.SerializeToElement("text"), ["isPrimary"] = JsonSerializer.SerializeToElement(true)
        };
        var annotate = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["fromContext"] = JsonSerializer.SerializeToElement(new Dictionary<string, string>
            {
                ["grammar.gender"] = "gender", ["grammar.number"] = "number", ["grammar.person"] = "person"
            })
        };
        var unify = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["requirements"] = JsonSerializer.SerializeToElement(new Dictionary<string, string>
            {
                ["grammar.gender"] = "$context.gender", ["grammar.number"] = "$context.number", ["grammar.person"] = "$context.person"
            }),
            ["bindMissing"] = JsonSerializer.SerializeToElement(true)
        };
        var agreement = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase) { ["target"] = JsonSerializer.SerializeToElement("primary") };
        var constraint = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["predicate"] = JsonSerializer.SerializeToElement("feature.grammar.number == context.number && feature.grammar.gender == context.gender")
        };
        var scorers = JsonSerializer.SerializeToElement(new CandidateScorerDefinition[]
        {
            new("semanticCoverage", CandidateScorerKind.FeatureCoverage, 1, Target: "grammar.gender,grammar.number,grammar.person")
        }, RecipeJson.Options);
        return new TextRecipe("phase6.semantic.agreement-line", 1, "Semantic Agreement Line", TextContentKind.Dialogue,
        [
            new TextRecipeStage("template", TemplateStage.StageKind, Settings: template),
            new TextRecipeStage("features", SemanticAnnotateStage.StageKind, Settings: annotate),
            new TextRecipeStage("unify", FeatureUnifyStage.StageKind, Settings: unify),
            new TextRecipeStage("agreement", AgreementStage.StageKind, Settings: agreement),
            new TextRecipeStage("validate", SemanticConstraintStage.StageKind, Settings: constraint),
            new TextRecipeStage("score", SemanticScoreStage.StageKind, Settings: new Dictionary<string, JsonElement> { ["scorers"] = scorers })
        ], parameters, Description: "Demonstrates typed grammatical features, feature unification, safe semantic constraints and surface agreement.");
    }

    private static TextRecipe RankedMarkovRumor()
    {
        const string corpus = "The bell keeper saw lanterns moving along the abandoned ridge after midnight.\nA trader bought every cracked mirror in the market and left before dawn.\nThree fishermen heard knocking beneath their boat where the river is deepest.\nThe old orchard has fruit again, although no one has tended it in twenty years.\nA courier arrived with a royal seal belonging to a king dead for a century.\nSomeone has been leaving fresh bread outside the sealed mine entrance.\nThe west-road shrine was found facing the opposite direction after the storm.\nChildren claim a woman in blue counts the windows of empty houses at sunset.";
        var markov = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["corpusText"] = JsonSerializer.SerializeToElement(corpus), ["corpusName"] = JsonSerializer.SerializeToElement("Ranked Rumor Corpus"),
            ["mode"] = JsonSerializer.SerializeToElement("word"), ["order"] = JsonSerializer.SerializeToElement(1), ["minimumOrder"] = JsonSerializer.SerializeToElement(1),
            ["minimumTokens"] = JsonSerializer.SerializeToElement(8), ["maximumTokens"] = JsonSerializer.SerializeToElement(28),
            ["maximumSimilarity"] = JsonSerializer.SerializeToElement(.94), ["candidateAttempts"] = JsonSerializer.SerializeToElement(96),
            ["field"] = JsonSerializer.SerializeToElement("text"), ["isPrimary"] = JsonSerializer.SerializeToElement(true)
        };
        var annotate = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["features"] = JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["tone"] = "grounded", ["genre"] = "rumor" })
        };
        var pipeline = new CandidatePipelineDefinition(14, Scorers:
        [
            new("originality", CandidateScorerKind.ExistingScore, 1.0, Target: "originality"),
            new("length", CandidateScorerKind.WordCount, .45, Ideal: 17, Minimum: 8, Maximum: 30),
            new("variety", CandidateScorerKind.UniqueWordRatio, .3)
        ], DiversityWeight: .35, MinimumDiversity: .15);
        return new TextRecipe("phase6.semantic.ranked-rumor", 1, "Ranked Tavern Rumor", TextContentKind.Rumor,
            [new TextRecipeStage("markov", MarkovGenerationStage.StageKind, Settings: markov), new TextRecipeStage("semantics", SemanticAnnotateStage.StageKind, Settings: annotate)],
            Description: "Markov generation ranked by originality, useful length, and diversity.", CandidatePipeline: pipeline);
    }

    private static TraceryRuleDefinition Rule(string text, params (string Key, SemanticFeatureValue Value)[] features)
        => new(text, Features: features.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase));
    private static SemanticFeatureValue S(string value) => SemanticFeatureValue.FromString(value);
    private static SemanticFeatureValue N(double value) => SemanticFeatureValue.FromNumber(value);
}
