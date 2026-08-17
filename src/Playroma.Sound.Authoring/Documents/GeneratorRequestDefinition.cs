using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Presets;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Authoring.Documents;

public sealed class EditableGeneratorRequest
{
    public int FormatVersion { get; set; } = SoundGeneratorRequestSerializer.CurrentFormatVersion;
    public string Id { get; set; } = "untitled.generator";
    public string DisplayName { get; set; } = "Untitled Generator Request";
    public string GeneratorId { get; set; } = "playroma.ui";
    public ulong RootSeed { get; set; } = 1;
    public ulong FamilySeed { get; set; } = 1;
    public int VariationIndex { get; set; }
    public SoundQualityMode QualityMode { get; set; } = SoundQualityMode.RealtimeHigh;
    public JsonObject Description { get; set; } = new();
}

public sealed class EditableGeneratorRequestSerializer
{
    private readonly JsonSerializerOptions _options;

    public EditableGeneratorRequestSerializer(bool indented = true)
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = indented,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        _options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public string Serialize(EditableGeneratorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.Serialize(request, _options);
    }

    public EditableGeneratorRequest Deserialize(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        EditableGeneratorRequest request = JsonSerializer.Deserialize<EditableGeneratorRequest>(content, _options)
            ?? throw new InvalidDataException("The .playgen document contained no generator request.");
        request.Id ??= "untitled.generator";
        request.DisplayName ??= "Untitled Generator Request";
        request.GeneratorId ??= "";
        request.Description ??= new JsonObject();
        return request;
    }

    public SoundGraphDefinition Generate(EditableGeneratorRequest request, SoundGeneratorCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);
        if (request.FormatVersion != SoundGeneratorRequestSerializer.CurrentFormatVersion)
            throw new NotSupportedException($"Generator request format {request.FormatVersion} is not supported.");
        IUntypedSoundGenerator generator = catalog.GetRequired(request.GeneratorId);
        object description = request.Description.Deserialize(generator.DescriptionType, _options)
            ?? throw new InvalidDataException("The generator description could not be deserialized.");
        return generator.Generate(description, new SoundGenerationContext
        {
            GraphId = request.Id,
            DisplayName = request.DisplayName,
            RootSeed = request.RootSeed,
            FamilySeed = request.FamilySeed,
            VariationIndex = request.VariationIndex,
            QualityMode = request.QualityMode
        });
    }
}
