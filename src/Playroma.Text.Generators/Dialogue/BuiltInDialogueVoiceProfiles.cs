using Playroma.Text.Core.Dialogue;

namespace Playroma.Text.Generators.Dialogue;

/// <summary>Original Playroma voice profiles intended as useful starting points, not character stereotypes tied to real cultures.</summary>
public static class BuiltInDialogueVoiceProfiles
{
    public static IReadOnlyList<DialogueVoiceProfile> All { get; } =
    [
        new("common", 1, "Clear Adventurer", "Neutral, direct RPG dialogue with mild warmth.",
            Formality:.48, Verbosity:.46, Warmth:.58, Aggression:.18, Humor:.18, ContractionPreference:.62,
            ActOpeners:Phrases((DialogueAct.Greeting,["Hello.","Good to see you."]), (DialogueAct.Warning,["Listen carefully."]), (DialogueAct.QuestOffer,["I could use your help."])),
            Catchphrases:["Stay sharp."], Tags:["neutral","adventurer"]),

        new("courtly-noble", 1, "Courtly Noble", "Polished, measured and ceremonious without becoming unreadable.",
            Formality:.94, Verbosity:.68, Warmth:.42, Aggression:.12, Humor:.06, Archaic:.18, ContractionPreference:.08,
            Vocabulary:Words(("yes","certainly"),("no","I am afraid not"),("want","wish"),("help","assistance"),("tell","inform"),("thanks","my thanks")),
            ActOpeners:Phrases((DialogueAct.Greeting,["You have my attention.","Welcome." ]),(DialogueAct.Request,["If I may impose upon you,"]),(DialogueAct.QuestOffer,["There is a matter requiring discretion."])),
            ActClosers:Phrases((DialogueAct.Thanks,["You have my gratitude."]),(DialogueAct.Farewell,["May fortune attend you."])),
            TabooWords:["ain't"], Tags:["formal","noble"]),

        new("gruff-veteran", 1, "Gruff Veteran", "Short, practical sentences from someone who dislikes wasting words.",
            Formality:.34, Verbosity:.22, Warmth:.24, Aggression:.46, Humor:.12, ContractionPreference:.78,
            Vocabulary:Words(("dangerous","bad"),("enemy","target"),("enemies","targets"),("leave","move"),("understand","got it")),
            Catchphrases:["Keep moving.","Eyes open."], Fillers:["Right.","Listen."],
            ActOpeners:Phrases((DialogueAct.Warning,["Heads up."]),(DialogueAct.Command,["Move."]),(DialogueAct.BattleBark,["Contact!"])),
            Tags:["terse","soldier"]),

        new("cheerful-merchant", 1, "Cheerful Merchant", "Energetic, welcoming, and always a little interested in the deal.",
            Formality:.28, Verbosity:.64, Warmth:.9, Aggression:.05, Humor:.46, ContractionPreference:.82, FillerPreference:.18, CatchphrasePreference:.2, ExclamationPreference:.28,
            Vocabulary:Words(("buy","take home"),("items","goods"),("item","piece"),("money","coin"),("good","fine")),
            Catchphrases:["Fair price, fair deal!","Have a look around!"], Fillers:["Ah!","Well now,"],
            ActOpeners:Phrases((DialogueAct.ShopGreeting,["Welcome, welcome!","Come in! You have excellent timing!"]),(DialogueAct.Greeting,["Good to see a new face!"])),
            Tags:["merchant","bright"]),

        new("suspicious-villager", 1, "Suspicious Villager", "Guarded local speech with pauses and wary questions.",
            Formality:.37, Verbosity:.38, Warmth:.17, Aggression:.24, Humor:.04, ContractionPreference:.68, EllipsisPreference:.2,
            Vocabulary:Words(("stranger","outsider"),("people","folk"),("why","what for")),
            Catchphrases:["That's all I know."], Fillers:["Hmm.","Well..."],
            ActOpeners:Phrases((DialogueAct.Question,["Why are you asking?"]),(DialogueAct.Greeting,["Haven't seen you before."]),(DialogueAct.Rumor,["I didn't say this came from me..."])),
            Tags:["guarded","local"]),

        new("ancient-sage", 1, "Ancient Sage", "Measured, metaphorical and slightly archaic scholarly speech.",
            Formality:.82, Verbosity:.79, Warmth:.52, Aggression:.06, Humor:.1, Archaic:.72, ContractionPreference:.12,
            Vocabulary:Words(("before","ere"),("often","oft"),("perhaps","perchance"),("among","amongst"),("while","whilst")),
            Catchphrases:["So the old records say."],
            ActOpeners:Phrases((DialogueAct.Exposition,["Attend the shape of what came before."]),(DialogueAct.Warning,["Mark this well."]),(DialogueAct.Answer,["There are several answers, and none are simple."])),
            Tags:["sage","archaic"]),

        new("street-rogue", 1, "Street Rogue", "Casual, quick, teasing, and comfortable bending formal language.",
            Formality:.12, Verbosity:.44, Warmth:.44, Aggression:.22, Humor:.72, ContractionPreference:.94, FillerPreference:.2,
            Vocabulary:Words(("money","coin"),("guards","watch"),("friend","pal"),("danger","trouble"),("leave","scram")),
            Catchphrases:["Easy enough.","No trouble at all."], Fillers:["Look,","Hey,"],
            DialectRules:[new("going to","gonna"),new("want to","wanna")],
            Tags:["casual","rogue"]),

        new("cold-antagonist", 1, "Cold Antagonist", "Controlled, precise menace; anger is expressed through restraint rather than shouting.",
            Formality:.76, Verbosity:.45, Warmth:.02, Aggression:.72, Humor:.08, ContractionPreference:.16, ExclamationPreference:0,
            Vocabulary:Words(("kill","remove"),("enemy","obstacle"),("hate","dislike"),("afraid","concerned")),
            ActOpeners:Phrases((DialogueAct.Threat,["Consider your next action carefully."]),(DialogueAct.Refusal,["No."]),(DialogueAct.Warning,["This is your only warning."])),
            ActClosers:Phrases((DialogueAct.Threat,["There will not be another opportunity."])),
            Tags:["villain","controlled"]),

        new("earnest-young-hero", 1, "Earnest Young Hero", "Open-hearted, energetic and direct, with occasional nervous enthusiasm.",
            Formality:.26, Verbosity:.55, Warmth:.86, Aggression:.24, Humor:.34, ContractionPreference:.84, ExclamationPreference:.2,
            Catchphrases:["We can do this!"], Fillers:["Okay,","Right,"],
            ActOpeners:Phrases((DialogueAct.Agreement,["Absolutely!"]),(DialogueAct.QuestAccept,["I'll do it."]),(DialogueAct.Thanks,["Thank you. Really."])),
            Tags:["heroic","warm"])
    ];

    public static DialogueVoiceProfile Default => All[0];

    private static IReadOnlyDictionary<string,string> Words(params (string From,string To)[] pairs) =>
        pairs.ToDictionary(x=>x.From,x=>x.To,StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string,IReadOnlyList<string>> Phrases(params (DialogueAct Act,string[] Values)[] values) =>
        values.ToDictionary(x=>x.Act.ToString(),x=>(IReadOnlyList<string>)x.Values,StringComparer.OrdinalIgnoreCase);
}
