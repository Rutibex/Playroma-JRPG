using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Registry;
using Playroma.Sound.Graph.Validation;

namespace Playroma.Sound.Authoring.Graphs;

public readonly record struct GraphNodePosition(float X, float Y);

public sealed record GraphConnectionValidation(bool IsValid, string Message)
{
    public static GraphConnectionValidation Valid { get; } = new(true, "Connection is valid.");
}

public sealed class GraphClipboardPackage
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public List<NodeDefinition> Nodes { get; set; } = [];
    public List<ConnectionDefinition> Connections { get; set; } = [];
    public List<GraphGroupDefinition> Groups { get; set; } = [];
    public List<GraphCommentDefinition> Comments { get; set; } = [];
}

/// <summary>Platform-neutral editing operations used by the visual graph editor and tests.</summary>
public static class VisualGraphEditing
{
    public static NodeDefinition AddNode(
        SoundGraphDefinition graph,
        GraphNodeRegistry registry,
        string typeId,
        GraphNodePosition position,
        string? preferredId = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(registry);
        NodeTypeDescriptor descriptor = registry.GetRequiredFactory(typeId).Descriptor;
        string id = UniqueId(graph.Nodes.Select(node => node.Id), preferredId ?? Slug(descriptor.DisplayName));
        var node = new NodeDefinition
        {
            Id = id,
            TypeId = descriptor.TypeId,
            SchemaVersion = descriptor.CurrentSchemaVersion,
            Editor = new NodeEditorMetadata { X = position.X, Y = position.Y }
        };
        foreach (GraphParameterDescriptor parameter in descriptor.Parameters)
            node.Parameters[parameter.Id] = parameter.DefaultValue;
        graph.Nodes.Add(node);
        if (descriptor.Category == GraphNodeCategory.Output && string.IsNullOrWhiteSpace(graph.Output.NodeId))
        {
            GraphPortDescriptor? output = descriptor.Ports.FirstOrDefault(port => port.Direction == GraphPortDirection.Output);
            graph.Output.NodeId = node.Id;
            graph.Output.PortId = output?.Id ?? "out";
        }
        return node;
    }

    public static void RemoveNodes(SoundGraphDefinition graph, IEnumerable<string> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(nodeIds);
        var ids = new HashSet<string>(nodeIds, StringComparer.Ordinal);
        graph.Nodes.RemoveAll(node => ids.Contains(node.Id));
        graph.Connections.RemoveAll(connection => ids.Contains(connection.FromNodeId) || ids.Contains(connection.ToNodeId));
        foreach (MacroDefinition macro in graph.Macros)
            macro.Bindings.RemoveAll(binding => TryBindingNode(binding.TargetPath, out string id) && ids.Contains(id));
        if (ids.Contains(graph.Output.NodeId))
        {
            graph.Output.NodeId = "";
            graph.Output.PortId = "out";
        }
    }

    public static IReadOnlyList<NodeDefinition> DuplicateNodes(
        SoundGraphDefinition graph,
        IEnumerable<string> nodeIds,
        float offsetX = 36,
        float offsetY = 36)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var selected = new HashSet<string>(nodeIds, StringComparer.Ordinal);
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = new List<NodeDefinition>();
        foreach (NodeDefinition original in graph.Nodes.Where(node => selected.Contains(node.Id)).ToArray())
        {
            string id = UniqueId(graph.Nodes.Select(node => node.Id).Concat(idMap.Values), original.Id);
            idMap.Add(original.Id, id);
            var copy = CloneNode(original);
            copy.Id = id;
            copy.Editor ??= new NodeEditorMetadata();
            copy.Editor.X += offsetX;
            copy.Editor.Y += offsetY;
            graph.Nodes.Add(copy);
            result.Add(copy);
        }
        foreach (ConnectionDefinition connection in graph.Connections.ToArray())
        {
            if (idMap.TryGetValue(connection.FromNodeId, out string? from) && idMap.TryGetValue(connection.ToNodeId, out string? to))
                graph.Connections.Add(new ConnectionDefinition { FromNodeId = from, FromPortId = connection.FromPortId, ToNodeId = to, ToPortId = connection.ToPortId });
        }
        return result;
    }

    public static GraphConnectionValidation ValidateConnection(
        SoundGraphDefinition graph,
        GraphNodeRegistry registry,
        string fromNodeId,
        string fromPortId,
        string toNodeId,
        string toPortId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(registry);
        if (StringComparer.Ordinal.Equals(fromNodeId, toNodeId)) return new(false, "A node cannot connect directly to itself.");
        NodeDefinition? fromNode = graph.Nodes.FirstOrDefault(node => node.Id == fromNodeId);
        NodeDefinition? toNode = graph.Nodes.FirstOrDefault(node => node.Id == toNodeId);
        if (fromNode is null || toNode is null) return new(false, "Both endpoint nodes must exist.");
        NodeTypeDescriptor fromDescriptor = registry.GetRequiredFactory(fromNode.TypeId).Descriptor;
        NodeTypeDescriptor toDescriptor = registry.GetRequiredFactory(toNode.TypeId).Descriptor;
        GraphPortDescriptor? fromPort = fromDescriptor.Ports.FirstOrDefault(port => port.Id == fromPortId && port.Direction == GraphPortDirection.Output);
        GraphPortDescriptor? toPort = toDescriptor.Ports.FirstOrDefault(port => port.Id == toPortId && port.Direction == GraphPortDirection.Input);
        if (fromPort is null) return new(false, $"Output port '{fromPortId}' does not exist.");
        if (toPort is null) return new(false, $"Input port '{toPortId}' does not exist.");
        if (!AreCompatible(fromPort.Type, toPort.Type)) return new(false, $"{fromPort.Type} cannot connect to {toPort.Type}.");
        if (graph.Connections.Any(connection => connection.FromNodeId == fromNodeId && connection.FromPortId == fromPortId && connection.ToNodeId == toNodeId && connection.ToPortId == toPortId))
            return new(false, "That connection already exists.");
        if (toPort.Cardinality == GraphPortCardinality.Single && graph.Connections.Any(connection => connection.ToNodeId == toNodeId && connection.ToPortId == toPortId))
            return new(false, "The destination port accepts only one connection.");
        // Validate the prospective graph so cycles and runtime shape rules use the production validator.
        graph.Connections.Add(new ConnectionDefinition { FromNodeId = fromNodeId, FromPortId = fromPortId, ToNodeId = toNodeId, ToPortId = toPortId });
        try
        {
            GraphValidationReport report = new GraphValidator(registry).Validate(graph);
            GraphDiagnostic? cycle = report.Diagnostics.FirstOrDefault(item => item.Code is "GRAPH_CYCLE" or "FEEDBACK_BOUNDARY");
            return cycle is null ? GraphConnectionValidation.Valid : new(false, cycle.Message);
        }
        finally
        {
            graph.Connections.RemoveAt(graph.Connections.Count - 1);
        }
    }

    public static void Connect(SoundGraphDefinition graph, GraphNodeRegistry registry, string fromNodeId, string fromPortId, string toNodeId, string toPortId)
    {
        GraphConnectionValidation validation = ValidateConnection(graph, registry, fromNodeId, fromPortId, toNodeId, toPortId);
        if (!validation.IsValid) throw new InvalidOperationException(validation.Message);
        graph.Connections.Add(new ConnectionDefinition { FromNodeId = fromNodeId, FromPortId = fromPortId, ToNodeId = toNodeId, ToPortId = toPortId });
    }

    public static bool Disconnect(SoundGraphDefinition graph, string fromNodeId, string fromPortId, string toNodeId, string toPortId) =>
        graph.Connections.RemoveAll(connection => connection.FromNodeId == fromNodeId && connection.FromPortId == fromPortId && connection.ToNodeId == toNodeId && connection.ToPortId == toPortId) > 0;

    public static void MoveNodes(SoundGraphDefinition graph, IReadOnlyDictionary<string, GraphNodePosition> positions)
    {
        foreach (NodeDefinition node in graph.Nodes)
        {
            if (!positions.TryGetValue(node.Id, out GraphNodePosition position)) continue;
            node.Editor ??= new NodeEditorMetadata();
            node.Editor.X = position.X;
            node.Editor.Y = position.Y;
        }
    }

    public static void RenameNode(SoundGraphDefinition graph, string oldId, string newId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newId);
        if (graph.Nodes.Any(node => node.Id == newId)) throw new InvalidOperationException($"Node ID '{newId}' is already in use.");
        NodeDefinition node = graph.Nodes.FirstOrDefault(item => item.Id == oldId) ?? throw new KeyNotFoundException($"Node '{oldId}' was not found.");
        node.Id = newId;
        foreach (ConnectionDefinition connection in graph.Connections)
        {
            if (connection.FromNodeId == oldId) connection.FromNodeId = newId;
            if (connection.ToNodeId == oldId) connection.ToNodeId = newId;
        }
        if (graph.Output.NodeId == oldId) graph.Output.NodeId = newId;
        const string prefix = "node:";
        const string marker = "/parameter:";
        foreach (MacroDefinition macro in graph.Macros)
        {
            foreach (MacroBinding binding in macro.Bindings)
            {
                if (binding.TargetPath.StartsWith(prefix + oldId + marker, StringComparison.Ordinal))
                    binding.TargetPath = prefix + newId + binding.TargetPath[(prefix.Length + oldId.Length)..];
            }
        }
    }

    public static void SetParameter(SoundGraphDefinition graph, string nodeId, GraphParameterDescriptor descriptor, object? value)
    {
        NodeDefinition node = graph.Nodes.FirstOrDefault(item => item.Id == nodeId) ?? throw new KeyNotFoundException($"Node '{nodeId}' was not found.");
        node.Parameters[descriptor.Id] = descriptor.Type switch
        {
            GraphParameterType.Boolean => GraphValue.FromBoolean(Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture)),
            GraphParameterType.Text or GraphParameterType.Enumeration => GraphValue.FromString(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""),
            GraphParameterType.Integer or GraphParameterType.Seed or GraphParameterType.Number => GraphValue.FromNumber(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)),
            _ => descriptor.DefaultValue
        };
    }

    public static GraphClipboardPackage Copy(SoundGraphDefinition graph, IEnumerable<string> nodeIds)
    {
        var ids = new HashSet<string>(nodeIds, StringComparer.Ordinal);
        return new GraphClipboardPackage
        {
            Nodes = graph.Nodes.Where(node => ids.Contains(node.Id)).Select(CloneNode).ToList(),
            Connections = graph.Connections.Where(connection => ids.Contains(connection.FromNodeId) && ids.Contains(connection.ToNodeId)).Select(CloneConnection).ToList(),
            Groups = graph.Editor.Groups.Where(group => IntersectsSelectedGroup(graph, group, ids)).Select(CloneGroup).ToList(),
            Comments = graph.Editor.Comments.Where(comment => IsInsideSelection(graph, comment.X, comment.Y, ids)).Select(CloneComment).ToList()
        };
    }

    public static IReadOnlyList<NodeDefinition> Paste(SoundGraphDefinition graph, GraphClipboardPackage package, GraphNodePosition anchor)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(package);
        if (package.Version != GraphClipboardPackage.CurrentVersion) throw new InvalidDataException($"Clipboard format {package.Version} is unsupported.");
        if (package.Nodes.Count == 0) return [];
        graph.Editor ??= new GraphEditorDocumentMetadata();
        graph.Editor.Groups ??= [];
        graph.Editor.Comments ??= [];
        float minimumX = package.Nodes.Min(node => node.Editor?.X ?? 0);
        float minimumY = package.Nodes.Min(node => node.Editor?.Y ?? 0);
        float offsetX = anchor.X - minimumX;
        float offsetY = anchor.Y - minimumY;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var pasted = new List<NodeDefinition>();
        foreach (NodeDefinition original in package.Nodes)
        {
            NodeDefinition copy = CloneNode(original);
            string newId = UniqueId(graph.Nodes.Select(node => node.Id).Concat(map.Values), original.Id);
            map[original.Id] = newId;
            copy.Id = newId;
            copy.Editor ??= new NodeEditorMetadata();
            copy.Editor.X += offsetX;
            copy.Editor.Y += offsetY;
            graph.Nodes.Add(copy);
            pasted.Add(copy);
        }
        foreach (ConnectionDefinition connection in package.Connections)
        {
            if (map.TryGetValue(connection.FromNodeId, out string? from) && map.TryGetValue(connection.ToNodeId, out string? to))
                graph.Connections.Add(new ConnectionDefinition { FromNodeId = from, FromPortId = connection.FromPortId, ToNodeId = to, ToPortId = connection.ToPortId });
        }
        foreach (GraphGroupDefinition original in package.Groups)
        {
            GraphGroupDefinition copy = CloneGroup(original);
            copy.Id = UniqueId(graph.Editor.Groups.Select(group => group.Id), original.Id);
            copy.X += offsetX;
            copy.Y += offsetY;
            graph.Editor.Groups.Add(copy);
        }
        foreach (GraphCommentDefinition original in package.Comments)
        {
            GraphCommentDefinition copy = CloneComment(original);
            copy.Id = UniqueId(graph.Editor.Comments.Select(comment => comment.Id), original.Id);
            copy.X += offsetX;
            copy.Y += offsetY;
            graph.Editor.Comments.Add(copy);
        }
        return pasted;
    }

    public static void AutoLayout(SoundGraphDefinition graph, GraphNodeRegistry registry, float columnWidth = 250, float rowHeight = 150)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var indegree = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        var incoming = graph.Nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);
        var outgoing = graph.Nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (ConnectionDefinition connection in graph.Connections)
        {
            if (!indegree.ContainsKey(connection.FromNodeId) || !indegree.ContainsKey(connection.ToNodeId)) continue;
            indegree[connection.ToNodeId]++;
            outgoing[connection.FromNodeId].Add(connection.ToNodeId);
            incoming[connection.ToNodeId].Add(connection.FromNodeId);
        }
        var queue = new Queue<string>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key).OrderBy(id => id, StringComparer.Ordinal));
        var layer = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            string id = queue.Dequeue();
            visited.Add(id);
            foreach (string target in outgoing[id])
            {
                layer[target] = Math.Max(layer[target], layer[id] + 1);
                if (--indegree[target] == 0) queue.Enqueue(target);
            }
        }
        foreach (NodeDefinition node in graph.Nodes.Where(node => !visited.Contains(node.Id)))
            layer[node.Id] = incoming[node.Id].Count == 0 ? 0 : incoming[node.Id].Max(source => layer.GetValueOrDefault(source)) + 1;
        foreach (IGrouping<int, NodeDefinition> column in graph.Nodes.GroupBy(node => layer[node.Id]).OrderBy(group => group.Key))
        {
            int row = 0;
            foreach (NodeDefinition node in column.OrderBy(node => registry.GetRequiredFactory(node.TypeId).Descriptor.Category).ThenBy(node => node.Id, StringComparer.Ordinal))
            {
                node.Editor ??= new NodeEditorMetadata();
                node.Editor.X = 40 + column.Key * columnWidth;
                node.Editor.Y = 40 + row++ * rowHeight;
            }
        }
    }

    public static SoundGraphDefinition CreateAuditionGraph(SoundGraphDefinition source, GraphNodeRegistry registry, string nodeId)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(registry);
        NodeDefinition node = source.Nodes.FirstOrDefault(item => item.Id == nodeId) ?? throw new KeyNotFoundException($"Node '{nodeId}' was not found.");
        NodeTypeDescriptor descriptor = registry.GetRequiredFactory(node.TypeId).Descriptor;
        GraphPortDescriptor outputPort = descriptor.Ports.FirstOrDefault(port => port.Direction == GraphPortDirection.Output && (port.Type is GraphPortType.AudioMono or GraphPortType.AudioStereo or GraphPortType.AudioAnyChannels))
            ?? throw new InvalidOperationException("The selected node does not expose an audio output.");
        var serializer = new Playroma.Sound.Graph.Serialization.SoundGraphSerializer();
        SoundGraphDefinition graph = serializer.Load(serializer.Serialize(source));
        NodeDefinition output = AddNode(graph, registry, "core.output", new GraphNodePosition((node.Editor?.X ?? 0) + 260, node.Editor?.Y ?? 0), "audition-output");
        graph.Output.NodeId = output.Id;
        graph.Output.PortId = "out";
        graph.Connections.RemoveAll(connection => connection.ToNodeId == output.Id);
        Connect(graph, registry, node.Id, outputPort.Id, output.Id, "in");
        graph.Id = source.Id + ".audition." + node.Id;
        graph.DisplayName = source.DisplayName + " — " + node.Id;
        return graph;
    }

    public static GraphGroupDefinition AddGroup(SoundGraphDefinition graph, GraphNodePosition position)
    {
        graph.Editor ??= new GraphEditorDocumentMetadata();
        graph.Editor.Groups ??= [];
        var group = new GraphGroupDefinition { Id = UniqueId(graph.Editor.Groups.Select(item => item.Id), "group"), X = position.X, Y = position.Y };
        graph.Editor.Groups.Add(group);
        return group;
    }

    public static GraphCommentDefinition AddComment(SoundGraphDefinition graph, GraphNodePosition position)
    {
        graph.Editor ??= new GraphEditorDocumentMetadata();
        graph.Editor.Comments ??= [];
        var comment = new GraphCommentDefinition { Id = UniqueId(graph.Editor.Comments.Select(item => item.Id), "comment"), X = position.X, Y = position.Y };
        graph.Editor.Comments.Add(comment);
        return comment;
    }

    private static bool AreCompatible(GraphPortType source, GraphPortType target)
    {
        if (source == target) return true;
        bool sourceAudio = source is GraphPortType.AudioMono or GraphPortType.AudioStereo or GraphPortType.AudioAnyChannels;
        bool targetAudio = target is GraphPortType.AudioMono or GraphPortType.AudioStereo or GraphPortType.AudioAnyChannels;
        if (sourceAudio && target == GraphPortType.AudioAnyChannels) return true;
        return source == GraphPortType.AudioMono && target == GraphPortType.AudioStereo && targetAudio;
    }

    private static string UniqueId(IEnumerable<string> existing, string baseId)
    {
        var used = new HashSet<string>(existing, StringComparer.Ordinal);
        string normalized = string.IsNullOrWhiteSpace(baseId) ? "node" : Slug(baseId);
        if (!used.Contains(normalized)) return normalized;
        int suffix = 2;
        while (used.Contains($"{normalized}-{suffix}")) suffix++;
        return $"{normalized}-{suffix}";
    }

    private static string Slug(string text)
    {
        string result = new(text.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
        while (result.Contains("--", StringComparison.Ordinal)) result = result.Replace("--", "-", StringComparison.Ordinal);
        return result.Trim('-');
    }

    private static bool TryBindingNode(string path, out string nodeId)
    {
        nodeId = "";
        if (!GraphValidator.TryParseTargetPath(path, out string parsed, out _)) return false;
        nodeId = parsed;
        return true;
    }

    private static NodeDefinition CloneNode(NodeDefinition node) => new()
    {
        Id = node.Id,
        TypeId = node.TypeId,
        SchemaVersion = node.SchemaVersion,
        LocalSeed = node.LocalSeed,
        Parameters = new Dictionary<string, GraphValue>(node.Parameters, StringComparer.Ordinal),
        Editor = node.Editor is null ? null : new NodeEditorMetadata { X = node.Editor.X, Y = node.Editor.Y, Collapsed = node.Editor.Collapsed, Bypassed = node.Editor.Bypassed, Comment = node.Editor.Comment }
    };

    private static ConnectionDefinition CloneConnection(ConnectionDefinition connection) => new() { FromNodeId = connection.FromNodeId, FromPortId = connection.FromPortId, ToNodeId = connection.ToNodeId, ToPortId = connection.ToPortId };
    private static GraphGroupDefinition CloneGroup(GraphGroupDefinition group) => new() { Id = group.Id, Title = group.Title, X = group.X, Y = group.Y, Width = group.Width, Height = group.Height, Collapsed = group.Collapsed };
    private static GraphCommentDefinition CloneComment(GraphCommentDefinition comment) => new() { Id = comment.Id, Text = comment.Text, X = comment.X, Y = comment.Y, Width = comment.Width, Height = comment.Height };

    private static bool IntersectsSelectedGroup(SoundGraphDefinition graph, GraphGroupDefinition group, HashSet<string> ids) => graph.Nodes.Any(node => ids.Contains(node.Id) && node.Editor is { } editor && editor.X >= group.X && editor.X <= group.X + group.Width && editor.Y >= group.Y && editor.Y <= group.Y + group.Height);
    private static bool IsInsideSelection(SoundGraphDefinition graph, float x, float y, HashSet<string> ids) => graph.Nodes.Where(node => ids.Contains(node.Id)).Any(node => node.Editor is { } editor && Math.Abs(editor.X - x) < 400 && Math.Abs(editor.Y - y) < 300);
}
