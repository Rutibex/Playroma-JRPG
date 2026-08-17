using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.RpgMakerMV.Documents;

namespace Playroma.JRPG.Editor.WinForms.Plugins;

public sealed record PluginParameterMetadata(
    string Name,
    string DisplayName,
    string Description,
    string Type,
    string DefaultValue,
    string? Minimum = null,
    string? Maximum = null,
    int? Decimals = null,
    IReadOnlyList<(string Text, string Value)>? Options = null,
    string? Parent = null,
    string? Directory = null,
    bool Required = false)
{
    public IReadOnlyList<(string Text, string Value)> EffectiveOptions => Options ?? [];
}

public sealed record PluginMetadata(
    string Name,
    string Description,
    string Author,
    string Help,
    IReadOnlyDictionary<string, PluginParameterMetadata> Parameters)
{
    public static PluginMetadata Empty(string name) => new(
        name,
        string.Empty,
        string.Empty,
        string.Empty,
        new Dictionary<string, PluginParameterMetadata>(StringComparer.Ordinal));
}

/// <summary>
/// Reads RPG Maker MV plugin header annotations without evaluating JavaScript.
/// Unknown tags are ignored and unknown parameter types remain editable as strings.
/// </summary>
public static class MvPluginMetadataParser
{
    public static async ValueTask<PluginMetadata> ParseFileAsync(
        string scriptPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        string name = Path.GetFileNameWithoutExtension(scriptPath);
        if (!File.Exists(scriptPath)) return PluginMetadata.Empty(name);
        string source = await File.ReadAllTextAsync(scriptPath, cancellationToken).ConfigureAwait(false);
        return Parse(name, source);
    }

    public static PluginMetadata Parse(string name, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        source ??= string.Empty;
        string description = string.Empty;
        string author = string.Empty;
        StringBuilder help = new();
        Dictionary<string, ParameterBuilder> parameters = new(StringComparer.Ordinal);
        ParameterBuilder? current = null;
        bool inHelp = false;

        foreach (string rawLine in source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = rawLine.Trim();
            line = line.TrimStart('/', '*').TrimEnd('*', '/').Trim();
            if (!line.StartsWith('@'))
            {
                if (inHelp && line.Length > 0) help.AppendLine(line);
                continue;
            }

            int separator = line.IndexOfAny([' ', '\t']);
            string tag = separator < 0 ? line[1..] : line[1..separator];
            string value = separator < 0 ? string.Empty : line[(separator + 1)..].Trim();
            switch (tag.ToLowerInvariant())
            {
                case "plugindesc":
                    description = value;
                    inHelp = false;
                    break;
                case "author":
                    author = value;
                    inHelp = false;
                    break;
                case "help":
                    inHelp = true;
                    if (value.Length > 0) help.AppendLine(value);
                    break;
                case "param":
                    inHelp = false;
                    if (value.Length == 0) break;
                    current = new ParameterBuilder(value);
                    parameters[value] = current;
                    break;
                case "text":
                    if (current is not null) current.DisplayName = value;
                    break;
                case "desc":
                    if (current is not null)
                    {
                        current.Description = string.IsNullOrWhiteSpace(current.Description)
                            ? value
                            : current.Description + Environment.NewLine + value;
                    }
                    break;
                case "type":
                    if (current is not null) current.Type = value;
                    break;
                case "default":
                    if (current is not null) current.DefaultValue = value;
                    break;
                case "min":
                    if (current is not null) current.Minimum = value;
                    break;
                case "max":
                    if (current is not null) current.Maximum = value;
                    break;
                case "decimals":
                    if (current is not null && int.TryParse(value, out int decimals)) current.Decimals = decimals;
                    break;
                case "option":
                    if (current is not null) current.PendingOptionText = value;
                    break;
                case "value":
                    if (current is not null)
                    {
                        current.Options.Add((current.PendingOptionText ?? value, value));
                        current.PendingOptionText = null;
                    }
                    break;
                case "parent":
                    if (current is not null) current.Parent = value;
                    break;
                case "dir":
                    if (current is not null) current.Directory = value;
                    break;
                case "require":
                    if (current is not null) current.Required = !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
                    break;
                case "on":
                    if (current is not null) current.Options.Add((value.Length == 0 ? "On" : value, "true"));
                    break;
                case "off":
                    if (current is not null) current.Options.Add((value.Length == 0 ? "Off" : value, "false"));
                    break;
            }
        }

        return new PluginMetadata(
            name,
            description,
            author,
            help.ToString().Trim(),
            parameters.ToDictionary(pair => pair.Key, pair => pair.Value.Build(), StringComparer.Ordinal));
    }

    private sealed class ParameterBuilder
    {
        public ParameterBuilder(string name)
        {
            Name = name;
            DisplayName = name;
        }

        public string Name { get; }
        public string DisplayName { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = "string";
        public string DefaultValue { get; set; } = string.Empty;
        public string? Minimum { get; set; }
        public string? Maximum { get; set; }
        public int? Decimals { get; set; }
        public List<(string Text, string Value)> Options { get; } = [];
        public string? PendingOptionText { get; set; }
        public string? Parent { get; set; }
        public string? Directory { get; set; }
        public bool Required { get; set; }

        public PluginParameterMetadata Build() => new(
            Name,
            string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName,
            Description,
            string.IsNullOrWhiteSpace(Type) ? "string" : Type,
            DefaultValue,
            Minimum,
            Maximum,
            Decimals,
            Options.ToArray(),
            Parent,
            Directory,
            Required);
    }
}

public sealed class PluginEditEntry
{
    public PluginEditEntry(JsonObject source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        LocalId = Guid.NewGuid();
    }

    public Guid LocalId { get; }
    public JsonObject Source { get; }
    public string Name
    {
        get => ReadString(Source["name"]);
        set => Source["name"] = value ?? string.Empty;
    }
    public string Description
    {
        get => ReadString(Source["description"]);
        set => Source["description"] = value ?? string.Empty;
    }
    public bool Enabled
    {
        get => Source["status"] is JsonValue value && value.TryGetValue(out bool enabled) && enabled;
        set => Source["status"] = value;
    }
    public JsonObject Parameters
    {
        get
        {
            if (Source["parameters"] is JsonObject parameters) return parameters;
            parameters = new JsonObject();
            Source["parameters"] = parameters;
            return parameters;
        }
    }

    public string GetParameter(string name) => ReadString(Parameters[name]);
    public void SetParameter(string name, string value) => Parameters[name] = value ?? string.Empty;
    public void RemoveParameter(string name) => Parameters.Remove(name);

    public PluginEditEntry Clone() => new((JsonObject)Source.DeepClone());

    private static string ReadString(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue(out string? text)) return text ?? string.Empty;
        return node?.ToJsonString() ?? string.Empty;
    }
}

/// <summary>Private, undoable working copy used by PluginManagerForm.</summary>
public sealed class PluginEditSession
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly Stack<byte[]> _undo = new();
    private readonly Stack<byte[]> _redo = new();
    private byte[] _baseline = "[]"u8.ToArray();

    public List<PluginEditEntry> Entries { get; } = [];
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsDirty => !Capture().AsSpan().SequenceEqual(_baseline);

    public void Load(MvPluginManifestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Entries.Clear();
        for (int index = 0; index < document.Count; index++)
        {
            JsonObject entry = JsonNode.Parse(document.GetRawEntry(index)) as JsonObject
                ?? throw new InvalidDataException($"Plugin entry {index + 1} is not a JSON object.");
            Entries.Add(new PluginEditEntry(entry));
        }
        _baseline = Capture();
        _undo.Clear();
        _redo.Clear();
    }

    public void RestoreSnapshot(ReadOnlySpan<byte> bytes, bool clearHistory = false)
    {
        Restore(bytes);
        if (clearHistory)
        {
            _undo.Clear();
            _redo.Clear();
        }
    }

    public void RestoreBaseline()
    {
        Restore(_baseline);
        _undo.Clear();
        _redo.Clear();
    }

    public void MarkApplied()
    {
        _baseline = Capture();
        _undo.Clear();
        _redo.Clear();
    }

    public void Mutate(Action mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        byte[] before = Capture();
        mutation();
        byte[] after = Capture();
        if (before.AsSpan().SequenceEqual(after)) return;
        _undo.Push(before);
        _redo.Clear();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(Capture());
        Restore(_undo.Pop());
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(Capture());
        Restore(_redo.Pop());
    }

    public byte[] Capture()
    {
        JsonArray array = new();
        foreach (PluginEditEntry entry in Entries)
        {
            array.Add(entry.Source.DeepClone());
        }
        return Encoding.UTF8.GetBytes(array.ToJsonString(SerializerOptions));
    }

    public PluginEditEntry Add(string name, bool enabled, PluginMetadata? metadata = null)
    {
        JsonObject parameters = new();
        if (metadata is not null)
        {
            foreach (PluginParameterMetadata parameter in metadata.Parameters.Values)
                parameters[parameter.Name] = parameter.DefaultValue;
        }
        PluginEditEntry entry = new(new JsonObject
        {
            ["name"] = name,
            ["status"] = enabled,
            ["description"] = metadata?.Description ?? string.Empty,
            ["parameters"] = parameters
        });
        Mutate(() => Entries.Add(entry));
        return entry;
    }

    private void Restore(ReadOnlySpan<byte> bytes)
    {
        JsonArray array = JsonNode.Parse(bytes) as JsonArray
            ?? throw new InvalidDataException("The plugin edit session must contain a JSON array.");
        Entries.Clear();
        foreach (JsonNode? node in array)
        {
            if (node is JsonObject entry) Entries.Add(new PluginEditEntry((JsonObject)entry.DeepClone()));
        }
    }
}
