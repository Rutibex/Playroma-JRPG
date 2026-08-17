using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Materials.Graph;

public abstract class MaterialNodeDefinitionBase : IMaterialNodeDefinition
{
    public abstract string TypeId { get; }
    public abstract string DisplayName { get; }
    public virtual string Description => string.Empty;
    public virtual int CurrentVersion => 1;
    public abstract MaterialNodeCategory Category { get; }
    public virtual TileabilitySupport Tileability => TileabilitySupport.Preserved;
    public virtual IReadOnlyList<MaterialPortDefinition> Inputs => Array.Empty<MaterialPortDefinition>();
    public virtual IReadOnlyList<MaterialPortDefinition> Outputs => new[] { new MaterialPortDefinition("Value", MaterialDataType.Color) };
    public virtual IReadOnlyDictionary<string, MaterialParameterValue> DefaultParameters { get; } = new Dictionary<string, MaterialParameterValue>();
    public abstract MaterialNodeResult Evaluate(MaterialNodeContext context);
    public virtual void Upgrade(int oldVersion, IDictionary<string, MaterialParameterValue> parameters) { }
}
