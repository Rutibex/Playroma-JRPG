using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playroma.JRPG.PluginTags.Yanfly;

public static class YanflyInventorySerializer
{
    public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();

    public static YanflyBundleInventory Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = File.OpenRead(path);
        return Load(stream, path);
    }

    public static YanflyBundleInventory Load(Stream stream, string sourceName = "stream")
    {
        ArgumentNullException.ThrowIfNull(stream);
        return JsonSerializer.Deserialize<YanflyBundleInventory>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Yanfly inventory deserialized to null: {sourceName}");
    }

    public static void Save(YanflyBundleInventory inventory, string path)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, inventory, JsonOptions);
    }

    public static YanflyBundleInventory LoadEmbeddedPhase0Inventory()
    {
        Assembly assembly = typeof(YanflyInventorySerializer).Assembly;
        string resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(".yanfly-phase0-inventory.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Embedded Phase 0 Yanfly inventory was not found.");
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Embedded Phase 0 Yanfly inventory could not be opened: {resourceName}");
        return Load(stream, resourceName);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
