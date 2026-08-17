using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Generators.Presets;

public sealed record SoundGeneratorRequestHeader(
    int FormatVersion,
    string Id,
    string DisplayName,
    string GeneratorId,
    ulong RootSeed,
    ulong FamilySeed,
    int VariationIndex,
    SoundQualityMode QualityMode);

public sealed class SoundGeneratorRequestSerializer
{
    public const int CurrentFormatVersion = 1;
    private readonly JsonSerializerOptions _options;

    public SoundGeneratorRequestSerializer()
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        _options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public string Serialize<TDescription>(
        string id,
        string displayName,
        ISoundGenerator<TDescription> generator,
        TDescription description,
        SoundGenerationContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(context);

        var document = new
        {
            formatVersion = CurrentFormatVersion,
            id,
            displayName,
            generatorId = generator.GeneratorId,
            rootSeed = context.RootSeed,
            familySeed = context.FamilySeed,
            variationIndex = context.VariationIndex,
            qualityMode = context.QualityMode,
            description
        };
        return JsonSerializer.Serialize(document, _options);
    }

    public SoundGraphDefinition Generate(string json, SoundGeneratorCatalog catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(catalog);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        int version = root.GetProperty("formatVersion").GetInt32();
        if (version != CurrentFormatVersion)
        {
            throw new JsonException($"Generator request format {version} is not supported.");
        }

        string generatorId = root.GetProperty("generatorId").GetString() ?? throw new JsonException("generatorId is required.");
        IUntypedSoundGenerator generator = catalog.GetRequired(generatorId);
        object description = root.GetProperty("description").Deserialize(generator.DescriptionType, _options)
            ?? throw new JsonException("Generator description could not be deserialized.");
        var context = new SoundGenerationContext
        {
            GraphId = root.GetProperty("id").GetString(),
            DisplayName = root.GetProperty("displayName").GetString(),
            RootSeed = root.GetProperty("rootSeed").GetUInt64(),
            FamilySeed = root.GetProperty("familySeed").GetUInt64(),
            VariationIndex = root.GetProperty("variationIndex").GetInt32(),
            QualityMode = root.GetProperty("qualityMode").Deserialize<SoundQualityMode>(_options)
        };
        return generator.Generate(description, context);
    }
}
