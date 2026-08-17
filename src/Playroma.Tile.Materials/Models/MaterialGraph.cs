namespace Playroma.Tile.Materials.Models;

public sealed class MaterialGraph
{
    public int FormatVersion { get; set; } = 1;
    public List<MaterialNodeInstance> Nodes { get; set; } = new();
    public Guid OutputNodeId { get; set; }

    public MaterialNodeInstance? Find(Guid id) => Nodes.FirstOrDefault(x => x.Id == id);
}

public sealed class MaterialNodeInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TypeId { get; set; } = string.Empty;
    public int NodeVersion { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public float EditorX { get; set; }
    public float EditorY { get; set; }
    public bool IsCollapsed { get; set; }
    public Dictionary<string, MaterialParameterValue> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, MaterialConnection> Inputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public MaterialNodeInstance DeepClone()
    {
        return new MaterialNodeInstance
        {
            Id = Id,
            TypeId = TypeId,
            NodeVersion = NodeVersion,
            Name = Name,
            EditorX = EditorX,
            EditorY = EditorY,
            IsCollapsed = IsCollapsed,
            Parameters = Parameters.ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.OrdinalIgnoreCase),
            Inputs = Inputs.ToDictionary(x => x.Key, x => x.Value with { }, StringComparer.OrdinalIgnoreCase)
        };
    }
}

public sealed record MaterialConnection(Guid SourceNodeId, string SourcePort = "Value");

public sealed record MaterialPortDefinition(string Name, MaterialDataType Type, bool Required = false, string Description = "");
