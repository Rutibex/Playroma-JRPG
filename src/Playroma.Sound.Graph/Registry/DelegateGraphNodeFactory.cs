using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Runtime;

namespace Playroma.Sound.Graph.Registry;

public sealed class DelegateGraphNodeFactory : IGraphNodeFactory
{
    private readonly Func<NodeDefinition, GraphNodeCreationContext, IRuntimeGraphNode> _factory;

    public DelegateGraphNodeFactory(
        NodeTypeDescriptor descriptor,
        Func<NodeDefinition, GraphNodeCreationContext, IRuntimeGraphNode> factory)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public NodeTypeDescriptor Descriptor { get; }

    public IRuntimeGraphNode Create(NodeDefinition definition, GraphNodeCreationContext context) =>
        _factory(definition, context);
}
