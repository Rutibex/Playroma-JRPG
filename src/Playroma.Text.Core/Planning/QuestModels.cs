using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Playroma.Text.Core.Narrative;

namespace Playroma.Text.Core.Planning;

public enum QuestEffectKind { Set, Increment, AddTag, RemoveTag }
public enum QuestTaskKind { Primitive, Compound }

public sealed record QuestEffectDefinition(QuestEffectKind Kind, string Key, object? Value = null, string? Description = null);

public sealed record QuestActionDefinition(
    string Id,
    string Name,
    string? Description = null,
    double Cost = 1,
    IReadOnlyList<string>? Preconditions = null,
    IReadOnlyList<StoryletBindingDefinition>? Bindings = null,
    IReadOnlyList<QuestEffectDefinition>? Effects = null,
    string? RealizationRecipeId = null,
    IReadOnlyDictionary<string, object?>? RealizationParameters = null,
    IReadOnlyList<string>? Tags = null)
{
    [JsonIgnore] public IReadOnlyList<string> EffectivePreconditions => Preconditions ?? Array.Empty<string>();
    [JsonIgnore] public IReadOnlyList<StoryletBindingDefinition> EffectiveBindings => Bindings ?? Array.Empty<StoryletBindingDefinition>();
    [JsonIgnore] public IReadOnlyList<QuestEffectDefinition> EffectiveEffects => Effects ?? Array.Empty<QuestEffectDefinition>();
    [JsonIgnore] public IReadOnlyDictionary<string, object?> EffectiveRealizationParameters => RealizationParameters ?? EmptyObjects;
    [JsonIgnore] public IReadOnlyList<string> EffectiveTags => Tags ?? Array.Empty<string>();
    private static IReadOnlyDictionary<string, object?> EmptyObjects { get; } = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
}

public sealed record QuestMethodDefinition(
    string Id,
    string TaskId,
    IReadOnlyList<string> Subtasks,
    string? Description = null,
    int Priority = 0,
    IReadOnlyList<string>? Preconditions = null)
{
    [JsonIgnore] public IReadOnlyList<string> EffectivePreconditions => Preconditions ?? Array.Empty<string>();
}

public sealed record QuestPlannerSettings(
    int MaxPlanSteps = 24,
    int MaxSearchNodes = 10000,
    int MaxDecompositionDepth = 24,
    bool PreferLowerCost = true);

public sealed record QuestGrammarDefinition(
    string Id,
    int Version,
    string Name,
    string? Description = null,
    bool Enabled = true,
    string? RootTask = null,
    IReadOnlyList<string>? GoalPredicates = null,
    IReadOnlyList<StoryletBindingDefinition>? Bindings = null,
    IReadOnlyList<QuestActionDefinition>? Actions = null,
    IReadOnlyList<QuestMethodDefinition>? Methods = null,
    string? SummaryRecipeId = null,
    IReadOnlyDictionary<string, object?>? RealizationParameters = null,
    QuestPlannerSettings? Planner = null,
    IReadOnlyList<string>? Tags = null)
{
    [JsonIgnore] public IReadOnlyList<string> EffectiveGoalPredicates => GoalPredicates ?? Array.Empty<string>();
    [JsonIgnore] public IReadOnlyList<StoryletBindingDefinition> EffectiveBindings => Bindings ?? Array.Empty<StoryletBindingDefinition>();
    [JsonIgnore] public IReadOnlyList<QuestActionDefinition> EffectiveActions => Actions ?? Array.Empty<QuestActionDefinition>();
    [JsonIgnore] public IReadOnlyList<QuestMethodDefinition> EffectiveMethods => Methods ?? Array.Empty<QuestMethodDefinition>();
    [JsonIgnore] public IReadOnlyDictionary<string, object?> EffectiveRealizationParameters => RealizationParameters ?? EmptyObjects;
    [JsonIgnore] public QuestPlannerSettings EffectivePlanner => Planner ?? new();
    [JsonIgnore] public IReadOnlyList<string> EffectiveTags => Tags ?? Array.Empty<string>();
    private static IReadOnlyDictionary<string, object?> EmptyObjects { get; } = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
}

public static class QuestGrammarValidator
{
    public static IReadOnlyList<string> Validate(QuestGrammarDefinition grammar)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(grammar.Id)) errors.Add("Quest grammar ID is required.");
        if (grammar.Version <= 0) errors.Add("Quest grammar version must be positive.");
        if (string.IsNullOrWhiteSpace(grammar.Name)) errors.Add("Quest grammar name is required.");
        if (grammar.EffectiveGoalPredicates.Count == 0) errors.Add("At least one goal predicate is required.");
        if (grammar.EffectiveActions.Count == 0) errors.Add("At least one quest action is required.");
        var duplicateAction = grammar.EffectiveActions.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => string.IsNullOrWhiteSpace(x.Key) || x.Count() > 1);
        if (duplicateAction is not null) errors.Add("Quest action IDs must be non-empty and unique.");
        var duplicateMethod = grammar.EffectiveMethods.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => string.IsNullOrWhiteSpace(x.Key) || x.Count() > 1);
        if (duplicateMethod is not null) errors.Add("Quest method IDs must be non-empty and unique.");
        var duplicateBinding = grammar.EffectiveBindings.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => string.IsNullOrWhiteSpace(x.Key) || x.Count() > 1);
        if (duplicateBinding is not null) errors.Add("Quest binding IDs must be non-empty and unique.");
        foreach (var action in grammar.EffectiveActions)
        {
            if (string.IsNullOrWhiteSpace(action.Name)) errors.Add($"Quest action '{action.Id}' requires a name.");
            if (double.IsNaN(action.Cost) || double.IsInfinity(action.Cost) || action.Cost < 0) errors.Add($"Quest action '{action.Id}' cost must be finite and non-negative.");
            foreach (var effect in action.EffectiveEffects) if (string.IsNullOrWhiteSpace(effect.Key)) errors.Add($"Quest action '{action.Id}' contains an effect with no key.");
        }
        foreach (var method in grammar.EffectiveMethods)
        {
            if (string.IsNullOrWhiteSpace(method.TaskId)) errors.Add($"Quest method '{method.Id}' requires taskId.");
            if (method.Subtasks is null || method.Subtasks.Count == 0) errors.Add($"Quest method '{method.Id}' requires at least one subtask.");
        }
        if (!string.IsNullOrWhiteSpace(grammar.RootTask))
        {
            bool rootExists = grammar.EffectiveActions.Any(x => x.Id.Equals(grammar.RootTask, StringComparison.OrdinalIgnoreCase)) || grammar.EffectiveMethods.Any(x => x.TaskId.Equals(grammar.RootTask, StringComparison.OrdinalIgnoreCase));
            if (!rootExists) errors.Add($"Root task '{grammar.RootTask}' is not an action and has no decomposition method.");
        }
        if (grammar.EffectivePlanner.MaxPlanSteps <= 0 || grammar.EffectivePlanner.MaxPlanSteps > 256) errors.Add("planner.maxPlanSteps must be between 1 and 256.");
        if (grammar.EffectivePlanner.MaxSearchNodes <= 0 || grammar.EffectivePlanner.MaxSearchNodes > 1_000_000) errors.Add("planner.maxSearchNodes must be between 1 and 1,000,000.");
        if (grammar.EffectivePlanner.MaxDecompositionDepth <= 0 || grammar.EffectivePlanner.MaxDecompositionDepth > 128) errors.Add("planner.maxDecompositionDepth must be between 1 and 128.");
        return errors;
    }
}

public sealed class QuestWorldState
{
    public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Tags { get; } = new(StringComparer.OrdinalIgnoreCase);
    public QuestWorldSnapshot Capture() => new(new Dictionary<string, object?>(Values, StringComparer.OrdinalIgnoreCase), Tags.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    public QuestWorldState Clone() => FromSnapshot(Capture());
    public static QuestWorldState FromSnapshot(QuestWorldSnapshot? snapshot)
    {
        var state = new QuestWorldState();
        if (snapshot is null) return state;
        foreach (var pair in snapshot.Values) state.Values[pair.Key] = pair.Value;
        foreach (var tag in snapshot.Tags) state.Tags.Add(tag);
        return state;
    }
}

public sealed record QuestWorldSnapshot(IReadOnlyDictionary<string, object?> Values, IReadOnlyList<string> Tags);

public sealed record QuestPlanStep(
    int Index,
    QuestActionDefinition Action,
    IReadOnlyList<StoryletBinding> Bindings,
    QuestWorldSnapshot StateBefore,
    QuestWorldSnapshot StateAfter,
    double CumulativeCost);

public sealed record QuestPlannerDiagnostics(
    string Mode,
    int ExpandedNodes,
    int GeneratedNodes,
    int MaxFrontier,
    int DecompositionDepth,
    IReadOnlyList<string>? Notes = null)
{
    [JsonIgnore] public IReadOnlyList<string> EffectiveNotes => Notes ?? Array.Empty<string>();
}

public sealed record QuestPlan(
    QuestGrammarDefinition Grammar,
    IReadOnlyList<StoryletBinding> Bindings,
    IReadOnlyList<QuestPlanStep> Steps,
    QuestWorldSnapshot InitialState,
    QuestWorldSnapshot FinalState,
    ulong Seed,
    QuestPlannerDiagnostics Diagnostics,
    IReadOnlyList<string>? ValidationIssues = null)
{
    [JsonIgnore] public IReadOnlyList<string> EffectiveValidationIssues => ValidationIssues ?? Array.Empty<string>();
    [JsonIgnore] public bool IsValid => EffectiveValidationIssues.Count == 0;
    [JsonIgnore] public double TotalCost => Steps.Count == 0 ? 0 : Steps[^1].CumulativeCost;
}

public interface IQuestGrammarResolver
{
    ValueTask<IReadOnlyList<QuestGrammarDefinition>> LoadAllAsync(CancellationToken cancellationToken = default);
    ValueTask<QuestGrammarDefinition?> ResolveAsync(string id, CancellationToken cancellationToken = default);
}

public sealed record QuestObjectiveNode(int StepIndex, string ActionId, string Name, IReadOnlyList<string>? Tags = null)
{
    [JsonIgnore] public IReadOnlyList<string> EffectiveTags => Tags ?? Array.Empty<string>();
}
public sealed record QuestObjectiveEdge(int FromStepIndex, int ToStepIndex, string Reason);
public sealed record QuestObjectiveGraph(IReadOnlyList<QuestObjectiveNode> Nodes, IReadOnlyList<QuestObjectiveEdge> Edges);
