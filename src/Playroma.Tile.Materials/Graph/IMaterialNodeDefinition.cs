using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Materials.Graph;

public interface IMaterialNodeDefinition
{
    string TypeId { get; }
    string DisplayName { get; }
    string Description { get; }
    int CurrentVersion { get; }
    MaterialNodeCategory Category { get; }
    TileabilitySupport Tileability { get; }
    IReadOnlyList<MaterialPortDefinition> Inputs { get; }
    IReadOnlyList<MaterialPortDefinition> Outputs { get; }
    IReadOnlyDictionary<string, MaterialParameterValue> DefaultParameters { get; }
    MaterialNodeResult Evaluate(MaterialNodeContext context);
    void Upgrade(int oldVersion, IDictionary<string, MaterialParameterValue> parameters);
}

public sealed class MaterialNodeResult
{
    private readonly Dictionary<string, MaterialValue> _outputs = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, MaterialValue> Outputs => _outputs;
    public MaterialNodeResult Add(string name, MaterialValue value) { _outputs[name] = value; return this; }
    public MaterialValue Get(string name = "Value") => _outputs.TryGetValue(name, out var value) ? value : throw new KeyNotFoundException($"Node output '{name}' was not produced.");
}
