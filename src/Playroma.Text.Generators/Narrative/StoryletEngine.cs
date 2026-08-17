using System.Collections;
using System.Globalization;
using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Narrative;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators.Candidates;
using Playroma.Text.Generators.Semantics;

namespace Playroma.Text.Generators.Narrative;

public sealed class StoryletEngine
{
    public IReadOnlyList<StoryletEvaluation> Evaluate(
        IEnumerable<StoryletDefinition> storylets,
        GenerationContext baseContext,
        StoryletRuntimeState state,
        ulong seed)
    {
        ArgumentNullException.ThrowIfNull(storylets);
        ArgumentNullException.ThrowIfNull(baseContext);
        ArgumentNullException.ThrowIfNull(state);
        var result = new List<StoryletEvaluation>();
        var rootRandom = new Xoshiro256StarStar(seed);

        foreach (StoryletDefinition storylet in storylets.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            string? reason = null;
            var validation = StoryletDefinitionValidator.Validate(storylet);
            if (validation.Count != 0) reason = string.Join(" ", validation);
            else if (!storylet.Enabled) reason = "Storylet is disabled.";
            else if (storylet.OnceOnly && state.SeenCounts.TryGetValue(storylet.Id, out int seen) && seen > 0) reason = "Storylet is once-only and has already fired.";
            else if (storylet.CooldownTurns > 0 && state.LastSelectedTurns.TryGetValue(storylet.Id, out int last) && state.Turn - last < storylet.CooldownTurns)
                reason = $"Cooldown active for {storylet.CooldownTurns - (state.Turn - last)} more turn(s).";

            var bindings = new List<StoryletBinding>();
            if (reason is null)
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (StoryletBindingDefinition definition in storylet.EffectiveBindings)
                {
                    var candidates = ExtractCandidates(baseContext, state, definition.CandidateContextKey)
                        .Where(x => definition.EffectiveRequiredTags.All(tag => x.EffectiveTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                        .Where(x => !definition.Distinct || !used.Contains(x.Id))
                        .ToArray();
                    if (candidates.Length == 0)
                    {
                        if (definition.Required) { reason = $"No candidate available for binding '{definition.Id}'."; break; }
                        continue;
                    }
                    IDeterministicRandom random = rootRandom.Fork(storylet.Id + ":binding:" + definition.Id);
                    StoryletCandidate selected = candidates[random.NextInt(candidates.Length)];
                    bindings.Add(new StoryletBinding(definition.Id, definition.Kind, selected));
                    if (definition.Distinct) used.Add(selected.Id);
                }
            }

            // Evaluate prerequisites after binding so predicates may reference binding.<id>.* values.
            if (reason is null)
            {
                var context = MergeContext(baseContext, state, bindings, storylet);
                foreach (string predicate in storylet.EffectivePrerequisites)
                {
                    SemanticPredicateResult evaluation = SemanticPredicateEvaluator.Evaluate(predicate, context, new GeneratedTextBundle());
                    if (!evaluation.Success) { reason = "Invalid prerequisite: " + evaluation.Error; break; }
                    if (!evaluation.Value) { reason = "Prerequisite not satisfied: " + predicate; break; }
                }
            }

            int timesSeen = state.SeenCounts.TryGetValue(storylet.Id, out int count) ? count : 0;
            double novelty = 1.0 / (1.0 + timesSeen * .6);
            double score = Math.Max(0, storylet.Weight) * Math.Max(0, storylet.Salience) * novelty;
            result.Add(new StoryletEvaluation(storylet, reason is null && score > 0, score, reason, bindings));
        }
        return result;
    }

    public StoryletSelection? Select(
        IEnumerable<StoryletDefinition> storylets,
        GenerationContext baseContext,
        StoryletRuntimeState state,
        ulong seed)
    {
        var evaluations = Evaluate(storylets, baseContext, state, seed).Where(x => x.Eligible && x.Score > 0).ToArray();
        if (evaluations.Length == 0) return null;
        double total = evaluations.Sum(x => x.Score);
        IDeterministicRandom random = new Xoshiro256StarStar(seed).Fork("storylet-selection");
        double roll = random.NextDouble() * total;
        double cumulative = 0;
        foreach (StoryletEvaluation evaluation in evaluations.OrderBy(x => x.Storylet.Id, StringComparer.OrdinalIgnoreCase))
        {
            cumulative += evaluation.Score;
            if (roll <= cumulative)
                return new StoryletSelection(evaluation.Storylet, evaluation.Score, evaluation.EffectiveBindings, seed, state.Turn);
        }
        StoryletEvaluation last = evaluations[^1];
        return new StoryletSelection(last.Storylet, last.Score, last.EffectiveBindings, seed, state.Turn);
    }

    public void ApplyEffects(StoryletSelection selection, StoryletRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(state);
        foreach (StoryletEffectDefinition effect in selection.Storylet.EffectiveEffects)
        {
            switch (effect.Kind)
            {
                case StoryletEffectKind.Set:
                    state.Values[effect.Key] = effect.Value;
                    break;
                case StoryletEffectKind.Increment:
                    state.Values.TryGetValue(effect.Key, out object? current);
                    double before = TryNumber(current, out double n) ? n : 0;
                    double delta = TryNumber(effect.Value, out double d) ? d : 1;
                    double after = before + delta;
                    state.Values[effect.Key] = Math.Abs(after - Math.Round(after)) < 1e-12 ? (object)(long)Math.Round(after) : after;
                    break;
                case StoryletEffectKind.AddTag:
                    state.Tags.Add(effect.Key);
                    break;
                case StoryletEffectKind.RemoveTag:
                    state.Tags.Remove(effect.Key);
                    break;
            }
        }
        state.SeenCounts[selection.Storylet.Id] = state.SeenCounts.TryGetValue(selection.Storylet.Id, out int seen) ? seen + 1 : 1;
        state.LastSelectedTurns[selection.Storylet.Id] = state.Turn;
        state.Turn++;
    }

    public static GenerationContext MergeContext(GenerationContext baseContext, StoryletRuntimeState state, IEnumerable<StoryletBinding>? bindings = null, StoryletDefinition? storylet = null)
    {
        var values = baseContext.Data.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in state.Values)
        {
            values[pair.Key] = pair.Value;
            values["storylet.state." + pair.Key] = pair.Value;
        }
        values["storylet.turn"] = state.Turn;
        if (storylet is not null)
        {
            values["storylet.id"] = storylet.Id;
            values["storylet.name"] = storylet.Name;
            foreach (var tag in storylet.EffectiveTags) values["storylet.tag." + tag] = true;
        }
        if (bindings is not null)
        {
            foreach (StoryletBinding binding in bindings)
            {
                string prefix = "binding." + binding.Id + ".";
                values[prefix + "id"] = binding.Candidate.Id;
                values[prefix + "name"] = binding.Candidate.Name;
                values[binding.Id] = binding.Candidate.Name;
                foreach (var pair in binding.Candidate.EffectiveValues) values[prefix + pair.Key] = pair.Value;
            }
        }
        var tags = new HashSet<string>(baseContext.EffectiveTags, StringComparer.OrdinalIgnoreCase);
        tags.UnionWith(state.Tags);
        return new GenerationContext(baseContext.ProjectId, baseContext.TargetKind, baseContext.TargetId, values, tags);
    }

    private static IReadOnlyList<StoryletCandidate> ExtractCandidates(GenerationContext context, StoryletRuntimeState state, string key)
    {
        object? value = context.Data.TryGetValue(key, out var fromContext) ? fromContext : state.Values.TryGetValue(key, out var fromState) ? fromState : null;
        if (value is null) return Array.Empty<StoryletCandidate>();
        if (value is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Array) return json.EnumerateArray().Select((x, i) =>
            {
                if (x.ValueKind == JsonValueKind.Object)
                {
                    string id = x.TryGetProperty("id", out var idNode) ? idNode.ToString() : i.ToString(CultureInfo.InvariantCulture);
                    string name = x.TryGetProperty("name", out var nameNode) ? nameNode.ToString() : id;
                    var tags = x.TryGetProperty("tags", out var tagsNode) && tagsNode.ValueKind == JsonValueKind.Array
                        ? tagsNode.EnumerateArray().Select(tag => tag.ToString()).Where(tag => !string.IsNullOrWhiteSpace(tag)).ToArray()
                        : Array.Empty<string>();
                    var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    if (x.TryGetProperty("values", out var valuesNode) && valuesNode.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty property in valuesNode.EnumerateObject())
                            values[property.Name] = JsonSerializer.Deserialize<object?>(property.Value.GetRawText());
                    }
                    return new StoryletCandidate(id, name, tags, values);
                }
                string text = x.ToString(); return new StoryletCandidate(text, text);
            }).Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToArray();
            string scalar = json.ToString();
            return string.IsNullOrWhiteSpace(scalar) ? Array.Empty<StoryletCandidate>() : [new StoryletCandidate(scalar, scalar)];
        }
        if (value is StoryletCandidate candidate) return [candidate];
        if (value is IEnumerable<StoryletCandidate> typed) return typed.ToArray();
        if (value is string single) return [new StoryletCandidate(single, single)];
        if (value is IEnumerable<string> strings) return strings.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => new StoryletCandidate(x, x)).ToArray();
        if (value is IDictionary dictionary)
        {
            var items = new List<StoryletCandidate>();
            foreach (DictionaryEntry entry in dictionary)
            {
                string id = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty;
                string name = Convert.ToString(entry.Value, CultureInfo.InvariantCulture) ?? id;
                if (!string.IsNullOrWhiteSpace(id)) items.Add(new StoryletCandidate(id, name));
            }
            return items;
        }
        if (value is IEnumerable enumerable)
        {
            var items = new List<StoryletCandidate>();
            foreach (object? item in enumerable)
            {
                if (item is null) continue;
                string text = Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text)) items.Add(new StoryletCandidate(text, text));
            }
            return items;
        }
        string fallback = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return string.IsNullOrWhiteSpace(fallback) ? Array.Empty<StoryletCandidate>() : [new StoryletCandidate(fallback, fallback)];
    }

    private static bool TryNumber(object? value, out double number)
    {
        try
        {
            if (value is null) { number = 0; return false; }
            if (value is JsonElement json)
            {
                if (json.ValueKind == JsonValueKind.Number && json.TryGetDouble(out number)) return true;
                value = json.ToString();
            }
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return !double.IsNaN(number) && !double.IsInfinity(number);
        }
        catch { number = 0; return false; }
    }
}

public sealed record StoryletRealizationResult(
    StoryletSelection Selection,
    GeneratedTextBundle Bundle,
    GenerationContext Context);

public sealed class StoryletNarrativeEngine
{
    private readonly CandidateGenerationEngine _generation;
    private readonly Func<string, TextRecipe?> _recipeResolver;

    public StoryletNarrativeEngine(CandidateGenerationEngine generation, Func<string, TextRecipe?> recipeResolver)
    {
        _generation = generation ?? throw new ArgumentNullException(nameof(generation));
        _recipeResolver = recipeResolver ?? throw new ArgumentNullException(nameof(recipeResolver));
    }

    public async ValueTask<StoryletRealizationResult> RealizeAsync(
        StoryletSelection selection,
        GenerationContext baseContext,
        StoryletRuntimeState state,
        CancellationToken cancellationToken = default)
    {
        StoryletRealizationDefinition realization = selection.Storylet.Realization
            ?? throw new InvalidOperationException("Storylet has no realization definition.");
        TextRecipe recipe = _recipeResolver(realization.RecipeId)
            ?? throw new InvalidOperationException($"Storylet realization recipe '{realization.RecipeId}' was not found.");
        GenerationContext context = StoryletEngine.MergeContext(baseContext, state, selection.Bindings, selection.Storylet);
        var values = context.Data.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in realization.EffectiveParameters) values[pair.Key] = pair.Value;
        if (!string.IsNullOrWhiteSpace(realization.VoiceProfileId)) values["voice.profileId"] = realization.VoiceProfileId;
        if (!string.IsNullOrWhiteSpace(realization.DialogueAct)) values["dialogue.act"] = realization.DialogueAct;
        if (!string.IsNullOrWhiteSpace(realization.Emotion)) values["voice.emotion"] = realization.Emotion;
        context = new GenerationContext(context.ProjectId, context.TargetKind, context.TargetId, values, context.EffectiveTags);
        ulong realizationSeed = SeedFactory.Derive(selection.Seed, "storylet-realization:" + selection.Storylet.Id);
        GeneratedTextBundle bundle = await _generation.GenerateBestAsync(recipe, context, realizationSeed, cancellationToken: cancellationToken).ConfigureAwait(false);
        var tags = bundle.Tags.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        tags["storylet.id"] = selection.Storylet.Id;
        tags["storylet.name"] = selection.Storylet.Name;
        tags["storylet.score"] = selection.Score.ToString("0.###", CultureInfo.InvariantCulture);
        bundle = bundle with { Tags = tags };
        return new StoryletRealizationResult(selection, bundle, context);
    }
}
