using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Tracery;
using Playroma.Text.Core.Semantics;
using Playroma.Text.Generators.Tracery;

namespace Playroma.Text.Generators.Stages;

public sealed class TraceryGrammarStage : ITextGenerationStage
{
    public const string StageKind = "phase3.tracery";
    private readonly TraceryEngine _engine;
    private readonly ITraceryGrammarResolver? _resolver;
    public string Kind => StageKind;

    public TraceryGrammarStage(TraceryEngine? engine = null, ITraceryGrammarResolver? resolver = null)
    {
        _engine = engine ?? new TraceryEngine();
        _resolver = resolver;
    }

    public async ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TraceryGrammarDocument? grammar = null;
        try
        {
            if (StageSettingsReader.TryGet(context.Stage, "grammar", out var inline) && inline.ValueKind == JsonValueKind.Object)
                grammar = TraceryGrammarJson.Parse(inline, context.Recipe.Id + ".inline", context.Recipe.Name + " Grammar");
            else
            {
                var grammarId = StageSettingsReader.GetString(context.Stage, "grammarId");
                if (!string.IsNullOrWhiteSpace(grammarId) && _resolver is not null)
                    grammar = await _resolver.ResolveAsync(grammarId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(GenerationDiagnosticCodes.InvalidTraceryGrammar,
                GenerationDiagnosticSeverity.Error, ex.Message, context.Stage.Id));
        }

        if (grammar is null)
            return StageSettingsReader.Invalid(context, "Tracery stage requires an inline 'grammar' object or a resolvable 'grammarId'.");

        JsonElement? supplied = StageSettingsReader.TryGet(context.Stage, "parameters", out var p) ? p : null;
        var binding = TraceryParameterBinder.Bind(grammar, context.Context, supplied, context.Stage.Id);
        if (binding.Diagnostics.Any(x => x.Severity == GenerationDiagnosticSeverity.Error))
            return new GenerationStageResult(false, context.WorkingBundle, binding.Diagnostics);

        var options = new TraceryExpansionOptions(
            Math.Clamp(StageSettingsReader.GetInt(context.Stage, "maxDepth", 64), 4, 256),
            Math.Clamp(StageSettingsReader.GetInt(context.Stage, "maxNodes", 4096), 32, 50_000),
            StageSettingsReader.GetBool(context.Stage, "executePostActions", false),
            StageSettingsReader.GetBool(context.Stage, "aggregateMetadata", true));
        var startRule = StageSettingsReader.GetString(context.Stage, "startRule");
        var start = StageSettingsReader.GetString(context.Stage, "start", grammar.Origin);
        var result = string.IsNullOrWhiteSpace(startRule)
            ? _engine.Expand(grammar, context.Context, context.Random, context.Budget, binding.Values, start, options, cancellationToken)
            : _engine.ExpandRule(grammar, startRule, context.Context, context.Random, context.Budget, binding.Values, options, cancellationToken);

        // Bridge the dedicated Tracery expansion tree into Playroma's shared generation trace.
        var idMap = new Dictionary<int, int>();
        foreach (var node in result.Trace.Nodes.OrderBy(x => x.Id))
        {
            int? parent = node.ParentId is { } localParent && idMap.TryGetValue(localParent, out var globalParent) ? globalParent : null;
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(node.Symbol)) metadata["symbol"] = node.Symbol!;
            if (!string.IsNullOrWhiteSpace(node.SelectedRule)) metadata["rule"] = node.SelectedRule!;
            if (node.Metadata is not null) foreach (var m in node.Metadata) metadata[m.Key] = m.Value;
            if (node.Features is not null) foreach (var f in node.Features) metadata["feature." + f.Key] = f.Value.DisplayText;
            idMap[node.Id] = context.Trace.Add(context.Stage.Id, "tracery." + node.Kind, parent, node.Raw, node.Output, metadata.Count > 0 ? metadata : null);
        }

        var field = StageSettingsReader.FieldName(context.Stage, "text");
        var primary = StageSettingsReader.IsPrimary(context.Stage, true);
        var tags = context.WorkingBundle.Tags.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        if (StageSettingsReader.GetBool(context.Stage, "emitMetadataTags", true)) foreach (var m in result.Metadata) tags[m.Key] = m.Value;
        var bundle = context.WorkingBundle.WithField(field, result.Text, "tracery", primary) with { Tags = tags };
        if (StageSettingsReader.GetBool(context.Stage, "emitSemanticFeatures", true) && result.EffectiveFeatures.Count > 0)
            bundle = bundle.WithFeatures(result.EffectiveFeatures);
        var diagnostics = binding.Diagnostics.Concat(result.Diagnostics.Select(d => d with { StageId = context.Stage.Id })).ToArray();
        return new GenerationStageResult(result.Success, bundle, diagnostics);
    }
}
