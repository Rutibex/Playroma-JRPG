using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Registry;

namespace Playroma.Sound.Graph.Compilation;

internal static class GraphBypassResolver
{
    public static SoundGraphDefinition Resolve(SoundGraphDefinition source, GraphNodeRegistry registry)
    {
        if (!source.Nodes.Any(node => node.Editor?.Bypassed == true)) return source;
        SoundGraphDefinition graph = Clone(source);
        foreach (NodeDefinition node in graph.Nodes.Where(item => item.Editor?.Bypassed == true).ToArray())
        {
            if (!registry.TryGetFactory(node.TypeId, out IGraphNodeFactory factory)) continue;
            NodeTypeDescriptor descriptor = factory.Descriptor;
            GraphPortDescriptor[] inputs = descriptor.Ports.Where(port => port.Direction == GraphPortDirection.Input && IsAudio(port.Type)).ToArray();
            GraphPortDescriptor[] outputs = descriptor.Ports.Where(port => port.Direction == GraphPortDirection.Output && IsAudio(port.Type)).ToArray();
            if (inputs.Length != 1 || outputs.Length != 1 || graph.Output.NodeId == node.Id) continue;
            ConnectionDefinition[] incoming = graph.Connections.Where(connection => connection.ToNodeId == node.Id && connection.ToPortId == inputs[0].Id).ToArray();
            ConnectionDefinition[] outgoing = graph.Connections.Where(connection => connection.FromNodeId == node.Id && connection.FromPortId == outputs[0].Id).ToArray();
            graph.Connections.RemoveAll(connection => connection.FromNodeId == node.Id || connection.ToNodeId == node.Id);
            foreach (ConnectionDefinition before in incoming)
            foreach (ConnectionDefinition after in outgoing)
            {
                if (!graph.Connections.Any(connection => connection.FromNodeId == before.FromNodeId && connection.FromPortId == before.FromPortId && connection.ToNodeId == after.ToNodeId && connection.ToPortId == after.ToPortId))
                    graph.Connections.Add(new ConnectionDefinition { FromNodeId = before.FromNodeId, FromPortId = before.FromPortId, ToNodeId = after.ToNodeId, ToPortId = after.ToPortId });
            }
            graph.Nodes.Remove(node);
            foreach (MacroDefinition macro in graph.Macros)
                macro.Bindings.RemoveAll(binding => binding.TargetPath.StartsWith($"node:{node.Id}/parameter:", StringComparison.Ordinal));
        }
        return graph;
    }

    private static bool IsAudio(GraphPortType type) => type is GraphPortType.AudioMono or GraphPortType.AudioStereo or GraphPortType.AudioAnyChannels;

    private static SoundGraphDefinition Clone(SoundGraphDefinition graph)
    {
        GraphEditorDocumentMetadata editor = graph.Editor ?? new GraphEditorDocumentMetadata();
        return new SoundGraphDefinition
        {
            FormatVersion = graph.FormatVersion,
            Id = graph.Id,
            DisplayName = graph.DisplayName,
            RootSeed = graph.RootSeed,
            Nodes = graph.Nodes.Select(node => new NodeDefinition
        {
            Id = node.Id, TypeId = node.TypeId, SchemaVersion = node.SchemaVersion, LocalSeed = node.LocalSeed,
            Parameters = new Dictionary<string, GraphValue>(node.Parameters, StringComparer.Ordinal),
            Editor = node.Editor is null ? null : new NodeEditorMetadata { X = node.Editor.X, Y = node.Editor.Y, Collapsed = node.Editor.Collapsed, Bypassed = node.Editor.Bypassed, Comment = node.Editor.Comment }
            }).ToList(),
            Connections = graph.Connections.Select(connection => new ConnectionDefinition { FromNodeId = connection.FromNodeId, FromPortId = connection.FromPortId, ToNodeId = connection.ToNodeId, ToPortId = connection.ToPortId }).ToList(),
            Macros = graph.Macros.Select(macro => new MacroDefinition
        {
            Id = macro.Id, DisplayName = macro.DisplayName, Minimum = macro.Minimum, Maximum = macro.Maximum, DefaultValue = macro.DefaultValue,
            Bindings = macro.Bindings.Select(binding => new MacroBinding { TargetPath = binding.TargetPath, InputMinimum = binding.InputMinimum, InputMaximum = binding.InputMaximum, OutputMinimum = binding.OutputMinimum, OutputMaximum = binding.OutputMaximum, Curve = binding.Curve, Steps = binding.Steps }).ToList()
            }).ToList(),
            Output = new GraphOutputDefinition { NodeId = graph.Output.NodeId, PortId = graph.Output.PortId, EnableLatencyCompensation = graph.Output.EnableLatencyCompensation, MaximumTailSeconds = graph.Output.MaximumTailSeconds },
            Editor = new GraphEditorDocumentMetadata
            {
                ViewX = editor.ViewX, ViewY = editor.ViewY, Zoom = editor.Zoom, ShowGrid = editor.ShowGrid, SnapToGrid = editor.SnapToGrid,
                Groups = editor.Groups.Select(group => new GraphGroupDefinition { Id = group.Id, Title = group.Title, X = group.X, Y = group.Y, Width = group.Width, Height = group.Height, Collapsed = group.Collapsed }).ToList(),
                Comments = editor.Comments.Select(comment => new GraphCommentDefinition { Id = comment.Id, Text = comment.Text, X = comment.X, Y = comment.Y, Width = comment.Width, Height = comment.Height }).ToList()
            },
            Metadata = new Dictionary<string, string>(graph.Metadata, StringComparer.Ordinal)
        };
    }
}
