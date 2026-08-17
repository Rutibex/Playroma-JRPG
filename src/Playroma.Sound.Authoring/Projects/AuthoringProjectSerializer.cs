using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Playroma.Sound.Authoring.Diagnostics;

namespace Playroma.Sound.Authoring.Projects;

public sealed class AuthoringProjectSerializer
{
    private readonly JsonSerializerOptions _options;
    private readonly SortedDictionary<int, IAuthoringProjectMigration> _migrations = [];

    public AuthoringProjectSerializer(bool indented = true, IEnumerable<IAuthoringProjectMigration>? migrations = null)
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        _options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        if (migrations is not null)
        {
            foreach (IAuthoringProjectMigration migration in migrations) RegisterMigration(migration);
        }
    }

    public void RegisterMigration(IAuthoringProjectMigration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        if (migration.ToVersion != migration.FromVersion + 1) throw new ArgumentException("Project migrations must advance exactly one format version.", nameof(migration));
        if (migration.FromVersion < 0) throw new ArgumentOutOfRangeException(nameof(migration));
        if (!_migrations.TryAdd(migration.FromVersion, migration)) throw new InvalidOperationException($"A project migration from format {migration.FromVersion} is already registered.");
    }

    public string Serialize(AuthoringProjectDefinition project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return JsonSerializer.Serialize(project, _options);
    }

    public AuthoringProjectDefinition Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        JsonObject document = JsonNode.Parse(
            json,
            new JsonNodeOptions { PropertyNameCaseInsensitive = true },
            new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })?.AsObject()
            ?? throw new InvalidDataException("The .playproject document contained no project definition.");
        int version = document["formatVersion"]?.GetValue<int>() ?? 1;
        while (version < AuthoringProjectDefinition.CurrentFormatVersion)
        {
            if (!_migrations.TryGetValue(version, out IAuthoringProjectMigration? migration) || migration is null)
                throw new NotSupportedException($"No .playproject migration is registered for project format {version}.");
            document = migration.Migrate(document);
            version = migration.ToVersion;
            document["formatVersion"] = version;
        }
        if (version > AuthoringProjectDefinition.CurrentFormatVersion)
            throw new NotSupportedException($"Project format {version} is newer than this authoring library supports ({AuthoringProjectDefinition.CurrentFormatVersion}).");
        if (version < 1) throw new NotSupportedException($"Project format {version} is invalid.");
        AuthoringProjectDefinition project = document.Deserialize<AuthoringProjectDefinition>(_options)
            ?? throw new InvalidDataException("The .playproject document contained no project definition.");
        Normalize(project);
        return project;
    }

    public AuthoringProjectDefinition Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Deserialize(File.ReadAllText(path));
    }

    public void Save(string path, AuthoringProjectDefinition project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AtomicTextFile.Write(path, Serialize(project));
    }

    public ValidationSummary Validate(AuthoringProjectDefinition project, string? projectFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var diagnostics = new List<AuthoringDiagnostic>();
        string documentId = project.Id;
        if (string.IsNullOrWhiteSpace(project.Id)) Error("PROJECT001", "Project ID is required.");
        if (string.IsNullOrWhiteSpace(project.DisplayName)) Error("PROJECT002", "Project display name is required.");
        if (project.FormatVersion != AuthoringProjectDefinition.CurrentFormatVersion) Error("PROJECT003", $"Project format {project.FormatVersion} is not supported.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        string? root = string.IsNullOrWhiteSpace(projectFilePath) ? null : Path.GetDirectoryName(Path.GetFullPath(projectFilePath));
        foreach (ProjectAssetEntry asset in project.Assets)
        {
            if (string.IsNullOrWhiteSpace(asset.Id)) Error("PROJECT010", "Every asset requires an ID.");
            else if (!ids.Add(asset.Id)) Error("PROJECT011", $"Duplicate asset ID '{asset.Id}'.", asset.Id);
            if (string.IsNullOrWhiteSpace(asset.RelativePath)) Error("PROJECT012", $"Asset '{asset.Id}' requires a relative path.", asset.Id);
            else
            {
                string normalized;
                try { normalized = ProjectPath.NormalizeRelative(asset.RelativePath); }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                    Error("PROJECT013", exception.Message, asset.Id);
                    continue;
                }
                if (!paths.Add(normalized)) Error("PROJECT014", $"Multiple assets use path '{normalized}'.", asset.Id);
                if (root is not null && !File.Exists(ProjectPath.Resolve(root, normalized)))
                    diagnostics.Add(new AuthoringDiagnostic("PROJECT015", AuthoringDiagnosticSeverity.Warning, $"Asset file '{normalized}' does not exist.", documentId, asset.Id, "relativePath", normalized));
                string expected = ExpectedExtension(asset.Kind);
                if (!string.IsNullOrEmpty(expected) && !normalized.EndsWith(expected, StringComparison.OrdinalIgnoreCase))
                    diagnostics.Add(new AuthoringDiagnostic("PROJECT016", AuthoringDiagnosticSeverity.Warning, $"Asset kind {asset.Kind} normally uses '{expected}'.", documentId, asset.Id, "relativePath", normalized));
            }
        }
        ValidateDirectoryList(project.SampleDirectories, "sampleDirectories", "PROJECT020");
        ValidateDirectoryList(project.ImpulseResponseDirectories, "impulseResponseDirectories", "PROJECT021");
        foreach (string exportProfile in project.ExportProfiles)
        {
            string normalized;
            try { normalized = ProjectPath.NormalizeRelative(exportProfile); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                diagnostics.Add(new AuthoringDiagnostic("PROJECT022", AuthoringDiagnosticSeverity.Error, exception.Message, documentId, PropertyPath: "exportProfiles"));
                continue;
            }
            if (root is not null && !File.Exists(ProjectPath.Resolve(root, normalized)))
                diagnostics.Add(new AuthoringDiagnostic("PROJECT023", AuthoringDiagnosticSeverity.Warning, $"Export profile '{normalized}' does not exist.", documentId, PropertyPath: "exportProfiles", SourcePath: normalized));
        }
        return new ValidationSummary(diagnostics);

        void ValidateDirectoryList(IEnumerable<string> values, string propertyPath, string code)
        {
            foreach (string value in values)
            {
                try { ProjectPath.NormalizeRelative(value); }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                    diagnostics.Add(new AuthoringDiagnostic(code, AuthoringDiagnosticSeverity.Error, exception.Message, documentId, PropertyPath: propertyPath));
                }
            }
        }

        void Error(string code, string message, string objectId = "") => diagnostics.Add(new AuthoringDiagnostic(code, AuthoringDiagnosticSeverity.Error, message, documentId, objectId));
    }

    private static string ExpectedExtension(AuthoringAssetKind kind) => kind switch
    {
        AuthoringAssetKind.SoundGraph => ".playsound",
        AuthoringAssetKind.GeneratorRequest => ".playgen",
        AuthoringAssetKind.StreamingScene => ".playstream",
        _ => ""
    };

    private static void Normalize(AuthoringProjectDefinition project)
    {
        project.Id ??= "untitled.project";
        project.DisplayName ??= "Untitled Playroma Sound Project";
        project.Assets ??= [];
        project.SampleDirectories ??= [];
        project.ImpulseResponseDirectories ??= [];
        project.ExportProfiles ??= [];
        project.Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ProjectAssetEntry asset in project.Assets)
        {
            asset.Id ??= "";
            asset.DisplayName ??= "";
            asset.RelativePath ??= "";
            asset.Tags ??= [];
            asset.Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}

public static class ProjectPath
{
    private static readonly char[] CrossPlatformInvalidCharacters = [':', '*', '?', '"', '<', '>', '|', '\0'];
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Converts a project-relative path to a portable forward-slash representation.
    /// Windows drive paths, UNC paths, traversal segments, and characters that are
    /// invalid on Windows are rejected even when validation runs on another OS.
    /// </summary>
    public static string NormalizeRelative(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string candidate = relativePath.Trim().Replace('\\', '/');
        if (candidate.StartsWith("/", StringComparison.Ordinal) ||
            (candidate.Length >= 2 && char.IsAsciiLetter(candidate[0]) && candidate[1] == ':'))
            throw new ArgumentException("Project asset paths must be relative on every supported platform.", nameof(relativePath));

        string[] segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) throw new ArgumentException("Project asset path must contain a file or directory name.", nameof(relativePath));
        foreach (string segment in segments)
        {
            if (segment is "." or "..") throw new InvalidOperationException("Project asset paths cannot contain '.' or '..' segments.");
            if (segment.IndexOfAny(CrossPlatformInvalidCharacters) >= 0 || segment.Any(char.IsControl))
                throw new InvalidOperationException($"Project asset path segment '{segment}' contains a character that is not portable.");
            if (segment.EndsWith(' ') || segment.EndsWith('.'))
                throw new InvalidOperationException($"Project asset path segment '{segment}' cannot end in a space or period.");
            string deviceName = segment.Split('.', 2)[0];
            if (WindowsReservedNames.Contains(deviceName))
                throw new InvalidOperationException($"Project asset path segment '{segment}' is a reserved device name.");
        }
        return string.Join('/', segments);
    }

    public static string Resolve(string rootDirectory, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        string root = Path.GetFullPath(rootDirectory);
        string normalized = NormalizeRelative(relativePath);
        string platformRelative = normalized.Replace('/', Path.DirectorySeparatorChar);
        string result = Path.GetFullPath(Path.Combine(root, platformRelative));
        string relativeToRoot = Path.GetRelativePath(root, result);
        if (Path.IsPathRooted(relativeToRoot) ||
            relativeToRoot == ".." ||
            relativeToRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relativeToRoot.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Resolved project path escaped the project directory.");
        return result;
    }
}

internal static class AtomicTextFile
{
    public static void Write(string path, string content)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
