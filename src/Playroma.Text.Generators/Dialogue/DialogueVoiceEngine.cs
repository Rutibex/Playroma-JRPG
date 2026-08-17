using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Playroma.Text.Core.Dialogue;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Random;
using Playroma.Text.Core.Semantics;

namespace Playroma.Text.Generators.Dialogue;

public sealed class DialogueVoiceProfileRegistry
{
    private readonly Dictionary<string, DialogueVoiceProfile> _builtIns = BuiltInDialogueVoiceProfiles.All.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    private readonly IDialogueVoiceResolver? _external;

    public DialogueVoiceProfileRegistry(IDialogueVoiceResolver? external = null) => _external = external;

    public IReadOnlyList<DialogueVoiceProfile> BuiltIns => BuiltInDialogueVoiceProfiles.All;

    public async ValueTask<DialogueVoiceProfile?> ResolveAsync(string? id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return BuiltInDialogueVoiceProfiles.Default;
        if (_external is not null)
        {
            DialogueVoiceProfile? project = await _external.ResolveAsync(id, cancellationToken).ConfigureAwait(false);
            if (project is not null) return project;
        }
        return _builtIns.TryGetValue(id, out var builtIn) ? builtIn : null;
    }
}

public sealed record DialogueVoiceRealizationOptions(
    DialogueAct Act = DialogueAct.SmallTalk,
    DialogueEmotion Emotion = DialogueEmotion.Neutral,
    double RelationshipAffinity = 0,
    double RelationshipTrust = 0,
    string? SpeakerName = null,
    string? ListenerName = null,
    bool AddActFraming = true,
    bool AddEmotionFraming = true,
    bool AddCatchphrase = true,
    bool ApplyDialect = true,
    bool ApplyVocabulary = true,
    string TabooReplacement = "…");

public sealed class DialogueVoiceEngine
{
    private static readonly IReadOnlyDictionary<string,string> InformalContractions = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["do not"]="don't", ["does not"]="doesn't", ["did not"]="didn't", ["cannot"]="can't", ["can not"]="can't",
        ["will not"]="won't", ["would not"]="wouldn't", ["should not"]="shouldn't", ["could not"]="couldn't",
        ["I am"]="I'm", ["I will"]="I'll", ["I have"]="I've", ["you are"]="you're", ["you will"]="you'll",
        ["we are"]="we're", ["we will"]="we'll", ["they are"]="they're", ["it is"]="it's", ["that is"]="that's"
    };
    private static readonly IReadOnlyDictionary<string,string> FormalExpansions = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["don't"]="do not", ["doesn't"]="does not", ["didn't"]="did not", ["can't"]="cannot",
        ["won't"]="will not", ["wouldn't"]="would not", ["shouldn't"]="should not", ["couldn't"]="could not",
        ["I'm"]="I am", ["I'll"]="I will", ["I've"]="I have", ["you're"]="you are", ["you'll"]="you will",
        ["we're"]="we are", ["we'll"]="we will", ["they're"]="they are", ["it's"]="it is", ["that's"]="that is"
    };
    private static readonly IReadOnlyDictionary<string,string> ArchaicWords = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["before"]="ere", ["often"]="oft", ["perhaps"]="perchance", ["among"]="amongst", ["while"]="whilst", ["until"]="until such time as"
    };

    public GeneratedTextBundle Apply(GeneratedTextBundle bundle, DialogueVoiceProfile profile, DialogueVoiceRealizationOptions options, IDeterministicRandom random, IReadOnlyCollection<string>? fieldNames = null)
    {
        ArgumentNullException.ThrowIfNull(bundle); ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(options); ArgumentNullException.ThrowIfNull(random);
        HashSet<string>? selected = fieldNames is null ? null : new(fieldNames, StringComparer.OrdinalIgnoreCase);
        var fields = new List<GeneratedField>(bundle.Fields.Count);
        bool primaryRewritten = false;
        foreach (GeneratedField field in bundle.Fields)
        {
            bool rewrite = selected is null ? LooksLikeDialogueField(field) : selected.Contains(field.Name) || (!string.IsNullOrWhiteSpace(field.SemanticRole) && selected.Contains(field.SemanticRole));
            if (!rewrite) { fields.Add(field); continue; }
            string styled = Realize(field.Text, profile, options, random);
            fields.Add(field with { Text = styled });
            if (field.IsPrimary) primaryRewritten = true;
        }

        string primary = bundle.PrimaryText;
        if (!primaryRewritten && (selected is null || selected.Contains("primary") || (bundle.Fields.Count == 0 && !string.IsNullOrWhiteSpace(primary))))
            primary = Realize(primary, profile, options, random);
        if (primaryRewritten)
            primary = fields.FirstOrDefault(x => x.IsPrimary)?.Text ?? primary;

        var tags = bundle.Tags.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.OrdinalIgnoreCase);
        tags["voiceProfile"] = profile.Id;
        tags["dialogueAct"] = options.Act.ToString();
        tags["dialogueEmotion"] = options.Emotion.ToString();
        if (!string.IsNullOrWhiteSpace(options.SpeakerName)) tags["speaker"] = options.SpeakerName!;

        double effectiveWarmth = Clamp(profile.Warmth + Math.Max(0, options.RelationshipAffinity) * .15 + Math.Max(0, options.RelationshipTrust) * .08 - Math.Max(0, -options.RelationshipAffinity) * .12);
        double effectiveAggression = Clamp(profile.Aggression + Math.Max(0, -options.RelationshipAffinity) * .16 - Math.Max(0, options.RelationshipTrust) * .05);
        GeneratedTextBundle result = bundle with { Fields = fields, PrimaryText = primary, Tags = tags };
        result = result
            .WithFeature("voice.id", SemanticFeatureValue.FromString(profile.Id))
            .WithFeature("voice.name", SemanticFeatureValue.FromString(profile.Name))
            .WithFeature("voice.formality", SemanticFeatureValue.FromNumber(profile.Formality))
            .WithFeature("voice.verbosity", SemanticFeatureValue.FromNumber(profile.Verbosity))
            .WithFeature("voice.warmth", SemanticFeatureValue.FromNumber(profile.Warmth))
            .WithFeature("voice.aggression", SemanticFeatureValue.FromNumber(profile.Aggression))
            .WithFeature("voice.effectiveWarmth", SemanticFeatureValue.FromNumber(effectiveWarmth))
            .WithFeature("voice.effectiveAggression", SemanticFeatureValue.FromNumber(effectiveAggression))
            .WithFeature("dialogue.act", SemanticFeatureValue.FromString(options.Act.ToString()))
            .WithFeature("dialogue.emotion", SemanticFeatureValue.FromString(options.Emotion.ToString()))
            .WithFeature("relationship.affinity", SemanticFeatureValue.FromNumber(options.RelationshipAffinity))
            .WithFeature("relationship.trust", SemanticFeatureValue.FromNumber(options.RelationshipTrust));
        if (profile.EffectiveTags.Count > 0) result = result.WithFeature("voice.tags", SemanticFeatureValue.FromStrings(profile.EffectiveTags));
        if (!string.IsNullOrWhiteSpace(options.SpeakerName)) result = result.WithFeature("speaker.name", SemanticFeatureValue.FromString(options.SpeakerName));
        if (!string.IsNullOrWhiteSpace(options.ListenerName)) result = result.WithFeature("listener.name", SemanticFeatureValue.FromString(options.ListenerName));
        return result;
    }

    public string Realize(string input, DialogueVoiceProfile profile, DialogueVoiceRealizationOptions options, IDeterministicRandom random)
    {
        if (string.IsNullOrWhiteSpace(input)) return input ?? string.Empty;
        string text = input.Trim();
        if (options.ApplyVocabulary)
            foreach (var pair in profile.EffectiveVocabulary.OrderByDescending(x=>x.Key.Length)) text = ReplaceLiteral(text, pair.Key, pair.Value, true, true);

        if (profile.ContractionPreference >= .58)
            foreach (var pair in InformalContractions.OrderByDescending(x=>x.Key.Length)) text = ReplaceLiteral(text, pair.Key, pair.Value, true, true);
        else if (profile.ContractionPreference <= .28)
            foreach (var pair in FormalExpansions.OrderByDescending(x=>x.Key.Length)) text = ReplaceLiteral(text, pair.Key, pair.Value, true, true);

        if (profile.Archaic >= .45)
            foreach (var pair in ArchaicWords)
                if (random.NextDouble() < profile.Archaic * .72) text = ReplaceLiteral(text, pair.Key, pair.Value, true, true);

        if (options.ApplyDialect)
            foreach (DialogueRewriteRule rule in profile.EffectiveDialectRules)
                text = ReplaceLiteral(text, rule.From, rule.To, rule.WholeWord, rule.CaseInsensitive);

        foreach (string taboo in profile.EffectiveTabooWords.Where(x=>!string.IsNullOrWhiteSpace(x)))
            text = ReplaceLiteral(text, taboo, options.TabooReplacement, true, true);

        if (profile.Verbosity < .28) text = Shorten(text, profile.Verbosity);
        if (options.AddActFraming)
        {
            string? opener = Pick(profile.EffectiveActOpeners, options.Act.ToString(), random);
            string? closer = Pick(profile.EffectiveActClosers, options.Act.ToString(), random);
            if (!string.IsNullOrWhiteSpace(opener) && !StartsSimilarly(text, opener)) text = opener + " " + text;
            if (!string.IsNullOrWhiteSpace(closer) && !EndsSimilarly(text, closer)) text = text.TrimEnd() + " " + closer;
        }
        if (options.AddEmotionFraming)
        {
            string? opener = Pick(profile.EffectiveEmotionOpeners, options.Emotion.ToString(), random);
            string? closer = Pick(profile.EffectiveEmotionClosers, options.Emotion.ToString(), random);
            if (!string.IsNullOrWhiteSpace(opener) && random.NextDouble() < .55) text = opener + " " + text;
            if (!string.IsNullOrWhiteSpace(closer) && random.NextDouble() < .35) text = text.TrimEnd() + " " + closer;
        }
        double nervousFiller = options.Emotion is DialogueEmotion.Nervous or DialogueEmotion.Afraid ? .12 : 0;
        double lowTrustFiller = Math.Max(0, -options.RelationshipTrust) * .04;
        if (profile.EffectiveFillers.Count > 0 && random.NextDouble() < Clamp(profile.FillerPreference + Math.Max(0, .3 - profile.Formality) * .18 + nervousFiller + lowTrustFiller))
        {
            string filler = profile.EffectiveFillers[random.NextInt(profile.EffectiveFillers.Count)];
            if (!StartsSimilarly(text, filler)) text = filler.Trim() + " " + LowercaseFirstWhenNatural(text);
        }
        if (options.AddCatchphrase && profile.EffectiveCatchphrases.Count > 0 && random.NextDouble() < Clamp(profile.CatchphrasePreference + Math.Max(0, options.RelationshipAffinity) * .08 + Math.Max(0, options.RelationshipTrust) * .04 + profile.Humor * .03))
        {
            string phrase = profile.EffectiveCatchphrases[random.NextInt(profile.EffectiveCatchphrases.Count)];
            if (!EndsSimilarly(text, phrase)) text = text.TrimEnd() + " " + phrase.Trim();
        }

        double emotionalPause = options.Emotion is DialogueEmotion.Nervous or DialogueEmotion.Afraid or DialogueEmotion.Suspicious ? .18 : options.Emotion == DialogueEmotion.Tired ? .07 : 0;
        double ellipsisChance = Clamp(profile.EllipsisPreference + emotionalPause);
        if (ellipsisChance > .1 && random.NextDouble() < ellipsisChance) text = AddPause(text);

        double aggressivePunctuation = options.Act is DialogueAct.Warning or DialogueAct.Threat or DialogueAct.Command or DialogueAct.BattleBark ? profile.Aggression * .14 : 0;
        double emotionalExclamation = options.Emotion == DialogueEmotion.Excited ? .2 : options.Emotion is DialogueEmotion.Angry or DialogueEmotion.Hostile ? .12 : 0;
        if (options.Emotion is DialogueEmotion.Sad or DialogueEmotion.Solemn or DialogueEmotion.Tired) emotionalExclamation -= .08;
        double exclamationChance = Clamp(profile.ExclamationPreference + aggressivePunctuation + emotionalExclamation);
        if (exclamationChance > 0 && random.NextDouble() < exclamationChance) text = EmphasizeFinalPunctuation(text);

        return NormalizeSpacing(text);
    }

    private static bool LooksLikeDialogueField(GeneratedField field)
    {
        string key = (field.SemanticRole ?? field.Name).ToLowerInvariant();
        return field.IsPrimary || key is "text" or "prompt" or "message1" or "message2" || key.StartsWith("response", StringComparison.Ordinal) || key.StartsWith("line", StringComparison.Ordinal);
    }

    private static string? Pick(IReadOnlyDictionary<string,IReadOnlyList<string>> source, string key, IDeterministicRandom random)
    {
        if (!source.TryGetValue(key, out var values) || values.Count == 0) return null;
        return values[random.NextInt(values.Count)];
    }

    private static string ReplaceLiteral(string input, string from, string to, bool wholeWord, bool insensitive)
    {
        if (string.IsNullOrWhiteSpace(from)) return input;
        string pattern = Regex.Escape(from);
        if (wholeWord && char.IsLetterOrDigit(from[0]) && char.IsLetterOrDigit(from[^1])) pattern = @"\b" + pattern + @"\b";
        RegexOptions options = RegexOptions.CultureInvariant | (insensitive ? RegexOptions.IgnoreCase : RegexOptions.None);
        return Regex.Replace(input, pattern, m => MatchCase(to, m.Value), options, TimeSpan.FromMilliseconds(60));
    }

    private static string MatchCase(string replacement, string matched)
    {
        if (replacement.Length == 0) return replacement;
        if (matched.All(c=>!char.IsLetter(c) || char.IsUpper(c))) return replacement.ToUpperInvariant();
        if (char.IsUpper(matched[0])) return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        return replacement;
    }

    private static string Shorten(string text, double verbosity)
    {
        string[] sentences = Regex.Split(text.Trim(), @"(?<=[.!?])\s+").Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray();
        if (sentences.Length <= 1) return text;
        int keep = Math.Max(1, (int)Math.Ceiling(sentences.Length * (.35 + Clamp(verbosity) * .45)));
        return string.Join(" ", sentences.Take(keep));
    }

    private static string AddPause(string text)
    {
        int comma = text.IndexOf(", ", StringComparison.Ordinal);
        if (comma > 4) return text[..comma] + "..." + text[(comma+1)..];
        int space = text.IndexOf(' ', Math.Min(12, Math.Max(0, text.Length-1)));
        return space > 0 && space < text.Length - 1 ? text[..space] + "..." + text[space..] : text;
    }

    private static string EmphasizeFinalPunctuation(string text)
    {
        text = text.TrimEnd();
        if (text.EndsWith("!", StringComparison.Ordinal)) return text;
        if (text.EndsWith(".", StringComparison.Ordinal)) return text[..^1] + "!";
        if (!text.EndsWith("?", StringComparison.Ordinal)) return text + "!";
        return text;
    }

    private static string NormalizeSpacing(string text)
    {
        text = Regex.Replace(text, @"[ \t]{2,}", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(60));
        text = Regex.Replace(text, @"\s+([,.;!?])", "$1", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(60));
        return text.Trim();
    }

    private static string LowercaseFirstWhenNatural(string text) => text.Length > 1 && char.IsUpper(text[0]) && char.IsLower(text[1]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;
    private static bool StartsSimilarly(string text,string phrase) => text.StartsWith(phrase.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool EndsSimilarly(string text,string phrase) => text.TrimEnd().EndsWith(phrase.Trim(), StringComparison.OrdinalIgnoreCase);
    private static double Clamp(double value) => Math.Clamp(value,0,1);
}
