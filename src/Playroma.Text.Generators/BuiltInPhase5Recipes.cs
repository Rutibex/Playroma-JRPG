using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Stages;

namespace Playroma.Text.Generators;

/// <summary>Original demonstration corpora and recipes for the Phase 5 Markov/Corpus Lab.</summary>
public static class BuiltInPhase5Recipes
{
    public static IReadOnlyList<TextRecipe> All { get; } =
    [
        MarkovRecipe("phase5.markov.tavern-rumor", "Tavern Rumor — Markov", TextContentKind.Rumor,
            "The old mill wheel turns after midnight, though the stream has been dry for years.\nA merchant paid silver for every blue feather brought to his room.\nNo one from the eastern road has seen the moon since the bell tower cracked.\nThe ferryman refuses red coins and will not explain why.\nThree hunters followed singing into the pines and only their dog returned.\nThe baker swears a warm light moves beneath the cemetery hill.\nTravelers say the ruined watchtower has a new door where no door stood before.\nThe mayor's horse came home wearing a stranger's saddle.", "word", 1, 8, 24, .93),
        MarkovRecipe("phase5.markov.npc-chatter", "NPC Chatter — Markov", TextContentKind.Dialogue,
            "Morning. Roads are muddy again.\nIf you need rope, buy it before sunset.\nI heard wagons on the north road all night.\nThe well water tastes of iron today.\nDon't bother the guard captain before breakfast.\nSomeone left flowers at the old gate.\nPrices went up after the bridge washed out.\nThat storm came from nowhere.\nI would stay clear of the quarry after dark.\nThe inn has a new cook, thank the gods.", "word", 1, 4, 18, .94),
        MarkovRecipe("phase5.markov.eldritch-note", "Eldritch Note Fragment — Markov", TextContentKind.Note,
            "The seventh lamp must remain unlit. The stone remembers every hand that turns the key.\nDo not answer the voice below the cistern, even if it knows your childhood name.\nAt dusk the western wall becomes thinner than paper and the stars press close behind it.\nI counted nine doors this morning. Yesterday there were eight.\nThe chalk circle is not a prison. It is an invitation written backward.\nWhen the bell rings without a rope, cover every mirror and wait for dawn.", "character", 4, 40, 180, .90),
        MarkovRecipe("phase5.markov.quest-gossip", "Quest Gossip — Markov", TextContentKind.Quest,
            "A courier vanished between the orchard and the west bridge while carrying the magistrate's sealed ledger.\nMiners broke into an older tunnel and now refuse to return until someone finds the source of the whispering.\nA shepherd offers a reward for three missing lambs, but the tracks leading away are shaped like bare human feet.\nThe shrine keeper needs moonwater from the hill spring before the coming eclipse.\nA retired soldier wants his old banner recovered from bandits occupying the ruined customs house.\nThe herbalist needs ash-bloom gathered from the burned grove before the flowers close at dawn.", "word", 1, 10, 32, .94),
        MarkovRecipe("phase5.markov.chronicle", "Chronicle Fragment — Sentence Markov", TextContentKind.Lore,
            "The first winter ended with the river frozen from bank to bank. The council opened the grain vaults and sent riders south. No rider returned before spring.\nThe bronze gate was raised in the fifth year. Merchants began using the eastern road again. A market formed outside the walls every seventh day.\nDuring the red comet, fishermen abandoned the lower harbor. The lighthouse burned for three nights without fuel. Afterward the old keeper resigned and moved inland.\nThe duke's census counted fewer farms but more households. New wells were dug along the ridge. By harvest, three new villages had petitioned for charters.", "sentence", 1, 18, 60, .97),
        MarkovRecipe("phase5.markov.item-lore", "Item Lore Fragment — Markov", TextContentKind.Lore,
            "This blade was carried by messengers who crossed the salt flats before the imperial road existed.\nThe clasp bears the faded mark of a river guild that vanished after the flood year.\nA thin line of blue glass runs through the handle and warms when rain is near.\nThe maker's name has been filed away, but three workshop stamps remain beneath the guard.\nOld soldiers claim the metal rings differently when drawn beneath an open sky.\nThe leather wrapping was replaced many times, while the iron core shows almost no corrosion.", "word", 1, 9, 28, .94)
    ];

    private static TextRecipe MarkovRecipe(string id, string name, TextContentKind kind, string corpusText, string mode, int order, int minTokens, int maxTokens, double maxSimilarity)
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["corpusText"] = JsonSerializer.SerializeToElement(corpusText),
            ["corpusName"] = JsonSerializer.SerializeToElement(name + " Source"),
            ["mode"] = JsonSerializer.SerializeToElement(mode),
            ["order"] = JsonSerializer.SerializeToElement(order),
            ["minimumOrder"] = JsonSerializer.SerializeToElement(1),
            ["backoff"] = JsonSerializer.SerializeToElement(true),
            ["minimumTokens"] = JsonSerializer.SerializeToElement(minTokens),
            ["maximumTokens"] = JsonSerializer.SerializeToElement(maxTokens),
            ["maximumSimilarity"] = JsonSerializer.SerializeToElement(maxSimilarity),
            ["candidateAttempts"] = JsonSerializer.SerializeToElement(96),
            ["preventExactMatches"] = JsonSerializer.SerializeToElement(true),
            ["field"] = JsonSerializer.SerializeToElement("text"),
            ["isPrimary"] = JsonSerializer.SerializeToElement(true)
        };
        return new TextRecipe(id, 1, name, kind, [new TextRecipeStage("markov", MarkovGenerationStage.StageKind, Settings: settings)],
            Description: "Markov demonstration using an original built-in corpus. Copy to the project to edit settings or replace the source corpus.");
    }
}
