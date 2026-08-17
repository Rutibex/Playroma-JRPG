using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Tracery;

namespace Playroma.Text.Generators.Tracery;

public static class TraceryGrammarAnalyzer
{
    public static IReadOnlyList<GenerationDiagnostic> Analyze(TraceryGrammarDocument grammar, TraceryModifierRegistry? modifiers = null)
    {
        ArgumentNullException.ThrowIfNull(grammar);
        modifiers ??= TraceryModifierRegistry.CreateDefault();
        var diagnostics = TraceryGrammarValidator.Validate(grammar).ToList();
        if (diagnostics.Any(x => x.Severity == GenerationDiagnosticSeverity.Error)) return diagnostics;

        var references = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var dynamicSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in grammar.Symbols.Keys) references[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in grammar.Symbols)
        {
            foreach (var rule in symbol.Value)
            {
                var parsed = TraceryParser.ParseRule(rule.Text, grammar.Compatibility == TraceryCompatibilityMode.PlayromaExtended);
                foreach (var error in parsed.Errors)
                    diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.TraceryParseError, GenerationDiagnosticSeverity.Error,
                        $"{symbol.Key}: {error}"));
                CollectSections(parsed.Sections, symbol.Key, references, dynamicSymbols, modifiers, diagnostics);
            }
        }

        foreach (var source in references)
        {
            foreach (var target in source.Value)
            {
                if (!grammar.Symbols.ContainsKey(target) && !dynamicSymbols.Contains(target))
                    diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.TraceryUnknownReference, GenerationDiagnosticSeverity.Warning,
                        $"Symbol '{source.Key}' references undefined symbol '{target}'. It may be supplied dynamically by an action or project context."));
            }
        }

        foreach (var cycle in FindCycles(references))
            diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.TraceryStaticCycle, GenerationDiagnosticSeverity.Warning,
                "Recursive Tracery cycle detected: " + string.Join(" -> ", cycle) + ". Runtime recursion guards remain active."));
        return diagnostics;
    }

    private static void CollectSections(IReadOnlyList<TracerySection> sections, string owner,
        Dictionary<string, HashSet<string>> references, HashSet<string> dynamicSymbols,
        TraceryModifierRegistry modifiers, List<GenerationDiagnostic> diagnostics)
    {
        foreach (var section in sections)
        {
            switch (section)
            {
                case TraceryTagSection tag:
                    references[owner].Add(tag.Symbol);
                    foreach (var mod in tag.Modifiers)
                        if (!modifiers.Names.Contains(mod, StringComparer.OrdinalIgnoreCase))
                            diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.TraceryUnknownModifier, GenerationDiagnosticSeverity.Warning,
                                $"Symbol '{owner}' uses unknown modifier '{mod}'."));
                    foreach (var action in tag.PreActions.Concat(tag.PostActions)) CollectAction(action, owner, references, dynamicSymbols);
                    break;
                case TraceryActionSection action:
                    CollectAction(action.Action, owner, references, dynamicSymbols);
                    break;
            }
        }
    }

    private static void CollectAction(string raw, string owner, Dictionary<string, HashSet<string>> references, HashSet<string> dynamicSymbols)
    {
        var colon = raw.IndexOf(':');
        var body = raw;
        if (colon > 0)
        {
            var key = raw[..colon].Trim();
            if (!string.IsNullOrWhiteSpace(key)) dynamicSymbols.Add(key);
            body = raw[(colon + 1)..];
            if (string.Equals(body.Trim(), "POP", StringComparison.OrdinalIgnoreCase)) return;
        }
        var parsed = TraceryParser.ParseRule(body);
        foreach (var section in parsed.Sections)
            if (section is TraceryTagSection tag) references[owner].Add(tag.Symbol);
    }

    private static IReadOnlyList<IReadOnlyList<string>> FindCycles(Dictionary<string, HashSet<string>> graph)
    {
        var cycles = new List<IReadOnlyList<string>>();
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string node)
        {
            state[node] = 1; stack.Add(node);
            if (graph.TryGetValue(node, out var next))
            {
                foreach (var target in next.Where(graph.ContainsKey))
                {
                    if (!state.TryGetValue(target, out var targetState)) Visit(target);
                    else if (targetState == 1)
                    {
                        var idx = stack.FindIndex(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0)
                        {
                            var cycle = stack.Skip(idx).Append(target).ToArray();
                            var sig = string.Join("|", cycle.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                            if (signatures.Add(sig)) cycles.Add(cycle);
                        }
                    }
                }
            }
            stack.RemoveAt(stack.Count - 1); state[node] = 2;
        }

        foreach (var node in graph.Keys) if (!state.ContainsKey(node)) Visit(node);
        return cycles;
    }
}
