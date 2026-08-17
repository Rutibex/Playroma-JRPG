using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Graph.Utility;

public sealed class GraphParameterReader
{
    private readonly NodeDefinition _definition;
    private readonly NodeTypeDescriptor _descriptor;

    public GraphParameterReader(NodeDefinition definition, NodeTypeDescriptor descriptor)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public GraphValue GetValue(string id)
    {
        if (_definition.Parameters.TryGetValue(id, out GraphValue value))
        {
            return value;
        }

        GraphParameterDescriptor? parameter = _descriptor.Parameters.FirstOrDefault(item => item.Id == id);
        return parameter?.DefaultValue ?? GraphValue.Null;
    }

    public float GetFloat(string id) => GetValue(id).AsFloat();
    public double GetDouble(string id) => GetValue(id).AsDouble();
    public int GetInt32(string id) => GetValue(id).AsInt32();
    public bool GetBoolean(string id) => GetValue(id).AsBoolean();
    public string GetString(string id) => GetValue(id).AsString();
    public ulong GetSeed(string id, ulong fallback) => GetValue(id).AsUInt64(fallback);
}
