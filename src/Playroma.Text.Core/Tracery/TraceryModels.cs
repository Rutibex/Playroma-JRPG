using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Semantics;

namespace Playroma.Text.Core.Tracery;

public enum TraceryCompatibilityMode
{
    Classic,
    PlayromaExtended
}

public sealed record TraceryParameterDefinition(
    string Name,
    RecipeParameterType Type,
    bool Required = false,
    JsonElement? DefaultValue = null,
    string? Description = null,
    IReadOnlyList<string>? Choices = null);

public sealed record TraceryRuleDefinition(
    string Text,
    double Weight = 1.0,
    IReadOnlyDictionary<string, string>? Metadata = null,
    string? Condition = null,
    IReadOnlyDictionary<string, SemanticFeatureValue>? Features = null)
{
    public IReadOnlyDictionary<string, string> EffectiveMetadata { get; } = Metadata ??
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    [JsonIgnore] public IReadOnlyDictionary<string, SemanticFeatureValue> EffectiveFeatures => Features ?? SemanticFeatureUnifier.Empty;
}

public sealed record TraceryGrammarDocument(
    string Id,
    int Version,
    string Name,
    IReadOnlyDictionary<string, IReadOnlyList<TraceryRuleDefinition>> Symbols,
    string Origin = "origin",
    IReadOnlyDictionary<string, TraceryParameterDefinition>? Parameters = null,
    IReadOnlySet<string>? Tags = null,
    string? Description = null,
    TraceryCompatibilityMode Compatibility = TraceryCompatibilityMode.PlayromaExtended)
{
    public IReadOnlyDictionary<string, TraceryParameterDefinition> EffectiveParameters { get; } = Parameters ??
        new ReadOnlyDictionary<string, TraceryParameterDefinition>(new Dictionary<string, TraceryParameterDefinition>(StringComparer.OrdinalIgnoreCase));
    public IReadOnlySet<string> EffectiveTags { get; } = Tags ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record TraceryExpansionNode(
    int Id,
    int? ParentId,
    string Kind,
    string Raw,
    string? Symbol,
    string? SelectedRule,
    string Output,
    IReadOnlyDictionary<string, string>? Metadata = null,
    IReadOnlyDictionary<string, SemanticFeatureValue>? Features = null);

public sealed record TraceryExpansionTrace(IReadOnlyList<TraceryExpansionNode> Nodes);

public sealed record TraceryExpansionResult(
    bool Success,
    string Text,
    TraceryExpansionTrace Trace,
    IReadOnlyList<GenerationDiagnostic> Diagnostics,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyDictionary<string, SemanticFeatureValue>? Features = null)
{
    [JsonIgnore] public IReadOnlyDictionary<string, SemanticFeatureValue> EffectiveFeatures => Features ?? SemanticFeatureUnifier.Empty;
}

public interface ITraceryGrammarResolver
{
    ValueTask<TraceryGrammarDocument?> ResolveAsync(string id, CancellationToken cancellationToken = default);
}

public static class TraceryGrammarValidator
{
    private static readonly char[] ReservedSymbolCharacters = ['[', ']', '.', ':', '#'];

    public static IReadOnlyList<GenerationDiagnostic> Validate(TraceryGrammarDocument grammar)
    {
        ArgumentNullException.ThrowIfNull(grammar);
        var diagnostics = new List<GenerationDiagnostic>();
        if (string.IsNullOrWhiteSpace(grammar.Id)) diagnostics.Add(Error("Grammar id is required."));
        if (grammar.Version < 1) diagnostics.Add(Error("Grammar version must be >= 1."));
        if (string.IsNullOrWhiteSpace(grammar.Name)) diagnostics.Add(Error("Grammar name is required."));
        if (string.IsNullOrWhiteSpace(grammar.Origin)) diagnostics.Add(Error("Grammar origin symbol is required."));
        if (grammar.Symbols.Count == 0) diagnostics.Add(Error("Grammar must define at least one symbol."));
        if (!string.IsNullOrWhiteSpace(grammar.Origin) && !grammar.Symbols.ContainsKey(grammar.Origin))
            diagnostics.Add(Warning($"Grammar origin symbol '{grammar.Origin}' is not defined; explicit-symbol or raw-rule expansion can still be used."));

        foreach (var pair in grammar.Symbols)
        {
            if (string.IsNullOrWhiteSpace(pair.Key)) diagnostics.Add(Error("Symbol names cannot be empty."));
            if (pair.Key.IndexOfAny(ReservedSymbolCharacters) >= 0)
                diagnostics.Add(Error($"Symbol '{pair.Key}' contains a reserved Tracery character."));
            if (pair.Value.Count == 0) diagnostics.Add(Error($"Symbol '{pair.Key}' has no rules."));
            foreach (var rule in pair.Value)
            {
                if (rule.Text is null) diagnostics.Add(Error($"Symbol '{pair.Key}' contains a null rule."));
                if (rule.Weight <= 0 || double.IsNaN(rule.Weight) || double.IsInfinity(rule.Weight))
                    diagnostics.Add(Error($"Symbol '{pair.Key}' contains a non-positive or invalid weight."));
            }
        }

        foreach (var parameter in grammar.EffectiveParameters.Values)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name)) diagnostics.Add(Error("Grammar parameter names cannot be empty."));
            if (parameter.Type == RecipeParameterType.Choice && (parameter.Choices is null || parameter.Choices.Count == 0))
                diagnostics.Add(Error($"Choice parameter '{parameter.Name}' must define choices."));
        }
        return diagnostics;
    }

    private static GenerationDiagnostic Error(string message) => new(
        GenerationDiagnosticCodes.InvalidTraceryGrammar,
        GenerationDiagnosticSeverity.Error,
        message);

    private static GenerationDiagnostic Warning(string message) => new(
        GenerationDiagnosticCodes.InvalidTraceryGrammar,
        GenerationDiagnosticSeverity.Warning,
        message);
}
