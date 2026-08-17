using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Registry;

namespace Playroma.Sound.Graph.Validation;

public sealed class GraphValidator
{
    private readonly GraphNodeRegistry _registry;

    public GraphValidator(GraphNodeRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public GraphValidationReport Validate(SoundGraphDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var report = new GraphValidationReport();
        definition.Editor ??= new GraphEditorDocumentMetadata();
        definition.Editor.Groups ??= [];
        definition.Editor.Comments ??= [];

        if (definition.FormatVersion != SoundGraphDefinition.CurrentFormatVersion)
        {
            report.Error("GRAPH_VERSION", $"Graph format version {definition.FormatVersion} is not supported.");
        }

        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            report.Error("GRAPH_ID", "The graph requires a stable ID.");
        }

        ValidateNodes(definition, report, out Dictionary<string, NodeDefinition> nodes, out Dictionary<string, NodeTypeDescriptor> descriptors);
        ValidateEditorMetadata(definition, descriptors, report);
        ValidateConnections(definition, nodes, descriptors, report);
        ValidateOutput(definition, nodes, descriptors, report);
        ValidateMacros(definition, nodes, descriptors, report);
        ValidateCycles(definition, nodes, report);
        ValidateReachability(definition, nodes, report);
        return report;
    }

    private void ValidateNodes(
        SoundGraphDefinition definition,
        GraphValidationReport report,
        out Dictionary<string, NodeDefinition> nodes,
        out Dictionary<string, NodeTypeDescriptor> descriptors)
    {
        nodes = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal);
        descriptors = new Dictionary<string, NodeTypeDescriptor>(StringComparer.Ordinal);

        foreach (NodeDefinition node in definition.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                report.Error("NODE_ID", "Every node requires a stable ID.");
                continue;
            }

            if (!nodes.TryAdd(node.Id, node))
            {
                report.Error("DUPLICATE_NODE", $"Node ID '{node.Id}' is duplicated.", node.Id);
                continue;
            }

            if (!_registry.TryGetFactory(node.TypeId, out IGraphNodeFactory factory))
            {
                report.Error("UNKNOWN_NODE_TYPE", $"Node type '{node.TypeId}' is not registered.", node.Id);
                continue;
            }

            NodeTypeDescriptor descriptor = factory.Descriptor;
            descriptors[node.Id] = descriptor;
            ValidateRuntimePortShape(node, descriptor, report);
            if (node.SchemaVersion > descriptor.CurrentSchemaVersion)
            {
                report.Error("NODE_SCHEMA_NEWER", $"Node schema {node.SchemaVersion} is newer than supported schema {descriptor.CurrentSchemaVersion}.", node.Id);
            }
            else if (node.SchemaVersion < 1)
            {
                report.Error("NODE_SCHEMA_INVALID", "Node schema versions begin at 1.", node.Id);
            }

            ValidateParameters(node, descriptor, report);
        }
    }

    private static void ValidateRuntimePortShape(NodeDefinition node, NodeTypeDescriptor descriptor, GraphValidationReport report)
    {
        GraphPortDescriptor[] audioInputs = descriptor.Ports
            .Where(port => port.Direction == GraphPortDirection.Input && IsAudioPort(port.Type))
            .ToArray();
        int audioOutputs = descriptor.Ports.Count(port =>
            port.Direction == GraphPortDirection.Output && IsAudioPort(port.Type));

        if (audioOutputs > 1)
        {
            report.Error("MULTI_OUTPUT_UNSUPPORTED", "Runtime nodes may expose at most one audio output.", node.Id);
        }

        if (audioInputs.Length > 1 && audioInputs.Any(port => port.Cardinality == GraphPortCardinality.Multiple))
        {
            report.Error("AMBIGUOUS_MULTI_INPUT", "A runtime node may use one multiple-cardinality audio input or multiple single-cardinality inputs, but not both.", node.Id);
        }
    }

    private static void ValidateParameters(NodeDefinition node, NodeTypeDescriptor descriptor, GraphValidationReport report)
    {
        foreach ((string id, GraphValue value) in node.Parameters)
        {
            GraphParameterDescriptor? parameter = descriptor.Parameters.FirstOrDefault(item => item.Id == id);
            if (parameter is null)
            {
                report.Warning("UNKNOWN_PARAMETER", $"Parameter '{id}' is not declared by node type '{descriptor.TypeId}'.", node.Id);
                continue;
            }

            switch (parameter.Type)
            {
                case GraphParameterType.Number:
                case GraphParameterType.Integer:
                case GraphParameterType.Seed:
                    double number = value.AsDouble(double.NaN);
                    if (!double.IsFinite(number))
                    {
                        report.Error("PARAMETER_NUMBER", $"Parameter '{id}' must be numeric.", node.Id);
                    }
                    else if ((parameter.Minimum.HasValue && number < parameter.Minimum.Value) ||
                             (parameter.Maximum.HasValue && number > parameter.Maximum.Value))
                    {
                        report.Error("PARAMETER_RANGE", $"Parameter '{id}' is outside its legal range.", node.Id);
                    }
                    break;

                case GraphParameterType.Boolean:
                    if (value.Kind is not GraphValueKind.Boolean and not GraphValueKind.Number and not GraphValueKind.String)
                    {
                        report.Error("PARAMETER_BOOLEAN", $"Parameter '{id}' must be Boolean.", node.Id);
                    }
                    break;

                case GraphParameterType.Enumeration:
                    string enumValue = value.AsString();
                    if (parameter.AllowedValues is not null && !parameter.AllowedValues.Contains(enumValue, StringComparer.OrdinalIgnoreCase))
                    {
                        report.Error("PARAMETER_ENUM", $"Parameter '{id}' has unsupported value '{enumValue}'.", node.Id);
                    }
                    break;
            }
        }
    }

    private static void ValidateEditorMetadata(
        SoundGraphDefinition definition,
        IReadOnlyDictionary<string, NodeTypeDescriptor> descriptors,
        GraphValidationReport report)
    {
        if (!float.IsFinite(definition.Editor.Zoom) || definition.Editor.Zoom is < 0.1f or > 4f ||
            !float.IsFinite(definition.Editor.ViewX) || !float.IsFinite(definition.Editor.ViewY))
            report.Error("EDITOR_VIEW", "Graph editor view coordinates and zoom must be finite, with zoom between 0.1 and 4.");

        foreach (NodeDefinition node in definition.Nodes)
        {
            if (node.Editor is null) continue;
            if (!float.IsFinite(node.Editor.X) || !float.IsFinite(node.Editor.Y))
                report.Error("EDITOR_NODE_POSITION", "Node editor coordinates must be finite.", node.Id);
            if (node.Editor.Bypassed &&
                descriptors.TryGetValue(node.Id, out NodeTypeDescriptor? descriptor) &&
                descriptor is not null)
            {
                int inputs = descriptor.Ports.Count(port => port.Direction == GraphPortDirection.Input && IsAudioPort(port.Type));
                int outputs = descriptor.Ports.Count(port => port.Direction == GraphPortDirection.Output && IsAudioPort(port.Type));
                if (inputs != 1 || outputs != 1 || descriptor.Category == GraphNodeCategory.Output)
                    report.Warning("BYPASS_UNSUPPORTED", "Only ordinary one-audio-input/one-audio-output nodes can be bypassed.", node.Id);
            }
        }

        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (GraphGroupDefinition group in definition.Editor.Groups)
        {
            if (string.IsNullOrWhiteSpace(group.Id) || !groupIds.Add(group.Id)) report.Error("EDITOR_GROUP_ID", $"Graph group ID '{group.Id}' is empty or duplicated.");
            if (!float.IsFinite(group.X) || !float.IsFinite(group.Y) || !float.IsFinite(group.Width) || !float.IsFinite(group.Height) || group.Width < 40 || group.Height < 40)
                report.Error("EDITOR_GROUP_BOUNDS", $"Graph group '{group.Id}' has invalid bounds.");
        }

        var commentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (GraphCommentDefinition comment in definition.Editor.Comments)
        {
            if (string.IsNullOrWhiteSpace(comment.Id) || !commentIds.Add(comment.Id)) report.Error("EDITOR_COMMENT_ID", $"Graph comment ID '{comment.Id}' is empty or duplicated.");
            if (!float.IsFinite(comment.X) || !float.IsFinite(comment.Y) || !float.IsFinite(comment.Width) || !float.IsFinite(comment.Height) || comment.Width < 40 || comment.Height < 40)
                report.Error("EDITOR_COMMENT_BOUNDS", $"Graph comment '{comment.Id}' has invalid bounds.");
        }
    }

    private static void ValidateConnections(
        SoundGraphDefinition definition,
        IReadOnlyDictionary<string, NodeDefinition> nodes,
        IReadOnlyDictionary<string, NodeTypeDescriptor> descriptors,
        GraphValidationReport report)
    {
        var inputCounts = new Dictionary<(string Node, string Port), int>();
        var connectionKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (ConnectionDefinition connection in definition.Connections)
        {
            string key = $"{connection.FromNodeId}:{connection.FromPortId}->{connection.ToNodeId}:{connection.ToPortId}";
            if (!connectionKeys.Add(key))
            {
                report.Warning("DUPLICATE_CONNECTION", "The connection is duplicated.", connection: key);
            }

            if (!nodes.ContainsKey(connection.FromNodeId))
            {
                report.Error("MISSING_SOURCE_NODE", $"Connection source node '{connection.FromNodeId}' does not exist.", connection: key);
                continue;
            }

            if (!nodes.ContainsKey(connection.ToNodeId))
            {
                report.Error("MISSING_TARGET_NODE", $"Connection target node '{connection.ToNodeId}' does not exist.", connection: key);
                continue;
            }

            if (!descriptors.TryGetValue(connection.FromNodeId, out NodeTypeDescriptor? sourceDescriptor) ||
                sourceDescriptor is null ||
                !descriptors.TryGetValue(connection.ToNodeId, out NodeTypeDescriptor? targetDescriptor) ||
                targetDescriptor is null)
            {
                continue;
            }

            GraphPortDescriptor? sourcePort = sourceDescriptor.Ports.FirstOrDefault(port => port.Id == connection.FromPortId && port.Direction == GraphPortDirection.Output);
            GraphPortDescriptor? targetPort = targetDescriptor.Ports.FirstOrDefault(port => port.Id == connection.ToPortId && port.Direction == GraphPortDirection.Input);

            if (sourcePort is null)
            {
                report.Error("MISSING_SOURCE_PORT", $"Output port '{connection.FromPortId}' does not exist.", connection.FromNodeId, connection.FromPortId, key);
                continue;
            }

            if (targetPort is null)
            {
                report.Error("MISSING_TARGET_PORT", $"Input port '{connection.ToPortId}' does not exist.", connection.ToNodeId, connection.ToPortId, key);
                continue;
            }

            if (!AreCompatible(sourcePort.Type, targetPort.Type))
            {
                report.Error("PORT_TYPE", $"Port types {sourcePort.Type} and {targetPort.Type} are incompatible.", connection.ToNodeId, connection.ToPortId, key);
            }
            else if (!IsAudioPort(sourcePort.Type) || !IsAudioPort(targetPort.Type))
            {
                report.Error("PORT_RUNTIME_UNSUPPORTED", "Control, trigger, and event port types are reserved, but the compiled runtime currently executes audio connections only.", connection.ToNodeId, connection.ToPortId, key);
            }

            (string Node, string Port) countKey = (connection.ToNodeId, connection.ToPortId);
            inputCounts.TryGetValue(countKey, out int count);
            count++;
            inputCounts[countKey] = count;
            if (count > 1 && targetPort.Cardinality == GraphPortCardinality.Single)
            {
                report.Error("MULTIPLE_INPUTS", $"Input port '{connection.ToPortId}' accepts only one connection.", connection.ToNodeId, connection.ToPortId);
            }
        }

        foreach ((string nodeId, NodeTypeDescriptor descriptor) in descriptors)
        {
            foreach (GraphPortDescriptor port in descriptor.Ports.Where(port => port.Direction == GraphPortDirection.Input && port.Required))
            {
                if (!inputCounts.ContainsKey((nodeId, port.Id)))
                {
                    report.Error("REQUIRED_INPUT", $"Required input '{port.Id}' is not connected.", nodeId, port.Id);
                }
            }
        }
    }

    private static bool IsAudioPort(GraphPortType type) =>
        type is GraphPortType.AudioMono or GraphPortType.AudioStereo or GraphPortType.AudioAnyChannels;

    private static bool AreCompatible(GraphPortType source, GraphPortType target)
    {
        if (source == target)
        {
            return true;
        }

        if ((source is GraphPortType.AudioMono or GraphPortType.AudioStereo or GraphPortType.AudioAnyChannels) &&
            target == GraphPortType.AudioAnyChannels)
        {
            return true;
        }

        return source == GraphPortType.AudioMono && target == GraphPortType.AudioStereo;
    }

    private static void ValidateOutput(
        SoundGraphDefinition definition,
        IReadOnlyDictionary<string, NodeDefinition> nodes,
        IReadOnlyDictionary<string, NodeTypeDescriptor> descriptors,
        GraphValidationReport report)
    {
        if (string.IsNullOrWhiteSpace(definition.Output.NodeId) || !nodes.ContainsKey(definition.Output.NodeId))
        {
            report.Error("OUTPUT_NODE", "The graph output node is missing.");
            return;
        }

        if (descriptors.TryGetValue(definition.Output.NodeId, out NodeTypeDescriptor? descriptor) &&
            descriptor is not null)
        {
            if (descriptor.Category != GraphNodeCategory.Output)
            {
                report.Error("OUTPUT_TYPE", "The selected graph output must use an output node type.", definition.Output.NodeId);
            }

            bool outputPortExists = descriptor.Ports.Any(port =>
                port.Direction == GraphPortDirection.Output && port.Id == definition.Output.PortId);
            if (!outputPortExists)
            {
                report.Error("OUTPUT_PORT", $"Output port '{definition.Output.PortId}' does not exist.", definition.Output.NodeId, definition.Output.PortId);
            }
        }

        if (!double.IsFinite(definition.Output.MaximumTailSeconds) || definition.Output.MaximumTailSeconds is < 0 or > 300)
        {
            report.Error("TAIL_LIMIT", "Maximum tail duration must be between 0 and 300 seconds.", definition.Output.NodeId);
        }
    }

    private static void ValidateMacros(
        SoundGraphDefinition definition,
        IReadOnlyDictionary<string, NodeDefinition> nodes,
        IReadOnlyDictionary<string, NodeTypeDescriptor> descriptors,
        GraphValidationReport report)
    {
        var macroIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (MacroDefinition macro in definition.Macros)
        {
            if (string.IsNullOrWhiteSpace(macro.Id) || !macroIds.Add(macro.Id))
            {
                report.Error("MACRO_ID", $"Macro ID '{macro.Id}' is empty or duplicated.");
            }

            if (!float.IsFinite(macro.Minimum) || !float.IsFinite(macro.Maximum) || macro.Minimum >= macro.Maximum ||
                macro.DefaultValue < macro.Minimum || macro.DefaultValue > macro.Maximum)
            {
                report.Error("MACRO_RANGE", $"Macro '{macro.Id}' has an invalid range.");
            }

            foreach (MacroBinding binding in macro.Bindings)
            {
                if (!TryParseTargetPath(binding.TargetPath, out string nodeId, out string parameterId) ||
                    !nodes.ContainsKey(nodeId) ||
                    !descriptors.TryGetValue(nodeId, out NodeTypeDescriptor? descriptor) ||
                    descriptor is null)
                {
                    report.Error("MACRO_TARGET", $"Macro '{macro.Id}' has invalid target '{binding.TargetPath}'.");
                    continue;
                }

                GraphParameterDescriptor? parameter = descriptor.Parameters.FirstOrDefault(item => item.Id == parameterId);
                if (parameter is null || !parameter.MacroAddressable || parameter.Type is not (GraphParameterType.Number or GraphParameterType.Integer))
                {
                    report.Error("MACRO_PARAMETER", $"Macro '{macro.Id}' cannot address '{binding.TargetPath}'.", nodeId);
                }

                if (!float.IsFinite(binding.InputMinimum) || !float.IsFinite(binding.InputMaximum) || binding.InputMinimum >= binding.InputMaximum ||
                    !float.IsFinite(binding.OutputMinimum) || !float.IsFinite(binding.OutputMaximum))
                {
                    report.Error("MACRO_BINDING_RANGE", $"Macro '{macro.Id}' has an invalid binding range.", nodeId);
                }

                if (binding.Curve == MacroMappingCurve.Stepped && binding.Steps < 2)
                {
                    report.Error("MACRO_STEPS", $"Macro '{macro.Id}' requires at least two steps.", nodeId);
                }
            }
        }
    }

    public static bool TryParseTargetPath(string path, out string nodeId, out string parameterId)
    {
        nodeId = "";
        parameterId = "";
        const string nodePrefix = "node:";
        const string parameterMarker = "/parameter:";
        if (!path.StartsWith(nodePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        int marker = path.IndexOf(parameterMarker, StringComparison.Ordinal);
        if (marker <= nodePrefix.Length)
        {
            return false;
        }

        nodeId = path[nodePrefix.Length..marker];
        parameterId = path[(marker + parameterMarker.Length)..];
        return nodeId.Length > 0 && parameterId.Length > 0;
    }

    private static void ValidateCycles(SoundGraphDefinition definition, IReadOnlyDictionary<string, NodeDefinition> nodes, GraphValidationReport report)
    {
        var indegree = nodes.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var adjacency = nodes.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (ConnectionDefinition connection in definition.Connections)
        {
            if (!nodes.ContainsKey(connection.FromNodeId) || !nodes.ContainsKey(connection.ToNodeId))
            {
                continue;
            }

            adjacency[connection.FromNodeId].Add(connection.ToNodeId);
            indegree[connection.ToNodeId]++;
        }

        var queue = new Queue<string>(indegree.Where(item => item.Value == 0).Select(item => item.Key));
        int visited = 0;
        while (queue.Count > 0)
        {
            string node = queue.Dequeue();
            visited++;
            foreach (string target in adjacency[node])
            {
                if (--indegree[target] == 0)
                {
                    queue.Enqueue(target);
                }
            }
        }

        if (visited != nodes.Count)
        {
            report.Error("GRAPH_CYCLE", "Audio and control cycles require an explicit stateful feedback boundary; this graph contains an unsupported direct cycle.");
        }
    }

    private static void ValidateReachability(SoundGraphDefinition definition, IReadOnlyDictionary<string, NodeDefinition> nodes, GraphValidationReport report)
    {
        if (!nodes.ContainsKey(definition.Output.NodeId))
        {
            return;
        }

        var reverse = nodes.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (ConnectionDefinition connection in definition.Connections)
        {
            if (nodes.ContainsKey(connection.FromNodeId) && nodes.ContainsKey(connection.ToNodeId))
            {
                reverse[connection.ToNodeId].Add(connection.FromNodeId);
            }
        }

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(definition.Output.NodeId);
        while (stack.Count > 0)
        {
            string current = stack.Pop();
            if (!reachable.Add(current))
            {
                continue;
            }

            foreach (string source in reverse[current])
            {
                stack.Push(source);
            }
        }

        foreach (string nodeId in nodes.Keys)
        {
            if (!reachable.Contains(nodeId))
            {
                report.Warning("ORPHAN_NODE", "The node does not contribute to the selected graph output.", nodeId);
            }
        }
    }
}
