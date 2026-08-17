using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflySemanticOverlaySerializer
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = CreateOptions();
    public YanflySemanticOverlayDocument Load(string path) => Deserialize(File.ReadAllText(path));
    public YanflySemanticOverlayDocument Deserialize(string json)
    {
        YanflySemanticOverlayDocument document = JsonSerializer.Deserialize<YanflySemanticOverlayDocument>(json, Options) ?? throw new InvalidDataException("Overlay JSON was empty.");
        if (document.SchemaVersion != CurrentSchemaVersion) throw new InvalidDataException($"Unsupported overlay schema {document.SchemaVersion}.");
        return document;
    }
    public string Serialize(YanflySemanticOverlayDocument document, bool indented = true) => JsonSerializer.Serialize(document, new JsonSerializerOptions(Options) { WriteIndented = indented });
    public static YanflySemanticOverlayDocument LoadEmbedded()
    {
        Assembly assembly = typeof(YanflySemanticOverlaySerializer).Assembly;
        string resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith("yanfly-phase2-semantic-overrides.json", StringComparison.OrdinalIgnoreCase));
        using Stream stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("Embedded Phase 2 semantic overlay was not found.");
        using StreamReader reader = new(stream);
        return new YanflySemanticOverlaySerializer().Deserialize(reader.ReadToEnd());
    }

    public static YanflySemanticOverlayDocument Empty(string version = "0.2.0") => new(CurrentSchemaVersion, version, [], []);
    private static JsonSerializerOptions CreateOptions() { JsonSerializerOptions o = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }; o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)); return o; }
}
