using System.Collections;
using System.Globalization;
using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Narrative;
using Playroma.Text.Core.Planning;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators.Semantics;

namespace Playroma.Text.Generators.Planning;

/// <summary>
/// Deterministic quest planner supporting authored HTN decomposition and bounded forward search.
/// Definitions remain declarative; no arbitrary code is executed from project files.
/// </summary>
public sealed class QuestPlanner
{
    public QuestPlan Plan(QuestGrammarDefinition grammar, GenerationContext baseContext, QuestWorldState initialState, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(grammar);
        ArgumentNullException.ThrowIfNull(baseContext);
        ArgumentNullException.ThrowIfNull(initialState);
        var errors = QuestGrammarValidator.Validate(grammar);
        if (errors.Count != 0) throw new InvalidOperationException("Quest grammar is invalid: " + string.Join(" ", errors));
        if (!grammar.Enabled) throw new InvalidOperationException("Quest grammar is disabled.");

        IReadOnlyList<StoryletBinding> globalBindings = ResolveBindings(grammar.EffectiveBindings, baseContext, initialState, seed, "quest");
        QuestPlan raw = string.IsNullOrWhiteSpace(grammar.RootTask)
            ? PlanBySearch(grammar, baseContext, initialState, globalBindings, seed)
            : PlanByDecomposition(grammar, baseContext, initialState, globalBindings, seed);
        IReadOnlyList<string> validation = QuestPlanValidator.Validate(raw, baseContext);
        return raw with { ValidationIssues = validation };
    }

    private QuestPlan PlanByDecomposition(QuestGrammarDefinition grammar, GenerationContext baseContext, QuestWorldState initialState, IReadOnlyList<StoryletBinding> globalBindings, ulong seed)
    {
        var actionById = grammar.EffectiveActions.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var methodsByTask = grammar.EffectiveMethods.GroupBy(x => x.TaskId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<QuestMethodDefinition>)x.OrderByDescending(m => m.Priority).ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);
        var primitive = new List<QuestActionDefinition>();
        int deepest = 0;
        int expanded = 0;
        var stateForMethodPredicates = initialState.Clone();

        void Expand(string taskId, int depth)
        {
            if (depth > grammar.EffectivePlanner.MaxDecompositionDepth) throw new InvalidOperationException("Quest HTN decomposition exceeded maxDecompositionDepth.");
            deepest = Math.Max(deepest, depth);
            expanded++;
            if (expanded > grammar.EffectivePlanner.MaxSearchNodes) throw new InvalidOperationException("Quest HTN decomposition exceeded maxSearchNodes.");
            if (actionById.TryGetValue(taskId, out QuestActionDefinition? action))
            {
                primitive.Add(action);
                return;
            }
            if (!methodsByTask.TryGetValue(taskId, out IReadOnlyList<QuestMethodDefinition>? methods) || methods.Count == 0)
                throw new InvalidOperationException($"No primitive action or HTN method can satisfy task '{taskId}'.");
            GenerationContext predicateContext = MergeState(baseContext, stateForMethodPredicates, globalBindings);
            var eligible = methods.Where(m => PredicatesHold(m.EffectivePreconditions, predicateContext)).ToArray();
            if (eligible.Length == 0) throw new InvalidOperationException($"No eligible HTN method can decompose task '{taskId}'.");
            // Preserve authored priority but deterministically vary among equal-priority alternatives.
            int bestPriority = eligible.Max(x => x.Priority);
            var best = eligible.Where(x => x.Priority == bestPriority).OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray();
            IDeterministicRandom random = new Xoshiro256StarStar(seed).Fork("quest-method:" + taskId + ":" + depth.ToString(CultureInfo.InvariantCulture));
            QuestMethodDefinition selected = best[random.NextInt(best.Length)];
            foreach (string subtask in selected.Subtasks) Expand(subtask, depth + 1);
        }

        Expand(grammar.RootTask!, 0);
        if (primitive.Count > grammar.EffectivePlanner.MaxPlanSteps)
            throw new InvalidOperationException($"Decomposed quest contains {primitive.Count} steps, exceeding maxPlanSteps={grammar.EffectivePlanner.MaxPlanSteps}.");

        var current = initialState.Clone();
        var steps = new List<QuestPlanStep>();
        double cost = 0;
        for (int i = 0; i < primitive.Count; i++)
        {
            QuestActionDefinition action = primitive[i];
            GenerationContext actionBase = MergeState(baseContext, current, globalBindings);
            IReadOnlyList<StoryletBinding> local = ResolveBindings(action.EffectiveBindings, actionBase, current, SeedFactory.Derive(seed, "action-bind:" + i), action.Id, globalBindings);
            GenerationContext context = MergeState(baseContext, current, globalBindings.Concat(local));
            if (!PredicatesHold(action.EffectivePreconditions, context))
                throw new InvalidOperationException($"HTN produced action '{action.Name}' whose preconditions are false at step {i + 1}.");
            QuestWorldSnapshot before = current.Capture();
            ApplyEffects(action.EffectiveEffects, current, globalBindings.Concat(local));
            cost += action.Cost;
            steps.Add(new(i, action, local, before, current.Capture(), cost));
        }
        return new(grammar, globalBindings, steps, initialState.Capture(), current.Capture(), seed,
            new("HTN", expanded, primitive.Count, 1, deepest, ["Used authored rootTask decomposition."]));
    }

    private QuestPlan PlanBySearch(QuestGrammarDefinition grammar, GenerationContext baseContext, QuestWorldState initialState, IReadOnlyList<StoryletBinding> globalBindings, ulong seed)
    {
        var root = new SearchNode(initialState.Clone(), Array.Empty<SearchAction>(), 0);
        var frontier = new PriorityQueue<SearchNode, double>();
        frontier.Enqueue(root, 0);
        var best = new Dictionary<string, double>(StringComparer.Ordinal) { [StateKey(root.State)] = 0 };
        int expanded = 0, generated = 1, maxFrontier = 1;
        SearchNode? solved = null;
        IDeterministicRandom rootRandom = new Xoshiro256StarStar(seed);

        while (frontier.TryDequeue(out SearchNode? node, out _))
        {
            if (GoalsHold(grammar.EffectiveGoalPredicates, MergeState(baseContext, node.State, globalBindings))) { solved = node; break; }
            if (node.Steps.Count >= grammar.EffectivePlanner.MaxPlanSteps) continue;
            if (++expanded > grammar.EffectivePlanner.MaxSearchNodes) break;

            foreach (QuestActionDefinition action in grammar.EffectiveActions.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
            {
                GenerationContext actionBase = MergeState(baseContext, node.State, globalBindings);
                IReadOnlyList<StoryletBinding> local;
                try { local = ResolveBindings(action.EffectiveBindings, actionBase, node.State, SeedFactory.Derive(seed, $"search:{expanded}:{action.Id}"), action.Id, globalBindings); }
                catch (InvalidOperationException) { continue; }
                GenerationContext context = MergeState(baseContext, node.State, globalBindings.Concat(local));
                if (!PredicatesHold(action.EffectivePreconditions, context)) continue;
                var nextState = node.State.Clone();
                ApplyEffects(action.EffectiveEffects, nextState, globalBindings.Concat(local));
                double nextCost = node.Cost + action.Cost;
                string key = StateKey(nextState);
                if (best.TryGetValue(key, out double old) && old <= nextCost + 1e-12) continue;
                best[key] = nextCost;
                var steps = node.Steps.Concat([new SearchAction(action, local)]).ToArray();
                var child = new SearchNode(nextState, steps, nextCost);
                double jitter = rootRandom.Fork("search-order:" + generated).NextDouble() * 1e-6;
                double priority = grammar.EffectivePlanner.PreferLowerCost ? nextCost + jitter : steps.Length + jitter;
                frontier.Enqueue(child, priority); generated++; maxFrontier = Math.Max(maxFrontier, frontier.Count);
            }
        }
        if (solved is null) throw new InvalidOperationException($"No quest plan could satisfy the goal within {grammar.EffectivePlanner.MaxSearchNodes} search nodes and {grammar.EffectivePlanner.MaxPlanSteps} steps.");

        var current = initialState.Clone();
        var planSteps = new List<QuestPlanStep>();
        double cumulative = 0;
        for (int i = 0; i < solved.Steps.Count; i++)
        {
            SearchAction step = solved.Steps[i];
            QuestWorldSnapshot before = current.Capture();
            ApplyEffects(step.Action.EffectiveEffects, current, globalBindings.Concat(step.Bindings));
            cumulative += step.Action.Cost;
            planSteps.Add(new(i, step.Action, step.Bindings, before, current.Capture(), cumulative));
        }
        return new(grammar, globalBindings, planSteps, initialState.Capture(), current.Capture(), seed,
            new("ForwardSearch", expanded, generated, maxFrontier, 0, ["Used bounded cost-aware state-space search."]));
    }

    public static GenerationContext MergeState(GenerationContext baseContext, QuestWorldState state, IEnumerable<StoryletBinding>? bindings = null, QuestGrammarDefinition? grammar = null)
    {
        var values = baseContext.Data.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in state.Values) { values[pair.Key] = pair.Value; values["quest.state." + pair.Key] = pair.Value; }
        if (grammar is not null) { values["quest.id"] = grammar.Id; values["quest.name"] = grammar.Name; }
        if (bindings is not null)
        {
            foreach (StoryletBinding binding in bindings)
            {
                string prefix = "binding." + binding.Id + ".";
                values[prefix + "id"] = binding.Candidate.Id; values[prefix + "name"] = binding.Candidate.Name; values[binding.Id] = binding.Candidate.Name;
                foreach (var pair in binding.Candidate.EffectiveValues) values[prefix + pair.Key] = pair.Value;
            }
        }
        var tags = new HashSet<string>(baseContext.EffectiveTags, StringComparer.OrdinalIgnoreCase); tags.UnionWith(state.Tags);
        return new(baseContext.ProjectId, baseContext.TargetKind, baseContext.TargetId, values, tags);
    }

    public static void ApplyEffects(IEnumerable<QuestEffectDefinition> effects, QuestWorldState state, IEnumerable<StoryletBinding>? bindings = null)
    {
        var bindingMap = (bindings ?? Array.Empty<StoryletBinding>()).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        foreach (QuestEffectDefinition effect in effects)
        {
            object? value = ResolveEffectValue(effect.Value, state, bindingMap);
            switch (effect.Kind)
            {
                case QuestEffectKind.Set: state.Values[effect.Key] = value; break;
                case QuestEffectKind.Increment:
                    state.Values.TryGetValue(effect.Key, out object? current);
                    double before = TryNumber(current, out double n) ? n : 0;
                    double delta = TryNumber(value, out double d) ? d : 1;
                    double after = before + delta;
                    state.Values[effect.Key] = Math.Abs(after - Math.Round(after)) < 1e-12 ? (object)(long)Math.Round(after) : after;
                    break;
                case QuestEffectKind.AddTag: state.Tags.Add(effect.Key); break;
                case QuestEffectKind.RemoveTag: state.Tags.Remove(effect.Key); break;
            }
        }
    }

    private static object? ResolveEffectValue(object? value, QuestWorldState state, IReadOnlyDictionary<string, StoryletBinding> bindings)
    {
        if (value is not string text || !text.StartsWith('$')) return value;
        string token = text[1..];
        if (token.StartsWith("state.", StringComparison.OrdinalIgnoreCase)) return state.Values.TryGetValue(token[6..], out object? stateValue) ? stateValue : null;
        if (token.StartsWith("binding.", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && bindings.TryGetValue(parts[1], out StoryletBinding? binding))
            {
                if (parts[2].Equals("id", StringComparison.OrdinalIgnoreCase)) return binding.Candidate.Id;
                if (parts[2].Equals("name", StringComparison.OrdinalIgnoreCase)) return binding.Candidate.Name;
                return binding.Candidate.EffectiveValues.TryGetValue(parts[2], out object? v) ? v : null;
            }
        }
        return value;
    }

    private static bool GoalsHold(IEnumerable<string> predicates, GenerationContext context) => PredicatesHold(predicates, context);
    private static bool PredicatesHold(IEnumerable<string> predicates, GenerationContext context)
    {
        foreach (string predicate in predicates)
        {
            SemanticPredicateResult result = SemanticPredicateEvaluator.Evaluate(predicate, context, new GeneratedTextBundle());
            if (!result.Success || !result.Value) return false;
        }
        return true;
    }

    private static IReadOnlyList<StoryletBinding> ResolveBindings(IEnumerable<StoryletBindingDefinition> definitions, GenerationContext context, QuestWorldState state, ulong seed, string scope, IEnumerable<StoryletBinding>? already = null)
    {
        var result = new List<StoryletBinding>();
        var used = new HashSet<string>((already ?? Array.Empty<StoryletBinding>()).Select(x => x.Candidate.Id), StringComparer.OrdinalIgnoreCase);
        foreach (StoryletBindingDefinition definition in definitions)
        {
            var candidates = ExtractCandidates(context, state, definition.CandidateContextKey)
                .Where(x => definition.EffectiveRequiredTags.All(tag => x.EffectiveTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                .Where(x => !definition.Distinct || !used.Contains(x.Id)).ToArray();
            if (candidates.Length == 0)
            {
                if (definition.Required) throw new InvalidOperationException($"No candidate available for quest binding '{definition.Id}'.");
                continue;
            }
            IDeterministicRandom random = new Xoshiro256StarStar(seed).Fork(scope + ":binding:" + definition.Id);
            StoryletCandidate selected = candidates[random.NextInt(candidates.Length)];
            result.Add(new(definition.Id, definition.Kind, selected)); if (definition.Distinct) used.Add(selected.Id);
        }
        return result;
    }

    private static IReadOnlyList<StoryletCandidate> ExtractCandidates(GenerationContext context, QuestWorldState state, string key)
    {
        object? value = context.Data.TryGetValue(key, out object? c) ? c : state.Values.TryGetValue(key, out object? s) ? s : null;
        if (value is null) return [];
        if (value is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Array) return json.EnumerateArray().Select((x, i) => FromJsonCandidate(x, i)).Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToArray();
            string scalar = json.ToString(); return string.IsNullOrWhiteSpace(scalar) ? [] : [new(scalar, scalar)];
        }
        if (value is StoryletCandidate one) return [one];
        if (value is IEnumerable<StoryletCandidate> typed) return typed.ToArray();
        if (value is string text) return [new(text, text)];
        if (value is IEnumerable<string> strings) return strings.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => new StoryletCandidate(x, x)).ToArray();
        if (value is IDictionary dictionary)
        {
            var result = new List<StoryletCandidate>();
            foreach (DictionaryEntry entry in dictionary)
            {
                string id = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty;
                string name = Convert.ToString(entry.Value, CultureInfo.InvariantCulture) ?? id;
                if (!string.IsNullOrWhiteSpace(id)) result.Add(new(id, name));
            }
            return result;
        }
        if (value is IEnumerable enumerable)
        {
            var result = new List<StoryletCandidate>(); foreach (object? item in enumerable) { if (item is null) continue; string t = Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty; if (!string.IsNullOrWhiteSpace(t)) result.Add(new(t, t)); }
            return result;
        }
        string fallback = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty; return string.IsNullOrWhiteSpace(fallback) ? [] : [new(fallback, fallback)];
    }

    private static StoryletCandidate FromJsonCandidate(JsonElement x, int i)
    {
        if (x.ValueKind != JsonValueKind.Object) { string text = x.ToString(); return new(text, text); }
        string id = x.TryGetProperty("id", out JsonElement idNode) ? idNode.ToString() : i.ToString(CultureInfo.InvariantCulture);
        string name = x.TryGetProperty("name", out JsonElement nameNode) ? nameNode.ToString() : id;
        var tags = x.TryGetProperty("tags", out JsonElement tagsNode) && tagsNode.ValueKind == JsonValueKind.Array ? tagsNode.EnumerateArray().Select(t => t.ToString()).Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() : [];
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (x.TryGetProperty("values", out JsonElement valuesNode) && valuesNode.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in valuesNode.EnumerateObject()) values[property.Name] = JsonSerializer.Deserialize<object?>(property.Value.GetRawText());
        return new(id, name, tags, values);
    }

    private static string StateKey(QuestWorldState state)
    {
        string values = string.Join("|", state.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.Key.ToLowerInvariant() + "=" + Canonical(x.Value)));
        string tags = string.Join(",", state.Tags.Order(StringComparer.OrdinalIgnoreCase).Select(x => x.ToLowerInvariant()));
        return values + "#" + tags;
    }

    private static string Canonical(object? value) => value switch
    {
        null => "null", bool b => b ? "true" : "false", JsonElement json => json.GetRawText(), IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty, _ => value.ToString() ?? string.Empty
    };

    private static bool TryNumber(object? value, out double number)
    {
        try { if (value is JsonElement json) value = json.ToString(); number = Convert.ToDouble(value, CultureInfo.InvariantCulture); return !double.IsNaN(number) && !double.IsInfinity(number); }
        catch { number = 0; return false; }
    }

    private sealed record SearchAction(QuestActionDefinition Action, IReadOnlyList<StoryletBinding> Bindings);
    private sealed record SearchNode(QuestWorldState State, IReadOnlyList<SearchAction> Steps, double Cost);
}

public static class QuestPlanValidator
{
    public static IReadOnlyList<string> Validate(QuestPlan plan, GenerationContext baseContext)
    {
        var issues = new List<string>();
        var state = QuestWorldState.FromSnapshot(plan.InitialState);
        double cost = 0;
        for (int i = 0; i < plan.Steps.Count; i++)
        {
            QuestPlanStep step = plan.Steps[i];
            GenerationContext context = QuestPlanner.MergeState(baseContext, state, plan.Bindings.Concat(step.Bindings), plan.Grammar);
            foreach (string predicate in step.Action.EffectivePreconditions)
            {
                SemanticPredicateResult result = SemanticPredicateEvaluator.Evaluate(predicate, context, new GeneratedTextBundle());
                if (!result.Success) issues.Add($"Step {i + 1} '{step.Action.Name}' has invalid precondition '{predicate}': {result.Error}");
                else if (!result.Value) issues.Add($"Step {i + 1} '{step.Action.Name}' violates precondition '{predicate}'.");
            }
            QuestPlanner.ApplyEffects(step.Action.EffectiveEffects, state, plan.Bindings.Concat(step.Bindings)); cost += step.Action.Cost;
        }
        GenerationContext finalContext = QuestPlanner.MergeState(baseContext, state, plan.Bindings, plan.Grammar);
        foreach (string goal in plan.Grammar.EffectiveGoalPredicates)
        {
            SemanticPredicateResult result = SemanticPredicateEvaluator.Evaluate(goal, finalContext, new GeneratedTextBundle());
            if (!result.Success) issues.Add($"Invalid goal predicate '{goal}': {result.Error}");
            else if (!result.Value) issues.Add($"Final state does not satisfy goal '{goal}'.");
        }
        if (Math.Abs(cost - plan.TotalCost) > 1e-9) issues.Add("Recorded plan cost does not match the sum of step costs.");
        return issues;
    }
}
