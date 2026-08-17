using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Selection;
using Playroma.Text.Generators.Templates;
using Playroma.Text.Generators.Transforms;

namespace Playroma.Text.Generators.Stages;

public sealed class AdLibStage : ITextGenerationStage
{
    public const string StageKind = "phase1.adlib";
    private readonly TemplateRenderer _renderer;
    public string Kind => StageKind;

    public AdLibStage(TextTransformRegistry? transforms = null) => _renderer = new TemplateRenderer(transforms);

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var template = StageSettingsReader.GetString(context.Stage, "template");
        if (template.Length == 0 || !StageSettingsReader.TryGet(context.Stage, "slots", out var slots) || slots.ValueKind != JsonValueKind.Object)
            return ValueTask.FromResult(StageSettingsReader.Invalid(context, "adlib requires a 'template' and object-valued 'slots'."));

        var values = StageUtilities.BuildTemplateValues(context);
        foreach (var slot in slots.EnumerateObject())
        {
            context.Budget.ConsumeCandidateAttempt();
            if (!TryResolveSlot(slot.Value, context, values, out var value, out var error))
                return ValueTask.FromResult(StageSettingsReader.Invalid(context, $"Ad-lib slot '{slot.Name}': {error}"));
            values[slot.Name] = value;
            context.Trace.Add(context.Stage.Id, "adlib.bind", output: value, metadata: new Dictionary<string, string> { ["slot"] = slot.Name });
        }

        var missingBehavior = StageSettingsReader.GetString(context.Stage, "missing", "error").Equals("empty", StringComparison.OrdinalIgnoreCase) ? MissingTemplateValueBehavior.Empty : MissingTemplateValueBehavior.Error;
        var rendered = _renderer.Render(template, values, context.Random.Fork("render"), new TemplateRenderOptions(MissingValueBehavior: missingBehavior));
        var diagnostics = new List<GenerationDiagnostic>();
        if (rendered.MissingKeys.Count > 0) diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.MissingTemplateValue, GenerationDiagnosticSeverity.Error, "Ad-lib template references missing values: " + string.Join(", ", rendered.MissingKeys), context.Stage.Id));
        if (rendered.UnknownTransforms.Count > 0) diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.UnknownTransform, GenerationDiagnosticSeverity.Error, "Unknown transforms: " + string.Join(", ", rendered.UnknownTransforms), context.Stage.Id));
        context.Trace.Add(context.Stage.Id, "adlib.render", input: template, output: rendered.Text);
        var bundle = StageUtilities.Write(context, rendered.Text, "text", "adlib");
        return ValueTask.FromResult(new GenerationStageResult(diagnostics.Count == 0, bundle, diagnostics));
    }

    private static bool TryResolveSlot(JsonElement slot, GenerationStageContext context, IReadOnlyDictionary<string, object?> values, out string value, out string error)
    {
        value = string.Empty; error = string.Empty;
        if (slot.ValueKind == JsonValueKind.String) { value = slot.GetString() ?? string.Empty; return true; }
        if (slot.ValueKind == JsonValueKind.Array)
        {
            var options = slot.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty).ToArray();
            if (options.Length == 0) { error = "array has no string values."; return false; }
            value = options[context.Random.NextInt(options.Length)]; return true;
        }
        if (slot.ValueKind != JsonValueKind.Object) { error = "slot must be a string, array, or object."; return false; }

        if (slot.TryGetProperty("fromContext", out var contextKey) && contextKey.ValueKind == JsonValueKind.String)
        {
            var key = contextKey.GetString() ?? string.Empty;
            if (values.TryGetValue(key, out var existing)) { value = Convert.ToString(existing, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty; return true; }
            error = $"context value '{key}' was not found."; return false;
        }
        if (slot.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            var options = items.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty).ToArray();
            if (options.Length == 0) { error = "items is empty."; return false; }
            value = options[context.Random.NextInt(options.Length)]; return true;
        }
        if (slot.TryGetProperty("weighted", out var weighted) && weighted.ValueKind == JsonValueKind.Array)
        {
            var entries = new List<WeightedText>();
            foreach (var item in weighted.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) { entries.Add(new WeightedText(item.GetString() ?? string.Empty)); continue; }
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("text", out var t) || t.ValueKind != JsonValueKind.String) continue;
                var weight = item.TryGetProperty("weight", out var w) && w.TryGetDouble(out var parsed) ? parsed : 1;
                entries.Add(new WeightedText(t.GetString() ?? string.Empty, weight));
            }
            if (entries.Count == 0) { error = "weighted has no valid entries."; return false; }
            try { value = WeightedSelector.Select(entries, x => x.Weight, context.Random).Text; return true; }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        error = "object slot needs fromContext, items, or weighted."; return false;
    }
}
