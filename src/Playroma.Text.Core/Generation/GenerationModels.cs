using System.Collections.ObjectModel;
using Playroma.Text.Core.Semantics;

namespace Playroma.Text.Core.Generation;

public enum TextContentKind
{
    Unknown,
    PersonName,
    PlaceName,
    CountryName,
    ObjectName,
    Item,
    Weapon,
    Armor,
    Skill,
    Spell,
    Monster,
    Enemy,
    Faction,
    Dialogue,
    Quest,
    Event,
    Rumor,
    Lore,
    Note,
    Description,
    Custom
}

public sealed record GenerationContext(
    string? ProjectId = null,
    string? TargetKind = null,
    string? TargetId = null,
    IReadOnlyDictionary<string, object?>? Values = null,
    IReadOnlySet<string>? Tags = null)
{
    public IReadOnlyDictionary<string, object?> Data { get; } = Values ?? new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
    public IReadOnlySet<string> EffectiveTags { get; } = Tags ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public T? Get<T>(string key)
    {
        if (!Data.TryGetValue(key, out var value) || value is null) return default;
        return value is T typed ? typed : default;
    }
}

public sealed record GeneratedField(string Name, string Text, string? SemanticRole = null, bool IsPrimary = false);

public sealed record GenerationScore(string Name, double Value, string? Explanation = null);

public sealed record GenerationProvenance(
    string RecipeId,
    int RecipeVersion,
    ulong Seed,
    string RecipeFingerprint,
    DateTimeOffset GeneratedAtUtc,
    string EngineVersion = TextEngineInfo.EngineVersion);

public sealed record GeneratedTextBundle
{
    public TextContentKind Kind { get; init; } = TextContentKind.Unknown;
    public string PrimaryText { get; init; } = string.Empty;
    public IReadOnlyList<GeneratedField> Fields { get; init; } = Array.Empty<GeneratedField>();
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    public IReadOnlyDictionary<string, SemanticFeatureValue> Features { get; init; } = SemanticFeatureUnifier.Empty;
    public IReadOnlyList<GenerationScore> Scores { get; init; } = Array.Empty<GenerationScore>();
    public GenerationProvenance? Provenance { get; init; }
    public GenerationTrace? Trace { get; init; }
    public IReadOnlyList<GenerationDiagnostic> Diagnostics { get; init; } = Array.Empty<GenerationDiagnostic>();

    public GeneratedTextBundle WithFeature(string name, SemanticFeatureValue value)
    {
        var features = Features.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        features[name] = value;
        return this with { Features = new ReadOnlyDictionary<string, SemanticFeatureValue>(features) };
    }

    public GeneratedTextBundle WithFeatures(IEnumerable<KeyValuePair<string, SemanticFeatureValue>> values)
    {
        var features = Features.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values) features[pair.Key] = pair.Value;
        return this with { Features = new ReadOnlyDictionary<string, SemanticFeatureValue>(features) };
    }

    public GeneratedTextBundle WithField(string name, string text, string? semanticRole = null, bool isPrimary = false)
    {
        var fields = Fields.Where(f => !string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        fields.Add(new GeneratedField(name, text, semanticRole, isPrimary));
        return this with { Fields = fields, PrimaryText = isPrimary ? text : PrimaryText };
    }
}

public static class TextEngineInfo
{
    public const string EngineVersion = "1.0.0-phase14";
}
