using Playroma.Sound.Core.Audio;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Registry;
using Playroma.Sound.Graph.Runtime;
using Playroma.Sound.Graph.Utility;
using Playroma.Sound.Graph.Validation;

namespace Playroma.Sound.Graph.Compilation;

public sealed class SoundGraphCompiler
{
    private readonly GraphNodeRegistry _registry;
    private readonly GraphValidator _validator;

    public SoundGraphCompiler(GraphNodeRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _validator = new GraphValidator(registry);
    }

    public GraphValidationReport Validate(SoundGraphDefinition definition) => _validator.Validate(GraphBypassResolver.Resolve(definition, _registry));

    public CompiledSoundGraph Compile(
        SoundGraphDefinition definition,
        AudioFormat format,
        GraphCompileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        options ??= new GraphCompileOptions();
        if (options.EventQueueCapacity is < 2 or > 1_048_576)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Event queue capacity must be between 2 and 1,048,576 entries.");
        }

        definition = GraphBypassResolver.Resolve(definition, _registry);
        GraphValidationReport report = _validator.Validate(definition);
        if (report.HasErrors)
        {
            throw new GraphCompilationException(report);
        }

        List<NodeDefinition> orderedDefinitions = TopologicalSort(definition);
        var executionIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < orderedDefinitions.Count; i++)
        {
            executionIndexById.Add(orderedDefinitions[i].Id, i);
        }

        PendingCommand[] pending = new PendingCommand[orderedDefinitions.Count];
        for (int i = 0; i < orderedDefinitions.Count; i++)
        {
            NodeDefinition nodeDefinition = orderedDefinitions[i];
            NodeTypeDescriptor descriptor = _registry.GetRequiredFactory(nodeDefinition.TypeId).Descriptor;
            Dictionary<string, int> inputPortOrder = descriptor.Ports
                .Where(port => port.Direction == GraphPortDirection.Input)
                .Select((port, index) => (Id: port.Id, Index: index))
                .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);

            int[] inputNodes = definition.Connections
                .Select((connection, index) => (Connection: connection, OriginalIndex: index))
                .Where(item => item.Connection.ToNodeId == nodeDefinition.Id)
                .OrderBy(item => inputPortOrder[item.Connection.ToPortId])
                .ThenBy(item => item.OriginalIndex)
                .Select(item => executionIndexById[item.Connection.FromNodeId])
                .ToArray();
            pending[i] = new PendingCommand(i, inputNodes);
        }

        int outputNodeIndex = executionIndexById[definition.Output.NodeId];
        (int[] bufferSlots, int bufferSlotCount) = GraphBufferPlanner.Plan(pending, outputNodeIndex, options.EnableBufferReuse);

        var runtimeNodes = new IRuntimeGraphNode[orderedDefinitions.Count];
        for (int i = 0; i < orderedDefinitions.Count; i++)
        {
            NodeDefinition node = orderedDefinitions[i];
            IGraphNodeFactory factory = _registry.GetRequiredFactory(node.TypeId);
            ulong derivedSeed = DeterministicSeed.Derive(definition.RootSeed, node.Id, options.VariationIndex);
            runtimeNodes[i] = factory.Create(node, new GraphNodeCreationContext(format, definition.RootSeed, options.VariationIndex, derivedSeed));
            runtimeNodes[i].Prepare(format);
        }

        int[] pathLatencies = new int[runtimeNodes.Length];
        int[] pathTails = new int[runtimeNodes.Length];
        for (int i = 0; i < runtimeNodes.Length; i++)
        {
            int[] inputs = pending[i].InputNodeIndices;
            int longestInputLatency = inputs.Length == 0 ? 0 : inputs.Max(index => pathLatencies[index]);
            if (definition.Output.EnableLatencyCompensation && runtimeNodes[i] is IInputLatencyCompensator compensator && inputs.Length > 0)
            {
                int[] delayFrames = inputs.Select(index => longestInputLatency - pathLatencies[index]).ToArray();
                compensator.ConfigureInputDelays(format, delayFrames);
            }

            pathLatencies[i] = checked(longestInputLatency + runtimeNodes[i].LatencyFrames);
            int longestInputTail = inputs.Length == 0 ? 0 : inputs.Max(index => pathTails[index]);
            pathTails[i] = checked(longestInputTail + runtimeNodes[i].MaximumTailFrames);
        }

        foreach (IRuntimeGraphNode runtimeNode in runtimeNodes)
        {
            runtimeNode.Reset();
        }

        var commands = new CompiledCommand[pending.Length];
        for (int i = 0; i < pending.Length; i++)
        {
            int[] inputSlots = pending[i].InputNodeIndices.Select(index => bufferSlots[index]).ToArray();
            commands[i] = new CompiledCommand(i, inputSlots, bufferSlots[i]);
        }

        var parameterAddresses = new Dictionary<string, CompiledParameterAddress>(StringComparer.Ordinal);
        for (int i = 0; i < runtimeNodes.Length; i++)
        {
            NodeDefinition definitionNode = orderedDefinitions[i];
            foreach (GraphParameterDescriptor parameter in runtimeNodes[i].Descriptor.Parameters)
            {
                if (runtimeNodes[i].TryGetParameterIndex(parameter.Id, out int parameterIndex))
                {
                    parameterAddresses[$"node:{definitionNode.Id}/parameter:{parameter.Id}"] = new CompiledParameterAddress(i, parameterIndex);
                }
            }
        }

        CompiledMacro[] macros = CompileMacros(definition.Macros, parameterAddresses);
        var macroIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < macros.Length; i++)
        {
            macroIndices.Add(macros[i].Id, i);
        }

        bool[] contributing = FindContributingNodes(pending, outputNodeIndex);
        int[] generators = Enumerable.Range(0, runtimeNodes.Length)
            .Where(index => contributing[index] && runtimeNodes[index].IsGenerator)
            .ToArray();
        bool infinite = generators.Any(index => runtimeNodes[index].IsInfinite);

        int totalLatency = pathLatencies[outputNodeIndex];
        int requestedTail = checked(pathTails[outputNodeIndex] + totalLatency);
        int tailLimit = format.FramesFromSeconds(definition.Output.MaximumTailSeconds);
        int maximumTail = Math.Min(requestedTail, tailLimit);

        CompiledGraphNodeInfo[] nodeInfos = Enumerable.Range(0, orderedDefinitions.Count)
            .Select(index => new CompiledGraphNodeInfo(
                orderedDefinitions[index].Id,
                orderedDefinitions[index].TypeId,
                index,
                bufferSlots[index],
                pathLatencies[index],
                pathTails[index]))
            .ToArray();

        return new CompiledSoundGraph(
            definition.Id,
            format,
            runtimeNodes,
            commands,
            bufferSlotCount,
            bufferSlots[outputNodeIndex],
            parameterAddresses,
            macros,
            macroIndices,
            generators,
            infinite,
            maximumTail,
            totalLatency,
            nodeInfos,
            options.EventQueueCapacity,
            options.ApplyMacroDefaults);
    }

    private static List<NodeDefinition> TopologicalSort(SoundGraphDefinition definition)
    {
        var byId = definition.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var originalOrder = definition.Nodes.Select((node, index) => (Id: node.Id, Index: index)).ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);
        var indegree = definition.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        var adjacency = definition.Nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (ConnectionDefinition connection in definition.Connections)
        {
            adjacency[connection.FromNodeId].Add(connection.ToNodeId);
            indegree[connection.ToNodeId]++;
        }

        var ready = new PriorityQueue<string, int>();
        foreach ((string id, int degree) in indegree)
        {
            if (degree == 0)
            {
                ready.Enqueue(id, originalOrder[id]);
            }
        }

        var result = new List<NodeDefinition>(definition.Nodes.Count);
        while (ready.Count > 0)
        {
            string id = ready.Dequeue();
            result.Add(byId[id]);
            foreach (string target in adjacency[id])
            {
                if (--indegree[target] == 0)
                {
                    ready.Enqueue(target, originalOrder[target]);
                }
            }
        }

        if (result.Count != definition.Nodes.Count)
        {
            throw new InvalidOperationException("The graph is cyclic after successful validation.");
        }

        return result;
    }

    private static CompiledMacro[] CompileMacros(
        IReadOnlyList<MacroDefinition> definitions,
        IReadOnlyDictionary<string, CompiledParameterAddress> parameters)
    {
        var result = new CompiledMacro[definitions.Count];
        for (int i = 0; i < definitions.Count; i++)
        {
            MacroDefinition macro = definitions[i];
            CompiledMacroBinding[] bindings = macro.Bindings.Select(binding =>
            {
                CompiledParameterAddress address = parameters[binding.TargetPath];
                return new CompiledMacroBinding(
                    address.NodeIndex,
                    address.ParameterIndex,
                    binding.InputMinimum,
                    binding.InputMaximum,
                    binding.OutputMinimum,
                    binding.OutputMaximum,
                    binding.Curve,
                    binding.Steps);
            }).ToArray();

            result[i] = new CompiledMacro(macro.Id, macro.Minimum, macro.Maximum, macro.DefaultValue, bindings);
        }

        return result;
    }

    private static bool[] FindContributingNodes(IReadOnlyList<PendingCommand> commands, int outputNodeIndex)
    {
        var contributing = new bool[commands.Count];
        var stack = new Stack<int>();
        stack.Push(outputNodeIndex);
        while (stack.Count > 0)
        {
            int current = stack.Pop();
            if (contributing[current])
            {
                continue;
            }

            contributing[current] = true;
            foreach (int input in commands[current].InputNodeIndices)
            {
                stack.Push(input);
            }
        }

        return contributing;
    }
}
