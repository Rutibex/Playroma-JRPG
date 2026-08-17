using Playroma.Sound.Core.Audio;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Runtime;

namespace Playroma.Sound.Graph.Registry;

public interface IGraphNodeFactory
{
    NodeTypeDescriptor Descriptor { get; }
    IRuntimeGraphNode Create(NodeDefinition definition, GraphNodeCreationContext context);
}

public sealed record GraphNodeCreationContext(
    AudioFormat Format,
    ulong RootSeed,
    int VariationIndex,
    ulong DerivedSeed);
