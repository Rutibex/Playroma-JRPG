namespace Playroma.Sound.Authoring.Projects;

public enum AuthoringAssetKind
{
    SoundGraph,
    GeneratorRequest,
    StreamingScene,
    Sample,
    ImpulseResponse,
    Other
}

public sealed class AuthoringProjectDefinition
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Id { get; set; } = "untitled.project";
    public string DisplayName { get; set; } = "Untitled Playroma Sound Project";
    public List<ProjectAssetEntry> Assets { get; set; } = [];
    public List<string> SampleDirectories { get; set; } = ["Samples"];
    public List<string> ImpulseResponseDirectories { get; set; } = ["ImpulseResponses"];
    public List<string> ExportProfiles { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ProjectAssetEntry
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public AuthoringAssetKind Kind { get; set; }
    public string RelativePath { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
}

public sealed record AuthoringProjectMetadata(
    string Id,
    string DisplayName,
    string ProjectFilePath,
    string RootDirectory,
    int AssetCount,
    int FormatVersion);
