using System.Collections.ObjectModel;
using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Random;
using Playroma.Text.Core.Tracery;
using Playroma.Text.Core.Semantics;

namespace Playroma.Text.Generators.Tracery;

public sealed record TraceryExpansionOptions(
    int MaxDepth = 64,
    int MaxNodes = 4096,
    bool ExecutePostActions = false,
    bool AggregateRuleMetadata = true);

public sealed class TraceryEngine
{
    private readonly TraceryModifierRegistry _modifiers;
    public TraceryEngine(TraceryModifierRegistry? modifiers = null) => _modifiers = modifiers ?? TraceryModifierRegistry.CreateDefault();

    public TraceryExpansionResult Expand(
        TraceryGrammarDocument grammar,
        GenerationContext context,
        IDeterministicRandom random,
        GenerationBudget budget,
        IReadOnlyDictionary<string, object?>? parameters = null,
        string? startSymbol = null,
        TraceryExpansionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grammar); ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(random); ArgumentNullException.ThrowIfNull(budget);
        options ??= new TraceryExpansionOptions();
        var diagnostics = TraceryGrammarValidator.Validate(grammar).ToList();
        if (diagnostics.Any(x => x.Severity == GenerationDiagnosticSeverity.Error))
            return new TraceryExpansionResult(false, string.Empty, new TraceryExpansionTrace([]), diagnostics, new Dictionary<string, string>());

        var effectiveParameters = ApplyDefaults(grammar, parameters);
        var state = new State(grammar, context, effectiveParameters, random, budget, options, _modifiers, diagnostics, cancellationToken);
        var symbol = string.IsNullOrWhiteSpace(startSymbol) ? grammar.Origin : startSymbol!;
        var text = string.Empty;
        try { text = state.ExpandSymbol(symbol, null, 0); }
        catch (TraceryExpansionAbortException) { }
        return new TraceryExpansionResult(!diagnostics.Any(x => x.Severity == GenerationDiagnosticSeverity.Error), text,
            new TraceryExpansionTrace(state.Nodes.OrderBy(x => x.Id).ToArray()), diagnostics.ToArray(),
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(state.AggregatedMetadata, StringComparer.OrdinalIgnoreCase)),
            new ReadOnlyDictionary<string, SemanticFeatureValue>(new Dictionary<string, SemanticFeatureValue>(state.AggregatedFeatures, StringComparer.OrdinalIgnoreCase)));
    }

    public TraceryExpansionResult ExpandRule(
        TraceryGrammarDocument grammar,
        string rawRule,
        GenerationContext context,
        IDeterministicRandom random,
        GenerationBudget budget,
        IReadOnlyDictionary<string, object?>? parameters = null,
        TraceryExpansionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grammar); ArgumentNullException.ThrowIfNull(rawRule); ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(random); ArgumentNullException.ThrowIfNull(budget);
        options ??= new TraceryExpansionOptions();
        var diagnostics = TraceryGrammarValidator.Validate(grammar).ToList();
        if (diagnostics.Any(x => x.Severity == GenerationDiagnosticSeverity.Error))
            return new TraceryExpansionResult(false, string.Empty, new TraceryExpansionTrace([]), diagnostics, new Dictionary<string, string>());
        var effectiveParameters = ApplyDefaults(grammar, parameters);
        var state = new State(grammar, context, effectiveParameters, random, budget, options, _modifiers, diagnostics, cancellationToken);
        var text = string.Empty;
        try { text = state.ExpandRaw(rawRule); }
        catch (TraceryExpansionAbortException) { }
        return new TraceryExpansionResult(!diagnostics.Any(x => x.Severity == GenerationDiagnosticSeverity.Error), text,
            new TraceryExpansionTrace(state.Nodes.OrderBy(x => x.Id).ToArray()), diagnostics.ToArray(),
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(state.AggregatedMetadata, StringComparer.OrdinalIgnoreCase)),
            new ReadOnlyDictionary<string, SemanticFeatureValue>(new Dictionary<string, SemanticFeatureValue>(state.AggregatedFeatures, StringComparer.OrdinalIgnoreCase)));
    }

    private static IReadOnlyDictionary<string, object?> ApplyDefaults(TraceryGrammarDocument grammar, IReadOnlyDictionary<string, object?>? supplied)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in grammar.EffectiveParameters)
        {
            if (pair.Value.DefaultValue is { } value)
                result[pair.Key] = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
                    JsonValueKind.Number => value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Array => value.EnumerateArray().Select(x => x.ToString()).ToArray(),
                    _ => value.ToString()
                };
        }
        if (supplied is not null) foreach (var pair in supplied) result[pair.Key] = pair.Value;
        return result;
    }

    private sealed class State
    {
        private readonly TraceryCompatibilityMode _compatibility;
        private readonly GenerationContext _context;
        private readonly IReadOnlyDictionary<string, object?> _parameters;
        private readonly IDeterministicRandom _random;
        private readonly GenerationBudget _budget;
        private readonly TraceryExpansionOptions _options;
        private readonly TraceryModifierRegistry _modifiers;
        private readonly List<GenerationDiagnostic> _diagnostics;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<string, List<IReadOnlyList<TraceryRuleDefinition>>> _stacks = new(StringComparer.OrdinalIgnoreCase);
        private int _nextNode;
        private int _nodeCount;

        public List<TraceryExpansionNode> Nodes { get; } = [];
        public Dictionary<string, string> AggregatedMetadata { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SemanticFeatureValue> AggregatedFeatures { get; } = new(StringComparer.OrdinalIgnoreCase);

        public State(TraceryGrammarDocument grammar, GenerationContext context, IReadOnlyDictionary<string, object?> parameters,
            IDeterministicRandom random, GenerationBudget budget, TraceryExpansionOptions options, TraceryModifierRegistry modifiers,
            List<GenerationDiagnostic> diagnostics, CancellationToken cancellationToken)
        {
            _compatibility = grammar.Compatibility; _context = context; _parameters = parameters; _random = random; _budget = budget; _options = options; _modifiers = modifiers; _diagnostics = diagnostics; _cancellationToken = cancellationToken;
            foreach (var pair in grammar.Symbols) _stacks[pair.Key] = [pair.Value];
            foreach (var pair in parameters)
            {
                IReadOnlyList<TraceryRuleDefinition> overlay = pair.Value switch
                {
                    IReadOnlyList<string> list => list.Select(x => new TraceryRuleDefinition(x)).ToArray(),
                    IEnumerable<string> sequence => sequence.Select(x => new TraceryRuleDefinition(x)).ToArray(),
                    null => [new TraceryRuleDefinition(string.Empty)],
                    _ => [new TraceryRuleDefinition(Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)]
                };
                if (!_stacks.TryGetValue(pair.Key, out var stack)) _stacks[pair.Key] = stack = [];
                stack.Add(overlay);
            }
        }

        public string ExpandRaw(string rawRule) => ExpandText(rawRule, null, 0);

        public string ExpandSymbol(string symbol, int? parentId, int depth)
        {
            Guard(depth);
            var nodeId = ReserveNode();
            if (!_stacks.TryGetValue(symbol, out var stack) || stack.Count == 0)
            {
                Error(GenerationDiagnosticCodes.TraceryMissingSymbol, $"Missing Tracery symbol '{symbol}'.");
                var missing = "{{" + symbol + "}}";
                Nodes.Add(new TraceryExpansionNode(nodeId, parentId, "symbol", "#" + symbol + "#", symbol, null, missing));
                return missing;
            }
            var rules = stack[^1].Where(r => TraceryConditionEvaluator.Evaluate(r.Condition, _context, _parameters, AggregatedFeatures)).ToArray();
            if (rules.Length == 0)
            {
                Error(GenerationDiagnosticCodes.TraceryNoEligibleRules, $"Symbol '{symbol}' has no rules eligible under the current conditions.");
                Nodes.Add(new TraceryExpansionNode(nodeId, parentId, "symbol", "#" + symbol + "#", symbol, null, string.Empty));
                return string.Empty;
            }
            var rule = SelectWeighted(rules);
            if (_options.AggregateRuleMetadata)
            {
                foreach (var m in rule.EffectiveMetadata) AggregatedMetadata[m.Key] = m.Value;
                foreach (var f in rule.EffectiveFeatures) AggregatedFeatures[f.Key] = f.Value;
            }
            var output = ExpandText(rule.Text, nodeId, depth + 1);
            Nodes.Add(new TraceryExpansionNode(nodeId, parentId, "symbol", "#" + symbol + "#", symbol, rule.Text, output, rule.EffectiveMetadata, rule.EffectiveFeatures));
            return output;
        }

        private string ExpandText(string text, int? parentId, int depth)
        {
            Guard(depth);
            var parsed = TraceryParser.ParseRule(text, _compatibility == TraceryCompatibilityMode.PlayromaExtended);
            foreach (var error in parsed.Errors) Error(GenerationDiagnosticCodes.TraceryParseError, error);
            if (parsed.Errors.Count > 0) return text;
            var sb = new System.Text.StringBuilder();
            foreach (var section in parsed.Sections)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                _budget.CheckTime();
                switch (section)
                {
                    case TraceryTextSection literal:
                        var textNode = ReserveNode();
                        Nodes.Add(new TraceryExpansionNode(textNode, parentId, "text", literal.Text, null, null, literal.Text));
                        sb.Append(literal.Text);
                        break;
                    case TraceryActionSection action:
                        ExecuteAction(action.Action, parentId, depth + 1, scoped: false);
                        break;
                    case TraceryTagSection tag:
                        sb.Append(ExpandTag(tag, parentId, depth + 1));
                        break;
                }
                _budget.CheckOutputLength(sb.Length);
            }
            return sb.ToString();
        }

        private string ExpandTag(TraceryTagSection tag, int? parentId, int depth)
        {
            Guard(depth);
            var tagNodeId = ReserveNode();
            var scopedPushes = new List<string>();
            foreach (var action in tag.PreActions)
                scopedPushes.AddRange(ExecuteAction(action, tagNodeId, depth + 1, scoped: true));
            var output = ExpandSymbol(tag.Symbol, tagNodeId, depth + 1);
            foreach (var modifierName in tag.Modifiers)
            {
                if (!_modifiers.TryApply(modifierName, output, out var modified))
                {
                    Error(GenerationDiagnosticCodes.TraceryUnknownModifier, $"Unknown Tracery modifier '{modifierName}'.");
                    continue;
                }
                output = modified;
            }
            if (_options.ExecutePostActions)
                foreach (var action in tag.PostActions) ExecuteAction(action, tagNodeId, depth + 1, scoped: false);
            // Classic tag pre-actions are scoped to the tag and automatically deactivate.
            for (var i = scopedPushes.Count - 1; i >= 0; i--) Pop(scopedPushes[i], reportErrors: false);
            Nodes.Add(new TraceryExpansionNode(tagNodeId, parentId, "tag", "#" + tag.RawTag + "#", tag.Symbol, null, output,
                tag.Modifiers.Count > 0 ? new Dictionary<string, string> { ["modifiers"] = string.Join(".", tag.Modifiers) } : null));
            return output;
        }

        private IReadOnlyList<string> ExecuteAction(string actionRaw, int? parentId, int depth, bool scoped)
        {
            Guard(depth);
            var actionNodeId = ReserveNode();

            // Classic Action.activate first flattens hashtags in the action text, then parses
            // that amended text with parseTag. Brackets inside the action therefore describe
            // nested sub-actions rather than ordinary top-level actions.
            var amended = ExpandTextForAction(actionRaw, actionNodeId, depth + 1);
            var parsed = TraceryParser.ParseTagContent(amended);
            foreach (var error in parsed.Errors) Error(GenerationDiagnosticCodes.TraceryParseError, error);
            if (parsed.Tag is null)
            {
                Nodes.Add(new TraceryExpansionNode(actionNodeId, parentId, "action", "[" + actionRaw + "]", null, null, string.Empty));
                return [];
            }

            var pushed = new List<string>();
            var actionTag = parsed.Tag;
            if (!string.IsNullOrWhiteSpace(actionTag.Symbol))
            {
                var symbolText = actionTag.Symbol.Trim();
                var colon = symbolText.IndexOf(':');
                if (colon <= 0 || colon == symbolText.Length - 1 ||
                    (_compatibility == TraceryCompatibilityMode.Classic && symbolText.IndexOf(':', colon + 1) >= 0))
                {
                    Error(GenerationDiagnosticCodes.TraceryActionError, $"Unknown Tracery action '[{actionRaw}]'. Expected symbol:rule.");
                }
                else
                {
                    var key = symbolText[..colon].Trim();
                    var rhs = symbolText[(colon + 1)..].Trim();
                    if (_compatibility == TraceryCompatibilityMode.PlayromaExtended && string.Equals(rhs, "POP", StringComparison.OrdinalIgnoreCase))
                    {
                        Pop(key, reportErrors: true);
                        Nodes.Add(new TraceryExpansionNode(actionNodeId, parentId, "action.pop", "[" + actionRaw + "]", key, null, string.Empty));
                    }
                    else
                    {
                        var rules = rhs.Split(',', StringSplitOptions.None).Select(x => new TraceryRuleDefinition(x)).ToArray();
                        if (!_stacks.TryGetValue(key, out var stack)) _stacks[key] = stack = [];
                        stack.Add(rules);
                        pushed.Add(key);
                        Nodes.Add(new TraceryExpansionNode(actionNodeId, parentId, scoped ? "action.push.scoped" : "action.push", "[" + actionRaw + "]", key, rhs, string.Empty));
                    }
                }
            }

            // tracery.js activates nested pre-actions after the main push. Returning all
            // pushed keys lets the caller deactivate them in reverse order.
            foreach (var subAction in actionTag.PreActions)
                pushed.AddRange(ExecuteAction(subAction, actionNodeId, depth + 1, scoped));

            if (actionTag.PostActions.Count > 0 && _options.ExecutePostActions)
                foreach (var postAction in actionTag.PostActions)
                    _ = ExecuteAction(postAction, actionNodeId, depth + 1, scoped: false);

            if (string.IsNullOrWhiteSpace(actionTag.Symbol) && actionTag.PreActions.Count == 0)
                Nodes.Add(new TraceryExpansionNode(actionNodeId, parentId, "action", "[" + actionRaw + "]", null, amended, string.Empty));
            return pushed;
        }

        private string ExpandTextForAction(string text, int? parentId, int depth)
        {
            Guard(depth);
            // Force classic rule parsing here: Action.activate's grammar.flatten expands
            // #tags# but leaves bracket syntax for the following parseTag pass.
            var parsed = TraceryParser.ParseRule(text, allowTopLevelActions: false);
            foreach (var error in parsed.Errors) Error(GenerationDiagnosticCodes.TraceryParseError, error);
            if (parsed.Errors.Count > 0) return text;
            var sb = new System.Text.StringBuilder();
            foreach (var section in parsed.Sections)
            {
                switch (section)
                {
                    case TraceryTextSection literal:
                        sb.Append(literal.Text);
                        break;
                    case TraceryTagSection tag:
                        sb.Append(ExpandTag(tag, parentId, depth + 1));
                        break;
                    case TraceryActionSection action:
                        // This case is unreachable with allowTopLevelActions=false, but keep
                        // behavior deterministic if the parser is extended later.
                        sb.Append('[').Append(action.Action).Append(']');
                        break;
                }
            }
            _budget.CheckOutputLength(sb.Length);
            return sb.ToString();
        }

        private void Pop(string key, bool reportErrors)
        {
            if (!_stacks.TryGetValue(key, out var stack) || stack.Count == 0)
            {
                if (reportErrors) Error(GenerationDiagnosticCodes.TraceryActionError, $"Cannot POP Tracery symbol '{key}' because its rule stack is empty.");
                return;
            }
            stack.RemoveAt(stack.Count - 1);
            if (stack.Count == 0) _stacks.Remove(key);
        }

        private TraceryRuleDefinition SelectWeighted(IReadOnlyList<TraceryRuleDefinition> rules)
        {
            var total = rules.Sum(x => x.Weight);
            var target = _random.NextDouble() * total;
            var running = 0d;
            foreach (var rule in rules)
            {
                running += rule.Weight;
                if (target < running) return rule;
            }
            return rules[^1];
        }

        private void Guard(int depth)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _budget.CheckTime();
            if (depth > _options.MaxDepth)
            {
                Error(GenerationDiagnosticCodes.TraceryRecursionLimit, $"Tracery recursion depth exceeded {_options.MaxDepth}.");
                throw new TraceryExpansionAbortException();
            }
            if (_nodeCount > _options.MaxNodes)
            {
                Error(GenerationDiagnosticCodes.TraceryNodeLimit, $"Tracery expansion node limit exceeded {_options.MaxNodes}.");
                throw new TraceryExpansionAbortException();
            }
        }

        private int ReserveNode()
        {
            if (++_nodeCount > _options.MaxNodes)
            {
                Error(GenerationDiagnosticCodes.TraceryNodeLimit, $"Tracery expansion node limit exceeded {_options.MaxNodes}.");
                throw new TraceryExpansionAbortException();
            }
            return ++_nextNode;
        }

        private void Error(string code, string message) => _diagnostics.Add(new GenerationDiagnostic(code, GenerationDiagnosticSeverity.Error, message));
        private static int FindTopLevelColon(string text)
        {
            var bracket = 0; var hash = false;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\') { i++; continue; }
                if (text[i] == '#') hash = !hash;
                else if (!hash && text[i] == '[') bracket++;
                else if (!hash && text[i] == ']') bracket--;
                else if (!hash && bracket == 0 && text[i] == ':') return i;
            }
            return -1;
        }
    }

    private sealed class TraceryExpansionAbortException : Exception { }
}
