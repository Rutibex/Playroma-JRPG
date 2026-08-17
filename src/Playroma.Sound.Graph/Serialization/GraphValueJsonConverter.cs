using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Graph.Serialization;

public sealed class GraphValueJsonConverter : JsonConverter<GraphValue>
{
    public override GraphValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.Null => GraphValue.Null,
        JsonTokenType.Number => GraphValue.FromNumber(reader.GetDouble()),
        JsonTokenType.True => GraphValue.FromBoolean(true),
        JsonTokenType.False => GraphValue.FromBoolean(false),
        JsonTokenType.String => GraphValue.FromString(reader.GetString() ?? ""),
        _ => throw new JsonException($"Unsupported graph value token {reader.TokenType}.")
    };

    public override void Write(Utf8JsonWriter writer, GraphValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case GraphValueKind.Null:
                writer.WriteNullValue();
                break;
            case GraphValueKind.Number:
                writer.WriteNumberValue(value.Number);
                break;
            case GraphValueKind.Boolean:
                writer.WriteBooleanValue(value.Boolean);
                break;
            case GraphValueKind.String:
                writer.WriteStringValue(value.Text);
                break;
            default:
                throw new JsonException($"Unsupported graph value kind {value.Kind}.");
        }
    }
}
