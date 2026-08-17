using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Playroma.Text.Core.Narrative;

public enum StoryletBindingKind
{
    Character,
    Location,
    Item,
    Faction,
    Custom
}

public enum StoryletEffectKind
{
    Set,
    Increment,
    AddTag,
    RemoveTag
}

public sealed record StoryletBindingDefinition(
    string Id,
    StoryletBindingKind Kind,
    string CandidateContextKey,
    bool Required = true,
    bool Distinct = true,
    IReadOnlyList<string>? RequiredTags = null,
    string? Description = null)
{
    [JsonIgnore] public IReadOnlyList<string> EffectiveRequiredTags => RequiredTags ?? Array.Empty<string>();
}

public sealed record StoryletEffectDefinition(
    StoryletEffectKind Kind,
    string Key,
    object? Value = null,
    string? Description = null);

public sealed record StoryletRealizationDefinition(
    string RecipeId,
    string? VoiceProfileId = null,
    string? DialogueAct = null,
    string? Emotion = null,
    IReadOnlyDictionary<string, object?>? Parameters = null)
{
    [JsonIgnore] public IReadOnlyDictionary<string, object?> EffectiveParameters => Parameters ?? EmptyObjects;
    private static IReadOnlyDictionary<string, object?> EmptyObjects { get; } =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
}

public sealed record StoryletDefinition(
    string Id,
    int Version,
    string Name,
    string? Description = null,
    bool Enabled = true,
    double Weight = 1.0,
    double Salience = 1.0,
    bool OnceOnly = false,
    int CooldownTurns = 0,
    IReadOnlyList<string>? Prerequisites = null,
    IReadOnlyList<StoryletBindingDefinition>? Bindings = null,
    IReadOnlyList<StoryletEffectDefinition>? Effects = null,
    StoryletRealizationDefinition? Realization = null,
    IReadOnlyList<string>? Tags = null)
{
    [JsonIgnore] public IReadOnlyList<string> EffectivePrerequisites => Prerequisites ?? Array.Empty<string>();
    [JsonIgnore] public IReadOnlyList<StoryletBindingDefinition> EffectiveBindings => Bindings ?? Array.Empty<StoryletBindingDefinition>();
    [JsonIgnore] public IReadOnlyList<StoryletEffectDefinition> EffectiveEffects => Effects ?? Array.Empty<StoryletEffectDefinition>();
    [JsonIgnore] public IReadOnlyList<string> EffectiveTags => Tags ?? Array.Empty<string>();
}

public static class StoryletDefinitionValidator
{
    public static IReadOnlyList<string> Validate(StoryletDefinition storylet)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(storylet.Id)) errors.Add("Storylet ID is required.");
        if (storylet.Version <= 0) errors.Add("Storylet version must be positive.");
        if (string.IsNullOrWhiteSpace(storylet.Name)) errors.Add("Storylet name is required.");
        if (double.IsNaN(storylet.Weight) || double.IsInfinity(storylet.Weight) || storylet.Weight < 0) errors.Add("Storylet weight must be a finite non-negative number.");
        if (double.IsNaN(storylet.Salience) || double.IsInfinity(storylet.Salience) || storylet.Salience < 0) errors.Add("Storylet salience must be a finite non-negative number.");
        if (storylet.CooldownTurns < 0) errors.Add("Storylet cooldownTurns cannot be negative.");
        if (storylet.Realization is null || string.IsNullOrWhiteSpace(storylet.Realization.RecipeId)) errors.Add("Storylet realization.recipeId is required.");

        var duplicateBinding = storylet.EffectiveBindings.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => string.IsNullOrWhiteSpace(x.Key) || x.Count() > 1);
        if (duplicateBinding is not null) errors.Add("Storylet binding IDs must be non-empty and unique.");
        foreach (var binding in storylet.EffectiveBindings)
            if (string.IsNullOrWhiteSpace(binding.CandidateContextKey)) errors.Add($"Binding '{binding.Id}' requires candidateContextKey.");
        foreach (var effect in storylet.EffectiveEffects)
            if (string.IsNullOrWhiteSpace(effect.Key)) errors.Add("Storylet effects require a non-empty key.");
        return errors;
    }
}

public sealed record StoryletCandidate(
    string Id,
    string Name,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyDictionary<string, object?>? Values = null)
{
    [JsonIgnore] public IReadOnlyList<string> EffectiveTags => Tags ?? Array.Empty<string>();
    [JsonIgnore] public IReadOnlyDictionary<string, object?> EffectiveValues => Values ?? EmptyValues;
    private static IReadOnlyDictionary<string, object?> EmptyValues { get; } =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
}

public sealed record StoryletBinding(string Id, StoryletBindingKind Kind, StoryletCandidate Candidate);

public sealed record StoryletEvaluation(
    StoryletDefinition Storylet,
    bool Eligible,
    double Score,
    string? Reason = null,
    IReadOnlyList<StoryletBinding>? Bindings = null)
{
    [JsonIgnore] public IReadOnlyList<StoryletBinding> EffectiveBindings => Bindings ?? Array.Empty<StoryletBinding>();
}

public sealed record StoryletSelection(
    StoryletDefinition Storylet,
    double Score,
    IReadOnlyList<StoryletBinding> Bindings,
    ulong Seed,
    int Turn);

public sealed class StoryletRuntimeState
{
    public int Turn { get; set; }
    public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Tags { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> SeenCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> LastSelectedTurns { get; } = new(StringComparer.OrdinalIgnoreCase);

    public StoryletRuntimeSnapshot Capture() => new(
        Turn,
        new Dictionary<string, object?>(Values, StringComparer.OrdinalIgnoreCase),
        Tags.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        new Dictionary<string, int>(SeenCounts, StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, int>(LastSelectedTurns, StringComparer.OrdinalIgnoreCase));

    public static StoryletRuntimeState FromSnapshot(StoryletRuntimeSnapshot? snapshot)
    {
        var state = new StoryletRuntimeState { Turn = snapshot?.Turn ?? 0 };
        if (snapshot is null) return state;
        foreach (var pair in snapshot.Values) state.Values[pair.Key] = pair.Value;
        foreach (var tag in snapshot.Tags) state.Tags.Add(tag);
        foreach (var pair in snapshot.SeenCounts) state.SeenCounts[pair.Key] = pair.Value;
        foreach (var pair in snapshot.LastSelectedTurns) state.LastSelectedTurns[pair.Key] = pair.Value;
        return state;
    }
}

public sealed record StoryletRuntimeSnapshot(
    int Turn,
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, int> SeenCounts,
    IReadOnlyDictionary<string, int> LastSelectedTurns);

public interface IStoryletResolver
{
    ValueTask<IReadOnlyList<StoryletDefinition>> LoadAllAsync(CancellationToken cancellationToken = default);
    ValueTask<StoryletDefinition?> ResolveAsync(string id, CancellationToken cancellationToken = default);
}
