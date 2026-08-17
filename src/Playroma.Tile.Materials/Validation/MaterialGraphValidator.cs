using Playroma.Tile.Materials.Graph;
using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Materials.Validation;

public sealed class MaterialGraphValidator
{
    private readonly MaterialNodeRegistry _registry;
    public MaterialGraphValidator(MaterialNodeRegistry registry) => _registry = registry;

    public IReadOnlyList<MaterialIssue> Validate(MaterialGraph graph)
    {
        var issues = new List<MaterialIssue>();
        if (graph.Nodes.Count == 0) { issues.Add(new(MaterialIssueSeverity.Error, "GRAPH_EMPTY", "The graph contains no nodes.")); return issues; }
        var ids = new HashSet<Guid>();
        foreach (var node in graph.Nodes)
        {
            if (!ids.Add(node.Id)) issues.Add(new(MaterialIssueSeverity.Error, "DUPLICATE_NODE", "Two nodes use the same identifier.", node.Id));
            if (!_registry.TryGet(node.TypeId, out var definition)) { issues.Add(new(MaterialIssueSeverity.Error, "MISSING_NODE", $"Node type '{node.TypeId}' is unavailable.", node.Id)); continue; }
            foreach (var input in definition.Inputs.Where(x => x.Required))
                if (!node.Inputs.ContainsKey(input.Name)) issues.Add(new(MaterialIssueSeverity.Error, "MISSING_INPUT", $"{definition.DisplayName} requires input '{input.Name}'.", node.Id));
            foreach (var connection in node.Inputs)
                if (graph.Find(connection.Value.SourceNodeId) is null) issues.Add(new(MaterialIssueSeverity.Error, "BROKEN_CONNECTION", $"Input '{connection.Key}' points to a missing node.", node.Id));
        }
        if (graph.OutputNodeId == Guid.Empty || graph.Find(graph.OutputNodeId) is null)
            issues.Add(new(MaterialIssueSeverity.Error, "OUTPUT_MISSING", "The graph output node is missing."));
        DetectCycles(graph, issues);
        ValidateTileability(graph, issues);
        return issues;
    }

    private static void DetectCycles(MaterialGraph graph, List<MaterialIssue> issues)
    {
        var visited = new HashSet<Guid>(); var stack = new HashSet<Guid>();
        bool Visit(Guid id)
        {
            if (stack.Contains(id)) return true;
            if (!visited.Add(id)) return false;
            stack.Add(id);
            var node = graph.Find(id);
            if (node is not null)
                foreach (var source in node.Inputs.Values.Select(x => x.SourceNodeId))
                    if (Visit(source)) return true;
            stack.Remove(id); return false;
        }
        foreach (var node in graph.Nodes)
            if (Visit(node.Id)) { issues.Add(new(MaterialIssueSeverity.Error, "GRAPH_CYCLE", "The graph contains a circular dependency.", node.Id)); break; }
    }

    private void ValidateTileability(MaterialGraph graph, List<MaterialIssue> issues)
    {
        var repaired = false;
        foreach (var node in graph.Nodes)
        {
            var support = _registry.Get(node.TypeId).Tileability;
            if (support == TileabilitySupport.RequiresRepair) repaired = true;
            if (support == TileabilitySupport.None && !repaired)
                issues.Add(new(MaterialIssueSeverity.Warning, "TILEABILITY", $"{_registry.Get(node.TypeId).DisplayName} may introduce seams; add Seam Repair after it.", node.Id));
        }
    }
}
