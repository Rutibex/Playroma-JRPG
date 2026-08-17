using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Playroma.Text.Core.Dialogue;

public enum DialogueAct
{
    Greeting,
    Farewell,
    SmallTalk,
    Question,
    Answer,
    Warning,
    Threat,
    Request,
    Command,
    Agreement,
    Refusal,
    Apology,
    Thanks,
    Compliment,
    Insult,
    Rumor,
    Exposition,
    QuestOffer,
    QuestAccept,
    QuestComplete,
    ShopGreeting,
    ShopFarewell,
    BattleBark,
    Victory,
    Defeat,
    Custom
}

public enum DialogueEmotion
{
    Neutral,
    Friendly,
    Warm,
    Happy,
    Excited,
    Amused,
    Nervous,
    Afraid,
    Sad,
    Tired,
    Suspicious,
    Solemn,
    Proud,
    Ashamed,
    Angry,
    Hostile
}

/// <summary>Safe literal rewrite used by the Phase 10 voice realization layer.</summary>
public sealed record DialogueRewriteRule(
    string From,
    string To,
    bool WholeWord = true,
    bool CaseInsensitive = true);

/// <summary>
/// Serializable authoring profile describing how an RPG character tends to speak. Values are
/// normalized to 0..1 so the editor can expose them as friendly sliders without engine-specific knobs.
/// </summary>
public sealed record DialogueVoiceProfile(
    string Id,
    int Version,
    string Name,
    string? Description = null,
    double Formality = .5,
    double Verbosity = .5,
    double Warmth = .5,
    double Aggression = .2,
    double Humor = .2,
    double Archaic = 0,
    double ContractionPreference = .5,
    double FillerPreference = .08,
    double CatchphrasePreference = .08,
    double ExclamationPreference = .08,
    double EllipsisPreference = .03,
    DialogueEmotion DefaultEmotion = DialogueEmotion.Neutral,
    IReadOnlyDictionary<string, string>? Vocabulary = null,
    IReadOnlyList<DialogueRewriteRule>? DialectRules = null,
    IReadOnlyList<string>? Catchphrases = null,
    IReadOnlyList<string>? Fillers = null,
    IReadOnlyList<string>? TabooWords = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ActOpeners = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ActClosers = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? EmotionOpeners = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? EmotionClosers = null,
    IReadOnlyList<string>? Tags = null)
{
    [JsonIgnore] public IReadOnlyDictionary<string, string> EffectiveVocabulary { get; } =
        Vocabulary ?? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    [JsonIgnore] public IReadOnlyList<DialogueRewriteRule> EffectiveDialectRules { get; } = DialectRules ?? [];
    [JsonIgnore] public IReadOnlyList<string> EffectiveCatchphrases { get; } = Catchphrases ?? [];
    [JsonIgnore] public IReadOnlyList<string> EffectiveFillers { get; } = Fillers ?? [];
    [JsonIgnore] public IReadOnlyList<string> EffectiveTabooWords { get; } = TabooWords ?? [];
    [JsonIgnore] public IReadOnlyDictionary<string, IReadOnlyList<string>> EffectiveActOpeners { get; } = NormalizePhraseMap(ActOpeners);
    [JsonIgnore] public IReadOnlyDictionary<string, IReadOnlyList<string>> EffectiveActClosers { get; } = NormalizePhraseMap(ActClosers);
    [JsonIgnore] public IReadOnlyDictionary<string, IReadOnlyList<string>> EffectiveEmotionOpeners { get; } = NormalizePhraseMap(EmotionOpeners);
    [JsonIgnore] public IReadOnlyDictionary<string, IReadOnlyList<string>> EffectiveEmotionClosers { get; } = NormalizePhraseMap(EmotionClosers);
    [JsonIgnore] public IReadOnlyList<string> EffectiveTags { get; } = Tags ?? [];

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyPhrases { get; } =
        new ReadOnlyDictionary<string, IReadOnlyList<string>>(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> NormalizePhraseMap(IReadOnlyDictionary<string, IReadOnlyList<string>>? source)
    {
        if (source is null || source.Count == 0) return EmptyPhrases;
        return new ReadOnlyDictionary<string, IReadOnlyList<string>>(source.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase));
    }
}

public static class DialogueVoiceProfileValidator
{
    public static IReadOnlyList<string> Validate(DialogueVoiceProfile profile)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.Id)) errors.Add("Voice profile ID is required.");
        if (string.IsNullOrWhiteSpace(profile.Name)) errors.Add("Voice profile name is required.");
        if (profile.Version <= 0) errors.Add("Voice profile version must be positive.");

        Check01(profile.Formality, nameof(profile.Formality));
        Check01(profile.Verbosity, nameof(profile.Verbosity));
        Check01(profile.Warmth, nameof(profile.Warmth));
        Check01(profile.Aggression, nameof(profile.Aggression));
        Check01(profile.Humor, nameof(profile.Humor));
        Check01(profile.Archaic, nameof(profile.Archaic));
        Check01(profile.ContractionPreference, nameof(profile.ContractionPreference));
        Check01(profile.FillerPreference, nameof(profile.FillerPreference));
        Check01(profile.CatchphrasePreference, nameof(profile.CatchphrasePreference));
        Check01(profile.ExclamationPreference, nameof(profile.ExclamationPreference));
        Check01(profile.EllipsisPreference, nameof(profile.EllipsisPreference));

        foreach (var rule in profile.EffectiveDialectRules)
            if (string.IsNullOrWhiteSpace(rule.From)) errors.Add("Dialect rewrite rules require a non-empty source phrase.");

        return errors;

        void Check01(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1)
                errors.Add($"{name} must be between 0 and 1.");
        }
    }
}

public sealed record DialogueVoiceContext(
    string? ProfileId = null,
    string? SpeakerName = null,
    DialogueAct? Act = null,
    DialogueEmotion? Emotion = null,
    double RelationshipAffinity = 0,
    double RelationshipTrust = 0,
    string? ListenerName = null,
    IReadOnlyDictionary<string, object?>? State = null)
{
    [JsonIgnore] public IReadOnlyDictionary<string, object?> EffectiveState { get; } =
        State ?? new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
}

public interface IDialogueVoiceResolver
{
    ValueTask<DialogueVoiceProfile?> ResolveAsync(string id, CancellationToken cancellationToken = default);
}

public sealed record DialogueVoiceAssignment(
    string TargetKey,
    string ProfileId,
    string? SpeakerName = null,
    DateTimeOffset? UpdatedUtc = null);

public sealed record DialogueVoiceAssignmentDocument(
    int Version,
    IReadOnlyList<DialogueVoiceAssignment> Assignments)
{
    public static int CurrentVersion => 1;
}
