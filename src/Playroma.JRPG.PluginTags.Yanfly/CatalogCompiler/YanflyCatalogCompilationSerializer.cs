using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflyCatalogCompilationSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public string Serialize(YanflyCatalogCompilation compilation, bool indented = true) =>
        JsonSerializer.Serialize(compilation, new JsonSerializerOptions(Options) { WriteIndented = indented });

    public YanflyCatalogCompilation Deserialize(string json) =>
        JsonSerializer.Deserialize<YanflyCatalogCompilation>(json, Options)
        ?? throw new InvalidDataException("Compilation JSON was empty.");

    public void Save(YanflyCatalogCompilation compilation, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, Serialize(compilation));
    }

    public YanflyCatalogCompilation Load(string path) => Deserialize(File.ReadAllText(path));

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
