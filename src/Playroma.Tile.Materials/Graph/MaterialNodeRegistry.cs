using Playroma.Tile.Materials.Models;
using Playroma.Tile.Materials.Nodes;

namespace Playroma.Tile.Materials.Graph;

public sealed class MaterialNodeRegistry
{
    private readonly Dictionary<string, IMaterialNodeDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    public IEnumerable<IMaterialNodeDefinition> Definitions => _definitions.Values.OrderBy(x => x.Category).ThenBy(x => x.DisplayName);

    public MaterialNodeRegistry(bool includeBuiltIns = true)
    {
        if (includeBuiltIns) RegisterBuiltIns();
    }

    public void Register(IMaterialNodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_definitions.TryAdd(definition.TypeId, definition))
            throw new InvalidOperationException($"A material node named '{definition.TypeId}' is already registered.");
    }

    public bool TryGet(string typeId, out IMaterialNodeDefinition definition) => _definitions.TryGetValue(typeId, out definition!);
    public IMaterialNodeDefinition Get(string typeId) => TryGet(typeId, out var result) ? result : new MissingNodeDefinition(typeId);

    public MaterialNodeInstance Create(string typeId, float x = 0, float y = 0)
    {
        var definition = Get(typeId);
        return new MaterialNodeInstance
        {
            TypeId = definition.TypeId,
            NodeVersion = definition.CurrentVersion,
            Name = definition.DisplayName,
            EditorX = x,
            EditorY = y,
            Parameters = definition.DefaultParameters.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase)
        };
    }

    private void RegisterBuiltIns()
    {
        Register(new ConstantColorNode());
        Register(new ConstantScalarNode());
        Register(new CoordinateNode());
        Register(new WhiteNoiseNode());
        Register(new PoissonPointsNode());
        Register(new NoiseNode());
        Register(new PatternNode());
        Register(new GradientMapNode());
        Register(new BlendNode());
        Register(new TransformNode());
        Register(new FilterNode());
        Register(new MorphologyNode());
        Register(new ThresholdNode());
        Register(new ColorAdjustNode());
        Register(new HeightShadeNode());
        Register(new ReactionDiffusionNode());
        Register(new ErosionNode());
        Register(new GrowthNode());
        Register(new FlowFieldNode());
        Register(new SeamRepairNode());
        Register(new ImageInputNode());
        Register(new ImageQuiltNode());
        Register(new PatchMatchNode());
        Register(new PixelArtNode());
        Register(new MaterialOutputNode());
    }
}

internal sealed class MissingNodeDefinition : MaterialNodeDefinitionBase
{
    private readonly string _typeId;
    public MissingNodeDefinition(string typeId) => _typeId = typeId;
    public override string TypeId => _typeId;
    public override string DisplayName => $"Missing: {_typeId}";
    public override string Description => "This node type is not installed. Its parameters and connections are preserved.";
    public override MaterialNodeCategory Category => MaterialNodeCategory.Output;
    public override IReadOnlyList<MaterialPortDefinition> Inputs => Array.Empty<MaterialPortDefinition>();
    public override IReadOnlyList<MaterialPortDefinition> Outputs => Array.Empty<MaterialPortDefinition>();
    public override MaterialNodeResult Evaluate(MaterialNodeContext context) => throw new InvalidOperationException($"Material node '{_typeId}' is unavailable.");
}
