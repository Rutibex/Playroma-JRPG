using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Graph.Registry;

public sealed class GraphNodeRegistry
{
    private readonly Dictionary<string, IGraphNodeFactory> _factories = new(StringComparer.Ordinal);

    public IEnumerable<NodeTypeDescriptor> Descriptors => _factories.Values.Select(factory => factory.Descriptor);

    public GraphNodeRegistry Register(IGraphNodeFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        string typeId = factory.Descriptor.TypeId;
        if (string.IsNullOrWhiteSpace(typeId))
        {
            throw new ArgumentException("A graph node factory requires a stable type ID.", nameof(factory));
        }

        if (!_factories.TryAdd(typeId, factory))
        {
            throw new InvalidOperationException($"Graph node type '{typeId}' is already registered.");
        }

        return this;
    }

    public bool TryGetFactory(string typeId, out IGraphNodeFactory factory)
    {
        if (_factories.TryGetValue(typeId, out IGraphNodeFactory? found) && found is not null)
        {
            factory = found;
            return true;
        }

        factory = null!;
        return false;
    }

    public IGraphNodeFactory GetRequiredFactory(string typeId) =>
        TryGetFactory(typeId, out IGraphNodeFactory factory)
            ? factory
            : throw new KeyNotFoundException($"Graph node type '{typeId}' is not registered.");
}
