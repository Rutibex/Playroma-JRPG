using System.Text.Json;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed record YanflyProjectPluginScanResult(
    string ProjectRoot,
    string ManifestPath,
    string PluginsDirectory,
    InstalledPluginAnalysis Analysis);

public sealed class YanflyProjectPluginIntelligenceScanner
{
    private readonly YanflyInstalledSourceInspector sourceInspector = new();
    private readonly YanflyInstalledPluginIntelligence intelligence;

    public YanflyProjectPluginIntelligenceScanner(YanflyInstalledPluginIntelligence? intelligence = null)
    {
        this.intelligence = intelligence ?? new YanflyInstalledPluginIntelligence();
    }

    public async ValueTask<YanflyProjectPluginScanResult> ScanAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        string root = Path.GetFullPath(projectRoot);
        string manifest = FindManifest(root) ?? throw new FileNotFoundException(
            "Could not find js/plugins.js or www/js/plugins.js under the project root.", root);
        string pluginsDirectory = Path.Combine(Path.GetDirectoryName(manifest)!, "plugins");
        string source = await File.ReadAllTextAsync(manifest, cancellationToken).ConfigureAwait(false);
        if (!TryExtractPluginArray(source, out string arrayText, out string? error))
            throw new InvalidDataException(error ?? "The $plugins array could not be extracted.");

        List<InstalledPluginDescriptor> descriptors = [];
        using JsonDocument document = JsonDocument.Parse(arrayText);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The extracted $plugins value is not a JSON array.");
        int order = 0;
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            order++;
            if (element.ValueKind != JsonValueKind.Object) continue;
            string name = ReadString(element, "name") ?? string.Empty;
            bool enabled = ReadBoolean(element, "status");
            Dictionary<string, string> parameters = ReadParameters(element);
            string fileName = name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ? name : name + ".js";
            string scriptPath = Path.Combine(pluginsDirectory, fileName);
            bool exists = File.Exists(scriptPath);
            YanflyInstalledSourceInspection? inspection = exists
                ? await sourceInspector.InspectFileAsync(name, scriptPath, cancellationToken).ConfigureAwait(false)
                : null;
            descriptors.Add(new InstalledPluginDescriptor(order, name, enabled, scriptPath, exists,
                inspection?.Version, inspection?.SourceFingerprint, inspection?.ImportedAliases, parameters));
        }

        string[] scripts = Directory.Exists(pluginsDirectory)
            ? Directory.EnumerateFiles(pluginsDirectory, "*.js", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToArray()
            : [];
        return new(root, manifest, pluginsDirectory, intelligence.Analyze(descriptors, scripts));
    }

    private static string? FindManifest(string root)
    {
        string direct = Path.Combine(root, "js", "plugins.js");
        if (File.Exists(direct)) return direct;
        string www = Path.Combine(root, "www", "js", "plugins.js");
        return File.Exists(www) ? www : null;
    }

    private static Dictionary<string, string> ReadParameters(JsonElement element)
    {
        Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty("parameters", out JsonElement value) || value.ValueKind != JsonValueKind.Object) return parameters;
        foreach (JsonProperty property in value.EnumerateObject())
            parameters[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.GetRawText();
        return parameters;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();

    internal static bool TryExtractPluginArray(string source, out string arrayText, out string? error)
    {
        arrayText = string.Empty;
        error = null;
        int assignment = source.IndexOf("$plugins", StringComparison.Ordinal);
        if (assignment < 0) { error = "The manifest does not contain $plugins."; return false; }
        int equals = source.IndexOf('=', assignment);
        int start = equals < 0 ? -1 : source.IndexOf('[', equals);
        if (start < 0) { error = "The $plugins assignment does not contain an array."; return false; }

        int depth = 0;
        bool inString = false;
        bool escaped = false;
        bool lineComment = false;
        bool blockComment = false;
        char quote = '\0';
        for (int index = start; index < source.Length; index++)
        {
            char current = source[index];
            char next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (lineComment) { if (current is '\r' or '\n') lineComment = false; continue; }
            if (blockComment) { if (current == '*' && next == '/') { blockComment = false; index++; } continue; }
            if (inString)
            {
                if (escaped) { escaped = false; continue; }
                if (current == '\\') { escaped = true; continue; }
                if (current == quote) inString = false;
                continue;
            }
            if (current == '/' && next == '/') { lineComment = true; index++; continue; }
            if (current == '/' && next == '*') { blockComment = true; index++; continue; }
            if (current is '"' or '\'') { inString = true; quote = current; continue; }
            if (current == '[') depth++;
            else if (current == ']' && --depth == 0)
            {
                arrayText = source[start..(index + 1)];
                return true;
            }
        }
        error = "The $plugins array does not have a matching closing bracket.";
        return false;
    }
}
