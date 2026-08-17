namespace Playroma.Text.Core.Generation;

public sealed record GenerationTraceNode(
    int Id,
    int? ParentId,
    string StageId,
    string Operation,
    string? Input = null,
    string? Output = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record GenerationTrace(IReadOnlyList<GenerationTraceNode> Nodes);

public sealed class GenerationTraceBuilder
{
    private readonly GenerationBudget _budget;
    private readonly List<GenerationTraceNode> _nodes = new();
    private int _nextId;

    public GenerationTraceBuilder(GenerationBudget budget) => _budget = budget;

    public int Add(string stageId, string operation, int? parentId = null, string? input = null, string? output = null, IReadOnlyDictionary<string, string>? metadata = null)
    {
        _budget.ConsumeTraceNode();
        var id = ++_nextId;
        _nodes.Add(new GenerationTraceNode(id, parentId, stageId, operation, input, output, metadata));
        return id;
    }

    public GenerationTrace Build() => new(_nodes.ToArray());
}
