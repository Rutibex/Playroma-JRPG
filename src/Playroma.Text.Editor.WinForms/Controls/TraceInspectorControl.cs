using Playroma.Text.Core.Generation;

namespace Playroma.Text.Editor.WinForms.Controls;

public sealed class TraceInspectorControl : UserControl
{
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly TextBox _details = new() { Dock = DockStyle.Bottom, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 180 };
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 330 };

    public TraceInspectorControl()
    {
        _split.Panel1.Controls.Add(_tree);
        _split.Panel2.Controls.Add(_details); _details.Dock = DockStyle.Fill;
        Controls.Add(_split);
        _tree.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is GenerationTraceNode trace) ShowTraceDetails(trace);
            else if (e.Node?.Tag is TextStudioResultItem result) ShowResultDetails(result);
            else _details.Clear();
        };
    }

    public void Inspect(TextStudioResultItem? item)
    {
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear(); _details.Clear();
            if (item is null)
            {
                _tree.Nodes.Add("Select a result to inspect its semantics, scores, provenance and generation trace.");
                return;
            }

            var summary = new TreeNode("Result semantics, scores & provenance") { Tag = item };
            if (item.Bundle.Features.Count > 0)
            {
                var features = new TreeNode($"Semantic features ({item.Bundle.Features.Count})");
                foreach (var feature in item.Bundle.Features.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                    features.Nodes.Add($"{feature.Key} = {feature.Value.DisplayText}");
                summary.Nodes.Add(features);
            }
            if (item.Bundle.Scores.Count > 0)
            {
                var scores = new TreeNode($"Scores ({item.Bundle.Scores.Count})");
                foreach (var score in item.Bundle.Scores.OrderByDescending(x => x.Value)) scores.Nodes.Add($"{score.Name} = {score.Value:0.000}");
                summary.Nodes.Add(scores);
            }
            if (item.Bundle.Tags.Count > 0)
            {
                var tags = new TreeNode($"Tags ({item.Bundle.Tags.Count})");
                foreach (var tag in item.Bundle.Tags.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) tags.Nodes.Add($"{tag.Key} = {tag.Value}");
                summary.Nodes.Add(tags);
            }
            _tree.Nodes.Add(summary);

            var traceData = item.Bundle.Trace;
            if (traceData is not null && traceData.Nodes.Count > 0)
            {
                var traceRoot = new TreeNode("Generation trace");
                var nodes = new Dictionary<int, TreeNode>();
                foreach (var node in traceData.Nodes.OrderBy(x => x.Id))
                {
                    var label = $"{node.Operation}" + (node.Metadata?.TryGetValue("symbol", out var symbol) == true ? $"  #{symbol}#" : string.Empty);
                    var ui = new TreeNode(label) { Tag = node, ToolTipText = node.Output ?? node.Input ?? string.Empty };
                    nodes[node.Id] = ui;
                    if (node.ParentId is { } parent && nodes.TryGetValue(parent, out var parentNode)) parentNode.Nodes.Add(ui); else traceRoot.Nodes.Add(ui);
                }
                _tree.Nodes.Add(traceRoot);
                traceRoot.Expand();
            }

            summary.Expand(); _tree.SelectedNode = summary;
        }
        finally { _tree.EndUpdate(); }
    }

    private void ShowResultDetails(TextStudioResultItem item)
    {
        var lines = new List<string>
        {
            $"Playground seed: {item.Seed}",
            $"Recipe: {item.Bundle.Provenance?.RecipeId}",
            $"Recipe version: {item.Bundle.Provenance?.RecipeVersion}",
            $"Actual generation seed: {item.Bundle.Provenance?.Seed}",
            $"Engine: {item.Bundle.Provenance?.EngineVersion}",
            $"Semantic features: {item.Bundle.Features.Count}",
            $"Scores: {item.Bundle.Scores.Count}",
            $"Diagnostics: {item.Bundle.Diagnostics.Count}"
        };
        if (item.Bundle.Features.Count > 0)
        {
            lines.Add(string.Empty); lines.Add("Features:");
            lines.AddRange(item.Bundle.Features.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => $"  {x.Key} = {x.Value.DisplayText} [{x.Value.Type}]"));
        }
        if (item.Bundle.Scores.Count > 0)
        {
            lines.Add(string.Empty); lines.Add("Scores:");
            lines.AddRange(item.Bundle.Scores.OrderByDescending(x => x.Value).Select(x => $"  {x.Name} = {x.Value:0.000}  {x.Explanation}"));
        }
        _details.Lines = lines.ToArray();
    }

    private void ShowTraceDetails(GenerationTraceNode node)
    {
        var lines = new List<string>
        {
            $"Stage: {node.StageId}", $"Operation: {node.Operation}", $"Input: {node.Input}", $"Output: {node.Output}"
        };
        if (node.Metadata is { Count: > 0 })
        {
            lines.Add(string.Empty); lines.Add("Metadata:");
            lines.AddRange(node.Metadata.OrderBy(x => x.Key).Select(x => $"  {x.Key} = {x.Value}"));
        }
        _details.Lines = lines.ToArray();
    }
}
