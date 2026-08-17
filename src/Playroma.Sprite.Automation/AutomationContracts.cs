using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.Sprite.Formats;

namespace Playroma.Sprite.Automation;

[Flags]
public enum SpriteAutomationCapability
{
    None = 0,
    ReadDocument = 1 << 0,
    ModifyDocument = 1 << 1,
    ReadFiles = 1 << 2,
    WriteFiles = 1 << 3,
    InstallRuntimeAsset = 1 << 4,
    LoadExtensions = 1 << 5
}

public enum AutomationDiagnosticSeverity { Information, Warning, Error }

public sealed record AutomationDiagnostic(
    AutomationDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? StepId = null,
    string? Path = null);

public sealed record AutomationOperation(
    string Kind,
    string Description,
    string? SourcePath = null,
    string? DestinationPath = null,
    bool Performed = true);

public sealed class AutomationRunResult
{
    public string Name { get; init; } = "Automation";
    public bool DryRun { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset FinishedAt { get; set; }
    public List<AutomationDiagnostic> Diagnostics { get; } = [];
    public List<AutomationOperation> Operations { get; } = [];
    public Dictionary<string, string> Outputs { get; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonIgnore] public bool Success => Diagnostics.All(item => item.Severity != AutomationDiagnosticSeverity.Error);
}

public sealed class SpriteAutomationStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Command { get; set; } = string.Empty;
    public string? Name { get; set; }
    public bool Enabled { get; set; } = true;
    public bool ContinueOnError { get; set; }
    public Dictionary<string, JsonElement> Arguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SpriteAutomationPipeline
{
    public string Schema { get; set; } = "playroma-sprite-pipeline/1";
    public string Name { get; set; } = "Sprite Pipeline";
    public string? Description { get; set; }
    public SpriteAutomationCapability RequiredCapabilities { get; set; } = SpriteAutomationCapability.ReadDocument;
    public bool ContinueOnError { get; set; }
    public List<SpriteAutomationStep> Steps { get; set; } = [];
}

public enum ExportFrameMode { Current, All, Tag, Selected }
public enum AtlasOrdering { Frame, Name, AreaDescending }

public sealed class SpriteExportPreset
{
    public string Schema { get; set; } = "playroma-sprite-export-preset/1";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "PNG Export";
    public string Format { get; set; } = "png";
    public string OutputPath { get; set; } = "exports/${document}.png";
    public ExportFrameMode FrameMode { get; set; } = ExportFrameMode.Current;
    public string? Tag { get; set; }
    public int Scale { get; set; } = 1;
    public bool TrimTransparent { get; set; }
    public int Padding { get; set; }
    public bool Extrude { get; set; }
    public bool PowerOfTwo { get; set; }
    public int MaximumWidth { get; set; } = 4096;
    public int MaximumHeight { get; set; } = 4096;
    public AtlasOrdering Ordering { get; set; } = AtlasOrdering.Frame;
    public string MetadataFormat { get; set; } = "playroma-json";
    public Dictionary<string, string> UserData { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SpriteAutomationContext
{
    public required SpritePackageData Package { get; set; }
    public string? PackagePath { get; set; }
    public required string WorkspaceRoot { get; init; }
    public string OutputRoot { get; init; } = "exports";
    public bool DryRun { get; init; }
    public SpriteAutomationCapability GrantedCapabilities { get; init; } = SpriteAutomationCapability.ReadDocument;
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public CancellationToken CancellationToken { get; init; }
    public Action<string>? Log { get; init; }
}

public static class AutomationJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
