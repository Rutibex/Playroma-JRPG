using System.Text.Json;

namespace Playroma.Sprite.Automation;

public sealed class SpriteScriptParameter
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Default { get; set; }
    public bool Required { get; set; }
}

public sealed class PlayromaSpriteScript
{
    public string Schema { get; set; } = "playroma-sprite-script/1";
    public string Name { get; set; } = "Sprite Script";
    public string? Description { get; set; }
    public SpriteAutomationCapability RequiredCapabilities { get; set; } = SpriteAutomationCapability.ReadDocument;
    public List<SpriteScriptParameter> Parameters { get; set; } = [];
    public List<SpriteAutomationStep> Steps { get; set; } = [];

    public static PlayromaSpriteScript Parse(string json)
    {
        PlayromaSpriteScript script = JsonSerializer.Deserialize<PlayromaSpriteScript>(json, AutomationJson.Options)
            ?? throw new InvalidDataException("The script is empty.");
        Validate(script); return script;
    }

    public static PlayromaSpriteScript Load(string path) => Parse(File.ReadAllText(path));

    public static void Validate(PlayromaSpriteScript script)
    {
        if (script.Schema != "playroma-sprite-script/1") throw new InvalidDataException("Unsupported Playroma Sprite script schema.");
        if (string.IsNullOrWhiteSpace(script.Name)) throw new InvalidDataException("A script name is required.");
        HashSet<string> parameters = new(StringComparer.OrdinalIgnoreCase);
        foreach (SpriteScriptParameter parameter in script.Parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name)) throw new InvalidDataException("Script parameter names cannot be empty.");
            if (!parameters.Add(parameter.Name)) throw new InvalidDataException($"Duplicate script parameter '{parameter.Name}'.");
        }
        HashSet<string> steps = new(StringComparer.OrdinalIgnoreCase);
        foreach (SpriteAutomationStep step in script.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Command)) throw new InvalidDataException("Every script step requires a command.");
            if (!steps.Add(step.Id)) throw new InvalidDataException($"Duplicate script step ID '{step.Id}'.");
        }
    }

    public SpriteAutomationPipeline CreatePipeline(IReadOnlyDictionary<string, string>? suppliedParameters = null)
    {
        suppliedParameters ??= new Dictionary<string, string>();
        Dictionary<string, string> resolved = new(StringComparer.OrdinalIgnoreCase);
        foreach (SpriteScriptParameter parameter in Parameters)
        {
            string? value = suppliedParameters.TryGetValue(parameter.Name, out string? supplied) ? supplied : parameter.Default;
            if (parameter.Required && string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"Script parameter '{parameter.Name}' is required.");
            if (value is not null) resolved[parameter.Name] = value;
        }
        string json = JsonSerializer.Serialize(Steps, AutomationJson.Options);
        foreach ((string key, string value) in resolved) json = json.Replace("${" + key + "}", JsonEncodedText.Encode(value).ToString(), StringComparison.OrdinalIgnoreCase);
        List<SpriteAutomationStep> steps = JsonSerializer.Deserialize<List<SpriteAutomationStep>>(json, AutomationJson.Options) ?? [];
        return new() { Name = Name, Description = Description, RequiredCapabilities = RequiredCapabilities, Steps = steps };
    }
}

public static class SpriteScriptTemplates
{
    public static string Starter => """
{
  "schema": "playroma-sprite-script/1",
  "name": "Export Sprite Atlas",
  "description": "Validate the document and export a power-of-two atlas.",
  "requiredCapabilities": "readDocument, writeFiles",
  "parameters": [
    { "name": "output", "default": "exports/${document}-atlas.png" }
  ],
  "steps": [
    { "id": "validate", "command": "validate-document" },
    { "id": "atlas", "command": "export-atlas", "arguments": {
      "path": "${output}", "padding": 1, "extrude": true, "powerOfTwo": true
    }}
  ]
}
""";
}
