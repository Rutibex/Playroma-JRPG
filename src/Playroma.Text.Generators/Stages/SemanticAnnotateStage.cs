using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Semantics;

namespace Playroma.Text.Generators.Stages;

public sealed class SemanticAnnotateStage : ITextGenerationStage
{
    public const string StageKind = "phase6.semantic-annotate";
    public string Kind => StageKind;

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var features = context.WorkingBundle.Features.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<GenerationDiagnostic>();

        if (StageSettingsReader.TryGet(context.Stage, "features", out var literal) && literal.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in literal.EnumerateObject())
            {
                try { features[property.Name] = SemanticFeatureValue.FromJsonElement(property.Value); }
                catch (Exception ex) { diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.InvalidSemanticFeature, GenerationDiagnosticSeverity.Warning, $"Could not read semantic feature '{property.Name}'.", context.Stage.Id, ex.Message)); }
            }
        }

        ImportMapped(context, "fromContext", features, (source) => context.Context.Data.TryGetValue(source, out var value) ? SemanticFeatureValue.FromObject(value) : null);
        ImportMapped(context, "fromTags", features, (source) => context.WorkingBundle.Tags.TryGetValue(source, out var value) ? SemanticFeatureValue.FromString(value) : null);
        ImportMapped(context, "fromFields", features, (source) =>
        {
            var field = context.WorkingBundle.Fields.LastOrDefault(x => x.Name.Equals(source, StringComparison.OrdinalIgnoreCase));
            return field is null ? null : SemanticFeatureValue.FromString(field.Text);
        });

        var bundle = context.WorkingBundle with { Features = new System.Collections.ObjectModel.ReadOnlyDictionary<string, SemanticFeatureValue>(features) };
        if (StageSettingsReader.GetBool(context.Stage, "emitStringTags", false))
        {
            var tags = bundle.Tags.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in features.Where(x => x.Value.Type is SemanticFeatureType.String or SemanticFeatureType.Integer or SemanticFeatureType.Number or SemanticFeatureType.Boolean)) tags[pair.Key] = pair.Value.DisplayText;
            bundle = bundle with { Tags = tags };
        }

        context.Trace.Add(context.Stage.Id, "semantic.annotate", output: bundle.PrimaryText, metadata: new Dictionary<string, string>
        {
            ["featureCount"] = features.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
        return ValueTask.FromResult(new GenerationStageResult(true, bundle, diagnostics));
    }

    private static void ImportMapped(GenerationStageContext context, string setting, Dictionary<string, SemanticFeatureValue> features, Func<string, SemanticFeatureValue?> resolver)
    {
        if (!StageSettingsReader.TryGet(context.Stage, setting, out var map) || map.ValueKind != JsonValueKind.Object) return;
        foreach (var property in map.EnumerateObject())
        {
            var source = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
            if (string.IsNullOrWhiteSpace(source)) continue;
            var value = resolver(source!);
            if (value is not null) features[property.Name] = value;
        }
    }
}
