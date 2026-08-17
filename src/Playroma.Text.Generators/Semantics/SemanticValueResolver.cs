using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Semantics;

namespace Playroma.Text.Generators.Semantics;

internal static class SemanticValueResolver
{
    public static SemanticFeatureValue ResolveOrLiteral(string? source, GenerationStageContext context)
    {
        if (string.IsNullOrWhiteSpace(source)) return SemanticFeatureValue.FromString(string.Empty);
        if (!source.StartsWith('$')) return SemanticFeatureValue.FromString(source);
        var key = source[1..];
        if (key.StartsWith("context.", StringComparison.OrdinalIgnoreCase))
        {
            var name = key[8..];
            return context.Context.Data.TryGetValue(name, out var value) ? SemanticFeatureValue.FromObject(value) : SemanticFeatureValue.FromString(string.Empty);
        }
        if (key.StartsWith("feature.", StringComparison.OrdinalIgnoreCase))
        {
            var name = key[8..];
            return context.WorkingBundle.Features.TryGetValue(name, out var value) ? value : SemanticFeatureValue.FromString(string.Empty);
        }
        if (key.StartsWith("field.", StringComparison.OrdinalIgnoreCase))
        {
            var name = key[6..];
            var field = context.WorkingBundle.Fields.LastOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return SemanticFeatureValue.FromString(field?.Text ?? string.Empty);
        }
        if (key.StartsWith("tag.", StringComparison.OrdinalIgnoreCase))
        {
            var name = key[4..];
            return context.WorkingBundle.Tags.TryGetValue(name, out var value) ? SemanticFeatureValue.FromString(value) : SemanticFeatureValue.FromBoolean(context.Context.EffectiveTags.Contains(name));
        }
        if (key.StartsWith("score.", StringComparison.OrdinalIgnoreCase))
        {
            var name = key[6..];
            var score = context.WorkingBundle.Scores.LastOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return score is null ? SemanticFeatureValue.FromNumber(0) : SemanticFeatureValue.FromNumber(score.Value);
        }
        if (context.WorkingBundle.Features.TryGetValue(key, out var feature)) return feature;
        if (context.Context.Data.TryGetValue(key, out var contextValue)) return SemanticFeatureValue.FromObject(contextValue);
        return SemanticFeatureValue.FromString(string.Empty);
    }
}
