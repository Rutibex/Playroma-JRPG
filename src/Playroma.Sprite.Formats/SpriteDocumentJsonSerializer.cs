using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.Sprite.Core;

namespace Playroma.Sprite.Formats;

public static class SpriteDocumentJsonSerializer
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(SpriteDocument document)
    {
        SpriteDocumentValidator.ThrowIfInvalid(document);
        return JsonSerializer.Serialize(document, Options);
    }

    public static SpriteDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        SpriteDocument document = JsonSerializer.Deserialize<SpriteDocument>(json, Options) ?? throw new InvalidDataException("Sprite document is empty.");
        SpriteDocumentValidator.ThrowIfInvalid(document);
        return document;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { WriteIndented = true, PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
